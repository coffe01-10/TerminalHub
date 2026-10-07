using System.Text;
using TerminalHub.Core.Ai;
using TerminalHub.Core.Terminal;
using Xunit;

namespace TerminalHub.Tests;

/// <summary>Protocol and attention support that makes AI CLIs usable: OSC 52
/// clipboard copy, ConEmu progress, the kitty key encoding AI CLIs negotiate,
/// and "needs you" detection. Everything is VT replay — no live CLI or account.</summary>
public class AiTerminalAdaptationTests
{
    private static TerminalFrame Screen(string vt, int cols = 100, int rows = 28)
    {
        using var terminal = new TerminalEmulator(columns: cols, rows: rows);
        terminal.Parser.Feed(vt);
        return terminal.Buffer.CaptureFrame();
    }

    [Fact]
    public void Osc52_DecodesClipboardCopy_AndRaisesItOutsideTheLock()
    {
        var copies = new List<string>();
        var buffer = new ScreenBuffer();
        var parser = new VtParser(buffer);
        parser.ClipboardCopyRequested += copies.Add;
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("复制这段"));
        parser.Feed($"\x1b]52;c;{payload}\a");
        Assert.Equal(["复制这段"], copies);
    }

    [Fact]
    public void Osc52_PasteQuery_IsIgnored()
    {
        var copies = new List<string>();
        var parser = new VtParser(new ScreenBuffer());
        parser.ClipboardCopyRequested += copies.Add;
        parser.Feed("\x1b]52;c;?\a");
        Assert.Empty(copies);
    }

    [Theory]
    [InlineData("")]
    [InlineData("c;")]
    public void Osc52_EmptyPayload_Clears(string payload)
    {
        Assert.True(VtParser.TryDecodeOsc52(payload.Length == 0 ? "c;" : payload, out var text));
        Assert.Equal("", text);
    }

    [Fact]
    public void Osc52_BadBase64_IsDropped()
    {
        Assert.False(VtParser.TryDecodeOsc52("c;@@@", out _));
        Assert.False(VtParser.TryDecodeOsc52("p;YQ==", out _)); // selection doesn't include clipboard
    }

    [Fact]
    public void Progress_Osc94_SetsStateAndValue()
    {
        var buffer = new ScreenBuffer();
        var parser = new VtParser(buffer);
        var changes = 0;
        buffer.ProgressChanged += () => changes++;
        parser.Feed("\x1b]9;4;1;40\a");
        Assert.Equal(1, buffer.ProgressState);
        Assert.Equal(40, buffer.ProgressValue);
        parser.Feed("\x1b]9;4;0;0\a");
        Assert.Equal(0, buffer.ProgressState);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Progress_DoesNotClobberCwdReport()
    {
        var buffer = new ScreenBuffer();
        new VtParser(buffer).Feed("\x1b]9;9;D:\\项目\a");
        Assert.Equal("D:\\项目", buffer.Cwd);
        Assert.Equal(0, buffer.ProgressState);
    }

    [Fact]
    public void KittyEncoding_ModifiedEnter_OnlyAfterNegotiation()
    {
        Assert.Null(KittyKeyEncoding.Functional(0, 13, 2));
        Assert.Equal("\x1b[13;2u", KittyKeyEncoding.Functional(KittyKeyEncoding.Disambiguate, 13, 2));
        // A plain Enter still submits.
        Assert.Null(KittyKeyEncoding.Functional(KittyKeyEncoding.Disambiguate, 13, 1));
    }

    [Fact]
    public void KittyEncoding_CtrlLetter_KeepsCtrlAndAltApart()
    {
        Assert.Null(KittyKeyEncoding.TextKey(0, 'c', 5));
        Assert.Equal("\x1b[c;5u", KittyKeyEncoding.TextKey(KittyKeyEncoding.Disambiguate, 'c', 5));
        Assert.Equal("\x1b[c;7u", KittyKeyEncoding.TextKey(KittyKeyEncoding.Disambiguate, 'c', 7));
    }

    [Fact]
    public void ClaudePermissionPrompt_NeedsYou()
    {
        var frame = Screen("\x1b[2J\x1b[H\x1b[4;1HBash command\x1b[6;1HDo you want to proceed?\x1b[8;1H1. Yes");
        Assert.Equal(AiAttention.NeedsYou, AiAttentionDetector.Detect(frame, 0));
    }

    [Fact]
    public void ClaudeTrustPrompt_NeedsYou()
    {
        var frame = Screen("\x1b[2J\x1b[H\x1b[4;1HDo you trust the files in this folder?");
        Assert.Equal(AiAttention.NeedsYou, AiAttentionDetector.Detect(frame, 0));
    }

    [Fact]
    public void OrdinaryOutput_StaysUnknown()
    {
        // Mentioning the prompt wording inside other text must not fire.
        var frame = Screen("\x1b[2J\x1b[H\x1b[2;1Hbuild failed: see Do you want to proceed? in the docs");
        Assert.Equal(AiAttention.Unknown, AiAttentionDetector.Detect(frame, 0));
    }

    [Fact]
    public void ProgressError_NeedsYou_ProgressBusy_IsWorking()
    {
        var frame = Screen("");
        Assert.Equal(AiAttention.NeedsYou, AiAttentionDetector.Detect(frame, 2));
        Assert.Equal(AiAttention.Working, AiAttentionDetector.Detect(frame, 3));
    }

    [Fact]
    public void CodexSignIn_NeedsYou_ButIsNotAnEditingPrompt()
    {
        var frame = Screen("\x1b[2J\x1b[H\x1b[2;3H>_ OpenAI Codex (v0.158.0)\x1b[10;1H› Sign in with ChatGPT\x1b[10;3H");
        Assert.False(CodexPrompt.IsEditing(frame));
        Assert.Equal(AiAttention.NeedsYou, AiAttentionDetector.Detect(frame, 0));
    }
}
