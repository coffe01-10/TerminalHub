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

        // Files tab — lands in the session cwd, entries populated
        vm.SelectedRightTab = 1;
        await Task.Delay(200);
        Assert.NotEmpty(vm.Files.Entries);
        Assert.NotEmpty(vm.Files.Breadcrumbs);
        window.CaptureRenderedFrame()?.Save(Path.Combine(outDir, "files.png"));

        // Codex assistant tab
        vm.SelectedRightTab = 4;
        await Task.Delay(120);
        window.CaptureRenderedFrame()?.Save(Path.Combine(outDir, "assistant.png"));

        Assert.True(File.Exists(Path.Combine(outDir, "dashboard.png")));
        Assert.True(File.Exists(Path.Combine(outDir, "files.png")));
        Assert.True(File.Exists(Path.Combine(outDir, "assistant.png")));
        window.Close();
    }

    [AvaloniaFact]
    public async Task DockSelect_Deploy_ReportsArtifactsOrInstructions()
    {
        PtySessionFactory.UseMock = true;
        var window = new MainWindow { Width = 1200, Height = 800 };
        window.Show();
        await Task.Delay(400);
        var vm = (TerminalHub.App.ViewModels.MainWindowViewModel)window.DataContext!;

        vm.DockSelectCommand.Execute(4);
        await Task.Delay(200);

        var deployLines = vm.Dashboard.OutputLog.Where(l => l.Message.Contains("Deploy")).ToList();
        Assert.NotEmpty(deployLines);
        Assert.Contains(deployLines, l =>
            l.Message.Contains("产物目录") || l.Message.Contains("publish") || l.Message.Contains("打包"));
        window.Close();
    }

    [AvaloniaFact]
    public async Task SessionCard_PreviewLines_Colored()
    {
        PtySessionFactory.UseMock = true;
        var window = new MainWindow { Width = 1200, Height = 800 };
        window.Show();
        await Task.Delay(400);
        var vm = (TerminalHub.App.ViewModels.MainWindowViewModel)window.DataContext!;

        FeedDemoOutput(vm);
        await Task.Delay(300);

        var card = vm.SessionCards.First(c => ReferenceEquals(c.Model, vm.ActiveSession));
        card.Refresh();
        Assert.NotEmpty(card.PreviewLines);
        Assert.Contains(card.PreviewLines, l => l.Text.Contains("Ready"));
        // Lines emitted with ANSI green/cyan/magenta foregrounds → non-default brushes.
        var defaultBrush = card.PreviewLines.First(l => !l.Text.Contains("Ready")).Foreground;
        Assert.Contains(card.PreviewLines, l => l.Foreground != defaultBrush);
        window.Close();
    }

    [AvaloniaFact]
    public async Task DockSelect_NavigatesAndHighlights()
    {
        PtySessionFactory.UseMock = true;
        var window = new MainWindow { Width = 1200, Height = 800 };
        window.Show();
        await Task.Delay(400);
        var vm = (TerminalHub.App.ViewModels.MainWindowViewModel)window.DataContext!;

        vm.DockSelectCommand.Execute(1); // Monitor → Processes
        Assert.Equal(0, vm.SelectedRightTab);
        Assert.Equal(1, vm.DockHighlight);

        vm.DockSelectCommand.Execute(2); // SSH → right tab 3
        Assert.Equal(3, vm.SelectedRightTab);
        Assert.Equal(2, vm.DockHighlight);

        vm.DockSelectCommand.Execute(3); // Logs → right tab 2
        Assert.Equal(2, vm.SelectedRightTab);
        Assert.Equal(3, vm.DockHighlight);

        // String parameters from XAML must parse too (the original dock bug:
        // RelayCommand<int> silently no-oped on string CommandParameter).
        vm.DockSelectCommand.Execute("2");
        Assert.Equal(3, vm.SelectedRightTab);
        Assert.Equal(2, vm.DockHighlight);

        vm.DockSelectCommand.Execute(5); // Settings toggles + highlights
        Assert.True(vm.SettingsOpen);
        Assert.Equal(5, vm.DockHighlight);
        vm.DockSelectCommand.Execute(5);
        Assert.False(vm.SettingsOpen);

        // New Session adds a session.
        var before = vm.SessionCards.Count;
        vm.DockSelectCommand.Execute(0);
        await Task.Delay(400);
        Assert.Equal(before + 1, vm.SessionCards.Count);
        window.Close();
    }

    /// <summary>End-to-end: real Linux PTY through the UI pipeline (skipped off-Linux).</summary>
    [AvaloniaFact]
    public async Task RealPty_EndToEnd()
    {
        if (!OperatingSystem.IsLinux()) return;
        PtySessionFactory.UseMock = false;

        var window = new MainWindow { Width = 1200, Height = 800 };
        window.Show();
        await Task.Delay(600);

        var vm = (TerminalHub.App.ViewModels.MainWindowViewModel)window.DataContext!;
        Assert.NotEmpty(vm.SessionCards);

        var emu = vm.ActiveSession!.Emulator;
        emu.SendText("echo E2E_$((6*7))\r");

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline && !emu.Buffer.TailText(30).Contains("E2E_42"))
            await Task.Delay(100);
        Assert.True(emu.Buffer.TailText(30).Contains("E2E_42"),
            "real PTY output never reached the screen buffer");

        // Debug tab gets the raw (ANSI-bearing) line for the same output.
        deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline && vm.Dashboard.DebugLog.Count == 0)
            await Task.Delay(100);
        Assert.NotEmpty(vm.Dashboard.DebugLog);

        // Search finds the echoed marker in the active session's buffer.
        vm.Dashboard.SearchQuery = "E2E_42";
        await Task.Delay(150);
        Assert.NotEmpty(vm.Dashboard.SearchHits);
        Assert.Contains(vm.Dashboard.SearchHits, h => h.Text.Contains("E2E_42"));
        window.Close();
    }
}
