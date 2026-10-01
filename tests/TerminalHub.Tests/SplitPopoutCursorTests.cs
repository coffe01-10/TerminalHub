using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TerminalHub.App.Controls;
using TerminalHub.App.Views;
using TerminalHub.Core.Terminal;
using TerminalHub.Pty;
using Xunit;

namespace TerminalHub.Tests;

/// <summary>Split-pane and popout/reattach must not drift the cursor off the
/// logical end of pending input, and must not carry selection coordinates
/// onto a buffer they no longer describe. All flows run through the real
/// view-model commands so view-driven resizes actually reflow the buffer.</summary>
public class SplitPopoutCursorTests
{
    private static async Task<(MainWindow Window, TerminalHub.App.ViewModels.MainWindowViewModel Vm)> Boot()
    {
        PtySessionFactory.UseMock = true;
        var window = new MainWindow { Width = 1440, Height = 900 };
        window.Show();
        var vm = (TerminalHub.App.ViewModels.MainWindowViewModel)window.DataContext!;
        var deadline = Environment.TickCount64 + 5000;
        while (vm.SessionCards.Count < 3 && Environment.TickCount64 < deadline)
            await Task.Delay(20);
        Assert.True(vm.SessionCards.Count >= 3);
        return (window, vm);
    }

    /// <summary>The cursor sits exactly one cell past the last char of
    /// <paramref name="tail"/> on its own screen row — independent of which
    /// row/col reflow put it there. Tail must be short enough never to wrap.</summary>
    private static void AssertCursorAfter(TerminalEmulator emu, string tail)
    {
        var buf = emu.Buffer;
        lock (buf.SyncRoot)
        {
            var row = buf.RowText(buf.CursorY);
            var idx = row.LastIndexOf(tail, StringComparison.Ordinal);
            Assert.True(idx >= 0,
                $"tail {tail} not on cursor row {buf.CursorY} ('{row}'); tail text: {buf.TailText(8)}");
            Assert.Equal(idx + tail.Length, buf.CursorX);
        }
    }

    private static void Set(TerminalView view, string name, object value) =>
        typeof(TerminalView).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(view, value);

    /// <summary>Select exactly the cells of <paramref name="token"/>.</summary>
    private static void SelectToken(TerminalView view, ScreenBuffer buf, string token)
    {
        int line, col;
        lock (buf.SyncRoot)
        {
            var hit = buf.SearchLines(token).Last();
            line = hit.Line;
            col = hit.Text.IndexOf(token, StringComparison.Ordinal);
        }
        Set(view, "_selAnchor", (line, col));
        Set(view, "_selEnd", (line, col + token.Length));
    }

    [AvaloniaFact]
    public async Task Split_EnterExit_ReflowKeepsCursorAfterInputTail()
    {
        var (window, vm) = await Boot();
        var emu = vm.ActiveSession!.Emulator;
        emu.Parser.Feed("earlier output\r\n$ edit SPLIT_TAIL7");
        AssertCursorAfter(emu, "SPLIT_TAIL7");
        var fullCols = emu.Buffer.Columns;

        vm.ToggleSplitCommand.Execute(null);
        await Task.Delay(500);
        Assert.True(vm.IsSplit);
        Assert.Same(emu, vm.LeftPane!.Emulator);
        Assert.True(emu.Buffer.Columns < fullCols); // reflow really happened
        AssertCursorAfter(emu, "SPLIT_TAIL7");      // cursor not left mid-line

        vm.ToggleSplitCommand.Execute(null);
        await Task.Delay(500);
        Assert.False(vm.IsSplit);
        Assert.Equal(fullCols, emu.Buffer.Columns);
        AssertCursorAfter(emu, "SPLIT_TAIL7");
        window.Close();
    }

    [AvaloniaFact]
    public async Task Split_FocusSwitch_OtherPaneSelectionAndCursorHold()
    {
        var (window, vm) = await Boot();
        var leftEmu = vm.ActiveSession!.Emulator;
        leftEmu.Parser.Feed("$ pick SELWORDS_42 please\r\n$ ");
        vm.ToggleSplitCommand.Execute(null);
        await Task.Delay(500);

        var leftView = window.FindControl<TerminalView>("LeftTerminal")!;
        Assert.Same(leftEmu, leftView.Emulator);
        SelectToken(leftView, leftEmu.Buffer, "SELWORDS_42");
        Assert.Equal("SELWORDS_42", leftView.GetSelectedText());
        int cx, cy;
        lock (leftEmu.Buffer.SyncRoot) { cx = leftEmu.Buffer.CursorX; cy = leftEmu.Buffer.CursorY; }

        // Work happens in the right pane while focus sits there.
        vm.FocusPane(1);
        Assert.Same(leftEmu, vm.LeftPane!.Emulator); // pane sessions don't swap
        vm.RightPane!.Emulator.Parser.Feed("$ echo RIGHT_BUSY_9\r\nRIGHT_BUSY_9\r\n$ ");

        Assert.Equal("SELWORDS_42", leftView.GetSelectedText());
        lock (leftEmu.Buffer.SyncRoot)
        {
            Assert.Equal(cx, leftEmu.Buffer.CursorX);
            Assert.Equal(cy, leftEmu.Buffer.CursorY);
            Assert.DoesNotContain("RIGHT_BUSY_9", leftEmu.Buffer.TailText(10));
        }

        // Focus returns — nothing rebinds, selection still describes same text.
        vm.FocusPane(0);
        Assert.Same(leftEmu, leftView.Emulator);
        Assert.Equal("SELWORDS_42", leftView.GetSelectedText());
        window.Close();
    }

    [AvaloniaFact]
    public async Task Popout_Reattach_CursorHolds_NoStaleSelection()
    {
        var (window, vm) = await Boot();
        vm.InspectorVisible = false; // main view must stay wider than the popout
        await Task.Delay(400);
        var target = vm.ActiveSession!;
        var emu = target.Emulator;
        emu.Parser.Feed("$ run POP_MARKER42\r\n$ POP_ED1T");
        AssertCursorAfter(emu, "POP_ED1T");

        var mainView = window.FindControl<TerminalView>("MainTerminal")!;
        SelectToken(mainView, emu.Buffer, "POP_MARKER42");
        Assert.Equal("POP_MARKER42", mainView.GetSelectedText());
        var colsBefore = emu.Buffer.Columns;

        vm.OpenInNewWindowCommand.Execute(null);
        await Task.Delay(500);
        var pop = Assert.Single(vm.Popouts);
        Assert.Same(emu, pop.Terminal.Emulator);
        pop.UpdateLayout();
        await Task.Delay(300);
        // Popout view is narrower → emulator reflowed; cursor still at input end.
        Assert.True(emu.Buffer.Columns < colsBefore,
            $"popout should reflow below {colsBefore} cols, got {emu.Buffer.Columns} (pop bounds {pop.Terminal.Bounds})");
        AssertCursorAfter(emu, "POP_ED1T");
        Assert.Null(pop.Terminal.GetSelectedText()); // fresh view, nothing copied
        Assert.Null(mainView.GetSelectedText());     // rebound to another session

        pop.Close();
        await Task.Delay(500);

        Assert.Empty(vm.Popouts);
        Assert.Same(target, vm.ActiveSession);        // session reclaimed
        Assert.Equal(colsBefore, emu.Buffer.Columns); // main view sized it back
        AssertCursorAfter(emu, "POP_ED1T");
        Assert.Null(mainView.GetSelectedText());      // rebind cleared, not restored
        window.Close();
    }
}
