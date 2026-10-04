using TerminalHub.Core.Terminal;
using Xunit;

namespace TerminalHub.Tests;

/// <summary>
/// Regressions for the 2026-10 core parsing fixes: zero-width runes anchor to
/// the glyph left of the cursor after repositioning, ED mode 2 erases with the
/// current background (bce), ESC ESC restarts the escape sequence, C0 controls
/// execute inside a CSI sequence without discarding it, and only full-screen
/// scrolls feed scrollback history.
/// </summary>
public class TerminalVtParsingRegressions
{
    private static (VtParser parser, ScreenBuffer buf) Make(int cols = 10, int rows = 4)
    {
        var buf = new ScreenBuffer(cols, rows);
        return (new VtParser(buf), buf);
    }

    // ---- zero-width anchor follows cursor repositioning ----

    [Fact]
    public void CombiningMarkAfterCup_JoinsGlyphLeftOfCursor()
    {
        var (p, b) = Make(20, 4);
        p.Feed("abc");
        p.Feed("\u001b[1;3H");                  // cursor right after 'b'
        p.Feed("\u0301");                       // combining acute
        Assert.Equal("b\u0301", b.CellAt(0, 1).Text);
        Assert.Equal("a", b.CellAt(0, 0).Text);
        Assert.Equal("c", b.CellAt(0, 2).Text);
    }

    [Fact]
    public void CombiningMarkAfterCursorUp_JoinsGlyphOnCursorRow()
    {
        var (p, b) = Make(20, 4);
        p.Feed("ab\r\ncd");
        p.Feed("\u001b[A");                     // up to row 0, after 'b'
        p.Feed("\u0301");
        Assert.Equal("b\u0301", b.CellAt(0, 1).Text);
        Assert.Equal("d", b.CellAt(1, 1).Text); // 'd' untouched — no stale anchor
    }

    [Fact]
    public void Vs16AfterCup_JoinsGlyphLeftOfCursor()
    {
        var (p, b) = Make(20, 4);
        p.Feed("\u2708x");                      // ✈ then 'x'
        p.Feed("\u001b[1;2H");                  // cursor right after ✈
        p.Feed("\uFE0F");
        Assert.Equal("\u2708\uFE0F", b.CellAt(0, 0).Text);
        Assert.Equal("x", b.CellAt(0, 1).Text);
    }

    [Fact]
    public void CombiningMarkAfterTab_NoBaseLeftOfCursor_Standalone()
    {
        var (p, b) = Make(20, 4);
        p.Feed("a\t\u0301");                    // tab jumps to column 8
        Assert.Equal("a", b.CellAt(0, 0).Text); // 'a' no longer absorbs the mark
        Assert.Equal("\u0301", b.CellAt(0, 8).Text);
    }

    [Fact]
    public void CombiningMarkAfterCr_OnColumnZero_Standalone()
    {
        var (p, b) = Make(20, 4);
        p.Feed("ab\r\u0301");                   // cursor back at column 0 — no base
        Assert.Equal("\u0301", b.CellAt(0, 0).Text);
        Assert.Equal("b", b.CellAt(0, 1).Text);
    }

    [Theory]
    [InlineData("abcd", 3, "d")]
    [InlineData("ab\u4e2d", 2, "\u4e2d")]
    public void CombiningMarkAfterRestoreAtRightMargin_JoinsLastGlyph(string text, int column, string glyph)
    {
        var (p, b) = Make(4, 3);
        p.Feed(text);
        b.SaveCursor();
        p.Feed("\r\nother");
        b.RestoreCursor();
        p.Feed("\u0301");

        Assert.Equal(glyph + "\u0301", b.CellAt(0, column).Text);
        Assert.Equal("b", b.CellAt(0, 1).Text);
        Assert.Equal(3, b.CursorX);
        p.Feed("x");
        Assert.Equal("x", b.CellAt(1, 0).Text);
    }

    // ---- ED mode 2 erases with the current background ----

    [Fact]
    public void EraseInDisplay2_UsesCurrentBackground()
    {
        var (p, b) = Make(10, 4);
        p.Feed("xyz");
        p.Feed("\u001b[41m");                   // red background
        p.Feed("\u001b[2J");
        Assert.Equal(TerminalColor.Indexed(1), b.CellAt(0, 0).Bg);
        Assert.Equal(TerminalColor.Indexed(1), b.CellAt(3, 9).Bg);
        Assert.Equal(' ', b.CellAt(1, 4).Char);
    }

    // ---- ESC inside the escape state restarts the sequence ----

    [Fact]
    public void EscEscBackslash_TerminatesWithoutPrinting()
    {
        var (p, b) = Make(20, 4);
        p.Feed("ok");
        p.Feed("\u001b\u001b\\");               // tmux-style ST passthrough
        Assert.Equal("ok", b.RowText(0).TrimEnd()); // no stray backslash
    }

    [Fact]
    public void SecondEsc_RestartsEscapeSequence()
    {
        var (p, b) = Make(20, 4);
        p.Feed("ok");
        p.Feed("\u001b\u001b[2J");              // ESC ESC [ 2 J — still one CSI
        Assert.Equal("", b.RowText(0).TrimEnd());
    }

    // ---- C0 controls execute inside a CSI sequence ----

    [Fact]
    public void LineFeedInsideCsi_ExecutesAndSequenceContinues()
    {
        var (p, b) = Make(10, 4);
        p.Feed("abc");
        p.Feed("\u001b[2\nJ");                  // LF executes mid-params, ED2 lands
        Assert.Equal("", b.RowText(0).TrimEnd());
        Assert.Equal(1, b.CursorY);             // LF moved the cursor down
    }

    [Fact]
    public void BackspaceInsideCsi_ExecutesWithoutDiscardingSequence()
    {
        var (p, b) = Make(10, 4);
        p.Feed("abc");                          // cursor at column 3
        p.Feed("\u001b[\bD");                   // BS executes, then CUB 1
        Assert.Equal(1, b.CursorX);
        Assert.Equal(' ', b.CellAt(0, 3).Char); // 'D' was not printed as text
    }

    // ---- region scrolls never feed history; full-screen scrolls do ----

    [Fact]
    public void RegionScroll_DoesNotAllocateHistory_FullScreenScrollDoes()
    {
        var (p, b) = Make(10, 4);
        p.Feed("aaa\r\nbbb\r\nccc\r\nddd");
        Assert.Equal(0, b.ScrollbackCount);
        b.ScrollUpRegion(1, 2, 1);              // inner region scroll
        Assert.Equal(0, b.ScrollbackCount);
        Assert.Equal("aaa", b.RowText(0).TrimEnd());
        Assert.Equal("ccc", b.RowText(1).TrimEnd());
        Assert.Equal("", b.RowText(2).TrimEnd());
        Assert.Equal("ddd", b.RowText(3).TrimEnd());
        b.ScrollUpRegion(0, 3, 1);              // full-screen scroll
        Assert.Equal(1, b.ScrollbackCount);
        Assert.Equal("aaa", b.ScrollbackText(0).TrimEnd());
    }
}
