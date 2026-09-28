using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using TerminalHub.App.Views;
using TerminalHub.Core.Pty;
using TerminalHub.Pty;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(TerminalHub.Tests.TestApp))]

namespace TerminalHub.Tests;

public class TestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TerminalHub.App.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont()
            .LogToTrace();
}

public class UiSmokeTests
{
    private static void FeedDemoOutput(TerminalHub.App.ViewModels.MainWindowViewModel vm)
    {
        // Push realistic ANSI-colored output through the real parser pipeline.
        var emu = vm.ActiveSession?.Emulator;
        Assert.NotNull(emu);
        emu!.Parser.Feed(
            "\u001b[36mdev@workstation\u001b[0m \u001b[34m~/Projects/web-app\u001b[0m " +
            "\u001b[35m⎇ main\u001b[0m \u001b[33mpnpm run dev\u001b[0m\r\n\r\n" +
            "> app@0.1.0 dev\r\n> next dev\r\n\r\n" +
            "\u001b[35m▲ Next.js 14.1.4\u001b[0m\r\n" +
            "- Local:   http://localhost:3000\r\n" +
            "- Environments: .env.local\r\n\r\n" +
            "\u001b[32m✓\u001b[0m Starting...\r\n" +
            "\u001b[32m✓\u001b[0m Ready in 846ms\r\n" +
            "\u001b[32m✓\u001b[0m Compiling / ...\r\n" +
            "\u001b[32m✓\u001b[0m Compiled / in 1.2s (893 modules)\r\n\r\n" +
            "GET / \u001b[32m200\u001b[0m in 310ms\r\n" +
            "GET /api/posts \u001b[32m200\u001b[0m in 124ms\r\n" +
            "GET /_next/static/chunks/app.js \u001b[32m200\u001b[0m in 23ms\r\n" +
            "GET /favicon.ico \u001b[32m200\u001b[0m in 12ms\r\n");
    }

    [AvaloniaFact]
    public async Task MainWindow_Renders_DashboardAndCodex()
    {
        PtySessionFactory.UseMock = true;

        var window = new MainWindow { Width = 1440, Height = 900 };
        window.Show();
        await Task.Delay(500); // startup sessions spawn

        var vm = (TerminalHub.App.ViewModels.MainWindowViewModel)window.DataContext!;
        Assert.True(vm.SessionCards.Count >= 3);

        FeedDemoOutput(vm);
        await Task.Delay(300);

        var outDir = Path.Combine(AppContext.BaseDirectory, "ui-snapshots");
        Directory.CreateDirectory(outDir);

        // Dashboard (Processes tab)
        vm.SelectedRightTab = 0;
        await Task.Delay(120);
        window.CaptureRenderedFrame()?.Save(Path.Combine(outDir, "dashboard.png"));

        // Codex assistant tab
        vm.SelectedRightTab = 4;
        await Task.Delay(120);
        window.CaptureRenderedFrame()?.Save(Path.Combine(outDir, "assistant.png"));

        Assert.True(File.Exists(Path.Combine(outDir, "dashboard.png")));
        Assert.True(File.Exists(Path.Combine(outDir, "assistant.png")));
        window.Close();
    }
}
