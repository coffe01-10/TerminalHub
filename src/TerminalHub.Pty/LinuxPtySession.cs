using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using TerminalHub.Core.Pty;

namespace TerminalHub.Pty;

/// <summary>
/// Real PTY on Linux/macOS via libc forkpty(). The forked child execs the shell
/// immediately; no managed code runs between fork and exec.
/// </summary>
public sealed class LinuxPtySession : IPtySession
{
    private int _masterFd = -1;
    private int _childPid;
    private CancellationTokenSource? _readLoopCts;
    private Task? _readLoop;
    private PtyOptions _options = new() { Shell = "bash" };

    public Guid Id { get; } = Guid.NewGuid();
    public bool IsRunning { get; private set; }
    public int? ExitCode { get; private set; }

    public event Action<IPtySession, ReadOnlyMemory<byte>>? OutputReceived;
    public event Action<IPtySession, int>? Exited;

    public Task StartAsync(PtyOptions options, CancellationToken cancellationToken = default)
    {
        _options = options;
        var win = new Winsize
        {
            ws_row = (ushort)options.Rows,
            ws_col = (ushort)options.Columns,
        };

        int pid = Native.forkpty(out int master, IntPtr.Zero, IntPtr.Zero, ref win);
        if (pid < 0)
            throw new InvalidOperationException($"forkpty failed: errno={Marshal.GetLastWin32Error()}");

        if (pid == 0)
        {
            // Child: exec the shell. Only async-signal-safe/libc calls allowed here.
            ExecShell(options);
            Native._exit(127); // exec failed
        }

        _masterFd = master;
        _childPid = pid;
        IsRunning = true;

        // Non-blocking-ish read loop on a dedicated thread.
        _readLoopCts = new CancellationTokenSource();
        _readLoop = Task.Run(() => ReadLoop(_readLoopCts.Token), CancellationToken.None);
        Task.Run(WatchChild);
        return Task.CompletedTask;
    }

    private static void ExecShell(PtyOptions options)
    {
        if (!string.IsNullOrEmpty(options.WorkingDirectory))
            Native.chdir(options.WorkingDirectory);

        Native.setenv("TERM", "xterm-256color", 1);
        Native.setenv("COLORTERM", "truecolor", 1);

        var parts = (options.Shell + " " + options.Arguments)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var argv = BuildArgv(parts);
        Native.execvp(parts[0], argv);
    }

    private static IntPtr[] BuildArgv(string[] parts)
    {
        var argv = new IntPtr[parts.Length + 1];
        for (var i = 0; i < parts.Length; i++)
            argv[i] = Marshal.StringToHGlobalAnsi(parts[i]);
        argv[^1] = IntPtr.Zero;
        return argv;
    }

    private void ReadLoop(CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        while (!ct.IsCancellationRequested && IsRunning)
        {
            int n;
            try
            {
                n = Native.read(_masterFd, buffer, buffer.Length);
            }
            catch { break; }

            if (n <= 0)
            {
                if (n < 0 && ct.IsCancellationRequested) break;
                // EIO/EAGAIN: child may have exited; waitpid decides.
                if (n == 0 || !IsRunning) break;
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
                // Drain remaining output, then exit.
                await Task.Delay(50);
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
        if (!IsRunning) return;
        try { Native.kill(_childPid, Native.SIGHUP); } catch { }
        try { Native.kill(_childPid, Native.SIGKILL); } catch { }
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
        try { _readLoop?.Wait(200); } catch { }
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
        [DllImport("libc")] public static extern int waitpid(int pid, ref int status, int options);
        [DllImport("libc")] public static extern int chdir(string path);
        [DllImport("libc")] public static extern int setenv(string name, string value, int overwrite);
        [DllImport("libc")] public static extern int execvp(string file, IntPtr[] argv);
        [DllImport("libc")] public static extern void _exit(int status);

        public static bool WIFEXITED(int status) => (status & 0x7f) == 0;
        public static int WEXITSTATUS(int status) => (status >> 8) & 0xff;
    }
}
