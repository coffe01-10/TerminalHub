using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using TerminalHub.App.Controls;
using TerminalHub.Core.Settings;
using Xunit;

namespace TerminalHub.Tests;

public class ConfigurableSessionShortcutTests
{
    [AvaloniaFact]
    public async Task DefaultsAndCustomBinding_SwitchInShelfOrderWithoutRestarting()
    {
        using var fixture = new StageLayoutTests.StageFixture();
        await Task.Delay(500);
        fixture.Window.KeyPressQwerty(PhysicalKey.Digit1, RawInputModifiers.Alt);
        Assert.Same(fixture.Vm.SessionCards[0], fixture.Vm.ActiveCard);
        var originalSession = fixture.Vm.ActiveSession;
        fixture.Window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Control);
        Assert.Same(fixture.Vm.SessionCards[1], fixture.Vm.ActiveCard);
        var next = fixture.Vm.SessionShortcuts[0];
        next.Gesture = "Ctrl+Alt+Right";
        fixture.Window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.Control | RawInputModifiers.Alt);
        Assert.Same(fixture.Vm.SessionCards[2], fixture.Vm.ActiveCard);
        Assert.True(originalSession!.IsRunning);
        fixture.Vm.MoveSessionCard(fixture.Vm.SessionCards[2], fixture.Vm.SessionCards[0]);
        fixture.Window.KeyPressQwerty(PhysicalKey.Digit1, RawInputModifiers.Alt);
        Assert.Same(fixture.Vm.SessionCards[0], fixture.Vm.ActiveCard);
    }

    [AvaloniaFact]
    public async Task RecordingCtrlTab_DoesNotSwitchSession_AndPersists()
    {
        using var fixture = new StageLayoutTests.StageFixture();
        await Task.Delay(500);
        fixture.Vm.SettingsOpen = true;
        var tabs = fixture.Window.FindControl<TabControl>("SettingsTabs")!;
        tabs.SelectedIndex = 2;
        fixture.Window.UpdateLayout();
        var editors = tabs.GetVisualDescendants().OfType<ShortcutEditor>().ToArray();
        var editor = editors[0];
        var active = fixture.Vm.ActiveSession;
        editor.Focus();
        fixture.Window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.Control | RawInputModifiers.Alt);
        Assert.Same(active, fixture.Vm.ActiveSession);
        Assert.Contains("Right", fixture.Vm.SessionShortcuts[0].Gesture);
        fixture.Window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Control);
        Assert.Same(active, fixture.Vm.ActiveSession);
        Assert.Equal(Key.Tab, fixture.Vm.SessionShortcuts[0].ParsedGesture!.Key);
        fixture.Vm.PersistSettings();
        var directory = Path.Combine(Path.GetTempPath(), "terminalhub-shortcut-" + Guid.NewGuid());
        try
        {
            var store = new SettingsStore(Path.Combine(directory, "settings.json"));
            store.Save(fixture.Vm.Settings);
            Assert.Equal(fixture.Vm.SessionShortcuts[0].Gesture, store.Load().SessionShortcuts[0].Gesture);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [AvaloniaFact]
    public async Task ConflictsAndPlainTyping_AreRejected_AndDefaultsCanBeRestored()
    {
        using var fixture = new StageLayoutTests.StageFixture();
        await Task.Delay(500);
        var first = fixture.Vm.SessionShortcuts[0];
        first.Gesture = "Ctrl+C";
        Assert.NotEmpty(first.Error);
        Assert.False(fixture.Vm.HandleSessionShortcut(new KeyEventArgs { Key = Key.C, KeyModifiers = KeyModifiers.Control }));
        first.Gesture = "Shift+A";
        Assert.NotEmpty(first.Error);
        first.Gesture = fixture.Vm.SessionShortcuts[1].Gesture;
        Assert.NotEmpty(first.Error);
        Assert.NotEmpty(fixture.Vm.SessionShortcuts[1].Error);
        first.Gesture = "";
        Assert.Empty(first.Error);
        Assert.Empty(fixture.Vm.SessionShortcuts[1].Error);
        fixture.Vm.ResetSessionShortcutsCommand.Execute(null);
        Assert.All(fixture.Vm.SessionShortcuts, s => Assert.Empty(s.Error));
    }
}
