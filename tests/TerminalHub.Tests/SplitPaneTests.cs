using Avalonia.Headless.XUnit;
using TerminalHub.App.Views;
using TerminalHub.Pty;
using Xunit;

namespace TerminalHub.Tests;

/// <summary>Split-pane: two independent sessions side-by-side, no input
/// cross-talk, clean toggling and graceful session-removal handling.</summary>
public class SplitPaneTests
{
    private static async Task<(MainWindow Window, TerminalHub.App.ViewModels.MainWindowViewModel Vm)> Boot()
    {
        PtySessionFactory.UseMock = true;
        var window = new MainWindow { Width = 1440, Height = 900 };
        window.Show();
        await Task.Delay(500); // startup sessions spawn
        var vm = (TerminalHub.App.ViewModels.MainWindowViewModel)window.DataContext!;
        Assert.True(vm.SessionCards.Count >= 3);
        return (window, vm);
    }

    private static async Task<string> TailOf(TerminalHub.Core.Terminal.TerminalEmulator emu, string marker)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline && !emu.Buffer.TailText(40).Contains(marker))
            await Task.Delay(80);
        return emu.Buffer.TailText(60);
    }

    [AvaloniaFact]
    public async Task Split_EnterExit_TwoDistinctSessions()
    {
        var window = new MainWindow { Width = 1440, Height = 900 };
        PtySessionFactory.UseMock = true;
        window.Show();
        await Task.Delay(500);
        var vm = (TerminalHub.App.ViewModels.MainWindowViewModel)window.DataContext!;

        Assert.False(vm.IsSplit);
        vm.ToggleSplitCommand.Execute(null);
        await Task.Delay(300);

        Assert.True(vm.IsSplit);
        Assert.NotNull(vm.LeftPane);
        Assert.NotNull(vm.RightPane);
        Assert.NotSame(vm.LeftPane, vm.RightPane);
        Assert.NotSame(vm.LeftPane!.Emulator, vm.RightPane!.Emulator);
        Assert.Equal(0, vm.FocusedPane);

        // Toggle off → single view, panes cleared, no sessions lost.
        var count = vm.SessionCards.Count;
        vm.ToggleSplitCommand.Execute(null);
        Assert.False(vm.IsSplit);
        Assert.Null(vm.LeftPane);
        Assert.Null(vm.RightPane);
        Assert.Equal(count, vm.SessionCards.Count);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Split_Input_NoCrossTalk()
    {
        var (window, vm) = await Boot();
        vm.ToggleSplitCommand.Execute(null);
        await Task.Delay(300);
        var left = vm.LeftPane!;
        var right = vm.RightPane!;

        // Type into each pane's emulator — mock PTY echoes locally.
        left.Emulator.SendText("echo LEFT_ONLY_111\r");
        right.Emulator.SendText("echo RIGHT_ONLY_222\r");

        var leftTail = await TailOf(left.Emulator, "LEFT_ONLY_111");
        var rightTail = await TailOf(right.Emulator, "RIGHT_ONLY_222");

        Assert.Contains("LEFT_ONLY_111", leftTail);
        Assert.DoesNotContain("RIGHT_ONLY_222", leftTail);
        Assert.Contains("RIGHT_ONLY_222", rightTail);
        Assert.DoesNotContain("LEFT_ONLY_111", rightTail);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Split_PaneFocus_SwitchesActiveSession()
    {
        var (window, vm) = await Boot();
        vm.ToggleSplitCommand.Execute(null);
        await Task.Delay(300);

        Assert.Same(vm.LeftPane, vm.ActiveSession); // split opened focused on left

        vm.FocusPane(1);
        await Task.Delay(150);
        Assert.Equal(1, vm.FocusedPane);
        Assert.Same(vm.RightPane, vm.ActiveSession);

        vm.FocusPane(0);
        await Task.Delay(150);
        Assert.Same(vm.LeftPane, vm.ActiveSession);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Split_CardClick_AssignsToFocusedPane()
    {
        var (window, vm) = await Boot();
        vm.ToggleSplitCommand.Execute(null);
        await Task.Delay(300);

        // Focus right pane, click a different card → right pane retargets.
        vm.FocusPane(1);
        var other = vm.SessionCards.First(c =>
            !ReferenceEquals(c.Model, vm.LeftPane) && !ReferenceEquals(c.Model, vm.RightPane));
        vm.ActiveCard = other;
        await Task.Delay(200);
        Assert.Same(other.Model, vm.RightPane);
        Assert.Same(other.Model, vm.ActiveSession);

        // Focus left, click another card → left pane retargets, right keeps its own.
        vm.FocusPane(0);
        var third = vm.SessionCards.First(c =>
            !ReferenceEquals(c.Model, vm.RightPane) && !ReferenceEquals(c.Model, vm.LeftPane));
        vm.ActiveCard = third;
        await Task.Delay(200);
        Assert.Same(third.Model, vm.LeftPane);
        Assert.Same(other.Model, vm.RightPane);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Split_SingleSession_SpawnsSecond_RepeatToggle_NoLeak()
    {
        PtySessionFactory.UseMock = true;
        var window = new MainWindow { Width = 1200, Height = 800 };
        window.Show();
        await Task.Delay(500);
        var vm = (TerminalHub.App.ViewModels.MainWindowViewModel)window.DataContext!;

        // Close down to exactly one session via the real close path.
        while (vm.SessionCards.Count > 1)
        {
            vm.CloseSessionCommand.Execute(vm.SessionCards.Last());
            await Task.Delay(120);
        }
        Assert.Single(vm.SessionCards);

        var before = vm.SessionCards.Count;
        vm.ToggleSplitCommand.Execute(null);
        await Task.Delay(500); // second session spawns asynchronously
        Assert.True(vm.IsSplit);
        Assert.Equal(before + 1, vm.SessionCards.Count);
        Assert.NotNull(vm.LeftPane);
        Assert.NotNull(vm.RightPane);
        Assert.NotSame(vm.LeftPane, vm.RightPane);

        // Repeated toggling reuses existing sessions — no leaks.
        var total = vm.SessionCards.Count;
        for (var i = 0; i < 3; i++)
        {
            vm.ToggleSplitCommand.Execute(null);
            Assert.False(vm.IsSplit);
            vm.ToggleSplitCommand.Execute(null);
            await Task.Delay(250);
            Assert.True(vm.IsSplit);
            Assert.Equal(total, vm.SessionCards.Count);
        }
        window.Close();
    }

    [AvaloniaFact]
    public async Task Split_RightSessionClosed_FallsBack()
    {
        var (window, vm) = await Boot();
        vm.ToggleSplitCommand.Execute(null);
        await Task.Delay(300);

        var right = vm.RightPane!;
        var rightCard = vm.SessionCards.First(c => ReferenceEquals(c.Model, right));
        vm.CloseSessionCommand.Execute(rightCard); // real close → SessionRemoved
        await Task.Delay(400);

        Assert.True(vm.IsSplit); // still split
        Assert.NotNull(vm.LeftPane);
        // Right pane repicks a different session or shows the empty hint.
        if (vm.RightPane is not null)
            Assert.NotSame(right, vm.RightPane);
        window.Close();
    }
}
