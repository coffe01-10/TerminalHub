using System.Reflection;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Media;
using TerminalHub.App.Controls;
using TerminalHub.Core.Terminal;
using Xunit;

namespace TerminalHub.Tests;

public class TerminalImeTests
{
    [AvaloniaFact]
    public async Task CandidateEnter_DoesNotSubmitShell_AndSwitchClearsComposition()
    {
        using var emulator = new TerminalEmulator();
        using var other = new TerminalEmulator();
        var pty = (TerminalHub.Core.Pty.MockPtySession)emulator.Pty;
        await emulator.StartAsync(new TerminalHub.Core.Pty.PtyOptions { Shell = "mock" });
        var view = new TerminalView { Emulator = emulator };
        var client = Client(view);
        client.SetPreeditText("ni", 2);
        var enter = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter };
        view.RaiseEvent(enter);
        Assert.True(enter.Handled);
        Assert.Empty(pty.RawInput.ToString());
        view.Emulator = other;
        Assert.Equal(0, client.CursorRectangle.X, 6);
        Assert.Null(typeof(TerminalView).GetField("_preedit", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view));
    }

    [AvaloniaFact]
    public void TwoViews_UseTheSameCell_WhenFontSizesDiffer()
    {
        using var emulator = new TerminalEmulator(columns: 40, rows: 8);
        var main = new TerminalView { Emulator = emulator, TerminalFontSize = 13 };
        var other = new TerminalView { Emulator = emulator, TerminalFontSize = 20 };
        emulator.Parser.Feed("ab中文cd");
        var mainColumn = Client(main).CursorRectangle.X / CellWidth(main);
        var otherColumn = Client(other).CursorRectangle.X / CellWidth(other);
        Assert.Equal(emulator.Buffer.CursorX, mainColumn, 3);
        Assert.Equal(emulator.Buffer.CursorX, otherColumn, 3);
    }

    [AvaloniaFact]
    public void VisibleCursor_ReflowAndFontChange_UpdatesImeBeforeRender()
    {
        using var emulator = new TerminalEmulator(columns: 20, rows: 6);
        var view = new TerminalView { Emulator = emulator };
        emulator.Parser.Feed("ab中文cdefghijkl");
        emulator.Resize(8, 6);
        view.TerminalFontSize = 18;
        var rect = Client(view).CursorRectangle;
        Assert.Equal(emulator.Buffer.CursorX * CellWidth(view), rect.X, 6);
        Assert.Equal(emulator.Buffer.CursorY * rect.Height, rect.Y, 6);
        Draw(view);
        Assert.Equal(rect, Client(view).CursorRectangle);
    }
    private static TextInputMethodClient Client(TerminalView view) =>
        (TextInputMethodClient)typeof(TerminalView).GetField("_imeClient",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;

    private static double CellWidth(TerminalView view) =>
        (double)typeof(TerminalView).GetField("_cellW",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;

    private static void Draw(TerminalView view)
    {
        using var context = new DrawingGroup().Open();
        view.Render(context);
    }

    private static void ClaudeFrame(TerminalEmulator emulator, string input)
    {
        emulator.Parser.Feed("\x1b[2J\x1b[?25l\x1b[6;1H" + new string('─', 60)
            + "\x1b[7;1H❯ " + input + "\x1b[8;1H" + new string('─', 60)
            + "\x1b[9;1HC-- INSERT -- manual mode on · for agents");
    }

    [AvaloniaTheory]
    [InlineData("")]
    [InlineData("中")]
    [InlineData("nd大大大哒哒哒顺丰v")]
    public void HiddenCursor_CompositionStaysInPrompt_WithoutCommittedTrail(string input)
    {
        using var emulator = new TerminalEmulator(columns: 60, rows: 10);
        var view = new TerminalView { Emulator = emulator };
        ClaudeFrame(emulator, input);
        // Query before rendering as Windows can do when composition starts.
        var client = Client(view);
        var cellW = CellWidth(view);
        var anchor = client.CursorRectangle;
        Assert.Equal((2 + input.Sum(ScreenBuffer.CharWidth)) * cellW, anchor.X, 6);
        Assert.Equal(6 * anchor.Height, anchor.Y, 6);
        client.SetPreeditText("n'd", 3);
        var composing = client.CursorRectangle;
        Assert.Equal(anchor.X + 3 * cellW, composing.X, 6);
        Assert.Equal(anchor.Y, composing.Y);
        Draw(view);
        Assert.Equal(composing, client.CursorRectangle);
    }

    [AvaloniaFact]
    public void VisibleCursor_AfterEditing_DoesNotJumpToEchoedTextEnd()
    {
        using var emulator = new TerminalEmulator(columns: 60, rows: 10);
        var view = new TerminalView { Emulator = emulator };
        view.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "hello" });
        emulator.Parser.Feed("\x1b[3;1Hhello\x1b[3;3H");
        Draw(view);
        var rect = Client(view).CursorRectangle;
        Assert.Equal(2 * CellWidth(view), rect.X, 6);
        Assert.Equal(2 * rect.Height, rect.Y, 6);
    }

    [AvaloniaFact]
    public void StartingCompositionAfterOutput_UsesCurrentFrameBeforeRender()
    {
        using var emulator = new TerminalEmulator(columns: 60, rows: 10);
        var view = new TerminalView { Emulator = emulator };
        Draw(view);
        emulator.Parser.Feed("\x1b[4;8H");
        var client = Client(view);
        client.SetPreeditText("ni", 1);
        var rect = client.CursorRectangle;
        Assert.Equal(8 * CellWidth(view), rect.X, 6);
        Assert.Equal(3 * rect.Height, rect.Y, 6);
    }

    [AvaloniaFact]
    public void UnborderedOutputPrompt_DoesNotStealHiddenCursor()
    {
        using var emulator = new TerminalEmulator(columns: 60, rows: 10);
        var view = new TerminalView { Emulator = emulator };
        emulator.Parser.Feed("❯ old output\x1b[?25l\x1b[5;1Hcurrent");
        var rect = Client(view).CursorRectangle;
        Assert.Equal(7 * CellWidth(view), rect.X, 6);
        Assert.Equal(4 * rect.Height, rect.Y, 6);
    }

    [AvaloniaFact]
    public void ClaudeSoftwareCursor_MovesAcrossCjk_AndImeFollowsIt()
    {
        using var emulator = new TerminalEmulator(columns: 60, rows: 10);
        var view = new TerminalView { Emulator = emulator };
        var client = Client(view);
        // Replay the reverse-video cursor emitted by Claude Code via ConPTY:
        // end -> Left x3 (文) -> Right (c) -> Home -> End.
        foreach (var (text, col) in new[]
        {
            ("ab中文cd\x1b[7m \x1b[27m", 10),
            ("ab中\x1b[7m文\x1b[27mcd", 6),
            ("ab中文\x1b[7mc\x1b[27md", 8),
            ("\x1b[7ma\x1b[27mb中文cd", 2),
            ("ab中文cd\x1b[7m \x1b[27m", 10),
            ("ab中文cd  \x1b[7m \x1b[27m", 12),
        })
        {
            ClaudeFrame(emulator, text);
            var anchor = client.CursorRectangle;
            Assert.Equal(col * CellWidth(view), anchor.X, 6);
            Assert.Equal(6 * anchor.Height, anchor.Y, 6);
            Draw(view);
            Assert.Equal(anchor, client.CursorRectangle);
            client.SetPreeditText("ni", 1);
            Assert.Equal((col + 1) * CellWidth(view), client.CursorRectangle.X, 6);
            client.SetPreeditText(null);
        }
    }

    [AvaloniaFact]
    public void ClaudeSoftwareCursor_OnWrappedRow_UsesItsOwnPosition()
    {
        using var emulator = new TerminalEmulator(columns: 60, rows: 12);
        var view = new TerminalView { Emulator = emulator };
        emulator.Parser.Feed("\x1b[?25l\x1b[6;1H" + new string('─', 60)
            + "\x1b[7;1H❯ first line\x1b[8;1H  中\x1b[7m文\x1b[27m at middle"
            + "\x1b[9;1H  last line\x1b[10;1H" + new string('─', 60)
            + "\x1b[11;1Hstatus");
        var rect = Client(view).CursorRectangle;
        Assert.Equal(4 * CellWidth(view), rect.X, 6);
        Assert.Equal(7 * rect.Height, rect.Y, 6);
    }

    [AvaloniaTheory]
    [InlineData("a中b", 1, 0, 7)]
    [InlineData("a中b", 2, 2, 7)]
    [InlineData("ab", 2, 0, 7)]
    [InlineData("😀", 1, 58, 6)]
    [InlineData("a\r\nb", 3, 0, 7)]
    public void ClaudeComposition_RightEdge_UsesWrappedCaretBeforeRender(string text, int caret, int col, int row)
    {
        using var emulator = new TerminalEmulator(columns: 60, rows: 12);
        var view = new TerminalView { Emulator = emulator };
        emulator.Parser.Feed("\x1b[?25l\x1b[6;1H" + new string('─', 60)
            + "\x1b[7;1H❯ " + new string('x', 56) + "\x1b[7m \x1b[27m"
            + "\x1b[10;1H" + new string('─', 60));
        var client = Client(view);
        client.SetPreeditText(text, caret);
        var rect = client.CursorRectangle;
        Assert.Equal(col * CellWidth(view), rect.X, 6);
        Assert.Equal(row * rect.Height, rect.Y, 6);
        Draw(view);
        Assert.Equal(rect, client.CursorRectangle);
    }

    [AvaloniaFact]
    public void ClaudeComposition_WideGlyphWraps_AndDoesNotPaintFooter()
    {
        using var emulator = new TerminalEmulator(columns: 60, rows: 12);
        var view = new TerminalView { Emulator = emulator };
        emulator.Parser.Feed("\x1b[?25l\x1b[6;1H" + new string('─', 60)
            + "\x1b[7;1H❯ " + new string('x', 56) + "\x1b[7m \x1b[27m"
            + "\x1b[9;1H" + new string('─', 60) + "\x1b[10;1Hfooter");
        Client(view).SetPreeditText("a中" + new string('文', 40), 42);
        var group = new DrawingGroup();
        using (var context = group.Open()) view.Render(context);
        var glyphs = Glyphs(group, Matrix.Identity).ToArray();
        var chinese = Assert.Single(glyphs, g => g.Text == "中");
        Assert.Equal(0, chinese.X, 6);
        var height = Client(view).CursorRectangle.Height;
        Assert.InRange(chinese.Y, 7 * height, 8 * height);
        var visibleTail = glyphs.Where(g => g.Text == "文").ToArray();
        Assert.Equal(29, visibleTail.Length);
        Assert.All(visibleTail, g => Assert.InRange(g.Y, 7 * height, 8 * height));
        Assert.Equal(7 * height, Client(view).CursorRectangle.Y, 6);
    }

    [AvaloniaTheory]
    [InlineData("Paper", " ")]
    [InlineData("Paper", "文")]
    [InlineData("Black", " ")]
    [InlineData("Black", "文")]
    public void ReverseVideo_DefaultColors_DrawsVisibleCursorBackground(string theme, string text)
    {
        var oldFg = TerminalPalette.DefaultFg;
        var oldBg = TerminalPalette.DefaultBg;
        try
        {
            TerminalPalette.SetTheme(theme);
            using var emulator = new TerminalEmulator(columns: 60, rows: 10);
            var view = new TerminalView { Emulator = emulator };
            emulator.Parser.Feed("\x1b[?25l\x1b[7m" + text + "\x1b[27m");
            var group = new DrawingGroup();
            using (var context = group.Open()) view.Render(context);
            var painted = Descendants(group).OfType<GeometryDrawing>()
                .Where(d => d.Brush is ISolidColorBrush b && b.Color == TerminalPalette.DefaultFg).ToArray();
            var cursor = Assert.Single(painted);
            Assert.Equal(text.Sum(ScreenBuffer.CharWidth) * CellWidth(view), cursor.Geometry!.Bounds.Width, 6);
            Assert.All(Descendants(group).OfType<GlyphRunDrawing>()
                .Where(d => d.GlyphRun!.Characters.ToString() == text),
                d => Assert.Equal(TerminalPalette.DefaultBg, ((ISolidColorBrush)d.Foreground!).Color));
        }
        finally
        {
            TerminalPalette.DefaultFg = oldFg;
            TerminalPalette.DefaultBg = oldBg;
        }
    }

    private static IEnumerable<Drawing> Descendants(Drawing drawing)
    {
        yield return drawing;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var item in Descendants(child)) yield return item;
    }

    [AvaloniaFact]
    public void MixedCjkText_GlyphPositionsUseTerminalCells()
    {
        var view = new TerminalView { Emulator = new TerminalEmulator() };
        using var emulator = view.Emulator;
        var group = new DrawingGroup();
        using (var context = group.Open())
            typeof(TerminalView).GetMethod("DrawRun", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(view, [context, "大大大v", 2, 0, Brushes.White, CellAttrs.None, Colors.White]);

        var glyphs = Glyphs(group, Matrix.Identity).ToArray();
        var cellW = CellWidth(view);
        Assert.Equal(4, glyphs.Length);
        Assert.Equal(new[] { "大", "大", "大", "v" }, glyphs.Select(g => g.Text));
        for (var i = 0; i < glyphs.Length; i++)
            Assert.Equal((2 + i * 2) * cellW, glyphs[i].X, 6);
    }

    private static IEnumerable<(string Text, double X, double Y)> Glyphs(Drawing drawing, Matrix transform)
    {
        if (drawing is DrawingGroup group)
        {
            var matrix = (group.Transform?.Value ?? Matrix.Identity) * transform;
            foreach (var child in group.Children)
                foreach (var glyph in Glyphs(child, matrix)) yield return glyph;
        }
        else if (drawing is GlyphRunDrawing { GlyphRun: { } run })
            yield return (run.Characters.ToString(), run.BaselineOrigin.Transform(transform).X,
                run.BaselineOrigin.Transform(transform).Y);
    }
}
