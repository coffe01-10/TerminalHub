using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using TerminalHub.App.Controls;
using TerminalHub.Core.Ai;
using TerminalHub.Core.Pty;
using TerminalHub.Core.Sessions;
using TerminalHub.Core.Terminal;
using Xunit;

namespace TerminalHub.Tests;

/// <summary>Headless end-to-end coverage of the AI-terminal workflow: an app's
/// OSC 52 request lands on the real window clipboard, negotiated kitty keys
/// reach the PTY as CSI u while legacy keys stay legacy, and the session-menu
/// action pastes a selection into an AI session without submitting it.</summary>
public class AiTerminalWorkflowTests
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

    [AvaloniaFact]
    public async Task Osc52_FromTheApp_ReachesTheWindowClipboard()
    {
        var pty = new RecordingPty();
        using var emulator = new TerminalEmulator(pty);
        var view = new TerminalView { Emulator = emulator };
        var window = new Window { Width = 640, Height = 400, Content = view };
        window.Show();
        try
        {
            await window.Clipboard!.SetTextAsync("before");
            var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("复制测试"));
            emulator.Parser.Feed($"\x1b]52;c;{payload}\a");
            await Task.Delay(50);
            Assert.Equal("复制测试", await window.Clipboard.GetTextAsync());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void KittyKeys_SwitchEncodingOnlyAfterTheCliNegotiates()
    {
        var pty = new RecordingPty();
        using var emulator = new TerminalEmulator(pty);
        var view = new TerminalView { Emulator = emulator };
        var window = new Window { Width = 640, Height = 400, Content = view };
        window.Show();
        try
        {
            view.Focus();
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Shift);
            Assert.Equal("\r", pty.Text); // no negotiation: Shift+Enter stays a plain return

            emulator.Parser.Feed("\x1b[>1u");
            pty.Writes.Clear();
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Shift);
            window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control);
            Assert.Equal("\x1b[13;2u\x1b[c;5u", pty.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task SendSelection_PastesIntoTheWaitingAiSession_WithoutSubmitting()
    {
        using var fixture = new StageLayoutTests.StageFixture();
        await fixture.ReadyAsync();
        var source = fixture.Vm.SessionCards[0].Model;
        var target = fixture.Vm.SessionCards[1].Model;
        target.Tag = SessionTag.Ai;
        target.Attention = AiAttention.NeedsYou;
        fixture.Vm.ActivateSearchSession(source);
        await Task.Delay(60);

        source.Emulator.Parser.Feed("\x1b[2J\x1b[Herror: build failed at line 12");
        var view = fixture.Window.FindControl<TerminalView>("MainTerminal");
        Assert.NotNull(view);
        view!.SelectLine(0);

        var menu = (MenuFlyout)fixture.Window.FindControl<Button>("SessionMenuButton")!.Flyout!;
        var item = menu.Items.OfType<MenuItem>().Single(i => i.Name == "SendSelectionToAiItem");
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await Task.Delay(40);

        Assert.Same(target, fixture.Vm.ActiveSession);
        var pasted = ((MockPtySession)target.Pty).RawInput.ToString();
        Assert.Contains("error: build failed at line 12", pasted);
        // Pasted, not submitted: no line break follows the selection, so the
        // mock shell never executes it.
        var at = pasted.IndexOf("error: build failed at line 12", StringComparison.Ordinal);
        var tail = pasted[at..];
        Assert.DoesNotContain('\n', tail);
        Assert.DoesNotContain('\r', tail);
    }
}
