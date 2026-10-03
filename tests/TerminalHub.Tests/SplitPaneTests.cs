using Avalonia.Headless.XUnit;
using TerminalHub.App.Views;
using TerminalHub.Pty;
using Xunit;

namespace TerminalHub.Tests;

/// <summary>Split-pane: two independent sessions side-by-side, no input
/// cross-talk, clean toggling and graceful session-removal handling.</summary>
public class SplitPaneTests
{
    private static async Task Until(Func<bool> condition, string? message = null)
    {
        var deadline = Environment.TickCount64 + 5000;
        while (!condition() && Environment.TickCount64 < deadline)
            await Task.Delay(20);
        Assert.True(condition(), message ?? "Timed out waiting for the split-pane state.");
    }

    private static async Task<(MainWindow Window, TerminalHub.App.ViewModels.MainWindowViewModel Vm)> Boot()
    {
        PtySessionFactory.UseMock = true;
        var window = new MainWindow { Width = 1440, Height = 900 };
        window.Show();
        var vm = (TerminalHub.App.ViewModels.MainWindowViewModel)window.DataContext!;
        await Until(() => vm.SessionCards.Count >= 3, "startup sessions never spawned");
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
        var (window, vm) = await Boot();

        Assert.False(vm.IsSplit);
        vm.ToggleSplitCommand.Execute(null);
        await Until(() => vm.IsSplit, "split never activated");

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

    /// <summary>Regression: exiting split must resize the shared emulator back to
    /// the main view's dimensions — a view-side "last size" cache once left the
    /// emulator/PTY stuck at the pane's narrower width.</summary>
    [AvaloniaFact]
    public async Task Split_Exit_RestoresEmulatorDimensions()
    {
        var (window, vm) = await Boot();
        var emulator = vm.ActiveSession!.Emulator;
        var fullCols = emulator.Buffer.Columns;
        var fullRows = emulator.Buffer.Rows;
        Assert.True(fullCols > 80);

        vm.ToggleSplitCommand.Execute(null);
        await Until(() => vm.IsSplit, "split never activated");
        Assert.Same(emulator, vm.LeftPane!.Emulator); // left pane reuses the active session
        await Until(() => emulator.Buffer.Columns < fullCols,
            $"left pane should have shrunk the grid below {fullCols}, got {emulator.Buffer.Columns}");

        vm.ToggleSplitCommand.Execute(null);
        await Until(() => !vm.IsSplit && emulator.Buffer.Columns == fullCols
            && emulator.Buffer.Rows == fullRows, "exiting split never restored the emulator size");
        window.Close();
    }

    [AvaloniaFact]
    public async Task Split_Input_NoCrossTalk()
    {
        var (window, vm) = await Boot();
        vm.ToggleSplitCommand.Execute(null);
        await Until(() => vm.IsSplit, "split never activated");
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
        await Until(() => vm.IsSplit, "split never activated");

        Assert.Same(vm.LeftPane, vm.ActiveSession); // split opened focused on left

        vm.FocusPane(1);
        await Until(() => ReferenceEquals(vm.ActiveSession, vm.RightPane), "right pane never activated");
        Assert.Equal(1, vm.FocusedPane);

        vm.FocusPane(0);
        await Until(() => ReferenceEquals(vm.ActiveSession, vm.LeftPane), "left pane never reactivated");
        window.Close();
    }

    [AvaloniaFact]
    public async Task Split_CardClick_AssignsToFocusedPane()
    {
        var (window, vm) = await Boot();
        vm.ToggleSplitCommand.Execute(null);
        await Until(() => vm.IsSplit, "split never activated");

        // Focus right pane, click a different card → right pane retargets.
        vm.FocusPane(1);
        var other = vm.SessionCards.First(c =>
            !ReferenceEquals(c.Model, vm.LeftPane) && !ReferenceEquals(c.Model, vm.RightPane));
        vm.ActiveCard = other;
        await Until(() => ReferenceEquals(vm.RightPane, other.Model), "right pane never retargeted to the clicked card");
        Assert.Same(other.Model, vm.ActiveSession);

        // Focus left, click another card → left pane retargets, right keeps its own.
        vm.FocusPane(0);
        var third = vm.SessionCards.First(c =>
            !ReferenceEquals(c.Model, vm.RightPane) && !ReferenceEquals(c.Model, vm.LeftPane));
        vm.ActiveCard = third;
        await Until(() => ReferenceEquals(vm.LeftPane, third.Model), "left pane never retargeted to the clicked card");
        Assert.Same(other.Model, vm.RightPane);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Split_SingleSession_SpawnsSecond_RepeatToggle_NoLeak()
    {
        PtySessionFactory.UseMock = true;
        var window = new MainWindow { Width = 1200, Height = 800 };
        window.Show();
        var vm = (TerminalHub.App.ViewModels.MainWindowViewModel)window.DataContext!;
        await Until(() => vm.SessionCards.Count >= 3, "startup sessions never spawned");

        // Close down to exactly one session via the real close path.
        while (vm.SessionCards.Count > 1)
        {
            var closing = vm.SessionCards.Last();
            vm.CloseSessionCommand.Execute(closing);
            await Until(() => !vm.SessionCards.Contains(closing), "session never closed");
        }
        Assert.Single(vm.SessionCards);

        var before = vm.SessionCards.Count;
        vm.ToggleSplitCommand.Execute(null);
        await Until(() => vm.IsSplit, "split never activated"); // second session spawns asynchronously
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
            await Until(() => vm.IsSplit, "re-entering split never completed");
            Assert.Equal(total, vm.SessionCards.Count);
        }
        window.Close();
    }

    [AvaloniaFact]
    public async Task Split_RightSessionClosed_FallsBack()
    {
        var (window, vm) = await Boot();
        vm.ToggleSplitCommand.Execute(null);
        await Until(() => vm.IsSplit, "split never activated");

        var right = vm.RightPane!;
        var rightCard = vm.SessionCards.First(c => ReferenceEquals(c.Model, right));
        vm.CloseSessionCommand.Execute(rightCard); // real close → SessionRemoved
        await Until(() => !ReferenceEquals(vm.RightPane, right), "right pane never fell back");

        Assert.True(vm.IsSplit); // still split
        Assert.NotNull(vm.LeftPane);
        // Right pane repicks a different session or shows the empty hint.
        if (vm.RightPane is not null)
            Assert.NotSame(right, vm.RightPane);
        window.Close();
    }

    /// <summary>Regression: closing the LEFT pane's session must fall back to a
    /// different session — never the one the right pane is already showing.</summary>
    [AvaloniaFact]
    public async Task Split_LeftSessionClosed_FallsToNeighbor_NotRightPaneSession()
    {
        var (window, vm) = await Boot();
        vm.ToggleSplitCommand.Execute(null);
        await Until(() => vm.IsSplit, "split never activated");
        Assert.True(vm.SessionCards.Count >= 3); // needs a session besides both panes

        var left = vm.LeftPane!;
        var right = vm.RightPane!;
        var leftCard = vm.SessionCards.First(c => ReferenceEquals(c.Model, left));
        vm.CloseSessionCommand.Execute(leftCard);
        await Until(() => vm.LeftPane is not null && !ReferenceEquals(vm.LeftPane, left),
            "left pane never fell back");

        Assert.True(vm.IsSplit);
        Assert.NotSame(right, vm.LeftPane); // panes must not collapse onto one session
        window.Close();
    }

    [AvaloniaFact]
    public async Task Split_CloseFocusedSide_WhenNoOtherSession_ExitsSplit()
    {
        var (window, vm) = await Boot();
        while (vm.SessionCards.Count > 2)
        {
            var extra = vm.SessionCards.First(c => !ReferenceEquals(c.Model, vm.ActiveSession));
            vm.CloseSessionCommand.Execute(extra);
            await Until(() => !vm.SessionCards.Contains(extra), "extra session never closed");
        }
        vm.ToggleSplitCommand.Execute(null);
        await Until(() => vm.IsSplit && vm.SessionCards.Count == 2, "split with two sessions never came up");
        Assert.NotSame(vm.LeftPane, vm.RightPane);

        vm.FocusPane(1);
        var right = vm.RightPane;
        var rightCard = vm.SessionCards.First(c => ReferenceEquals(c.Model, right));
        vm.CloseSessionCommand.Execute(rightCard);
        await Until(() => !vm.IsSplit && vm.SessionCards.Count == 1, "closing the last focused side never exited split");

        Assert.Null(vm.LeftPane);
        Assert.Null(vm.RightPane);
        Assert.NotSame(right, vm.ActiveSession);
        window.Close();
    }
}
