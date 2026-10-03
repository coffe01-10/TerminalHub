using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using TerminalHub.App.Controls;
using TerminalHub.App.ViewModels;
using TerminalHub.App.Views;
using TerminalHub.Core.Monitoring;
using TerminalHub.Core.Pty;
using TerminalHub.Core.Sessions;
using TerminalHub.Core.Settings;
using TerminalHub.Core.Terminal;
using TerminalHub.Pty;
using Xunit;

namespace TerminalHub.Tests;

public class CodeReviewFixTests
{
    private static (VtParser Parser, ScreenBuffer Buffer) Make(int cols = 10, int rows = 8)
    {
        var buffer = new ScreenBuffer(cols, rows);
        return (new VtParser(buffer), buffer);
    }

    [Fact]
    public void IllegalUtf8_BecomesReplacement_AndDoesNotThrow()
    {
        var (parser, buffer) = Make();
        parser.Feed(new byte[] { 0xF4, 0x90, 0x80, 0x80, (byte)'A' });
        Assert.Equal('\uFFFD', buffer.CellAt(0, 0).Char);
        Assert.Equal('A', buffer.CellAt(0, 1).Char);
        parser.Feed(new byte[] { 0xED, 0xA0, 0x80, (byte)'B' });
        Assert.Equal('\uFFFD', buffer.CellAt(0, 2).Char);
        Assert.NotEqual('\uD800', buffer.CellAt(0, 2).Char);
        Assert.Equal('B', buffer.CellAt(0, 3).Char);

        buffer.PutRune(0x110000);
        Assert.Equal('\uFFFD', buffer.CellAt(0, 4).Char);
        buffer.PutRune(0xD800);
        Assert.Equal('\uFFFD', buffer.CellAt(0, 5).Char);
    }

    [Fact]
    public void SgrColonSubparameters_SetColorAndUnderlineStyle()
    {
        var (parser, buffer) = Make();
        parser.Feed("\u001b[38:2:255:0:0mX");
        Assert.Equal(TerminalColor.Rgb(255, 0, 0), buffer.CellAt(0, 0).Fg);
        Assert.Equal('X', buffer.CellAt(0, 0).Char);
        Assert.Equal(' ', buffer.CellAt(0, 1).Char);

        parser.Feed("\u001b[0m\u001b[38:2::1:2:3mY");
        Assert.Equal(TerminalColor.Rgb(1, 2, 3), buffer.CellAt(0, 1).Fg);
        Assert.Equal('Y', buffer.CellAt(0, 1).Char);

        parser.Feed("\u001b[0m\u001b[38;2;300;400;1mZ");
        Assert.Equal(TerminalColor.Rgb(255, 255, 1), buffer.CellAt(0, 2).Fg);

        // CSI '-' is an intermediate, not a sign. An empty colon field is the negative component.
        parser.Feed("\u001b[0m\u001b[38:2:300::1mW");
        Assert.Equal(TerminalColor.Rgb(255, 0, 1), buffer.CellAt(0, 3).Fg);

        parser.Feed("\u001b[0m\u001b[4:3mU");
        var curly = buffer.CellAt(0, 4);
        Assert.Equal('U', curly.Char);
        Assert.True(curly.Attrs.HasFlag(CellAttrs.Underline));
        Assert.False(curly.Attrs.HasFlag(CellAttrs.Italic));

        parser.Feed("\u001b[0m\u001b[4;3mI");
        var both = buffer.CellAt(0, 5);
        Assert.True(both.Attrs.HasFlag(CellAttrs.Underline));
        Assert.True(both.Attrs.HasFlag(CellAttrs.Italic));
    }

    [Fact]
    public void DeckPam_DoesNotChangeCursorKeys()
    {
        var (parser, buffer) = Make();
        parser.Feed("\u001b[?1h\u001b=");
        Assert.True(buffer.ApplicationCursorKeys);
        Assert.True(buffer.ApplicationKeypad);
        parser.Feed("\u001b>");
        Assert.True(buffer.ApplicationCursorKeys);
        Assert.False(buffer.ApplicationKeypad);
    }

    [Fact]
    public void CursorUpDown_RespectsScrollRegionOnlyWhenStartedInside()
    {
        var (parser, buffer) = Make(10, 8);
        parser.Feed("\u001b[3;6r\u001b[4;1H\u001b[10A");
        Assert.Equal(2, buffer.CursorY);
        parser.Feed("\u001b[4;1H\u001b[10B");
        Assert.Equal(5, buffer.CursorY);
        parser.Feed("\u001b[1;1H\u001b[3A");
        Assert.Equal(0, buffer.CursorY);
        parser.Feed("\u001b[8;1H\u001b[4B");
        Assert.Equal(7, buffer.CursorY);
    }

    [Fact]
    public void SavedCursor_IsSeparateForEachScreen()
    {
        var (parser, buffer) = Make(10, 8);
        parser.Feed("\u001b[4;5H\u001b7\u001b[?1049h\u001b[2;2H\u001b7\u001b[?1049l");
        Assert.False(buffer.OnAlternateScreen);
        Assert.Equal(3, buffer.CursorY);
        Assert.Equal(4, buffer.CursorX);
    }

    [Fact]
    public void OriginMode_CursorReport_IsRelativeToScrollTop()
    {
        var replies = new List<string>();
        var buffer = new ScreenBuffer(10, 8);
        var parser = new VtParser(buffer, bytes => replies.Add(Encoding.ASCII.GetString(bytes)));
        parser.Feed("\u001b[3;6r\u001b[?6h\u001b[2;1H\u001b[6n");
        Assert.Equal(3, buffer.CursorY);
        Assert.Contains("\u001b[2;1R", replies);
    }

    [Fact]
    public void Ris_LeavesAlternateScreen_Decstr_ClearsSgrOnly()
    {
        var (parser, buffer) = Make();
        parser.Feed("\u001b[?1049h\u001b[1;1HZ\u001b[?1h\u001b=");
        parser.Feed("\u001bc");
        Assert.False(buffer.OnAlternateScreen);
        Assert.False(buffer.ApplicationCursorKeys);
        Assert.False(buffer.ApplicationKeypad);
        Assert.Equal(' ', buffer.CellAt(0, 0).Char);

        parser.Feed("\u001b[?1049h\u001b[1;31m\u001b[!pX");
        Assert.True(buffer.OnAlternateScreen);
        Assert.Equal('X', buffer.CellAt(0, 0).Char);
        Assert.False(buffer.CellAt(0, 0).Attrs.HasFlag(CellAttrs.Bold));
        Assert.True(buffer.CellAt(0, 0).Fg.IsDefault);
        Assert.False(buffer.ApplicationCursorKeys);
    }

    [Fact]
    public void SynchronizedOutput_ClearsFlagWhenTheHoldExpires()
    {
        var (parser, buffer) = Make();
        parser.Feed("A\u001b[?2026h");
        Assert.True(buffer.SynchronizedOutput);
        parser.Feed("B");
        Assert.Equal(' ', buffer.CaptureFrame().Cells[1].Char);
        Thread.Sleep(220);
        var live = buffer.CaptureFrame();
        Assert.False(buffer.SynchronizedOutput);
        Assert.Equal('B', live.Cells[1].Char);
        parser.Feed("\u001b[?2026h");
        Assert.True(buffer.SynchronizedOutput);
        Assert.Equal('B', buffer.CaptureFrame().Cells[1].Char);
    }

    [Fact]
    public void MissingWidePath_StillResolvesTheRealFileInsideIt()
    {
        var directory = Path.Combine(Path.GetTempPath(), "th-link-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var real = Path.Combine(directory, "real.cs");
        try
        {
            File.WriteAllText(real, "x");
            var text = "prefix/ text real.cs:12";
            var index = text.IndexOf("real.cs", StringComparison.Ordinal);
            var link = TerminalContentLinks.Resolve(text, index, directory, false);
            Assert.NotNull(link);
            Assert.Equal(real, link!.Target);
            Assert.Equal(12, link.Line);

            var spaced = Path.Combine(directory, "file name.cs");
            File.WriteAllText(spaced, "y");
            var spacedText = spaced + ":4";
            var spacedLink = TerminalContentLinks.Resolve(spacedText, 2, directory, false);
            Assert.Equal(spaced, spacedLink!.Target);
            Assert.Equal(4, spacedLink.Line);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void DottedParentDirectory_DoesNotHideTheActualFileLink()
    {
        var directory = Path.Combine(Path.GetTempPath(), "th.links." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "source file.cs");
        File.WriteAllText(path, "source");
        try
        {
            var link = TerminalContentLinks.Resolve(path + ":12:3", 2, directory, false);
            Assert.Equal(path, link!.Target);
            Assert.Equal(12, link.Line);
            Assert.Equal(3, link.Column);
        }
        finally { Directory.Delete(directory, true); }
    }

    [AvaloniaFact]
    public void OrphanWideGlyph_RendersWithoutReadingTheNextRow()
    {
        using var emulator = new TerminalEmulator(columns: 4, rows: 2);
        emulator.Parser.Feed("\u001b[?7l\u001b[1;4H中");
        var buffer = emulator.Buffer;
        Assert.Equal('中', buffer.CellAt(0, 3).Char);
        Assert.False(buffer.CellAt(0, 3).IsWide);
        buffer.CellAt(0, 3).IsWide = true;
        buffer.CellAt(1, 0).Char = 'Z';
        var view = new TerminalView { Emulator = emulator, Width = 200, Height = 80 };
        using var context = new DrawingGroup().Open();
        view.Render(context);
        Assert.Equal('Z', buffer.CellAt(1, 0).Char);
    }

    [AvaloniaFact]
    public void DoubleClick_OneCellWord_SurvivesRelease_TripleClick_SelectsSoftWrap()
    {
        using var word = new TerminalEmulator(columns: 20, rows: 3);
        word.Parser.Feed("a b");
        var wordView = new TerminalView { Emulator = word, Width = 400, Height = 80 };
        var pointer = new Avalonia.Input.Pointer(1, PointerType.Mouse, true);
        // Cell (0,0)'s center: the constructor's glyph measurement guarantees
        // cellW >= 4 and cellH >= 8, so (1,1) always lands there.
        Press(wordView, pointer, 1, new Point(1, 1));
        Release(wordView, pointer, new Point(1, 1));
        Assert.Null(wordView.GetSelectedText());

        Press(wordView, pointer, 2, new Point(1, 1));
        Release(wordView, pointer, new Point(1, 1));
        Assert.Equal("a", wordView.GetSelectedText());

        using var wrapped = new TerminalEmulator(columns: 4, rows: 3);
        wrapped.Parser.Feed("abcdefgh");
        var wrapView = new TerminalView { Emulator = wrapped, Width = 200, Height = 80 };
        Press(wrapView, pointer, 3, new Point(1, 1));
        Release(wrapView, pointer, new Point(1, 1));
        Assert.Equal("abcdefgh", wrapView.GetSelectedText());
    }

    [AvaloniaFact]
    public void CtrlAltPlus_DoesNotZoom_CtrlShiftPlusDoes()
    {
        PtySessionFactory.UseMock = true;
        var window = new MainWindow { Width = 800, Height = 600 };
        window.Show();
        var vm = (MainWindowViewModel)window.DataContext!;
        var before = vm.FontSize;
        window.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.OemPlus,
            KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt,
            Source = window,
        });
        Assert.Equal(before, vm.FontSize);
        window.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.OemPlus,
            KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift,
            Source = window,
        });
        Assert.Equal(before + 1, vm.FontSize);
        window.Close();
    }

    [AvaloniaFact]
    public void CdQuote_UsesTheSessionShell()
    {
        var directory = Path.Combine(Path.GetTempPath(), "th-cd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var vm = new MainWindowViewModel(new IdleMonitor(), new SettingsStore(Path.Combine(directory, "settings.json")));
        using var emulator = new TerminalEmulator();
        vm.ActiveSession = new TerminalSessionModel
        {
            Name = "pwsh",
            Emulator = emulator,
            Shell = "pwsh.exe",
        };
        try
        {
            var quote = typeof(MainWindowViewModel).GetMethod("QuoteForShell", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.Equal(@"'C:\work\$HOME'", quote.Invoke(vm, new object[] { @"C:\work\$HOME" }));
            vm.ActiveSession.Shell = "cmd.exe";
            Assert.Equal("\"C:\\work\\$HOME\"", quote.Invoke(vm, new object[] { @"C:\work\$HOME" }));
            vm.ActiveSession.Shell = "bash";
            Assert.Equal(@"'C:\work\$HOME'", quote.Invoke(vm, new object[] { @"C:\work\$HOME" }));
        }
        finally
        {
            vm.Dispose();
            Directory.Delete(directory, true);
        }
    }

    private static void Press(TerminalView view, Avalonia.Input.Pointer pointer, int clicks, Point point)
    {
        var props = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonPressed);
        view.RaiseEvent(new PointerPressedEventArgs(view, pointer, view, point, 0, props, KeyModifiers.None, clicks));
    }

    private static void Release(TerminalView view, Avalonia.Input.Pointer pointer, Point point)
    {
        var props = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased);
        view.RaiseEvent(new PointerReleasedEventArgs(view, pointer, view, point, 0, props, KeyModifiers.None, MouseButton.Left));
    }

    private sealed class IdleMonitor : ISystemMonitor
    {
        public SystemSample Current { get; } = new();
        public IReadOnlyList<ProcessInfo> Processes { get; } = [];
#pragma warning disable CS0067
        public event Action<ISystemMonitor>? Sampled;
#pragma warning restore CS0067
        public void Start(TimeSpan interval) { }
        public void Stop() { }
        public void Dispose() { }
    }
}
