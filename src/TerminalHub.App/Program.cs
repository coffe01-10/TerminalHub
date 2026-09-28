using Avalonia;
using TerminalHub.Core.Pty;
using TerminalHub.Pty;

namespace TerminalHub.App;

internal static class Program
{
    /// <summary>Global mutex keeps a single instance running.</summary>
    private static Mutex? _singleInstance;

    [STAThread]
    public static int Main(string[] args)
    {
        // --mock forces the in-memory PTY (CI / smoke).
        if (args.Contains("--mock"))
            PtySessionFactory.UseMock = true;

        _singleInstance = new Mutex(initiallyOwned: true, "TerminalHub.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            // Second instance: exit quietly. (Later: forward args to first instance.)
            return 0;
        }

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            _singleInstance.ReleaseMutex();
            _singleInstance.Dispose();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
