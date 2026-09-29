using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using TerminalHub.App.Controls;
using TerminalHub.App.ViewModels;
using TerminalHub.App.Views;
using Xunit;

namespace TerminalHub.Tests;

/// <summary>Temporary visual sweep: capture many UI states as PNGs for inspection.</summary>
public class UiBugHuntTests
{
    private static string Cap => Environment.GetEnvironmentVariable("TERMINALHUB_STAGE_CAPTURES")!;

    private static async Task Snap(MainWindow window, string name, int delay = 420)
    {
        await Task.Delay(delay);
        window.CaptureRenderedFrame()!.Save(Path.Combine(Cap, $"hunt-{name}.png"));
    }

    private static async Task SnapWindow(Window w, string name, int delay = 420)
    {
        await Task.Delay(delay);
        w.CaptureRenderedFrame()!.Save(Path.Combine(Cap, $"hunt-{name}.png"));
    }

    [AvaloniaFact]
    public async Task Capture_PanelStates()
    {
        if (Cap is null) return;
        using var fixture = new StageLayoutTests.StageFixture();
        var window = fixture.Window;
        var vm = fixture.Vm;
        await Task.Delay(700);

        // Output panel open
        window.KeyPressQwerty(PhysicalKey.J, RawInputModifiers.Control | RawInputModifiers.Shift);
        await Snap(window, "output-open");

        // Inspector tabs: 0 Monitor, 1 Files, 2 Logs, 3 Ssh, 4 Codex
        window.KeyPressQwerty(PhysicalKey.B, RawInputModifiers.Control | RawInputModifiers.Shift);
        await Snap(window, "inspector-monitor");

        vm.SelectedRightTab = 2;
        await Snap(window, "inspector-logs");

        vm.SelectedRightTab = 3;
        await Snap(window, "inspector-ssh");

        vm.SelectedRightTab = 4;
        await Snap(window, "inspector-codex");
        vm.SelectedRightTab = 0;

        // Settings overlay
        vm.DockSelectCommand.Execute("5");
        await Snap(window, "settings-open");
        vm.DockSelectCommand.Execute("5");

        // Split view
        vm.ToggleSplitCommand.Execute(null);
        await Snap(window, "split", 600);

        // Popout window
        vm.ToggleSplitCommand.Execute(null);
        vm.OpenInNewWindowCommand.Execute(null);
        await Task.Delay(400);
        var pop = Assert.Single(vm.Popouts);
        await SnapWindow(pop, "popout");
        pop.Close();
        await Task.Delay(300);

        // Rename dialog open
        window.KeyPressQwerty(PhysicalKey.F2, RawInputModifiers.None);
        await Task.Delay(300);
        var dialog = window.OwnedWindows.FirstOrDefault();
        if (dialog is not null)
        {
            await SnapWindow(dialog, "rename-dialog", 100);
            dialog.Close();
        }

        // ••• menu flyout — open via the toolbar button
        var menuBtn = window.GetVisualDescendants().OfType<Button>()
            .First(b => b.Content as string == "•••" && b.Flyout is not null);
        menuBtn.Flyout!.ShowAt(menuBtn);
        await Snap(window, "session-menu", 300);
        // close flyout by pressing Escape on the window
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await Task.Delay(200);
    }

    [AvaloniaFact]
    public async Task Capture_ManySessions_AndEmptyState()
    {
        if (Cap is null) return;
        using var fixture = new StageLayoutTests.StageFixture();
        var window = fixture.Window;
        var vm = fixture.Vm;
        await Task.Delay(700);

        // Grow to 10 sessions — shelf must scroll
        for (var i = 0; i < 5; i++) await vm.NewSessionCommand.ExecuteAsync(null);
        await Snap(window, "shelf-10", 800);

        // Close all sessions — empty state
        foreach (var card in vm.SessionCards.ToArray())
            vm.CloseSessionCommand.Execute(card);
        await Snap(window, "empty-state", 500);
    }
}
