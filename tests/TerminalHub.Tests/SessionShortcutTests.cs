using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TerminalHub.App.Views;
using TerminalHub.Pty;
using Xunit;

namespace TerminalHub.Tests;

/// <summary>Session shortcuts: Ctrl+W close, Ctrl+Tab / Ctrl+Shift+Tab cycle,
/// and the ••• toolbar menu wiring.</summary>
public class SessionShortcutTests
{
    private static async Task<(MainWindow Win, TerminalHub.App.ViewModels.MainWindowViewModel Vm)> Boot()
    {
        PtySessionFactory.UseMock = true;
        var window = new MainWindow { Width = 1200, Height = 800 };
        window.Show();
        await Task.Delay(500); // startup sessions spawn
        var vm = (TerminalHub.App.ViewModels.MainWindowViewModel)window.DataContext!;
        Assert.True(vm.SessionCards.Count >= 3);
        return (window, vm);
    }

    [AvaloniaFact]
    public async Task CycleSession_NextAndPrev_Wraps()
    {
        var (window, vm) = await Boot();
        var cards = vm.SessionCards.ToList();

        // Active is the last-created card (sessions activate on spawn).
        var startIdx = cards.IndexOf(vm.ActiveCard!);
        Assert.True(startIdx >= 0);

        vm.CycleSession(+1);
        await Task.Delay(50);
        var expectedNext = cards[(startIdx + 1) % cards.Count];
        Assert.Same(expectedNext, vm.ActiveCard);
        Assert.Same(expectedNext.Model, vm.ActiveSession);

        vm.CycleSession(+1);
        await Task.Delay(50);
        Assert.Same(cards[(startIdx + 2) % cards.Count], vm.ActiveCard);

        // Prev twice lands back where we started minus one.
        vm.CycleSession(-1);
        vm.CycleSession(-1);
        await Task.Delay(50);
        Assert.Same(cards[startIdx], vm.ActiveCard);

        // Wrap-around: from first card, prev → last card.
        vm.ActiveCard = cards[0];
        await Task.Delay(50);
        vm.CycleSession(-1);
        Assert.Same(cards[^1], vm.ActiveCard);
        window.Close();
    }

    [AvaloniaFact]
    public async Task CloseActiveSession_ClosesAndActivatesNext()
    {
        var (window, vm) = await Boot();
        var count = vm.SessionCards.Count;
        var closing = vm.ActiveCard!;

        vm.CloseActiveSessionCommand.Execute(null);
        await Task.Delay(200);

        Assert.Equal(count - 1, vm.SessionCards.Count);
        Assert.DoesNotContain(vm.SessionCards, c => ReferenceEquals(c, closing));
        Assert.False(closing.Model.IsRunning); // PTY killed by real close path
        Assert.NotNull(vm.ActiveSession);
        Assert.NotSame(closing.Model, vm.ActiveSession);
        window.Close();
    }

    [AvaloniaFact]
    public async Task CopyActiveCwd_WritesUiLogLine()
    {
        var (window, vm) = await Boot();
        vm.CopyActiveCwdCommand.Execute(null);
        await Task.Delay(100);
        Assert.Contains(vm.Dashboard.OutputLog,
            l => l.Message.Contains("已复制 CWD") && l.Source == "ui");
        window.Close();
    }

    /// <summary>Real input pipeline: Ctrl+Shift+W must close the active session even
    /// while the terminal has focus (window-level tunnel handler wins). Bare Ctrl+W
    /// is a shell control byte (readline delete-word) and must reach the PTY.</summary>
    [AvaloniaFact]
    public async Task CtrlW_RealKeyPress_ClosesSession()
    {
        var (window, vm) = await Boot();
        var count = vm.SessionCards.Count;

        window.KeyPressQwerty(Avalonia.Input.PhysicalKey.W,
            Avalonia.Input.RawInputModifiers.Control | Avalonia.Input.RawInputModifiers.Shift);
        await Task.Delay(300);

        Assert.Equal(count - 1, vm.SessionCards.Count);
        window.Close();
    }

    /// <summary>Bare Ctrl+letter goes to the PTY as a control byte — ^W (0x17),
    /// ^B (0x02), ^J (0x0A) must not be swallowed by app shortcuts.</summary>
    [AvaloniaFact]
    public async Task BareCtrlLetters_ReachPtyAsControlBytes()
    {
        var (window, vm) = await Boot();
        var view = window.GetVisualDescendants().OfType<TerminalHub.App.Controls.TerminalView>()
            .First(v => !v.IsPreview && v.IsEffectivelyVisible);
        view.Focus();
        await Task.Delay(100);
        var mock = (TerminalHub.Core.Pty.MockPtySession)vm.ActiveSession!.Emulator.Pty;
        mock.RawInput.Clear();

        var before = vm.SessionCards.Count;
        var inspectorWas = vm.InspectorVisible;
        window.KeyPressQwerty(Avalonia.Input.PhysicalKey.W, Avalonia.Input.RawInputModifiers.Control);
        window.KeyPressQwerty(Avalonia.Input.PhysicalKey.B, Avalonia.Input.RawInputModifiers.Control);
        window.KeyPressQwerty(Avalonia.Input.PhysicalKey.J, Avalonia.Input.RawInputModifiers.Control);
        await Task.Delay(200);

        Assert.Equal(before, vm.SessionCards.Count);      // ^W must NOT close the session
        Assert.Equal(inspectorWas, vm.InspectorVisible);  // ^B must NOT toggle the inspector
        Assert.Equal("\x17\x02\n", mock.RawInput.ToString());
        window.Close();
    }

    /// <summary>Ctrl+Tab cycles forward, Ctrl+Shift+Tab cycles back.</summary>
    [AvaloniaFact]
    public async Task CtrlTab_RealKeyPress_Cycles()
    {
        var (window, vm) = await Boot();
        var cards = vm.SessionCards.ToList();
        var startIdx = cards.IndexOf(vm.ActiveCard!);

        window.KeyPressQwerty(Avalonia.Input.PhysicalKey.Tab, Avalonia.Input.RawInputModifiers.Control);
        await Task.Delay(150);
        Assert.Same(cards[(startIdx + 1) % cards.Count], vm.ActiveCard);

        window.KeyPressQwerty(Avalonia.Input.PhysicalKey.Tab,
            Avalonia.Input.RawInputModifiers.Control | Avalonia.Input.RawInputModifiers.Shift);
        await Task.Delay(150);
        Assert.Same(cards[startIdx], vm.ActiveCard);
        window.Close();
    }

    [AvaloniaFact]
    public async Task F2_InTerminal_ReachesCliInsteadOfOpeningRenameDialog()
    {
        using var fixture = new StageLayoutTests.StageFixture();
        await Task.Delay(700);
        var terminal = fixture.Window.FindControl<TerminalHub.App.Controls.TerminalView>("MainTerminal")!;
        terminal.Focus();
        var mock = (TerminalHub.Core.Pty.MockPtySession)fixture.Vm.ActiveSession!.Pty;
        mock.RawInput.Clear();
        fixture.Window.KeyPressQwerty(Avalonia.Input.PhysicalKey.F2, Avalonia.Input.RawInputModifiers.None);
        Assert.Equal("\x1bOQ", mock.RawInput.ToString());
        Assert.True(terminal.IsFocused);
    }

    [AvaloniaFact]
    public async Task ToolbarMenu_HasShortcutItems()
    {
        var (window, vm) = await Boot();
        await Task.Delay(200);

        var more = window.GetVisualDescendants().OfType<Button>()
            .First(b => b.Content as string == "•••");
        Assert.NotNull(more.Flyout);
        var flyout = Assert.IsType<MenuFlyout>(more.Flyout);
        var headers = flyout.Items.OfType<MenuItem>().Select(i => i.Header as string).ToList();
        Assert.Contains(headers, h => h!.Contains("关闭会话"));
        Assert.Contains(headers, h => h!.Contains("Ctrl+Tab"));
        Assert.Contains(headers, h => h!.Contains("复制 CWD"));

        // MenuItem bindings resolve when the flyout opens (DataContext flows in).
        flyout.ShowAt(more);
        await Task.Delay(150);
        var items = flyout.Items.OfType<MenuItem>().ToList();
        Assert.Contains(items, i => ReferenceEquals(i.Command, vm.CloseActiveSessionCommand));
        Assert.Contains(items, i => ReferenceEquals(i.Command, vm.CopyActiveCwdCommand));
        flyout.Hide();
        window.Close();
    }
}
