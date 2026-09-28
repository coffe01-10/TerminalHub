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
    private CancellationTokenSource? _readCts;

    public Guid Id { get; } = Guid.NewGuid();
    public bool IsRunning => _process is { HasExited: false };
    public int? ExitCode => _process is { HasExited: true } p ? p.ExitCode : null;

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
        if (!Native.CreatePipe(out var outRead, out var outWrite, IntPtr.Zero, 0))
            throw new InvalidOperationException("CreatePipe failed (out)");

        try
        {
            var hr = Native.CreatePseudoConsole(size, inRead, outWrite, 0, out _hpc);
            if (hr != 0)
                throw new InvalidOperationException($"CreatePseudoConsole failed: 0x{hr:X8}");
        }
        finally
        {
            // Console owns its copies now.
            inRead.Close();
            outWrite.Close();
        }

        _ptyIn = inWrite;
        _ptyOut = outRead;

        _process = StartShell(options);
        _process.EnableRaisingEvents = true;
        _process.Exited += (_, _) =>
        {
            var code = 0;
            try { code = _process.ExitCode; } catch { }
            Exited?.Invoke(this, code);
        };

        _readCts = new CancellationTokenSource();
        Task.Run(() => ReadLoop(_readCts.Token));
        return Task.CompletedTask;
    }

    private Process StartShell(PtyOptions options)
    {
        var cmdline = string.IsNullOrWhiteSpace(options.Arguments)
            ? options.Shell
            : $"{options.Shell} {options.Arguments}";

        var si = new STARTUPINFOEX();
        si.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();

        var attrSize = IntPtr.Zero;
        Native.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attrSize);
        si.lpAttributeList = Marshal.AllocHGlobal(attrSize);
        if (!Native.InitializeProcThreadAttributeList(si.lpAttributeList, 1, 0, ref attrSize))
            throw new InvalidOperationException("InitializeProcThreadAttributeList failed");

        try
        {
            if (!Native.UpdateProcThreadAttribute(
                    si.lpAttributeList, 0,
                    (IntPtr)Native.PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                    _hpc, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                throw new InvalidOperationException("UpdateProcThreadAttribute failed");

            var flags = Native.EXTENDED_STARTUPINFO_PRESENT | Native.CREATE_UNICODE_ENVIRONMENT;
            var cwd = string.IsNullOrEmpty(options.WorkingDirectory) ? null : options.WorkingDirectory;

            if (!Native.CreateProcessW(
                    null, cmdline, IntPtr.Zero, IntPtr.Zero, false, flags,
                    IntPtr.Zero, cwd, ref si, out var pi))
            {
                var err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"CreateProcess failed: {err}");
            }

            Native.CloseHandle(pi.hProcess);
            Native.CloseHandle(pi.hThread);
            return Process.GetProcessById(pi.dwProcessId);
        }
        finally
        {
            Native.DeleteProcThreadAttributeList(si.lpAttributeList);
            Marshal.FreeHGlobal(si.lpAttributeList);
        }
    }

    private async Task ReadLoop(CancellationToken ct)
    {
        var stream = new FileStream(_ptyOut!, FileAccess.Read, 64 * 1024, isAsync: true);
        var buffer = new byte[64 * 1024];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var n = await stream.ReadAsync(buffer, ct);
                if (n == 0) break;
                var chunk = new byte[n];
                Array.Copy(buffer, chunk, n);
                OutputReceived?.Invoke(this, chunk);
            }
        }
        catch (OperationCanceledException) { }
        catch { /* pipe closed */ }
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        if (_ptyIn is null || !IsRunning) return;
        var tmp = data.ToArray();
        if (!Native.WriteFile(_ptyIn, tmp, tmp.Length, out _, IntPtr.Zero))
            Debug.WriteLine("ConPTY WriteFile failed");
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
        Kill();
        _readCts?.Cancel();
        _ptyIn?.Dispose();
        _ptyOut?.Dispose();
        if (_hpc != IntPtr.Zero)
        {
            Native.ClosePseudoConsole(_hpc);
            _hpc = IntPtr.Zero;
        }
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
        public static extern bool WriteFile(SafeFileHandle hFile, byte[] lpBuffer, int nNumberOfBytesToWrite, out int lpNumberOfBytesWritten, IntPtr lpOverlapped);

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
