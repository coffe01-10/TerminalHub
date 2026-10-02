using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using TerminalHub.App.Controls;
using Xunit;

namespace TerminalHub.Tests;

public class ThemeWorkspaceTests
{
    [AvaloniaTheory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task ThemeCoversTerminalAndPreview_WithoutChangingPtyDimensions(int theme)
    {
        using var fixture = new StageLayoutTests.StageFixture();
        await Task.Delay(700);
        var vm = fixture.Vm;
        vm.ThemeIndex = theme;
        vm.DockVisibilityMode = 1;
        await Task.Delay(450);
        foreach (var card in vm.SessionCards)
        {
            var buffer = card.Model.Emulator.Buffer;
            card.Model.Emulator.Parser.Feed("\x1b[2J\x1b[H\x1b[36m~/Projects/TerminalHub\x1b[0m\r\n\r\n" +
                "  Terminal workspace\r\n  Streaming response, token by token...\r\n\r\n" +
                "\x1b[32m  + src/terminal/renderer.cs\x1b[0m\r\n\x1b[33m  ~ tests/streaming.cs\x1b[0m\r\n\r\n" +
                "  Chinese output: 实时工作区与完整终端预览\r\n" +
                $"\x1b[{buffer.Rows};1H\x1b[35m> Ready for input\x1b[0m");
        }
        var inactive = vm.SessionCards[0].Model.Emulator;
        var dimensions = (inactive.Buffer.Columns, inactive.Buffer.Rows);
        await Task.Delay(500);
        Assert.Equal(dimensions, (inactive.Buffer.Columns, inactive.Buffer.Rows));
        Assert.Equal(5, fixture.Window.GetVisualDescendants().OfType<StagePreview>().Count());
        Assert.Equal(ThemeManager.Names[theme], vm.Settings.Theme);
        Assert.Equal(ThemeManager.IsLight, theme is 2 or 3);
        // Header tabs previously kept Fluent's blue selection in Paper and Black.
        foreach (var name in new[] { "OutputTabs", "InspectorTabs" })
        {
            var tabs = fixture.Window.FindControl<ListBox>(name)!;
            var selected = tabs.GetVisualDescendants().OfType<ListBoxItem>().First(i => i.IsSelected);
            var surface = selected.GetVisualDescendants().OfType<ContentPresenter>().First();
            Assert.Equal(((ISolidColorBrush)ThemeManager.Brush("AccentSoft")).Color,
                ((ISolidColorBrush)surface.Background!).Color);
        }
        // The session menu used to keep Fluent's grey background in Paper.
        var menuButton = fixture.Window.FindControl<Button>("SessionMenuButton")!;
        var menu = (MenuFlyout)menuButton.Flyout!;
        menu.ShowAt(menuButton);
        await Task.Delay(80);
        var presenter = ((MenuItem)menu.Items[0]!).FindAncestorOfType<MenuFlyoutPresenter>()!;
        Assert.NotNull(presenter);
        Assert.Same(ThemeManager.Brush("Floating"), presenter.Background);
        menu.Hide();
        var output = Environment.GetEnvironmentVariable("TERMINALHUB_STAGE_CAPTURES");
        if (output is not null)
        {
            Directory.CreateDirectory(output);
            fixture.Window.CaptureRenderedFrame()!.Save(Path.Combine(output, $"theme-{ThemeManager.Current}.png"));
            vm.ShowWorkspaceCommand.Execute(null);
            await Task.Delay(200);
            fixture.Window.CaptureRenderedFrame()!.Save(Path.Combine(output, $"workspace-{ThemeManager.Current}.png"));
            vm.InspectorVisible = false;
            vm.SettingsOpen = true;
            await Task.Delay(150);
            fixture.Window.CaptureRenderedFrame()!.Save(Path.Combine(output, $"settings-{ThemeManager.Current}.png"));
            var settingsTabs = fixture.Window.FindControl<TabControl>("SettingsTabs")!;
            settingsTabs.SelectedIndex = 2;
            await Task.Delay(100);
            fixture.Window.CaptureRenderedFrame()!.Save(Path.Combine(output, $"shortcuts-{ThemeManager.Current}.png"));
            settingsTabs.SelectedIndex = 0;
            vm.SettingsOpen = false;
            vm.ToggleSplitCommand.Execute(null);
            await Task.Delay(150);
            fixture.Window.CaptureRenderedFrame()!.Save(Path.Combine(output, $"split-{ThemeManager.Current}.png"));
        }
    }

    [AvaloniaFact]
    public async Task ShellIndex_CustomKind_RoundTrips()
    {
        using var fixture = new StageLayoutTests.StageFixture();
        await Task.Delay(400);
        // Index 4 is the "自定义 Custom" combo item — ShellIndex must map it
        // both ways instead of silently collapsing it to PowerShell.
        fixture.Vm.ShellIndex = 4;
        Assert.Equal(TerminalHub.Core.Settings.ShellKind.Custom, fixture.Vm.Settings.Shell);
        Assert.Equal(4, fixture.Vm.ShellIndex);
    }

    [AvaloniaFact]
    public async Task RenameWithF2_PreservesRunningSession()
    {
        using var fixture = new StageLayoutTests.StageFixture();
        await Task.Delay(700);
        var session = fixture.Vm.ActiveSession;
        fixture.Window.KeyPressQwerty(PhysicalKey.F2, RawInputModifiers.None);
        await Task.Delay(150);
        var dialog = Assert.Single(fixture.Window.OwnedWindows);
        var input = dialog.GetVisualDescendants().OfType<TextBox>().Single();
        input.Text = "Claude · TerminalHub";
        dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await Task.Delay(150);
        Assert.Equal("Claude · TerminalHub", fixture.Vm.ActiveCard!.Name);
        Assert.Same(session, fixture.Vm.ActiveSession);
        Assert.True(session!.IsRunning);
    }

    [AvaloniaFact]
    public async Task WorkspaceFollowsShellDirectoryAndLiveFileChanges()
    {
        var directory = Path.Combine(Path.GetTempPath(), "terminalhub-workspace-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            using var fixture = new StageLayoutTests.StageFixture();
            await Task.Delay(700);
            fixture.Vm.ActiveSession!.Emulator.Parser.Feed("\x1b]9;9;" + directory + "\a");
            await Task.Delay(100);
            fixture.Vm.ShowWorkspaceCommand.Execute(null);
            Assert.Equal(directory, fixture.Vm.ActiveWorkingDirectory);
            Assert.Equal(directory, fixture.Vm.Files.CurrentPath);
            await File.WriteAllTextAsync(Path.Combine(directory, "stream.txt"), "live");
            for (var attempt = 0; attempt < 20 && fixture.Vm.Files.Entries.Count == 0; attempt++)
                await Task.Delay(50);
            Assert.Single(fixture.Vm.Files.Entries);
            fixture.Vm.ActiveCard = fixture.Vm.SessionCards[0];
            await Task.Delay(150);
            Assert.Equal(fixture.Vm.ActiveWorkingDirectory, fixture.Vm.Files.CurrentPath);
        }
        finally { Directory.Delete(directory, true); }
    }
}
