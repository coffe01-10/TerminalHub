using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using TerminalHub.Core.Pty;

namespace TerminalHub.Pty;

/// <summary>
/// Real PTY on Linux/macOS via libc forkpty().
/// All native allocations happen in the parent before fork. After forkpty the
/// child calls warmed chdir/execve stubs and a pre-resolved _exit address. SafeFileHandle is
/// constructed in the parent, after the pid==0 branch, so the child does not
/// allocate managed objects or enter the parent's catch.
/// </summary>
public sealed class LinuxPtySession : IPtySession
{
    private SafeFileHandle? _master;
    private int _childPid;
    private CancellationTokenSource? _readLoopCts;
    private Task? _readLoop;
    private IntPtr _argvBlock, _envpBlock;
    private readonly List<IntPtr> _allocations = new();

    // Read-loop / waitpid-watcher / Write / UI threads all touch these.
    private volatile bool _isRunning;
    private volatile bool _hasExitCode;
    private volatile bool _disposed;
    private int _exitCode;

    public Guid Id { get; } = Guid.NewGuid();
    public bool IsRunning => _isRunning;
    public int? ExitCode => _hasExitCode ? _exitCode : null;
    public int? ProcessId => _childPid > 0 ? _childPid : null;

    public event Action<IPtySession, ReadOnlyMemory<byte>>? OutputReceived;
    public event Action<IPtySession, int>? Exited;

    public unsafe Task StartAsync(PtyOptions options, CancellationToken cancellationToken = default)
    {
        var parts = SplitCommandLine(options.Shell + " " + options.Arguments);
        if (parts.Length == 0) throw new ArgumentException("Empty shell command");
        // Calling _exit to warm its P/Invoke stub would terminate the parent.
        // Resolve it before fork and use a direct unmanaged call in the child.
        var exit = (delegate* unmanaged[Cdecl]<int, void>)WarmChildPath();

        var exeBytes = ToNativeString(ResolveExecutable(parts[0]));
        var cwdBytes = string.IsNullOrEmpty(options.WorkingDirectory)
            ? null : ToNativeString(options.WorkingDirectory);

        _argvBlock = BuildPointerBlock(parts);
        _envpBlock = BuildEnvironmentBlock(options);

        var win = new Winsize { ws_row = (ushort)options.Rows, ws_col = (ushort)options.Columns };
        int pid;
        int masterFd;
        try
        {
            pid = Native.forkpty(out masterFd, IntPtr.Zero, IntPtr.Zero, ref win);
        }
        catch
        {
            FreeNativeAllocations(); // argv/envp blocks — no child will inherit them
            throw;
        }
        if (pid < 0)
        {
            FreeNativeAllocations();
            throw new InvalidOperationException($"forkpty failed: errno={Marshal.GetLastWin32Error()}");
        }
        if (pid == 0)
        {
            // CHILD — only pre-warmed libc calls. No managed allocation, and this
            // branch must not fall into the parent's catch.
            if (cwdBytes is not null && Native.chdir(cwdBytes) != 0)
                exit(127);
            Native.execve(exeBytes, _argvBlock, _envpBlock);
            // exec failed: exit with 127 (the shell convention for a command we
            // could not run). Killing ourselves instead would surface as a
            // signal death and the parent would report a meaningless -1.
            exit(127);
        }
        try
        {
            _master = new SafeFileHandle(masterFd, ownsHandle: true);
        }
        catch
        {
            try { Native.kill(pid, Native.SIGKILL); } catch { /* child already gone */ }
            var status = 0;
            try { Native.waitpid(pid, ref status, 0); } catch { /* already reaped */ }
            try { Native.close(masterFd); } catch { /* fd already closed */ }
            FreeNativeAllocations();
            throw;
        }

        _childPid = pid;
        _isRunning = true;
        // The child execs from its own private copies — the parent's argv/envp
        // blocks can go now instead of staying pinned for the session's lifetime.
        FreeNativeAllocations();

        _readLoopCts = new CancellationTokenSource();
        _readLoop = Task.Run(() => ReadLoop(_readLoopCts.Token), CancellationToken.None);
        _ = Task.Run(WatchChild);
        return Task.CompletedTask;
    }

    /// <summary>Whitespace split that honors "..." and '...' quoting and \
    /// backslash escapes (outside single quotes, like a POSIX shell).</summary>
    private static string[] SplitCommandLine(string cmdline)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        var quote = '\0';
        var esc = false;
        foreach (var c in cmdline)
        {
            if (esc) { sb.Append(c); esc = false; continue; }
            if (c == '\\' && quote != '\'') { esc = true; continue; }
            if (quote == '\0' && (c == '"' || c == '\'')) { quote = c; continue; }
            if (c == quote) { quote = '\0'; continue; }
            if (quote == '\0' && char.IsWhiteSpace(c))
            {
                if (sb.Length > 0) { parts.Add(sb.ToString()); sb.Clear(); }
                continue;
            }
            sb.Append(c);
        }
        if (esc) sb.Append('\\'); // trailing backslash kept verbatim
        if (sb.Length > 0) parts.Add(sb.ToString());
        return parts.ToArray();
    }

    private void FreeNativeAllocations()
    {
        foreach (var p in _allocations) Marshal.FreeHGlobal(p);
        _allocations.Clear();
        _argvBlock = _envpBlock = IntPtr.Zero;
    }

    private static IntPtr WarmChildPath()
    {
        Native.getpid();
        // An empty path always fails, without changing the parent's cwd or
        // accidentally executing a real /x file while warming the stub.
        var empty = new byte[] { 0 };
        Native.chdir(empty);
        Native.execve(empty, IntPtr.Zero, IntPtr.Zero);
        return Native.ExitAddress;
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
        var env = PtyEnvironment.Build(options).Select(pair => $"{pair.Key}={pair.Value}").ToList();

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
        // poll() drives the loop: cancellation is observed within 100ms, and a
        // blocked read() can never outlive Dispose (close() does not reliably
        // wake a read() already in progress on Linux).
        // The fd is captured once: Dispose waits for this loop before closing
        // the handle, so the number stays valid for the loop's lifetime.
        var fd = _master is { } m ? (int)m.DangerousGetHandle() : -1;
        var fds = new PollFd { Fd = fd, Events = Native.POLLIN };
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int ready;
                try { ready = Native.poll(ref fds, 1, 100); }
                catch { break; }
                if (ready <= 0) continue;           // timeout → re-check ct; EINTR → retry

                int n;
                try { n = Native.read(fd, buffer, buffer.Length); }
                catch { break; }
                // After POLLHUP, read() still returns the kernel-buffered tail
                // before reporting EIO/EOF — so the child's final output survives
                // its exit (the old IsRunning gate dropped it).
                if (n < 0)
                {
                    if (Marshal.GetLastWin32Error() == Native.EINTR) continue;
                    break;
                }
                if (n == 0) break; // EOF
                var chunk = new byte[n];
                Array.Copy(buffer, chunk, n);
                // A subscriber exception must not fault this task. It does not
                // continue later subscribers of the same Invoke.
                try { OutputReceived?.Invoke(this, chunk); }
                catch (Exception ex) { Debug.WriteLine($"PTY output subscriber failed: {ex.Message}"); }
            }
        }
        catch (ObjectDisposedException) { /* master fd closed beneath us */ }
    }

    private async Task WatchChild()
    {
        while (IsRunning)
        {
            var status = 0;
            var r = Native.waitpid(_childPid, ref status, Native.WNOHANG);
            if (r == _childPid)
            {
                // Publish exit code first — the volatile flag orders the write
                // so readers can never observe a torn int?.
                _exitCode = Native.WIFEXITED(status) ? Native.WEXITSTATUS(status) : -1;
                _hasExitCode = true;
                _isRunning = false;
                // After Dispose the owner is gone — a late Exited callback would
                // touch already-torn-down views. The code itself stays queryable.
                if (_disposed) return;
                // Do NOT cancel the read loop here — it drains the kernel-buffered
                // tail itself and exits on EIO. Cancelling would truncate output.
                await Task.Delay(50); // let the read loop deliver the last chunks first
                if (_disposed) return;
                Exited?.Invoke(this, _exitCode);
                return;
            }
            await Task.Delay(120);
        }
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        var master = _master;
        if (master is null || !IsRunning) return;
        var tmp = data.ToArray();
        // Pin the fd for the whole write: VT query responses come in on the
        // PTY read thread while Dispose can close the handle from the UI
        // thread — without AddRef the OS could recycle the descriptor and the
        // write would hit an unrelated fd.
        var release = false;
        try
        {
            master.DangerousAddRef(ref release);
            var fd = (int)master.DangerousGetHandle();
            // Loop on short writes — a large paste can exceed the PTY kernel
            // buffer (on Linux, 4096 bytes in canonical-echo terms); EINTR → retry.
            var off = 0;
            while (off < tmp.Length)
            {
                var n = Native.write(fd, ref tmp[off], tmp.Length - off);
                if (n < 0)
                {
                    if (Marshal.GetLastWin32Error() == Native.EINTR) continue;
                    break;
                }
                if (n == 0) break;
                off += n;
            }
        }
        catch (ObjectDisposedException) { /* handle closed concurrently */ }
        finally
        {
            if (release) master.DangerousRelease();
        }
    }

    public void Resize(int columns, int rows)
    {
        var master = _master;
        if (master is null) return;
        var release = false;
        try
        {
            master.DangerousAddRef(ref release);
            var win = new Winsize { ws_row = (ushort)rows, ws_col = (ushort)columns };
            Native.ioctl((int)master.DangerousGetHandle(), Native.TIOCSWINSZ, ref win);
        }
        catch (ObjectDisposedException) { /* handle closed concurrently */ }
        finally
        {
            if (release) master.DangerousRelease();
        }
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
        _disposed = true;
        Kill();
        _readLoopCts?.Cancel();
        // Wait for the read loop BEFORE closing the fd — otherwise the loop could
        // read() a stale descriptor number that the OS may have already recycled.
        try { _readLoop?.Wait(300); } catch { }
        // SafeFileHandle.Dispose defers the real close() until any in-flight
        // DangerousAddRef (Write/Resize on another thread) has released.
        _master?.Dispose();
        FreeNativeAllocations();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Winsize
    {
        public ushort ws_row;
        public ushort ws_col;
        public ushort ws_xpixel;
        public ushort ws_ypixel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int Fd;
        public short Events;
        public short Revents;
    }

    private static class Native
    {
        // libc stays loaded for the session API's lifetime, like its DllImports.
        public static readonly IntPtr ExitAddress = NativeLibrary.GetExport(
            NativeLibrary.Load("libc", typeof(LinuxPtySession).Assembly, null), "_exit");
        public const int WNOHANG = 1;
        public const int SIGHUP = 1;
        public const int SIGKILL = 9;
        public const int EINTR = 4; // same value on Linux and macOS
        // 0x5414 is the Linux value; macOS needs 0x80087467 — this class serves both.
        public static readonly uint TIOCSWINSZ =
            OperatingSystem.IsMacOS() ? 0x80087467u : 0x5414u;
        public const short POLLIN = 0x0001;

        [DllImport("libc", SetLastError = true)]
        public static extern int forkpty(out int amaster, IntPtr name, IntPtr termp, ref Winsize winp);

        [DllImport("libc", SetLastError = true)] public static extern int read(int fd, byte[] buf, int count);
        [DllImport("libc", SetLastError = true)] public static extern int write(int fd, ref byte buf, int count);
        [DllImport("libc", SetLastError = true)] public static extern int poll(ref PollFd fds, nint nfds, int timeoutMs);
        [DllImport("libc")] public static extern int ioctl(int fd, uint request, ref Winsize winp);
        [DllImport("libc")] public static extern int kill(int pid, int sig);
        [DllImport("libc")] public static extern int getpgid(int pid);
        [DllImport("libc")] public static extern int getpid();
        [DllImport("libc")] public static extern int waitpid(int pid, ref int status, int options);
        [DllImport("libc")] public static extern int chdir(byte[] path);
        [DllImport("libc")] public static extern int close(int fd);
        [DllImport("libc")] public static extern int execve(byte[] path, IntPtr argv, IntPtr envp);

        public static bool WIFEXITED(int status) => (status & 0x7f) == 0;
        public static int WEXITSTATUS(int status) => (status >> 8) & 0xff;
    }
}
