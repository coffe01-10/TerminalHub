using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TerminalHub.App.Controls;
using TerminalHub.App.ViewModels;
using TerminalHub.App.Views;
using TerminalHub.Core.Settings;
using TerminalHub.Pty;
using Xunit;

namespace TerminalHub.Tests;

public class PlatformParityTests
{
    [Fact]
    public void FreshSettings_UseThePlatformShell_ForAllStartupSessions()
    {
        var settings = new AppSettings();
        var expected = OperatingSystem.IsWindows() ? ShellKind.PowerShell : ShellKind.Bash;
        Assert.Equal(expected, settings.Shell);
        Assert.All(settings.StartupSessions, session => Assert.Equal(expected, session.Shell));
    }

    [AvaloniaFact]
    public void ShellSettings_OfferPlatformCommands_AndPersistTheSameKinds()
    {
        using var vm = new MainWindowViewModel();
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(new[] { "PowerShell (pwsh)", "cmd.exe", "WSL / bash", "bash", "自定义" }, vm.SettingsShellLabels);
            vm.SettingsShellIndex = 1;
            Assert.Equal(ShellKind.Cmd, vm.Settings.Shell);
        }
        else
        {
            Assert.Equal(new[] { "PowerShell (pwsh)", "bash", "自定义" }, vm.SettingsShellLabels);
            vm.SettingsShellIndex = 1;
            Assert.Equal(ShellKind.Bash, vm.Settings.Shell);
            vm.ShellIndex = 2; // Existing Windows workspace; Linux resolves this to bash.
            Assert.Equal(1, vm.SettingsShellIndex);
        }
        vm.SettingsShellIndex = vm.SettingsShellLabels.Count - 1;
        Assert.Equal(ShellKind.Custom, vm.Settings.Shell);
    }

    [AvaloniaTheory]
    [InlineData("DarkGlass", 1440, 900)]
    [InlineData("Black", 1440, 900)]
    [InlineData("White", 1440, 900)]
    [InlineData("Paper", 1440, 900)]
    [InlineData("DarkGlass", 1100, 680)]
    [InlineData("Black", 1100, 680)]
    [InlineData("White", 1100, 680)]
    [InlineData("Paper", 1100, 680)]
    public async Task Themes_KeepTerminalDockAndSettingsUsable(string theme, int width, int height)
    {
        var previousTheme = ThemeManager.Current;
        PtySessionFactory.UseMock = true;
        var directory = Path.Combine(Path.GetTempPath(), "th-visual-" + Guid.NewGuid().ToString("N"));
        var store = new SettingsStore(Path.Combine(directory, "settings.json"));
        store.Save(new AppSettings
        {
            StartupSessions = Enumerable.Range(1, 3).Select(i => new StartupSession
            {
                Name = $"Terminal {i:00}", WorkingDirectory = Path.GetPathRoot(Environment.CurrentDirectory)!
            }).ToList()
        });
        var window = new MainWindow(store) { Width = width, Height = height };
        try
        {
            window.Show();
            var vm = (MainWindowViewModel)window.DataContext!;
            var deadline = Environment.TickCount64 + 5000;
            while (vm.SessionCards.Count < 3 && Environment.TickCount64 < deadline) await Task.Delay(20);
            Assert.Equal(3, vm.SessionCards.Count);
            ThemeManager.Apply(theme);
            vm.DockVisibilityMode = 1;
            vm.OutputVisible = true;
            vm.ActiveSession!.Emulator.Parser.Feed("\x1b[2J\x1b[H\x1b[32mREADY · 中文 English\x1b[0m\r\n$ ");
            await Task.Delay(350);
            window.UpdateLayout();
            var terminal = window.FindControl<TerminalView>("MainTerminal")!;
            var dock = window.FindControl<DropletDock>("ActionDock")!;
            Assert.True(terminal.Bounds.Width > 200 && terminal.Bounds.Height > 120);
            Assert.True(dock.IsHitTestVisible);
            var buttons = dock.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("dock")).ToList();
            Assert.Equal(6, buttons.Count);
            Assert.All(buttons, b => Assert.True(b.Bounds.Width > 0 && b.Command is not null));
            var outputDirectory = Environment.GetEnvironmentVariable("TERMINALHUB_PARITY_CAPTURES");
            if (!string.IsNullOrEmpty(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
                window.CaptureRenderedFrame()!.Save(Path.Combine(outputDirectory, $"{theme}-{width}.png"));
            }
            vm.DockSelectCommand.Execute("5");
            window.UpdateLayout();
            Assert.True(vm.SettingsOpen);
            var tabs = window.FindControl<TabControl>("SettingsTabs")!;
            foreach (var tab in tabs.Items.OfType<TabItem>())
            {
                tabs.SelectedItem = tab;
                window.UpdateLayout();
                Assert.True(tabs.Bounds.Width > 0 && tabs.Bounds.Height > 0);
            }
        }
        finally { window.Close(); ThemeManager.Apply(previousTheme); Directory.Delete(directory, true); }
    }
}
