using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;
using TerminalHub.App.Controls;
using TerminalHub.Core.Terminal;
using Xunit;

namespace TerminalHub.Tests;

public class TerminalAppearanceTests
{
    [AvaloniaFact]
    public async Task PaperWorkspace_WithGrokReplay_KeepsOuterTheme()
    {
        var previous = ThemeManager.Current;
        try
        {
            using var fixture = new StageLayoutTests.StageFixture();
            await Task.Delay(700);
            fixture.Vm.ThemeIndex = Array.IndexOf(ThemeManager.Names, "Paper");
            var emulator = fixture.Vm.ActiveSession!.Emulator;
            // This recorded startup painted a 100x28 screen. Preserve that grid
            // while showing the replay inside the larger workspace.
            fixture.Window.FindControl<TerminalView>("MainTerminal")!.IsPreview = true;
            emulator.Resize(100, 28);
            emulator.Parser.Feed(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
                "Fixtures", "grok-build-1.0.46-startup.vt")));
            await Task.Delay(250);
            Assert.Equal("Paper", ThemeManager.Current);
            Assert.False(TerminalPalette.ForFrame(emulator.Buffer.CaptureFrame(), emulator.ColorScheme).IsLight);
            if (Environment.GetEnvironmentVariable("TERMINALHUB_APPEARANCE_CAPTURES") is { } output)
            {
                Directory.CreateDirectory(output);
                fixture.Window.CaptureRenderedFrame()!.Save(Path.Combine(output, "grok-paper.png"));
            }
        }
        finally { ThemeManager.Apply(previous); }
    }

    [AvaloniaFact]
    public void RealGrokBackground_UsesReadableDefaultsInPaper_AndSharedViewsFollowOverride()
    {
        var previous = ThemeManager.Current;
        try
        {
            ThemeManager.Apply("Paper");
            using var terminal = new TerminalEmulator(columns: 100, rows: 28);
            var main = new TerminalView { Emulator = terminal };
            var preview = new TerminalView { Emulator = terminal, IsPreview = true };
            terminal.Parser.Feed(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
                "Fixtures", "grok-build-1.0.46-startup.vt")));
            var colors = TerminalPalette.ForFrame(terminal.Buffer.CaptureFrame(), terminal.ColorScheme);
            Assert.Equal(Color.Parse("#141414"), colors.Background);
            Assert.Equal(Color.Parse("#D8DDE7"), colors.Foreground);
            Assert.Equal("Paper", ThemeManager.Current);
            Assert.Equal("1414/1414/1414", terminal.Parser.DefaultColorQuery!(false));
            Assert.Equal(colors.Foreground, TerminalPalette.Resolve(TerminalColor.Default, true, colors));
            Assert.Equal(Color.Parse("#808080"), TerminalPalette.Resolve(TerminalColor.Rgb(128, 128, 128), true, colors));
            Draw(main); Draw(preview);
            terminal.ColorScheme = TerminalColorScheme.FollowTheme;
            Assert.Equal(TerminalPalette.DefaultFg, TerminalPalette.ForFrame(terminal.Buffer.CaptureFrame(), terminal.ColorScheme).Foreground);
            Assert.Equal(TerminalPalette.QueryDefaultColor(true), terminal.Parser.DefaultColorQuery!(true));
            Draw(main); Draw(preview);
        }
        finally { ThemeManager.Apply(previous); }
    }

    [AvaloniaFact]
    public void OneColoredPrompt_DoesNotChangeSessionDefaults()
    {
        using var terminal = new TerminalEmulator(columns: 32, rows: 16);
        terminal.Parser.Feed("\x1b[48;2;20;20;20mPrompt\x1b[0m");
        Assert.Equal(TerminalPalette.ThemeColors,
            TerminalPalette.ForFrame(terminal.Buffer.CaptureFrame(), TerminalColorScheme.Automatic));
    }

    [AvaloniaFact]
    public void FractionalCellBackgrounds_HaveNoSeamsBetweenRowsOrStyleRuns()
    {
        using var terminal = new TerminalEmulator(columns: 32, rows: 16);
        var view = new TerminalView { Emulator = terminal, TerminalFontSize = 13.3 };
        terminal.Parser.Feed("\x1b[?25l\x1b[48;2;20;20;20m\x1b[2J");
        for (var row = 5; row <= 7; row++)
        {
            terminal.Parser.Feed($"\x1b[{row};1H\x1b[48;2;64;64;64m");
            for (var run = 0; run < 8; run++)
                terminal.Parser.Feed($"\x1b[38;5;{run + 1}m    ");
        }
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var width = (double)typeof(TerminalView).GetField("_cellW", flags)!.GetValue(view)!;
        var height = (double)typeof(TerminalView).GetField("_cellH", flags)!.GetValue(view)!;
        view.Measure(new Size(width * 32, height * 16));
        view.Arrange(new Rect(0, 0, width * 32, height * 16));
        Draw(view); // Exercise the cached row drawings too.
        using var target = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(width * 32),
            (int)Math.Ceiling(height * 16)), new Vector(96, 96));
        target.Render(view);
        using var bytes = new MemoryStream();
        target.Save(bytes);
        using var pixels = SKBitmap.Decode(bytes.ToArray());
        for (var y = (int)Math.Ceiling(height * 4) + 1; y < (int)(height * 7) - 1; y++)
            for (var x = 1; x < (int)(width * 32) - 1; x++)
                Assert.Equal(new SKColor(64, 64, 64), pixels.GetPixel(x, y));
    }

    private static DrawingGroup Draw(TerminalView view)
    {
        var group = new DrawingGroup();
        using (var context = group.Open()) view.Render(context);
        return group;
    }
}

