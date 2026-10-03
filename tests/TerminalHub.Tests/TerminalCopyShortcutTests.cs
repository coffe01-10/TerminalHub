using System.Reflection;
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

/// <summary>Ctrl+Shift+C is the terminal's copy shortcut: it must copy the
/// selection locally without interrupting the CLI, while bare Ctrl+C keeps
/// sending ETX so the terminal interrupt stays intact.</summary>
public class TerminalCopyShortcutTests
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

    /// <summary>The view's measured cell metrics — set by the constructor's real
    /// glyph measurement; read-only, same pattern as TerminalHistoryTests.</summary>
    private static (double Width, double Height) CellSize(TerminalView view)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        return ((double)typeof(TerminalView).GetField("_cellW", flags)!.GetValue(view)!,
            (double)typeof(TerminalView).GetField("_cellH", flags)!.GetValue(view)!);
    }

    /// <summary>Drags a real pointer press/move/release across cells
    /// <paramref name="col0"/>..<paramref name="col1"/> of absolute buffer line
    /// <paramref name="line"/> — the same user-path construction as
    /// TerminalHistoryTests.DragSelect. SelectAll is deliberately avoided here:
    /// it selects the whole buffer rectangle, so a copy carries one newline per
    /// empty viewport row below the text (real terminals do the same for
    /// select-all); this test pins the shortcut on an ordinary user drag.</summary>
    private static void DragSelect(TerminalView view, TerminalEmulator terminal, int line, int col0, int col1)
    {
        var buf = terminal.Buffer;
        var (cellW, cellH) = CellSize(view);
        int row, viewOffset;
        lock (buf.SyncRoot)
        {
            viewOffset = Math.Clamp(buf.ScrollbackCount + buf.Rows - 1 - line, 0, buf.ScrollbackCount);
            row = line - buf.ScrollbackCount + viewOffset;
        }
        view.ScrollToOffset(viewOffset);
        var y = (row + 0.5) * cellH;
        Point At(int col) => new((col + 0.5) * cellW, y);
        var pointer = new Avalonia.Input.Pointer(1, PointerType.Mouse, true);
        view.RaiseEvent(new PointerPressedEventArgs(view, pointer, view, At(col0), 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None, 1));
        view.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, view, pointer, view, At(col1), 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other),
            KeyModifiers.None));
        view.RaiseEvent(new PointerReleasedEventArgs(view, pointer, view, At(col1), 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonReleased),
            KeyModifiers.None, MouseButton.Left));
    }

    [AvaloniaFact]
    public async Task CtrlShiftC_CopiesSelection_WithoutSendingAnythingToShell()
    {
        using var f = new Fixture();
        f.Emulator.Parser.Feed("copy中文");
        // "copy中文" occupies line 0 cells 0..7 (中/文 are wide: 2 cells each);
        // releasing on the wide glyph's continuation cell snaps to its lead, so
        // the copy still carries the full glyph.
        DragSelect(f.View, f.Emulator, line: 0, col0: 0, col1: 7);
        await f.Window.Clipboard!.SetTextAsync("sentinel");

        f.Window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control | RawInputModifiers.Shift);
        await Task.Delay(30);

        Assert.Equal("copy中文", await f.Window.Clipboard.GetTextAsync());
        Assert.Empty(f.Pty.Writes);   // copy is a UI gesture — the CLI must not see a byte
    }

    [AvaloniaFact]
    public async Task BareCtrlC_StillSendsEtx_AndDoesNotTouchClipboard()
    {
        using var f = new Fixture();
        f.Emulator.Parser.Feed("interrupt me");
        f.View.SelectAll();
        await f.Window.Clipboard!.SetTextAsync("sentinel");

        f.View.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.C, KeyModifiers = KeyModifiers.Control });

        Assert.Equal("\x03", f.Pty.Text);
        Assert.Equal("sentinel", await f.Window.Clipboard.GetTextAsync());
    }
}
