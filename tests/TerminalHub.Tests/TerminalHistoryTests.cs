using System.Reflection;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using TerminalHub.App.Controls;
using TerminalHub.Core.Terminal;
using Xunit;

namespace TerminalHub.Tests;

public class TerminalHistoryTests
{
    /// <summary>The view's measured cell metrics — set by the constructor's real
    /// glyph measurement; read-only, same pattern as CliInteractionTests.</summary>
    private static (double Width, double Height) CellSize(TerminalView view)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        return ((double)typeof(TerminalView).GetField("_cellW", flags)!.GetValue(view)!,
            (double)typeof(TerminalView).GetField("_cellH", flags)!.GetValue(view)!);
    }

    /// <summary>Drags a real pointer press/move/release across cells
    /// <paramref name="col0"/>..<paramref name="col1"/> of absolute buffer line
    /// <paramref name="line"/>, scrolling it into view first — the same path a
    /// user's drag takes, instead of injecting the selection fields.</summary>
    private static void DragSelect(TerminalView view, TerminalEmulator terminal, int line, int col0, int col1)
    {
        var buf = terminal.Buffer;
        var (cellW, cellH) = CellSize(view);
        int row, viewOffset;
        lock (buf.SyncRoot)
        {
            // SearchHit.Line is a current-coordinate line; bring it into the
            // viewport (bottom row when it fits, clamped otherwise).
            viewOffset = Math.Clamp(buf.ScrollbackCount + buf.Rows - 1 - line, 0, buf.ScrollbackCount);
            row = line - buf.ScrollbackCount + viewOffset;
        }
        view.ScrollToOffset(viewOffset);
        var y = (row + 0.5) * cellH;
        Point At(int col) => new((col + 0.5) * cellW, y);
        var pointer = new Avalonia.Input.Pointer(1, PointerType.Mouse, true);
        view.RaiseEvent(new PointerPressedEventArgs(view, pointer, view, At(col0), 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None, 1));
        view.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, view, pointer, view, At(col1), 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other),
            KeyModifiers.None));
        view.RaiseEvent(new PointerReleasedEventArgs(view, pointer, view, At(col1), 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonReleased),
            KeyModifiers.None, MouseButton.Left));
    }

    [AvaloniaFact]
    public void HistoryTrim_SurvivingSelectionAndSearchMarkerStillIdentifySameText()
    {
        using var terminal = new TerminalEmulator(columns: 10, rows: 3);
        var view = new TerminalView { Emulator = terminal, SearchQuery = "keep" };
        var buf = terminal.Buffer;
        typeof(ScreenBuffer).GetField("_scrollbackLimit", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(buf, 2);
        terminal.Parser.Feed(string.Concat(Enumerable.Range(0, 130).Select(i => $"L{i:D3}\r\n")) + "keep");
        var line = buf.SearchLines("keep").Single().Line;
        var result = buf.SearchLines("keep").Single();
        DragSelect(view, terminal, line, 0, 3);
        Assert.True(view.GoToMatch(1));
        terminal.Parser.Feed("\r\n1\r\n2\r\n3\r\n4");
        Assert.True(buf.RemovedLineCount > 0);
        // No render/timer tick: clipboard may be queried immediately after PTY output.
        Assert.Equal("keep", view.GetSelectedText());
        var marker = (int)typeof(TerminalView).GetField("_hitLine", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
        Assert.Equal(buf.SearchLines("keep").Single().Line, marker);
        Assert.True(view.RevealSearchHit(result));
    }

    [AvaloniaFact]
    public void RemovedSelection_ClearsInsteadOfCopyingReplacementContent()
    {
        using var terminal = new TerminalEmulator(columns: 10, rows: 3);
        var view = new TerminalView { Emulator = terminal };
        terminal.Parser.Feed("old!\r\nnew!\r\nmore\r\nlast");
        DragSelect(view, terminal, 0, 0, 3);
        Assert.Equal("old!", view.GetSelectedText());
        var result = terminal.Buffer.SearchLines("old!").Single();
        terminal.Buffer.ClearScrollback();
        Assert.Null(view.GetSelectedText());
        Assert.False(view.RevealSearchHit(result));
    }

    [AvaloniaFact]
    public void ReflowAndAlternateScreen_DoNotCopyStaleSelection()
    {
        using var terminal = new TerminalEmulator(columns: 10, rows: 3);
        var view = new TerminalView { Emulator = terminal };
        terminal.Parser.Feed("abcd");
        DragSelect(view, terminal, 0, 0, 3);
        terminal.Resize(6, 3);
        Assert.Null(view.GetSelectedText());
        DragSelect(view, terminal, 0, 0, 3);
        terminal.Parser.Feed("\x1b[?1049hnew!");
        Assert.Null(view.GetSelectedText());
    }
}
