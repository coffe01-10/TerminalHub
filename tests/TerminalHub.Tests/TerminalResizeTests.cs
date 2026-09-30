using TerminalHub.Core.Terminal;
using Xunit;

namespace TerminalHub.Tests;

public class TerminalResizeTests
{
    [Fact]
    public void SavedCursor_ReflowRestoresLogicalInsertionPoint()
    {
        var b = new ScreenBuffer(16, 6);
        var p = new VtParser(b);
        p.Feed("abcdefghijkl\x1b[1;9H\u001b7\x1b[2;1Hother");
        b.Resize(6, 6);
        p.Feed("\u001b8!");
        Assert.Equal("abcdefgh!jkl\nother", Text(b));
    }
    [Fact]
    public void ReflowAndCopy_PreserveSpaceAtSoftWrapBoundary()
    {
        var b = new ScreenBuffer(4, 5);
        var p = new VtParser(b);
        p.Feed("abc def ghi");
        Assert.Equal("abc def ghi", Text(b));
        b.Resize(7, 5);
        Assert.Equal("abc def ghi", Text(b));
        b.Resize(4, 5);
        Assert.Equal("abc def ghi", Text(b));
    }
    private static string Text(ScreenBuffer buffer) => buffer.ExtractText(0, 0,
        buffer.TotalLines - 1, buffer.Columns - 1).TrimEnd('\n');

    [Fact]
    public void NarrowThenWiden_PreservesLogicalLinesAndCursor()
    {
        var b = new ScreenBuffer(20, 6);
        var p = new VtParser(b);
        p.Feed("first\r\nabcdefghijklmnop");
        b.Resize(7, 6);
        Assert.Equal("first\nabcdefghijklmnop", Text(b));
        Assert.Equal((2, 3), (b.CursorX, b.CursorY));
        b.Resize(20, 6);
        Assert.Equal("first\nabcdefghijklmnop", Text(b));
        Assert.Equal((16, 1), (b.CursorX, b.CursorY));
        p.Feed("!");
        Assert.Equal("abcdefghijklmnop!", b.RowText(1));
    }

    [Fact]
    public void Reflow_KeepsWideClustersAndAttributesAcrossHistory()
    {
        var b = new ScreenBuffer(12, 3);
        var p = new VtParser(b);
        p.Feed("\x1b[31mabc中文😀def\x1b[0m\r\nsecond\r\nthird\r\nfourth");
        var original = Text(b);
        b.Resize(4, 3);
        Assert.Equal(original, Text(b));
        for (var r = 0; r < b.TotalLines; r++)
        {
            var row = b.GetLine(r);
            for (var c = 0; c < row.Length; c++)
            {
                if (row[c].IsWide) Assert.True(c + 1 < row.Length && row[c + 1].IsWideContinuation);
                if (row[c].Char == '中') Assert.Equal(TerminalColor.Indexed(1), row[c].Fg);
            }
        }
        b.Resize(12, 3);
        Assert.Equal(original, Text(b));
    }

    [Fact]
    public void PendingWrap_ReflowKeepsInsertionAfterLastGlyph()
    {
        var b = new ScreenBuffer(8, 4);
        var p = new VtParser(b);
        p.Feed("abcdefgh");
        b.Resize(4, 4);
        p.Feed("i");
        Assert.Equal("abcdefghi", Text(b));
        b.Resize(12, 4);
        Assert.Equal((9, 0), (b.CursorX, b.CursorY));
    }

    [Fact]
    public void AlternateGrid_StaysFixed_PrimaryReflowsAndRestoresCursor()
    {
        var b = new ScreenBuffer(16, 6);
        var p = new VtParser(b);
        p.Feed("abcdefghijkl\x1b[?1049h\x1b[2J\x1b[1;6H中\x1b[2;3HUI");
        b.Resize(6, 6);
        Assert.Equal('U', b.CellAt(1, 2).Char);
        Assert.False(b.CellAt(0, 5).IsWide);
        Assert.Equal(0, b.ScrollbackCount);
        p.Feed("\x1b[?1049l");
        Assert.Equal("abcdefghijkl", Text(b));
        p.Feed("!");
        Assert.Equal("abcdefghijkl!", Text(b));
    }
}
