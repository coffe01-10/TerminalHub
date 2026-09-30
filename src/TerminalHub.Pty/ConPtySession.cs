using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using TerminalHub.Core.Pty;

namespace TerminalHub.Pty;

/// <summary>
/// Windows ConPTY session: CreatePseudoConsole + pipes + CreateProcess with
/// PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE. Compiles everywhere; runs on Windows only.
/// </summary>
public sealed class ConPtySession : IPtySession
{
    private IntPtr _hpc = IntPtr.Zero;
    private SafeFileHandle? _ptyIn;      // write end -> conpty input
    private SafeFileHandle? _ptyOut;     // read end  <- conpty output
    private Process? _process;
    // Cached at exit so ExitCode/IsRunning stay queryable after _process is disposed.
    private volatile bool _hasExitCode;
    private int _exitCode;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;
    private int _disposed;

    public Guid Id { get; } = Guid.NewGuid();
    public bool IsRunning => _process is { HasExited: false };
    public int? ExitCode => _hasExitCode ? _exitCode : _process is { HasExited: true } p ? p.ExitCode : null;
    public int? ProcessId => _process?.Id;

    public event Action<IPtySession, ReadOnlyMemory<byte>>? OutputReceived;
    public event Action<IPtySession, int>? Exited;

    public Task StartAsync(PtyOptions options, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("ConPTY requires Windows.");

        var size = new COORD { X = (short)options.Columns, Y = (short)options.Rows };

        // Pipe layout: our stdin-write -> conpty-in; conpty-out -> our stdout-read.
        if (!Native.CreatePipe(out var inRead, out var inWrite, IntPtr.Zero, 0))
            throw new InvalidOperationException("CreatePipe failed (in)");
        SafeFileHandle? outRead = null, outWrite = null;
        try
        {
            if (!Native.CreatePipe(out var r, out var w, IntPtr.Zero, 0))
                throw new InvalidOperationException("CreatePipe failed (out)");
            outRead = r; outWrite = w;
            var hr = Native.CreatePseudoConsole(size, inRead, outWrite, 0, out _hpc);
            if (hr != 0)
                throw new InvalidOperationException($"CreatePseudoConsole failed: 0x{hr:X8}");
        }
        catch
        {
            // Partial init — every handle we still own must be closed.
            inWrite.Dispose();
            outRead?.Dispose();
            throw;
        }
        finally
        {
            // On success the console owns copies of its ends; on failure nothing
            // took them, so closing ours is still correct.
            inRead.Dispose();
            outWrite?.Dispose();
        }

        _ptyIn = inWrite;
        _ptyOut = outRead;

        try
        {
            _process = StartShell(options);
        }
        catch
        {
            Dispose(); // pipes + pseudo console
            throw;
        }
        _process.EnableRaisingEvents = true;
        _process.Exited += (_, _) =>
        {
            // Default -1 ("unknown"), NOT 0: a race that makes ExitCode throw
            // must not be reported as a successful exit.
            var code = -1;
            var p = _process;
            try
            {
                if (p is not null)
                {
                    if (!p.HasExited) p.WaitForExit(1000);
                    code = p.ExitCode;
                }
            }
            catch { }
            _exitCode = code;
            _hasExitCode = true;
            Exited?.Invoke(this, code);
        };

        _readCts = new CancellationTokenSource();
        // Dedicated thread: the read blocks in ReadFile until conhost emits data
        // or the pipe breaks. LongRunning keeps it off the thread pool.
        _readTask = Task.Factory.StartNew(() => ReadLoop(_readCts.Token),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        return Task.CompletedTask;
    }

    private Process StartShell(PtyOptions options)
    {
        // Quote a shell given as a path ("C:\Program Files\...\pwsh.exe") —
        // bare names resolved via PATH must stay unquoted.
        var shell = options.Shell;
        if (shell.IndexOfAny(['\\', '/']) >= 0 && !shell.StartsWith('"'))
            shell = $"\"{shell}\"";
        var cmdline = string.IsNullOrWhiteSpace(options.Arguments)
            ? shell
            : $"{shell} {options.Arguments}";

        var si = new STARTUPINFOEX();
        var environment = IntPtr.Zero;
        si.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();

        var attrSize = IntPtr.Zero;
        Native.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attrSize);
        si.lpAttributeList = Marshal.AllocHGlobal(attrSize);
        try
        {
            if (!Native.InitializeProcThreadAttributeList(si.lpAttributeList, 1, 0, ref attrSize))
                throw new InvalidOperationException("InitializeProcThreadAttributeList failed");
            environment = Marshal.StringToHGlobalUni(string.Join('\0', PtyEnvironment.Build(options)
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).Select(pair => $"{pair.Key}={pair.Value}")) + "\0\0");
            if (!Native.UpdateProcThreadAttribute(
                    si.lpAttributeList, 0,
                    (IntPtr)Native.PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                    _hpc, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                throw new InvalidOperationException("UpdateProcThreadAttribute failed");

            var flags = Native.EXTENDED_STARTUPINFO_PRESENT | Native.CREATE_UNICODE_ENVIRONMENT;
            var cwd = string.IsNullOrEmpty(options.WorkingDirectory) ? null : options.WorkingDirectory;

            if (!Native.CreateProcessW(
                    null, cmdline, IntPtr.Zero, IntPtr.Zero, false, flags,
                    environment, cwd, ref si, out var pi))
            {
                var err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"CreateProcess failed: {err}");
            }

            // Keep hProcess open until GetProcessById succeeds: for a shell that
            // exits within microseconds the open handle keeps the PID resolvable;
            // after the close a vanished PID would throw here and look like a
            // startup failure.
            try
            {
                return Process.GetProcessById(pi.dwProcessId);
            }
            finally
            {
                Native.CloseHandle(pi.hProcess);
                Native.CloseHandle(pi.hThread);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(environment);
            Native.DeleteProcThreadAttributeList(si.lpAttributeList);
            Marshal.FreeHGlobal(si.lpAttributeList);
        }
    }

    private void ReadLoop(CancellationToken ct)
    {
        // CreatePipe handles are synchronous-only: an async FileStream over them
        // throws in the ctor. Blocking ReadFile is what ConPTY expects anyway.
        var buffer = new byte[64 * 1024];
        while (!ct.IsCancellationRequested)
        {
            int n;
            try
            {
                if (_ptyOut is null
                    || !Native.ReadFile(_ptyOut, buffer, buffer.Length, out n, IntPtr.Zero)
                    || n == 0)
                    break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ConPTY ReadLoop ended: {ex.Message}");
                break;
            }
            var chunk = new byte[n];
            Array.Copy(buffer, chunk, n);
            // One subscriber failure must not end the read while the child is
            // still alive. This does not resume later subscribers of the same
            // Invoke, and it does not repair a parser exception on the next chunk.
            try { OutputReceived?.Invoke(this, chunk); }
            catch (Exception ex) { Debug.WriteLine($"ConPTY output subscriber failed: {ex.Message}"); }
        }
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        if (_ptyIn is null || !IsRunning) return;
        var tmp = data.ToArray();
        // Loop on short writes — a large paste can exceed the pipe's kernel buffer.
        var off = 0;
        while (off < tmp.Length)
        {
            try
            {
                // Race with Dispose closing the handle mid-write: ObjectDisposed
                // means the session is gone, not an error worth crashing the UI for.
                if (!Native.WriteFile(_ptyIn, ref tmp[off], tmp.Length - off, out var written, IntPtr.Zero) || written <= 0)
                {
                    Debug.WriteLine($"ConPTY WriteFile failed: {Marshal.GetLastWin32Error()}");
                    break;
                }
                off += written;
            }
            catch (ObjectDisposedException) { return; }
        }
    }

    public void Resize(int columns, int rows)
    {
        if (_hpc == IntPtr.Zero) return;
        var size = new COORD { X = (short)columns, Y = (short)rows };
        Native.ResizePseudoConsole(_hpc, size);
    }

    public void Kill()
    {
        try { _process?.Kill(entireProcessTree: true); } catch { }
    }

    public void Dispose()
    {
        // Process.Exited can re-enter us via the VM's close path — run once only.
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Kill();
        _readCts?.Cancel();
        // Close the pseudo console FIRST: conhost holds duplicates of the pipe
        // ends, so closing our copies alone never unblocks the reader — the old
        // order burned the full 300ms wait and relied on ObjectDisposed inside
        // ReadFile to unwind the thread.
        if (_hpc != IntPtr.Zero)
        {
            Native.ClosePseudoConsole(_hpc);
            _hpc = IntPtr.Zero;
        }
        // Cache the exit code before releasing the Process object — IsRunning
        // and ExitCode keep answering correctly after the handle is gone.
        var p = _process;
        _process = null;
        if (p is not null)
        {
            try
            {
                if (!_hasExitCode)
                {
                    p.WaitForExit(2000);   // Kill() is asynchronous — give it a moment
                    if (p.HasExited) { _exitCode = p.ExitCode; _hasExitCode = true; }
                }
            }
            catch { /* handle already gone — exit code stays unknown */ }
            p.Dispose();
        }
        _ptyIn?.Dispose();
        _ptyOut?.Dispose();
        // ReadFile/WriteFile take SafeFileHandle, so an in-flight call keeps the
        // handle alive and a later call throws ObjectDisposedException, which the
        // read loop catches. The wait only lets that loop notice the closed console.
        try { _readTask?.Wait(300); } catch { }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct COORD { public short X; public short Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFO
    {
        public int cb;
        public IntPtr lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars;
        public int dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    private static class Native
    {
        public const int EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
        public const int CREATE_UNICODE_ENVIRONMENT = 0x00000400;
        public const int PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern int CreatePseudoConsole(COORD size, SafeFileHandle hInput, SafeFileHandle hOutput, uint dwFlags, out IntPtr phPC);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern int ResizePseudoConsole(IntPtr hPC, COORD size);

        [DllImport("kernel32.dll")]
        public static extern void ClosePseudoConsole(IntPtr hPC);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CreatePipe(out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe, IntPtr lpPipeAttributes, int nSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteFile(SafeFileHandle hFile, ref byte lpBuffer, int nNumberOfBytesToWrite, out int lpNumberOfBytesWritten, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadFile(SafeFileHandle hFile, byte[] lpBuffer, int nNumberOfBytesToRead, out int lpNumberOfBytesRead, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CreateProcessW(string? lpApplicationName, string lpCommandLine,
            IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles,
            int dwCreationFlags, IntPtr lpEnvironment, string? lpCurrentDirectory,
            ref STARTUPINFOEX lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool UpdateProcThreadAttribute(IntPtr lpAttributeList, uint dwFlags,
            IntPtr attribute, IntPtr lpValue, IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);

        [DllImport("kernel32.dll")]
        public static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);
    }
}
