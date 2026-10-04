using TerminalHub.Core.Terminal;
using Xunit;

namespace TerminalHub.Tests;

/// <summary>
/// Grapheme-width regressions for emoji skin-tone modifiers (U+1F3FB..U+1F3FF):
/// they are UAX #29 Extend and must join the base emoji cluster as zero-width
/// code points instead of taking two cells of their own.
/// </summary>
public class TerminalGraphemeWidthTests
{
    [Theory]
    [InlineData(0x1F3FB)]
    [InlineData(0x1F3FC)]
    [InlineData(0x1F3FD)]
    [InlineData(0x1F3FE)]
    [InlineData(0x1F3FF)]
    public void EmojiSkinToneModifier_IsZeroWidth(int rune)
        => Assert.Equal(0, GraphemeWidth.OfRune(rune));

    [Fact]
    public void EmojiWithSkinTone_ClusterAndTextWidthIsTwo()
    {
        Assert.Equal(2, GraphemeWidth.OfCluster("\U0001F44D\U0001F3FD"));   // 👍🏽
        Assert.Equal(2, GraphemeWidth.OfText("\U0001F44D\U0001F3FD"));
    }

    [Fact]
    public void EmojiWithSkinTone_OccupiesOneWideCell()
    {
        var b = new ScreenBuffer(10, 4);
        var p = new VtParser(b);
        p.Feed("\U0001F44D\U0001F3FDx");                    // 👍🏽 then 'x'
        Assert.Equal("\U0001F44D\U0001F3FD", b.CellAt(0, 0).Text);
        Assert.True(b.CellAt(0, 0).IsWide);
        Assert.True(b.CellAt(0, 1).IsWideContinuation);     // not a second glyph
        Assert.Equal('x', b.CellAt(0, 2).Char);
        Assert.Equal(3, b.CursorX);
    }

    [Fact]
    public void SkinToneModifier_WidensNarrowEmojiBase()
    {
        var b = new ScreenBuffer(10, 4);
        var p = new VtParser(b);
        p.Feed("\u270C\U0001F3FDx");                        // ✌🏽 then 'x'
        Assert.True(b.CellAt(0, 0).IsWide);                 // emoji presentation
        Assert.True(b.CellAt(0, 1).IsWideContinuation);
        Assert.Equal('x', b.CellAt(0, 2).Char);
        Assert.Equal(3, b.CursorX);
        Assert.Equal(2, GraphemeWidth.OfCluster("\u270C\U0001F3FD"));
    }

    [Fact]
    public void ZwjSequence_WithSkinTone_StaysOneWideCell()
    {
        var b = new ScreenBuffer(10, 4);
        var p = new VtParser(b);
        p.Feed("\U0001F469\U0001F3FD\u200D\U0001F4BBx");    // 👩🏽‍💻 then 'x'
        Assert.Equal("\U0001F469\U0001F3FD\u200D\U0001F4BB", b.CellAt(0, 0).Text);
        Assert.True(b.CellAt(0, 0).IsWide);
        Assert.Equal('x', b.CellAt(0, 2).Char);
        Assert.Equal(3, b.CursorX);
    }
}
