using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace TerminalHub.Core.Monitoring;

/// <summary>
/// Cross-platform sampler. CPU/mem use Windows P/Invoke on Windows and /proc on Linux;
/// disk and network use portable BCL APIs.
/// </summary>
public sealed class SystemMonitor : ISystemMonitor
{
    private readonly TimeSpan _processSampleFloor = TimeSpan.FromSeconds(1);
    private Timer? _timer;
    private long _lastIdleTicks, _lastTotalTicks;
    private long _lastRxBytes, _lastTxBytes;
    private DateTime _lastNetSample = DateTime.MinValue;
    private readonly Dictionary<int, TimeSpan> _lastProcCpu = new();
    private IReadOnlyList<ProcessInfo> _processes = [];
    private DateTime _lastProcSample = DateTime.MinValue;

    public SystemSample Current { get; private set; } = new();
    public IReadOnlyList<ProcessInfo> Processes => _processes;

    public event Action<ISystemMonitor>? Sampled;

    public void Start(TimeSpan interval)
    {
        _timer?.Dispose();
        _timer = new Timer(_ => Tick(), null, TimeSpan.Zero, interval);
    }

    public void Stop() => _timer?.Change(Timeout.Infinite, Timeout.Infinite);

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>Timer callbacks are not serialized — a slow sample (process list on a
    /// loaded box) must not let a second Tick run concurrently and corrupt the deltas.</summary>
    private int _ticking;

    private void Tick()
    {
        if (Interlocked.Exchange(ref _ticking, 1) != 0) return;
        try
        {
            TickCore();
        }
        finally
        {
            Volatile.Write(ref _ticking, 0);
        }
    }

    private void TickCore()
    {
        var (cpu, memUsed, memTotal) = SampleCpuAndMemory();
        var (rx, tx) = SampleNetwork();
        var (diskUsed, diskTotal) = SampleDisk();

        Current = new SystemSample
        {
            CpuPercent = cpu,
            MemoryUsedBytes = memUsed,
            MemoryTotalBytes = memTotal,
            DiskUsedBytes = diskUsed,
            DiskTotalBytes = diskTotal,
            NetworkRxBytesPerSec = rx,
            NetworkTxBytesPerSec = tx,
        };

        if (DateTime.UtcNow - _lastProcSample >= _processSampleFloor)
        {
            _processes = SampleProcesses();
            _lastProcSample = DateTime.UtcNow;
        }

        Sampled?.Invoke(this);
    }

    private (double cpu, double memUsed, double memTotal) SampleCpuAndMemory()
    {
        if (OperatingSystem.IsWindows())
            return WindowsSample();
        return ProcfsSample();
    }

    // ---- Linux /proc ----

    private (double, double, double) ProcfsSample()
    {
        double cpu = 0, memUsed = 0, memTotal = 0;
        try
        {
            // /proc/stat: "cpu  user nice system idle iowait irq softirq steal"
            var stat = File.ReadLines("/proc/stat").First();
            var parts = stat.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            long idle = long.Parse(parts[4]) + long.Parse(parts[5]);
            long total = parts.Skip(1).Take(8).Sum(long.Parse);
            var dTotal = total - _lastTotalTicks;
            var dIdle = idle - _lastIdleTicks;
            if (dTotal > 0 && _lastTotalTicks > 0)
                cpu = Math.Clamp((1.0 - (double)dIdle / dTotal) * 100, 0, 100);
            _lastIdleTicks = idle;
            _lastTotalTicks = total;
        }
        catch { }

        try
        {
            long totalKb = 0, availKb = 0;
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                if (line.StartsWith("MemTotal:"))
                    totalKb = ParseKb(line);
                else if (line.StartsWith("MemAvailable:"))
                    availKb = ParseKb(line);
                if (totalKb > 0 && availKb > 0) break;
            }
            memTotal = totalKb * 1024.0;
            memUsed = (totalKb - availKb) * 1024.0;
        }
        catch { }

        return (cpu, memUsed, memTotal);

        static long ParseKb(string line)
        {
            var span = line.AsSpan();
            var i = 0;
            while (i < span.Length && !char.IsDigit(span[i])) i++;
            var start = i;
            while (i < span.Length && char.IsDigit(span[i])) i++;
            return long.TryParse(span[start..i], out var v) ? v : 0;
        }
    }

    // ---- Windows ----

    private (double, double, double) WindowsSample()
    {
        double cpu = 0;
        if (NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            long idleTicks = idle.ToInt64();
            long totalTicks = kernel.ToInt64() + user.ToInt64();
            var dTotal = totalTicks - _lastTotalTicks;
            var dIdle = idleTicks - _lastIdleTicks;
            if (dTotal > 0 && _lastTotalTicks > 0)
                cpu = Math.Clamp((1.0 - (double)dIdle / dTotal) * 100, 0, 100);
            _lastIdleTicks = idleTicks;
            _lastTotalTicks = totalTicks;
        }

        double memUsed = 0, memTotal = 0;
        var mem = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
        if (NativeMethods.GlobalMemoryStatusEx(ref mem))
        {
            memTotal = mem.ullTotalPhys;
            memUsed = memTotal - mem.ullAvailPhys;
        }
        return (cpu, memUsed, memTotal);
    }

    // ---- Portable ----

    private (double rx, double tx) SampleNetwork()
    {
        try
        {
            long rx = 0, tx = 0;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType is NetworkInterfaceType.Loopback)
                    continue;
                // Per-adapter isolation: tunnel/virtual adapters (Tailscale,
                // WireGuard) can throw on GetIPv4Statistics — one bad adapter
                // must not zero out the whole sample.
                try
                {
                    var s = nic.GetIPv4Statistics();
                    rx += s.BytesReceived;
                    tx += s.BytesSent;
                }
                catch (InvalidOperationException) { }
            }
            var now = DateTime.UtcNow;
            var elapsed = (now - _lastNetSample).TotalSeconds;
            double rxRate = 0, txRate = 0;
            if (_lastNetSample != DateTime.MinValue && elapsed > 0)
            {
                rxRate = Math.Max(0, (rx - _lastRxBytes) / elapsed);
                txRate = Math.Max(0, (tx - _lastTxBytes) / elapsed);
            }
            _lastRxBytes = rx;
            _lastTxBytes = tx;
            _lastNetSample = now;
            return (rxRate, txRate);
        }
        catch { return (0, 0); }
    }

    private static (double used, double total) SampleDisk()
    {
        try
        {
            // The OS drive, not a hardcoded C:\ — some machines install Windows elsewhere.
            var root = OperatingSystem.IsWindows()
                ? Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\"
                : "/";
            var drive = DriveInfo.GetDrives()
                .FirstOrDefault(d => d.IsReady && root.StartsWith(d.Name, StringComparison.OrdinalIgnoreCase));
            if (drive is null) return (0, 0);
            var used = drive.TotalSize - drive.AvailableFreeSpace;
            return (used, drive.TotalSize);
        }
        catch { return (0, 0); }
    }

    private DateTime _lastProcCpuTime = DateTime.MinValue;

    private IReadOnlyList<ProcessInfo> SampleProcesses()
    {
        var now = DateTime.UtcNow;
        var elapsedMs = _lastProcCpuTime == DateTime.MinValue
            ? 0
            : (now - _lastProcCpuTime).TotalMilliseconds;
        _lastProcCpuTime = now;

        var result = new List<ProcessInfo>();
        var seen = new HashSet<int>();

        var procs = Process.GetProcesses();
        try
        {
            foreach (var p in procs)
            {
                try
                {
                    seen.Add(p.Id);
                    var cpuTime = p.TotalProcessorTime;
                    double cpu = 0;
                    if (elapsedMs > 0 && _lastProcCpu.TryGetValue(p.Id, out var prev))
                    {
                        // % of one logical core: cpu-time delta / wall-time delta.
                        var deltaMs = (cpuTime - prev).TotalMilliseconds;
                        cpu = Math.Clamp(deltaMs / elapsedMs * 100, 0, 100);
                    }
                    _lastProcCpu[p.Id] = cpuTime;
                    result.Add(new ProcessInfo
                    {
                        Pid = p.Id,
                        Name = p.ProcessName,
                        CpuPercent = Math.Round(cpu, 1),
                        MemoryBytes = p.WorkingSet64,
                    });
                }
                catch { }
            }
        }
        finally
        {
            // GetProcesses opens a process handle per entry the moment
            // TotalProcessorTime/WorkingSet64 is touched — release them all.
            foreach (var p in procs) p.Dispose();
        }

        foreach (var stale in _lastProcCpu.Keys.Where(k => !seen.Contains(k)).ToList())
            _lastProcCpu.Remove(stale);

        return result.OrderByDescending(p => p.CpuPercent).ThenByDescending(p => p.MemoryBytes).Take(12).ToList();
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [StructLayout(LayoutKind.Sequential)]
        public struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
            public long ToInt64() => ((long)dwHighDateTime << 32) | dwLowDateTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }
    }
}
