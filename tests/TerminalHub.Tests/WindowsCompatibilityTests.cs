using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using TerminalHub.App.Controls;
using TerminalHub.App.ViewModels;
using TerminalHub.Core.Settings;
using Xunit;

namespace TerminalHub.Tests;

/// <summary>Contracts from Windows main@1e4a9c4, exercised through the actual UI.
/// These cover changes introduced by the Linux/parity branch rather than API snapshots.</summary>
public class WindowsCompatibilityTests
{
    [AvaloniaFact]
    public void OldWindowsSettings_MissingNewFields_KeepPriorDefaultsAndExplicitChoices()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "th-win-settings-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "settings.json");
        try
        {
            File.WriteAllText(path, """{"InspectorVisible":true,"OutputVisible":true,"DockVisibilityMode":2,"FontSize":17}""");
            var store = new SettingsStore(path);
            var settings = store.Load();
            Assert.True(settings.InspectorVisible);
            Assert.True(settings.OutputVisible);
            Assert.Equal(2, settings.DockVisibilityMode);
            Assert.Equal(17, settings.FontSize);
            Assert.Equal(ShellKind.PowerShell, settings.Shell);
            Assert.True(settings.FilesShowHidden);
            store.Save(settings);
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            // The pre-merge Windows reader requires a JSON boolean here.
            Assert.Equal(System.Text.Json.JsonValueKind.True, json.RootElement.GetProperty("InspectorVisible").ValueKind);
            Assert.True(store.Load().FilesShowHidden);
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public void WindowsFiles_SelectionDoesNotOpenPreview_AndExistingEntriesStayVisible()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "th-win-files-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "sample.txt"), "explicit preview");
        File.WriteAllText(Path.Combine(root, ".config"), "dot entry");
        var hidden = Path.Combine(root, "hidden.txt");
        File.WriteAllText(hidden, "hidden entry");
        File.SetAttributes(hidden, FileAttributes.Hidden);
        try
        {
            using var vm = new FilesViewModel();
            vm.NavigateTo(root);
            Assert.Contains(vm.Entries, e => e.Name == ".config");
            Assert.Contains(vm.Entries, e => e.Name == "hidden.txt");
            vm.SelectedEntry = vm.Entries.Single(e => e.Name == "sample.txt");
            Assert.False(vm.HasPreview);
            vm.OpenSelected();
            Assert.True(vm.HasPreview);
            Assert.Equal("explicit preview", vm.PreviewText);
            vm.SelectedEntry = vm.Entries.Single(e => e.Name == ".config");
            Assert.Equal("sample.txt", vm.PreviewTitle);
            vm.OpenSelected();
            Assert.Equal("dot entry", vm.PreviewText);
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task WindowsSsh_EditorRemainsOpen_AndEnterDoesNotSaveOrConnect()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new StageLayoutTests.StageFixture();
        fixture.Vm.SelectedRightTab = 3;
        await Task.Delay(600);
        var host = fixture.Window.GetVisualDescendants().OfType<TextBox>()
            .Single(t => t.Watermark as string == "主机 host / IP");
        host.Text = "compat.invalid";
        host.Focus();
        fixture.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.Empty(fixture.Vm.Ssh.Hosts);
        Assert.Equal(5, fixture.Vm.SessionCards.Count);
        fixture.Vm.Ssh.AddOrUpdateCommand.Execute(null);
        Assert.Single(fixture.Vm.Ssh.Hosts);
        Assert.True(host.IsEffectivelyVisible);
        fixture.Vm.Ssh.ToggleEditingCommand.Execute(null);
        Assert.True(host.IsEffectivelyVisible);
        Assert.Equal("compat.invalid", fixture.Vm.Ssh.EditHost);
    }

    [AvaloniaFact]
    public async Task WindowsChrome_TwoRowsAndStableTitle_KeepExistingCommandsReachable()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new StageLayoutTests.StageFixture(width: 1100);
        await Task.Delay(600);
        var window = fixture.Window;
        var name = window.FindControl<Grid>("TerminalChromeTitle")!;
        var path = window.FindControl<Button>("ChromeDirectory")!;
        var close = window.FindControl<Button>("ChromeClose")!;
        var menu = window.FindControl<Button>("SessionMenuButton")!;
        var top = name.TranslatePoint(default, window)!.Value.Y;
        var bottom = path.TranslatePoint(default, window)!.Value.Y;
        Assert.True(bottom >= top + name.Bounds.Height);
        Assert.True(close.TranslatePoint(default, window)!.Value.Y < bottom);
        Assert.Equal(84, window.FindControl<Grid>("StageContentGrid")!.RowDefinitions[0].ActualHeight);
        Assert.Equal(default(Thickness), window.FindControl<Border>("TerminalViewport")!.BorderThickness);
        Assert.Contains(window.FindControl<Button>("ChromeSplit")!.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "分屏");
        Assert.Contains(window.FindControl<Button>("ChromePopout")!.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "弹出");
        Assert.NotNull(path.Command);
        Assert.NotNull(close.Command);
        Assert.NotNull(menu.Flyout);
        fixture.Vm.RenameSession((fixture.Vm.ActiveCard!, "renamed"));
        Assert.Equal("Terminal Hub · 终端控制中心", window.Title);
    }

    [AvaloniaFact]
    public async Task WindowsShelf_MiddleReleaseAndHorizontalDrag_DoNotCloseOrDetachSessions()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new StageLayoutTests.StageFixture();
        await Task.Delay(700);
        var window = fixture.Window;
        var shelf = window.FindControl<ListBox>("SessionShelf")!;
        var cards = shelf.GetVisualDescendants().OfType<StageCard>().OrderBy(c => c.Bounds.Y).ToArray();
        var first = cards[0];
        var second = cards[1];
        var original = fixture.Vm.SessionCards.Select(c => c.Model).ToHashSet();
        var start = first.TranslatePoint(new Point(70, 40), window)!.Value;
        window.MouseDown(start, MouseButton.Middle);
        window.MouseUp(start, MouseButton.Middle);
        Assert.True(original.SetEquals(fixture.Vm.SessionCards.Select(c => c.Model)));
        var secondTop = second.TranslatePoint(default, window)!.Value.Y;
        var firstTop = first.TranslatePoint(default, window)!.Value.Y;
        Assert.Equal(first.Bounds.Height + 16, secondTop - firstTop, 1);
        window.MouseDown(start, MouseButton.Left);
        var end = new Point(650, secondTop + second.Bounds.Height / 2);
        window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        window.MouseUp(end, MouseButton.Left);
        await Task.Delay(150);
        Assert.Empty(fixture.Vm.Popouts);
        Assert.True(original.SetEquals(fixture.Vm.SessionCards.Select(c => c.Model)));
        Assert.All(original, m => Assert.True(m.Pty.IsRunning));
    }


    [AvaloniaFact]
    public async Task WindowsScrollback_DoesNotOverlayTheTerminalMouseArea()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new StageLayoutTests.StageFixture();
        await Task.Delay(600);
        var terminal = fixture.Window.FindControl<TerminalView>("MainTerminal")!;
        terminal.Emulator!.Parser.Feed(string.Join("\r\n", Enumerable.Range(0, 200).Select(i => $"line-{i}")));
        await Task.Delay(100);
        Assert.True(terminal.Emulator.Buffer.ScrollbackCount > 0);
        var scrollbar = Assert.Single(fixture.Window.GetVisualDescendants()
            .OfType<Avalonia.Controls.Primitives.ScrollBar>(), b =>
                b.Parent is Panel panel && panel.Children.Contains(terminal));
        Assert.False(scrollbar.IsEffectivelyVisible);
        terminal.ScrollToOffset(3);
        Assert.True(terminal.IsScrolledUp);
        terminal.ScrollToBottom();
        Assert.False(terminal.IsScrolledUp);
    }

    [AvaloniaFact]
    public async Task WindowsCtrlShiftF_DoesNotReplaceTerminalFocusWithNewSearchShortcut()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new StageLayoutTests.StageFixture();
        await Task.Delay(600);
        fixture.Vm.OutputVisible = false;
        var terminal = fixture.Window.FindControl<TerminalView>("MainTerminal")!;
        terminal.Focus();
        fixture.Window.KeyPressQwerty(PhysicalKey.F, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.False(fixture.Vm.OutputVisible);
        Assert.True(terminal.IsFocused);
        fixture.Window.KeyTextInput("compat-input");
        await Task.Delay(100);
        Assert.Contains("compat-input", fixture.Vm.ActiveSession!.Emulator.Buffer.TailText(10));
    }
}
