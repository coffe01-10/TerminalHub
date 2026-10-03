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

    [AvaloniaFact]
    public async Task CtrlShiftC_CopiesSelection_WithoutSendingAnythingToShell()
    {
        using var f = new Fixture();
        f.Emulator.Parser.Feed("copy中文");
        f.View.SelectAll();
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
