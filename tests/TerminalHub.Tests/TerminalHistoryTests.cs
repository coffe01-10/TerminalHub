using System.Reflection;
using Avalonia.Headless.XUnit;
using TerminalHub.App.Controls;
using TerminalHub.Core.Terminal;
using Xunit;

namespace TerminalHub.Tests;

public class TerminalHistoryTests
{
    private static void Set(TerminalView view, string name, object value) =>
        typeof(TerminalView).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(view, value);

    private static void Select(TerminalView view, int line)
    {
        Set(view, "_selAnchor", (line, 0));
        Set(view, "_selEnd", (line, 3));
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
        Select(view, line);
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
        Select(view, 0);
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
        Select(view, 0);
        terminal.Resize(6, 3);
        Assert.Null(view.GetSelectedText());
        Select(view, 0);
        terminal.Parser.Feed("\x1b[?1049hnew!");
        Assert.Null(view.GetSelectedText());
    }
}
