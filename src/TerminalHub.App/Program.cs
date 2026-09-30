using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using TerminalHub.Core.Pty;
using TerminalHub.Core.Settings;
using TerminalHub.Pty;

namespace TerminalHub.App;

internal static class Program
{
    /// <summary>Global mutex keeps a single instance running.</summary>
    private static Mutex? _singleInstance;
    internal static SingleInstanceActivation? Activation { get; private set; }
    internal static string? PendingDirectory { get; private set; }
    internal static string? PendingDirectoryError { get; private set; }

    internal static (string? Directory, string? Error) TakePendingLaunch()
    {
        var result = (PendingDirectory, PendingDirectoryError);
        PendingDirectory = null;
        PendingDirectoryError = null;
        return result;
    }

    private const string RelaunchedEnvVar = "TERMINALHUB_RELAUNCHED";

    [STAThread]
    public static int Main(string[] args)
    {
        // --mock forces the in-memory PTY (CI / smoke).
        if (args.Contains("--mock"))
            PtySessionFactory.UseMock = true;

        // Windows ConPTY quirk: when our own std handles are pipes or files
        // (launched from a redirected shell / CI runner), child shells silently
        // skip pseudo-console attach and die on stdin EOF. Relaunch through
        // ShellExecute once to get clean handles — GUI apps get NULL std handles
        // there, which attach correctly. Runs before the mutex so the old
        // process is gone before the relaunched one checks it.
        if (OperatingSystem.IsWindows()
            && Environment.GetEnvironmentVariable(RelaunchedEnvVar) is null
            && StdHandlesRedirected()
            && RelaunchViaShell(args))
            return 0;

        LaunchRequest.TryGetDirectory(args, out var launchDirectory, out var launchError);
        var instanceName = Environment.GetEnvironmentVariable("TERMINALHUB_INSTANCE_NAME") ?? "TerminalHub";
        _singleInstance = new Mutex(initiallyOwned: true, instanceName + ".SingleInstance", out var createdNew);
        if (!createdNew)
        {
            var pipe = instanceName + ".Activate";
            if (launchError is not null)
                SingleInstanceActivation.RequestNoticeAsync(pipe, launchError).GetAwaiter().GetResult();
            else if (launchDirectory.Length > 0)
                SingleInstanceActivation.RequestDirectoryAsync(pipe, launchDirectory).GetAwaiter().GetResult();
            else
                SingleInstanceActivation.RequestAsync(pipe).GetAwaiter().GetResult();
            _singleInstance.Dispose();
            return 0;
        }

        PendingDirectory = launchDirectory.Length > 0 ? launchDirectory : null;
        PendingDirectoryError = launchError;

        try
        {
            Activation = new SingleInstanceActivation(instanceName + ".Activate");
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            Activation?.Dispose();
            _singleInstance.ReleaseMutex();
            _singleInstance.Dispose();
        }
    }

    /// <summary>True when any std handle is a pipe or file (not console/absent).</summary>
    private static bool StdHandlesRedirected()
    {
        const int STD_INPUT = -10, STD_OUTPUT = -11, STD_ERROR = -12;
        const uint FILE_TYPE_CHAR = 0x0002;   // console / serial
        const uint FILE_TYPE_UNKNOWN = 0x0000; // invalid handle
        foreach (var id in new[] { STD_INPUT, STD_OUTPUT, STD_ERROR })
        {
            var h = Native.GetStdHandle(id);
            if (h == IntPtr.Zero || h == new IntPtr(-1)) continue;
            var t = Native.GetFileType(h);
            if (t is not (FILE_TYPE_CHAR or FILE_TYPE_UNKNOWN)) return true;
        }
        return false;
    }

    private static bool RelaunchViaShell(string[] args)
    {
        // Only relaunch our own exe — under `dotnet run` ProcessPath is dotnet.exe.
        var exe = Environment.ProcessPath;
        if (exe is null || !Path.GetFileName(exe)
                .Equals(AppDomain.CurrentDomain.FriendlyName + ".exe", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true, // clean std handles via shell
                WorkingDirectory = Environment.CurrentDirectory,
            };
            foreach (var a in args)
                psi.ArgumentList.Add(a);
            // ShellExecuteEx can't set env vars on the child; it inherits ours.
            Environment.SetEnvironmentVariable(RelaunchedEnvVar, "1");
            var p = Process.Start(psi);
            if (p is null)
            {
                Environment.SetEnvironmentVariable(RelaunchedEnvVar, null);
                return false;
            }
            return true;
        }
        catch
        {
            Environment.SetEnvironmentVariable(RelaunchedEnvVar, null);
            return false;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static class Native
    {
        [DllImport("kernel32.dll")]
        public static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll")]
        public static extern uint GetFileType(IntPtr hFile);
    }
}
