using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using TerminalHub.Core.Pty;

namespace TerminalHub.Pty;

/// <summary>
/// Real PTY on Linux/macOS via libc forkpty().
/// All native allocations happen in the parent before fork; the child only calls
/// pre-warmed libc functions (chdir / execve / kill) so no managed locks are needed
/// between fork and exec.
/// </summary>
public sealed class LinuxPtySession : IPtySession
{
    private int _masterFd = -1;
    private int _childPid;
    private CancellationTokenSource? _readLoopCts;
    private Task? _readLoop;
    private IntPtr _argvBlock, _envpBlock;
    private readonly List<IntPtr> _allocations = new();

    public Guid Id { get; } = Guid.NewGuid();
    public bool IsRunning { get; private set; }
    public int? ExitCode { get; private set; }
    public int? ProcessId => _childPid > 0 ? _childPid : null;

    public event Action<IPtySession, ReadOnlyMemory<byte>>? OutputReceived;
    public event Action<IPtySession, int>? Exited;

    public Task StartAsync(PtyOptions options, CancellationToken cancellationToken = default)
    {
        var parts = (options.Shell + " " + options.Arguments)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) throw new ArgumentException("Empty shell command");

        var exeBytes = ToNativeString(ResolveExecutable(parts[0]));
        var cwdBytes = string.IsNullOrEmpty(options.WorkingDirectory)
            ? null : ToNativeString(options.WorkingDirectory);

        _argvBlock = BuildPointerBlock(parts);
        _envpBlock = BuildEnvironmentBlock(options);

        // Warm every stub the child might invoke so no stub code is JIT-built post-fork.
        WarmChildPath();

        var win = new Winsize { ws_row = (ushort)options.Rows, ws_col = (ushort)options.Columns };
        int pid = Native.forkpty(out int master, IntPtr.Zero, IntPtr.Zero, ref win);
        if (pid < 0)
            throw new InvalidOperationException($"forkpty failed: errno={Marshal.GetLastWin32Error()}");

        if (pid == 0)
        {
            // CHILD — only pre-warmed libc calls, nothing that allocates managed objects.
            if (cwdBytes is not null)
                Native.chdir(cwdBytes);
            Native.execve(exeBytes, _argvBlock, _envpBlock);
            Native.kill(Native.getpid(), Native.SIGKILL); // exec failed
            Native._exit(127);
        }

        _masterFd = master;
        _childPid = pid;
        IsRunning = true;

        _readLoopCts = new CancellationTokenSource();
        _readLoop = Task.Run(() => ReadLoop(_readLoopCts.Token), CancellationToken.None);
        _ = Task.Run(WatchChild);
        return Task.CompletedTask;
    }

    private static void WarmChildPath()
    {
        Native.getpid();
        var dot = new byte[] { (byte)'.', 0 };
        Native.chdir(dot);
        var bad = new byte[] { (byte)'/', (byte)'x', 0 };
        Native.execve(bad, IntPtr.Zero, IntPtr.Zero);
        Native.kill(0, 0);
    }

    private static byte[] ToNativeString(string s) => Encoding.UTF8.GetBytes(s + '\0');

    private IntPtr BuildPointerBlock(string[] argv)
    {
        var block = Marshal.AllocHGlobal((argv.Length + 1) * IntPtr.Size);
        _allocations.Add(block);
        var ptrs = new IntPtr[argv.Length + 1];
        for (var i = 0; i < argv.Length; i++)
        {
            ptrs[i] = Marshal.StringToHGlobalAnsi(argv[i]);
            _allocations.Add(ptrs[i]);
        }
        Marshal.Copy(ptrs, 0, block, ptrs.Length);
        return block;
    }

    private IntPtr BuildEnvironmentBlock(PtyOptions options)
    {
        var env = new List<string>();
        foreach (var key in Environment.GetEnvironmentVariables().Keys)
            env.Add($"{key}={Environment.GetEnvironmentVariable(key.ToString()!)}");
        env.RemoveAll(e => e.StartsWith("TERM=") || e.StartsWith("COLORTERM="));
        env.Add("TERM=xterm-256color");
        env.Add("COLORTERM=truecolor");
        foreach (var kv in options.Environment)
            env.Add($"{kv.Key}={kv.Value}");

        var block = Marshal.AllocHGlobal((env.Count + 1) * IntPtr.Size);
        _allocations.Add(block);
        var ptrs = new IntPtr[env.Count + 1];
        for (var i = 0; i < env.Count; i++)
        {
            ptrs[i] = Marshal.StringToHGlobalAnsi(env[i]);
            _allocations.Add(ptrs[i]);
        }
        Marshal.Copy(ptrs, 0, block, ptrs.Length);
        return block;
    }

    private static string ResolveExecutable(string name)
    {
        if (name.Contains('/')) return name;
        var path = Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin";
        foreach (var dir in path.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, name);
            if (File.Exists(candidate)) return candidate;
        }
        return name; // let execve fail; child exits
    }

    private void ReadLoop(CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        while (!ct.IsCancellationRequested && IsRunning)
        {
            int n;
            try { n = Native.read(_masterFd, buffer, buffer.Length); }
            catch { break; }

            if (n <= 0)
            {
                if (n == 0 || !IsRunning || ct.IsCancellationRequested) break;
                Thread.Sleep(5);
                continue;
            }
            var chunk = new byte[n];
            Array.Copy(buffer, chunk, n);
            OutputReceived?.Invoke(this, chunk);
        }
    }

    private async Task WatchChild()
    {
        while (IsRunning)
        {
            var status = 0;
            var r = Native.waitpid(_childPid, ref status, Native.WNOHANG);
            if (r == _childPid)
            {
                IsRunning = false;
                ExitCode = Native.WIFEXITED(status) ? Native.WEXITSTATUS(status) : -1;
                await Task.Delay(50); // let the read loop drain
                _readLoopCts?.Cancel();
                Exited?.Invoke(this, ExitCode.Value);
                return;
            }
            await Task.Delay(120);
        }
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        if (_masterFd < 0 || !IsRunning) return;
        var tmp = data.ToArray();
        Native.write(_masterFd, tmp, tmp.Length);
    }

    public void Resize(int columns, int rows)
    {
        if (_masterFd < 0) return;
        var win = new Winsize { ws_row = (ushort)rows, ws_col = (ushort)columns };
        Native.ioctl(_masterFd, Native.TIOCSWINSZ, ref win);
    }

    public void Kill()
    {
        if (!IsRunning || _childPid <= 0) return;
        // forkpty/login_tty puts the child in its own session, so it is the
        // process-group leader (pgid == pid). Signal the group — SIGHUP then
        // SIGKILL — so script children (dotnet publish, sleep, …) die with the
        // shell. Never signal pgid 0/-1 (our group, or every process we can reach).
        // If the child is not a leader, or still shares our group, fall back to its pid.
        var child = _childPid;
        var pgid = Native.getpgid(child);
        var mine = Native.getpgid(0);
        if (pgid > 1 && pgid == child && pgid != mine)
        {
            Signal(-pgid, Native.SIGHUP);
            Signal(-pgid, Native.SIGKILL);
        }
        Signal(child, Native.SIGHUP);
        Signal(child, Native.SIGKILL);

        static void Signal(int pid, int sig)
        {
            if (pid == 0 || pid == -1) return;
            try { Native.kill(pid, sig); } catch { /* already reaped, or not a group */ }
        }
    }

    public void Dispose()
    {
        Kill();
        _readLoopCts?.Cancel();
        if (_masterFd >= 0)
        {
            try { Native.close(_masterFd); } catch { }
            _masterFd = -1;
        }
        try { _readLoop?.Wait(300); } catch { }
        foreach (var p in _allocations) Marshal.FreeHGlobal(p);
        _allocations.Clear();
        _argvBlock = _envpBlock = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Winsize
    {
        public ushort ws_row;
        public ushort ws_col;
        public ushort ws_xpixel;
        public ushort ws_ypixel;
    }

    private static class Native
    {
        public const int WNOHANG = 1;
        public const int SIGHUP = 1;
        public const int SIGKILL = 9;
        public const uint TIOCSWINSZ = 0x5414;

        [DllImport("libc", SetLastError = true)]
        public static extern int forkpty(out int amaster, IntPtr name, IntPtr termp, ref Winsize winp);

        [DllImport("libc")] public static extern int read(int fd, byte[] buf, int count);
        [DllImport("libc")] public static extern int write(int fd, byte[] buf, int count);
        [DllImport("libc")] public static extern int close(int fd);
        [DllImport("libc")] public static extern int ioctl(int fd, uint request, ref Winsize winp);
        [DllImport("libc")] public static extern int kill(int pid, int sig);
        [DllImport("libc")] public static extern int getpgid(int pid);
        [DllImport("libc")] public static extern int getpid();
        [DllImport("libc")] public static extern int waitpid(int pid, ref int status, int options);
        [DllImport("libc")] public static extern int chdir(byte[] path);
        [DllImport("libc")] public static extern int execve(byte[] path, IntPtr argv, IntPtr envp);
        [DllImport("libc")] public static extern void _exit(int status);

        public static bool WIFEXITED(int status) => (status & 0x7f) == 0;
        public static int WEXITSTATUS(int status) => (status >> 8) & 0xff;
    }
}
