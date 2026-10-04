using System.Text;
using TerminalHub.Core.Pty;
using TerminalHub.Core.Terminal;
using Xunit;

namespace TerminalHub.Tests;

public class TerminalStreamingTests
{
    private static string Text(TerminalFrame frame) => new(frame.Cells.Select(c => c.Char).ToArray());

    [Fact]
    public void PartialOutput_IsVisibleBeforeNewline_AndFramesStayStable()
    {
        var buffer = new ScreenBuffer(20, 3);
        var parser = new VtParser(buffer);
        parser.Feed("Thinking");
        var first = buffer.CaptureFrame();
        parser.Feed("... next token");
        Assert.StartsWith("Thinking... next", Text(buffer.CaptureFrame()));
        Assert.StartsWith("Thinking     ", Text(first));
    }

    [Fact]
    public void SynchronizedRedraw_HoldsPreviousFrameUntilEnd()
    {
        var buffer = new ScreenBuffer(20, 3);
        var parser = new VtParser(buffer);
        parser.Feed("Previous response");
        parser.Feed("\x1b[?2026h\x1b[2J\x1b[H");
        Assert.StartsWith("Previous response", Text(buffer.CaptureFrame()));
        parser.Feed("New response\x1b[?2026l");
        Assert.StartsWith("New response", Text(buffer.CaptureFrame()));
    }

    [Fact]
    public async Task InterruptedSynchronizedRedraw_DoesNotFreezeForever()
    {
        var buffer = new ScreenBuffer(20, 3);
        var parser = new VtParser(buffer);
        parser.Feed("Old\x1b[?2026h\x1b[2J\x1b[HNew");
        await Task.Delay(180);
        Assert.StartsWith("New", Text(buffer.CaptureFrame()));
    }

    [Fact]
    public void ToolCapabilityQueries_ReportModesAndThemeColors()
    {
        var replies = new List<string>();
        var parser = new VtParser(new ScreenBuffer(), bytes => replies.Add(Encoding.UTF8.GetString(bytes)))
        { DefaultColorQuery = foreground => foreground ? "1111/2222/3333" : "eeee/eeee/eeee" };
        parser.Feed("\x1b[?2026$p\x1b[?2004h\x1b[?2004$p\x1b]11;?\a\x1b[>c");
        Assert.Contains("\x1b[?2026;2$y", replies);
        Assert.Contains("\x1b[?2004;1$y", replies);
        Assert.Contains("\x1b]11;rgb:eeee/eeee/eeee\x1b\\", replies);
        Assert.Contains("\x1b[>0;1;0c", replies);
    }

    [Fact]
    public void Decaln_EscHash8_FillsScreenWithE()
    {
        // ESC # 8 — the '#' intermediate must reach EscIntermediateByte; a stray
        // special-case used to swallow it and DECALN silently did nothing.
        var buffer = new ScreenBuffer(10, 4);
        var parser = new VtParser(buffer);
        parser.Feed("hello\x1b#8");
        Assert.Equal(new string('E', 40), Text(buffer.CaptureFrame()));
    }

    [Fact]
    public void KittyKeyboardFlags_PushSetPopAndQuery()
    {
        var replies = new List<string>();
        var buffer = new ScreenBuffer();
        var parser = new VtParser(buffer, bytes => replies.Add(Encoding.UTF8.GetString(bytes)));

        parser.Feed("\x1b[?u");                       // query before negotiation
        parser.Feed("\x1b[>1u");                      // push flags = 1 (disambiguate)
        Assert.Equal(1, buffer.KittyKeyboardFlags);
        parser.Feed("\x1b[=5;1u");                    // mode 1: assign → 5
        Assert.Equal(5, buffer.KittyKeyboardFlags);
        parser.Feed("\x1b[=2;3u");                    // mode 3: clear bits 2 → 5 & ~2 = 5
        Assert.Equal(5, buffer.KittyKeyboardFlags);
        parser.Feed("\x1b[=2;2u");                    // mode 2: set bits 2 → 5 | 2 = 7
        Assert.Equal(7, buffer.KittyKeyboardFlags);
        parser.Feed("\x1b[?u");                       // query → current flags
        parser.Feed("\x1b[<u");                       // pop → restore pre-push value
        Assert.Equal(0, buffer.KittyKeyboardFlags);
        parser.Feed("\x1b[<3u");                      // pop on empty stack → stays 0
        Assert.Equal(0, buffer.KittyKeyboardFlags);

        Assert.Contains("\x1b[?0u", replies);
        Assert.Contains("\x1b[?7u", replies);
    }

    [Fact]
    public void RIS_ResetsKittyKeyboardFlags()
    {
        var buffer = new ScreenBuffer();
        var parser = new VtParser(buffer);
        parser.Feed("\x1b[>15u");
        Assert.Equal(15, buffer.KittyKeyboardFlags);
        parser.Feed("\x1b" + "c"); // ESC c — "\x1bc" would be a single U+01BC char!
        Assert.Equal(0, buffer.KittyKeyboardFlags);
    }

    [Fact]
    public void UnicodeCwd_ArrivesAcrossByteChunks()
    {
        var buffer = new ScreenBuffer();
        var parser = new VtParser(buffer);
        var data = Encoding.UTF8.GetBytes("\x1b]9;9;D:\\项目\\终端\a");
        foreach (var b in data) parser.Feed(new byte[] { b });
        Assert.Equal("D:\\项目\\终端", buffer.Cwd);
    }

    [Fact]
    public void AlternateScreenResize_RestoresValidPrimaryGridAndHistory()
    {
        var buffer = new ScreenBuffer(20, 3);
        var parser = new VtParser(buffer);
        parser.Feed("saved\x1b[?1049hTUI");
        buffer.Resize(40, 6);
        parser.Feed("\x1b[?1049l");
        var frame = buffer.CaptureFrame();
        Assert.Equal(240, frame.Cells.Length);
        Assert.StartsWith("saved", Text(frame));
        parser.Feed(string.Concat(Enumerable.Repeat("line\r\n", 12)));
        buffer.Resize(50, 6);
        Assert.Equal(300, buffer.CaptureFrame(3).Cells.Length);
    }

    [Fact]
    public void BracketedPaste_PreservesMultilineWithoutSubmitting()
    {
        using var pty = new RecordingPty();
        using var emulator = new TerminalEmulator(pty);
        emulator.Parser.Feed("\x1b[?2004h");
        emulator.PasteText("first\r\nsecond");
        Assert.Equal("\x1b[200~first\nsecond\x1b[201~", pty.LastWrite);
        emulator.Parser.Feed("\x1b[?2004l");
        emulator.PasteText("first\nsecond");
        Assert.Equal("first\rsecond", pty.LastWrite);
    }

    [Theory]
    [InlineData("\x1b[201~")]
    [InlineData("\x1b[20\x1b[201~1~")]
    public void BracketedPaste_StripsPayloadEndMarker(string marker)
    {
        using var pty = new RecordingPty();
        using var emulator = new TerminalEmulator(pty);
        emulator.Parser.Feed("\x1b[?2004h");
        // A pasted terminator must not break out of the bracket — it lands as
        // inert text instead of running as typed input (paste injection).
        emulator.PasteText("safe" + marker + "\necho INJECTED");
        Assert.Equal("\x1b[200~safe\necho INJECTED\x1b[201~", pty.LastWrite);
    }

    [Fact]
    public void C1_Dcs_SwallowsPayloadUntilSt()
    {
        var buffer = new ScreenBuffer(20, 3);
        var parser = new VtParser(buffer);
        // Raw 8-bit DCS (0x90): the payload must be consumed by the string
        // state, not printed as garbage text. (Feed(string) would UTF-8
        // encode these — real PTY output carries the raw C1 byte.)
        parser.Feed(new byte[] { 0x90 }.Concat(Encoding.ASCII.GetBytes("payload")).Concat(new byte[] { 0x9C }).ToArray());
        Assert.Equal(new string(' ', 60), Text(buffer.CaptureFrame()));
        // Same for 8-bit APC/SOS/PM.
        parser.Feed(new byte[] { 0x9F }.Concat(Encoding.ASCII.GetBytes("more")).Concat(new byte[] { 0x9C, 0x9E })
            .Concat(Encoding.ASCII.GetBytes("also")).Concat(new byte[] { 0x9C, 0x98 })
            .Concat(Encoding.ASCII.GetBytes("hidden")).Concat(new byte[] { 0x9C }).ToArray());
        Assert.Equal(new string(' ', 60), Text(buffer.CaptureFrame()));
    }

    [Fact]
    public void C1_NelAndRi_KeepControlMeaning()
    {
        var buffer = new ScreenBuffer(10, 3);
        var parser = new VtParser(buffer);
        parser.Feed(Encoding.ASCII.GetBytes("ab").Concat(new byte[] { 0x85 }).Concat(Encoding.ASCII.GetBytes("cd")).ToArray());
        Assert.StartsWith("ab", Text(buffer.CaptureFrame()));
        Assert.Equal('c', buffer.CaptureFrame().Cells[10].Char);
        // RI (0x8D) at the top scrolls the content down instead of printing U+FFFD.
        parser.Feed(Encoding.ASCII.GetBytes("\u001b[H").Concat(new byte[] { 0x8D }).ToArray());
        Assert.Equal('a', buffer.CaptureFrame().Cells[10].Char);
    }

    [Fact]
    public void KittyFlagStack_CappedAt64()
    {
        var buffer = new ScreenBuffer();
        // An app looping CSI >u must not grow the stack without bound: past 64
        // the oldest saved entries are forgotten.
        for (var i = 1; i <= 200; i++) buffer.KittyPush(i);
        buffer.KittyPop(200);
        // Entries 136..199 were retained (0..135 evicted): draining the stack
        // leaves the oldest retained value, not the original 0.
        Assert.Equal(136, buffer.KittyKeyboardFlags);
    }

    [Fact]
    public void EraseInLine_StartOnContinuation_ClearsWholePair()
    {
        var buffer = new ScreenBuffer(4, 2);
        var parser = new VtParser(buffer);
        parser.Feed("中中");               // 4 cells: two wide pairs
        // EL0 starting on the first pair's continuation: the torn lead must be
        // cleared too (xterm erases the whole glyph), not left as a half-width ghost.
        parser.Feed("\x1b[2G\x1b[K");
        var frame = buffer.CaptureFrame();
        for (var c = 0; c < 4; c++)
        {
            Assert.False(frame.Cells[c].IsWide);
            Assert.False(frame.Cells[c].IsWideContinuation);
            Assert.Equal(' ', frame.Cells[c].Char);
        }
    }

    [Fact]
    public void EraseChars_OnContinuation_ClearsDanglingLead()
    {
        var buffer = new ScreenBuffer(4, 2);
        var parser = new VtParser(buffer);
        parser.Feed("中A");
        // Cursor onto the '中' continuation (col 1), ECH 1: the lead at col 0
        // loses its partner and must not render as a half-width ghost.
        parser.Feed("\x1b[2G\x1b[X");
        var frame = buffer.CaptureFrame();
        for (var c = 0; c < 8; c++)
        {
            Assert.False(frame.Cells[c].IsWideContinuation);
        }
        Assert.DoesNotContain(frame.Cells.Take(8), cell => cell.IsWide && cell.Char != ' ');
    }

    [Fact]
    public void DeleteChars_PullingContinuationToStart_ClearsOrphan()
    {
        var buffer = new ScreenBuffer(4, 2);
        var parser = new VtParser(buffer);
        // 中@0-1, A@2, B@3 — DCH at col 0 pulls the continuation to col 0,
        // an orphan that used to shift the rest of the row right by one cell.
        parser.Feed("中AB\x1b[H\x1b[P");
        var frame = buffer.CaptureFrame();
        Assert.False(frame.Cells[0].IsWideContinuation);
        Assert.Equal('A', frame.Cells[1].Char);
        Assert.Equal('B', frame.Cells[2].Char);
    }

    private sealed class RecordingPty : IPtySession
    {
        public string LastWrite = "";
        public Guid Id { get; } = Guid.NewGuid();
        public bool IsRunning => true;
        public int? ExitCode => null;
        public int? ProcessId => null;
        public event Action<IPtySession, ReadOnlyMemory<byte>>? OutputReceived { add { } remove { } }
        public event Action<IPtySession, int>? Exited { add { } remove { } }
        public Task StartAsync(PtyOptions options, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Write(ReadOnlySpan<byte> data) => LastWrite = Encoding.UTF8.GetString(data);
        public void Resize(int columns, int rows) { }
        public void Kill() { }
        public void Dispose() { }
    }
}
