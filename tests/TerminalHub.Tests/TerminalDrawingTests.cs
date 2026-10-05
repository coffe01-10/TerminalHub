using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using TerminalHub.App.Controls;
using TerminalHub.Core.Terminal;
using TerminalHub.Core.Pty;
using Xunit;

namespace TerminalHub.Tests;

public class TerminalDrawingTests
{
    [AvaloniaFact]
    public async Task Preview_PaintsFinalOutputAfterProducerStops()
    {
        using var terminal = new TerminalEmulator(new MockPtySession(), 30, 4);
        await terminal.StartAsync(new PtyOptions { Shell = "cmd.exe" });
        var preview = new RecordingPreview { Emulator = terminal, IsPreview = true };
        var window = new Window { Width = 300, Height = 120, Content = preview };
        window.Show();
        try
        {
            using (window.CaptureRenderedFrame()) { }
            await Task.Run(() => terminal.SendText("A"));
            var deadline = Environment.TickCount64 + 2000;
            while (!preview.PaintedText.Contains("A") && Environment.TickCount64 < deadline)
            {
                await Task.Delay(20);
                using (window.CaptureRenderedFrame()) { }
            }
            Assert.Contains("A", preview.PaintedText);
            // The last batch arrives within the throttle window; no later
            // output, scrolling or focus changes should be needed to paint it.
            await Task.Run(() => terminal.SendText("B"));
            await Task.Delay(600);
            using (window.CaptureRenderedFrame()) { }
            Assert.Contains("AB", preview.PaintedText);
            Assert.Equal(30, terminal.Buffer.Columns);
            Assert.Equal(4, terminal.Buffer.Rows);
        }
        finally { window.Close(); }
    }

    private sealed class RecordingPreview : TerminalView
    {
        public string PaintedText { get; private set; } = "";
        public override void Render(DrawingContext context)
        {
            base.Render(context);
            PaintedText = Emulator!.Buffer.TailText(4);
        }
    }

    [AvaloniaFact]
    public void Scrolling_ReusedRowsKeepTheirNewPositions()
    {
        using var terminal = new TerminalEmulator(columns: 20, rows: 3);
        var view = new TerminalView { Emulator = terminal };
        terminal.Parser.Feed("\x1b[?25lAAA\r\nBBB\r\nCCC");
        Draw(view);
        var before = Glyphs(Draw(view), Matrix.Identity).Where(g => g.Text.Length > 0).ToArray();
        terminal.Parser.Feed("\r\nDDD");
        var after = Glyphs(Draw(view), Matrix.Identity).Where(g => g.Text.Length > 0).ToArray();
        Assert.Equal(new[] { "BBB", "CCC", "DDD" }, after.Select(g => g.Text));
        Assert.Equal(before[0].Y, after[0].Y, 6);
        Assert.Equal(before[1].Y, after[1].Y, 6);
        Assert.Equal(before[2].Y, after[2].Y, 6);
    }

    [AvaloniaFact]
    public void SameText_NewColorAndInverse_RepaintsGlyphs()
    {
        using var terminal = new TerminalEmulator(columns: 20, rows: 3);
        var view = new TerminalView { Emulator = terminal };
        terminal.Parser.Feed("\x1b[?25l\x1b[31mAAA");
        Draw(view);
        var before = Assert.Single(Glyphs(Draw(view), Matrix.Identity), g => g.Text == "AAA");
        terminal.Parser.Feed("\x1b[H\x1b[32mAAA");
        var after = Assert.Single(Glyphs(Draw(view), Matrix.Identity), g => g.Text == "AAA");
        Assert.NotEqual(before.Color, after.Color);
        terminal.Parser.Feed("\x1b[H\x1b[0;7mAAA\x1b[27m");
        var inverse = Assert.Single(Glyphs(Draw(view), Matrix.Identity), g => g.Text == "AAA");
        Assert.Equal(TerminalPalette.DefaultBg, inverse.Color);
    }

    [AvaloniaFact]
    public void ReattachedPane_AfterThemeChange_UsesNewPalette()
    {
        var oldTheme = ThemeManager.Current;
        using var terminal = new TerminalEmulator();
        var view = new TerminalView { Emulator = terminal };
        var host = new Border { Child = view };
        var window = new Window { Width = 400, Height = 200, Content = host };
        window.Show();
        try
        {
            ThemeManager.Apply("Black");
            terminal.Parser.Feed("\x1b[?25lAAA");
            Draw(view);
            var before = Assert.Single(Glyphs(Draw(view), Matrix.Identity), g => g.Text == "AAA");
            host.Child = null;
            ThemeManager.Apply("Paper");
            host.Child = view;
            var after = Assert.Single(Glyphs(Draw(view), Matrix.Identity), g => g.Text == "AAA");
            Assert.Equal(TerminalPalette.DefaultFg, after.Color);
            Assert.NotEqual(before.Color, after.Color);
        }
        finally { window.Close(); ThemeManager.Apply(oldTheme); }
    }

    private static DrawingGroup Draw(TerminalView view)
    {
        var group = new DrawingGroup();
        using (var context = group.Open()) view.Render(context);
        return group;
    }

    private static IEnumerable<(string Text, double Y, Color Color)> Glyphs(Drawing drawing, Matrix transform)
    {
        if (drawing is DrawingGroup group)
        {
            var matrix = (group.Transform?.Value ?? Matrix.Identity) * transform;
            foreach (var child in group.Children)
                foreach (var glyph in Glyphs(child, matrix)) yield return glyph;
        }
        else if (drawing is GlyphRunDrawing { GlyphRun: { } run, Foreground: ISolidColorBrush brush })
            yield return (run.Characters.ToString().TrimEnd(), run.BaselineOrigin.Transform(transform).Y, brush.Color);
    }
}
