using TerminalHub.Core.Terminal;
using Xunit;

namespace TerminalHub.Tests;

public class TerminalCoreTests
{
    private static (VtParser parser, ScreenBuffer buf) Make(int cols = 10, int rows = 4)
    {
        var buf = new ScreenBuffer(cols, rows);
        return (new VtParser(buf), buf);
    }

    [Fact]
    public void PlainText_WritesLeftToRight()
    {
        var (p, b) = Make();
        p.Feed("hi");
        Assert.Equal('h', b.CellAt(0, 0).Char);
        Assert.Equal('i', b.CellAt(0, 1).Char);
        Assert.Equal(2, b.CursorX);
    }

    [Fact]
    public void NewlineAndCR_MoveCursor()
    {
        var (p, b) = Make();
        p.Feed("ab\r\ncd");
        Assert.Equal('a', b.CellAt(0, 0).Char);
        Assert.Equal('c', b.CellAt(1, 0).Char);
        Assert.Equal('d', b.CellAt(1, 1).Char);
    }

    [Fact]
    public void AutoWrap_ScrollsAtBottom()
    {
        var (p, b) = Make(cols: 5, rows: 2);
        p.Feed("line1\nline2\nline3\n");
        // scrolled twice; "line1" should be in scrollback
        Assert.True(b.ScrollbackCount >= 1);
        Assert.Equal("line1", new string(b.GetScrollbackRow(0).ToArray().Select(c => c.Char).ToArray()).TrimEnd());
    }

    [Fact]
    public void Sgr_SetsColorsAndReset()
    {
        var (p, b) = Make();
        p.Feed("\u001b[31mR\u001b[0mN");
        var red = b.CellAt(0, 0);
        var normal = b.CellAt(0, 1);
        Assert.Equal(TerminalColor.Indexed(1), red.Fg);
        Assert.Equal(TerminalColor.Default, normal.Fg);
    }

    [Fact]
    public void Sgr_256AndTruecolor()
    {
        var (p, b) = Make();
        p.Feed("\u001b[38;5;196mA\u001b[48;2;1;2;3mB");
        Assert.Equal(TerminalColor.Indexed(196), b.CellAt(0, 0).Fg);
        var cell = b.CellAt(0, 1);
        Assert.Equal(TerminalColor.Rgb(1, 2, 3), cell.Bg);
    }

    [Fact]
    public void Sgr_BrightColors()
    {
        var (p, b) = Make();
        p.Feed("\u001b[91mA");
        Assert.Equal(TerminalColor.Indexed(9), b.CellAt(0, 0).Fg);
    }

    [Fact]
    public void CursorPositioning_CUP()
    {
        var (p, b) = Make(10, 4);
        p.Feed("\u001b[2;3HX");
        Assert.Equal('X', b.CellAt(1, 2).Char);
        Assert.Equal(1, b.CursorY);
        Assert.Equal(3, b.CursorX);
    }

    [Fact]
    public void EraseInLine_ClearsToRight()
    {
        var (p, b) = Make();
        p.Feed("abcdef\u001b[3D\u001b[K");
        Assert.Equal('c', b.CellAt(0, 2).Char);
        Assert.Equal(' ', b.CellAt(0, 3).Char);
        Assert.Equal(' ', b.CellAt(0, 5).Char);
    }

    [Fact]
    public void EraseInDisplay2_ClearsAll()
    {
        var (p, b) = Make();
        p.Feed("abcdef\u001b[2J");
        Assert.Equal("".PadRight(0), b.RowText(0));
    }

    [Fact]
    public void AltScreen_SwitchesAndRestores()
    {
        var (p, b) = Make();
        p.Feed("primary");
        p.Feed("\u001b[?1049h");
        Assert.True(b.OnAlternateScreen);
        p.Feed("\u001b[Halt");
        Assert.Equal("alt", b.RowText(0));
        p.Feed("\u001b[?1049l");
        Assert.False(b.OnAlternateScreen);
        Assert.Equal("primary", b.RowText(0));
    }

    [Fact]
    public void ScrollRegion_ScrollsInsideOnly()
    {
        var (p, b) = Make(cols: 6, rows: 4);
        // rows: 1:"top" 2-3 region 4:"bottom"
        p.Feed("\u001b[1;1Htop");
        p.Feed("\u001b[4;1Hbot");
        p.Feed("\u001b[2;3r");            // region rows 2..3
        p.Feed("\u001b[3;1HXX\u001b[M");    // delete line at row3 -> scroll up within region
        Assert.Equal("top", b.RowText(0));
        Assert.Equal("bot", b.RowText(3));
    }

    [Fact]
    public void Utf8_Multibyte()
    {
        var (p, b) = Make();
        p.Feed("中文");
        Assert.Equal('中', b.CellAt(0, 0).Char);
        Assert.True(b.CellAt(0, 0).IsWide);
        Assert.True(b.CellAt(0, 1).IsWideContinuation);
        Assert.Equal('文', b.CellAt(0, 2).Char);
        Assert.Equal(4, b.CursorX);
    }

    [Fact]
    public void DecSpecial_DrawsBoxChars()
    {
        var (p, b) = Make();
        p.Feed("\u001b(0lqk\u001b(B");
        Assert.Equal('┌', b.CellAt(0, 0).Char);
        Assert.Equal('─', b.CellAt(0, 1).Char);
        Assert.Equal('┐', b.CellAt(0, 2).Char);
    }

    [Fact]
    public void Osc_SetsTitle()
    {
        var (p, b) = Make();
        string? title = null;
        b.TitleChanged += t => title = t;
        p.Feed("\u001b]2;my title\x07");
        Assert.Equal("my title", title);
    }

    [Fact]
    public void Osc_StTerminated()
    {
        var (p, b) = Make();
        p.Feed("\u001b]0;hello\u001b\\after");
        Assert.Equal("hello", b.Title);
        Assert.Contains("after", b.RowText(0));
    }

    [Fact]
    public void InsertDeleteChars()
    {
        var (p, b) = Make();
        p.Feed("abcd\u001b[1;1H\u001b[2@");   // insert 2 blanks at start
        Assert.Equal("  abcd", b.RowText(0).PadRight(6).Substring(0, 6));
        p.Feed("\u001b[2P");               // delete 2 chars
        Assert.Equal("abcd", b.RowText(0));
    }

    [Fact]
    public void InsertDeleteLines()
    {
        var (p, b) = Make(6, 4);
        p.Feed("\u001b[1;1Hone\u001b[2;1Htwo\u001b[3;1Hthree\u001b[4;1Hfour");
        p.Feed("\u001b[1;1H\u001b[L");       // insert blank line at row 1
        Assert.Equal("", b.RowText(0));
        Assert.Equal("one", b.RowText(1));
        Assert.Equal("two", b.RowText(2));
        Assert.Equal("three", b.RowText(3)); // "four" pushed out
        p.Feed("\u001b[1;1H\u001b[M");       // delete line
        Assert.Equal("one", b.RowText(0));
    }

    [Fact]
    public void SaveRestoreCursor()
    {
        var (p, b) = Make();
        p.Feed("ab\u001b7cd\u001b8X");  // save, write cd, restore, write X over c
        Assert.Equal('X', b.CellAt(0, 2).Char);
    }

    [Fact]
    public void DeviceAttributes_Responds()
    {
        var sent = new List<byte>();
        var buf = new ScreenBuffer(10, 4);
        var p = new VtParser(buf, sent.AddRange);
        p.Feed("\u001b[c");
        Assert.NotEmpty(sent);
    }

    [Fact]
    public void CursorReport_RespondsPosition()
    {
        var sent = new List<byte>();
        var buf = new ScreenBuffer(10, 4);
        var p = new VtParser(buf, sent.AddRange);
        p.Feed("\u001b[2;3H\u001b[6n");
        var s = System.Text.Encoding.ASCII.GetString(sent.ToArray());
        Assert.Equal("\u001b[2;3R", s);
    }

    [Fact]
    public void Resize_KeepsContent()
    {
        var (p, b) = Make(10, 4);
        p.Feed("hello");
        b.Resize(20, 8);
        Assert.Equal(20, b.Columns);
        Assert.Equal('h', b.CellAt(0, 0).Char);
    }

    [Fact]
    public void PendingWrap_DoesNotAdvanceUntilNextChar()
    {
        var (p, b) = Make(cols: 5, rows: 3);
        p.Feed("12345");
        Assert.Equal(4, b.CursorX); // stays on last cell
        p.Feed("6");
        Assert.Equal(1, b.CursorY); // wrapped now
        Assert.Equal(0, b.CursorX - 1); // after writing '6' cursor at col 1
        Assert.Equal('6', b.CellAt(1, 0).Char);
    }

    [Fact]
    public void Backspace_RespectsLeftEdge()
    {
        var (p, b) = Make();
        p.Feed("ab\b");
        Assert.Equal(1, b.CursorX);
        p.Feed("\b\b\b");
        Assert.Equal(0, b.CursorX);
    }

    [Fact]
    public void OriginMode_UsesRegionTop()
    {
        var (p, b) = Make(10, 5);
        p.Feed("\u001b[2;4r\u001b[?6h\u001b[1;1HX");
        Assert.Equal('X', b.CellAt(1, 0).Char); // row 1 = region top (0-indexed row 2)
    }

    [Fact]
    public void Hidden_AndInverse_Attrs()
    {
        var (p, b) = Make();
        p.Feed("\u001b[7mA\u001b[8mB");
        Assert.True(b.CellAt(0, 0).Attrs.HasFlag(CellAttrs.Inverse));
        Assert.True(b.CellAt(0, 1).Attrs.HasFlag(CellAttrs.Hidden));
    }

    [Fact]
    public void TailText_ReturnsLastRows()
    {
        var (p, b) = Make(20, 4);
        p.Feed("\u001b[1;1Haaa\u001b[4;1Hzzz");
        var tail = b.TailText(2);
        Assert.Contains("zzz", tail);
    }

    [Fact]
    public void TailLines_ColoredRows_GetDominantFg()
    {
        var (p, b) = Make(20, 4);
        p.Feed("\u001b[1;1H\u001b[31mred-line\u001b[0m \u001b[4;1Hplain");
        var lines = b.TailLines(4);
        var red = lines.First(l => l.Text.Contains("red-line"));
        Assert.Equal("#CD0000", red.FgHex);
        var plain = lines.First(l => l.Text.Contains("plain"));
        Assert.Null(plain.FgHex);
    }

    [Fact]
    public void TailLines_RgbAndIndexed_Colors()
    {
        var (p, b) = Make(20, 4);
        // SGR 38;5;196 = indexed bright red; 38;2 = truecolor
        p.Feed("\u001b[1;1H\u001b[38;5;196mi196 \u001b[4;1H\u001b[38;2;16;32;200mrgb");
        var lines = b.TailLines(4);
        Assert.Equal("#FF0000", lines.First(l => l.Text.Contains("i196")).FgHex);
        Assert.Equal("#1020C8", lines.First(l => l.Text.Contains("rgb")).FgHex);
    }

    [Fact]
    public void TerminalColor_ToRgbHex()
    {
        Assert.Null(TerminalColor.Default.ToRgbHex());
        Assert.Equal("#FF0000", TerminalColor.Indexed(196).ToRgbHex());
        Assert.Equal("#080808", TerminalColor.Indexed(232).ToRgbHex());
        Assert.Equal("#010203", TerminalColor.Rgb(1, 2, 3).ToRgbHex());
    }
}
