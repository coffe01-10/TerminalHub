using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using TerminalHub.App.Controls;
using TerminalHub.Core.Pty;
using TerminalHub.Core.Terminal;
using Xunit;

namespace TerminalHub.Tests;

public class CliInteractionTests
{
    private sealed class RecordingPty : IPtySession
    {
        public List<byte[]> Writes { get; } = [];
        public string Text => Encoding.UTF8.GetString(Writes.SelectMany(b => b).ToArray());
        public Guid Id { get; } = Guid.NewGuid();
        public bool IsRunning => true;
        public int? ExitCode => null;
        public int? ProcessId => null;
        public event Action<IPtySession, ReadOnlyMemory<byte>>? OutputReceived { add { } remove { } }
        public event Action<IPtySession, int>? Exited { add { } remove { } }
        public Task StartAsync(PtyOptions options, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Write(ReadOnlySpan<byte> data) => Writes.Add(data.ToArray());
        public void Resize(int columns, int rows) { }
        public void Kill() { }
        public void Dispose() { }
    }

    private sealed class Fixture : IDisposable
    {
        public RecordingPty Pty { get; } = new();
        public TerminalEmulator Emulator { get; }
        public TerminalView View { get; }
        public Window Window { get; }
        public Fixture()
        {
            Emulator = new TerminalEmulator(Pty);
            View = new TerminalView { Emulator = Emulator };
            Window = new Window { Width = 640, Height = 400, Content = View };
            Window.Show();
            View.Focus();
        }
        public void Dispose() { Window.Close(); Emulator.Dispose(); }
    }

    [AvaloniaTheory]
    [InlineData(PhysicalKey.V, RawInputModifiers.Control)]
    [InlineData(PhysicalKey.V, RawInputModifiers.Control | RawInputModifiers.Shift)]
    [InlineData(PhysicalKey.Insert, RawInputModifiers.Shift)]
    public async Task PasteShortcuts_ReadClipboard_AndPreserveMultiline(PhysicalKey key, RawInputModifiers modifiers)
    {
        using var f = new Fixture();
        f.Emulator.Parser.Feed("\x1b[?2004h");
        await f.Window.Clipboard!.SetTextAsync("ab中文cd\r\nsecond line");
        f.Window.KeyPressQwerty(key, modifiers);
        await Task.Delay(30);
        Assert.Equal("\x1b[200~ab中文cd\nsecond line\x1b[201~", f.Pty.Text);
        Assert.Single(f.Pty.Writes);
    }

    [AvaloniaFact]
    public async Task RightClick_PastesInShell_ButBelongsToMouseEnabledCli()
    {
        using var f = new Fixture();
        await f.Window.Clipboard!.SetTextAsync("paste sample");
        f.Window.MouseDown(new Point(40, 40), MouseButton.Right);
        f.Window.MouseUp(new Point(40, 40), MouseButton.Right);
        await Task.Delay(30);
        Assert.Equal("paste sample", f.Pty.Text);
        f.Pty.Writes.Clear();
        f.Emulator.Parser.Feed("\x1b[?1003;1006h"); // Captured from Grok Build 1.0.41 on ConPTY.
        f.Window.MouseDown(new Point(40, 40), MouseButton.Right);
        f.Window.MouseUp(new Point(40, 40), MouseButton.Right);
        Assert.StartsWith("\x1b[<2;", f.Pty.Text);
        Assert.EndsWith("m", f.Pty.Text);
        Assert.DoesNotContain("paste sample", f.Pty.Text);
    }

    [AvaloniaFact]
    public void GrokMouse_ClickDragHoverWheel_AreForwarded_AndShiftSelectsLocally()
    {
        using var f = new Fixture();
        f.Emulator.Parser.Feed("clickable block\r\nsecond line\x1b[?1003;1006h");
        f.Window.MouseMove(new Point(40, 8));
        Assert.StartsWith("\x1b[<35;", f.Pty.Text);
        f.Pty.Writes.Clear();
        f.Window.MouseDown(new Point(5, 8), MouseButton.Left);
        f.Window.MouseMove(new Point(80, 8));
        f.Window.MouseUp(new Point(80, 8), MouseButton.Left);
        Assert.StartsWith("\x1b[<0;1;1M", f.Pty.Text);
        Assert.Contains("\x1b[<32;", f.Pty.Text);
        Assert.EndsWith("m", f.Pty.Text);
        Assert.Null(f.View.GetSelectedText());
        f.Pty.Writes.Clear();
        f.Window.MouseWheel(new Point(40, 8), new Vector(0, -1));
        Assert.StartsWith("\x1b[<65;", f.Pty.Text);
        f.Pty.Writes.Clear();
        f.Window.MouseDown(new Point(5, 8), MouseButton.Left, RawInputModifiers.Shift);
        f.Window.MouseMove(new Point(80, 8), RawInputModifiers.Shift | RawInputModifiers.LeftMouseButton);
        f.Window.MouseUp(new Point(80, 8), MouseButton.Left, RawInputModifiers.Shift);
        Assert.Empty(f.Pty.Writes);
        Assert.StartsWith("click", f.View.GetSelectedText());
    }

    [Fact]
    public void MouseNegotiation_ResetsAndFiltersMotion_AndReportsLargeSgrCoordinates()
    {
        using var pty = new RecordingPty();
        using var emulator = new TerminalEmulator(pty, 300, 100);
        emulator.SendMouse(0, 2, 3);
        Assert.Empty(pty.Writes);
        emulator.Parser.Feed("\x1b[?1002;1006h");
        emulator.SendMouse(3, 2, 3, motion: true);
        Assert.Empty(pty.Writes);
        emulator.SendMouse(0, 250, 80, motion: true, modifiers: 16);
        Assert.Equal("\x1b[<48;251;81M", pty.Text);
        pty.Writes.Clear();
        emulator.Parser.Feed("\x1b[?1000h\x1b[?1002l");
        Assert.Equal(1000, emulator.Buffer.MouseTracking);
        emulator.SendMouse(0, 2, 3, motion: true);
        Assert.Empty(pty.Writes);
        emulator.Parser.Feed("\x1b[?1006l");
        emulator.SendMouse(0, 2, 3);
        Assert.Equal(new byte[] { 27, 91, 77, 32, 35, 36 }, Assert.Single(pty.Writes));
        emulator.Parser.Feed("\x1b[?1004h\u001bc");
        Assert.Equal(0, emulator.Buffer.MouseTracking);
        Assert.False(emulator.Buffer.FocusReporting);
        Assert.False(emulator.Buffer.SgrMouse);
    }

    [AvaloniaTheory]
    [InlineData(Key.Enter, KeyModifiers.Alt, "\x1b\r")]
    // Alt+Shift+Enter keeps the Alt: without kitty it is still ESC CR, not a bare CR.
    [InlineData(Key.Enter, KeyModifiers.Alt | KeyModifiers.Shift, "\x1b\r")]
    [InlineData(Key.F2, KeyModifiers.None, "\x1bOQ")]
    [InlineData(Key.Left, KeyModifiers.Control, "\x1b[1;5D")]
    [InlineData(Key.Right, KeyModifiers.Shift, "\x1b[1;2C")]
    [InlineData(Key.Back, KeyModifiers.Control, "\x17")]
    [InlineData(Key.C, KeyModifiers.Control, "\x03")]
    [InlineData(Key.Oem5, KeyModifiers.Control, "\x1c")]
    // Shift does not change these control bytes: Ctrl+Shift+[ is still ESC, etc.
    [InlineData(Key.Oem4, KeyModifiers.Control | KeyModifiers.Shift, "\x1b")]
    [InlineData(Key.Oem5, KeyModifiers.Control | KeyModifiers.Shift, "\x1c")]
    [InlineData(Key.Oem6, KeyModifiers.Control | KeyModifiers.Shift, "\x1d")]
    [InlineData(Key.Space, KeyModifiers.Control | KeyModifiers.Shift, "\x00")]
    [InlineData(Key.B, KeyModifiers.Alt, "\x1b" + "b")]
    public void CliEditingKeys_KeepModifiers_AndCtrlCRemainsInterrupt(Key key, KeyModifiers modifiers, string expected)
    {
        using var f = new Fixture();
        f.View.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });
        Assert.Equal(expected, f.Pty.Text);
    }

    /// <summary>Alt-screen has no local scrollback — the wheel belongs to the app
    /// as cursor keys (xterm alternateScroll), batched per notch.</summary>
    [AvaloniaFact]
    public void AltScreen_WheelGoesToApp_AsCursorKeys()
    {
        using var f = new Fixture();
        f.Emulator.Parser.Feed("\x1b[?1049h");           // alternate screen
        f.Window.MouseWheel(new Point(40, 8), new Vector(0, -1));
        Assert.Equal("\x1b[B\x1b[B\x1b[B", f.Pty.Text); // one notch → 3 cursor-downs
        f.Pty.Writes.Clear();
        f.Emulator.Parser.Feed("\x1b[?1h");             // application cursor keys → SS3 form
        f.Window.MouseWheel(new Point(40, 8), new Vector(0, 1));
        Assert.Equal("\x1bOA\x1bOA\x1bOA", f.Pty.Text);
    }

    /// <summary>Dragging onto a wide glyph's padding cell selects the whole glyph —
    /// the highlight already covers it; the copied text must not silently drop it.</summary>
    [AvaloniaFact]
    public void Selection_DragOntoWideGlyphPadding_IncludesWholeGlyph()
    {
        using var f = new Fixture();
        f.Emulator.Parser.Feed("中Xtail");                 // 中 = cells 0-1 (cell 1 padding), X = cell 2
        var cellW = (double)typeof(TerminalView).GetField("_cellW",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(f.View)!;
        var cellH = (double)typeof(TerminalView).GetField("_cellH",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(f.View)!;
        var y = cellH / 2;                                 // row 0
        f.Window.MouseDown(new Point(2.5 * cellW, y), MouseButton.Left);
        f.Window.MouseMove(new Point(1.5 * cellW, y), RawInputModifiers.LeftMouseButton);
        f.Window.MouseUp(new Point(1.5 * cellW, y), MouseButton.Left);
        Assert.Equal("中X", f.View.GetSelectedText());
    }

    [AvaloniaFact]
    public void NegotiatedEnter_AndSessionFocus_AreSentToTheCorrectCli()
    {
        using var f = new Fixture();
        f.Emulator.Parser.Feed("\x1b[>1u\x1b[?1004h");
        f.View.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Enter, KeyModifiers = KeyModifiers.Control });
        Assert.Equal("\x1b[13;5u", f.Pty.Text);
        f.Pty.Writes.Clear();
        using var otherPty = new RecordingPty();
        using var other = new TerminalEmulator(otherPty);
        other.Parser.Feed("\x1b[?1004h");
        f.View.Emulator = other;
        Assert.Equal("\x1b[O", f.Pty.Text);
        Assert.Equal("\x1b[I", otherPty.Text);
    }
}
