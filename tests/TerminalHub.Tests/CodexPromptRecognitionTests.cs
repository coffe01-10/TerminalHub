using TerminalHub.Core.Terminal;
using Xunit;

namespace TerminalHub.Tests;

/// <summary>VT-replay recognition of the Codex editing composer. No real
/// account, login or CLI process is involved — the observed layouts are fed
/// through the real parser and judged by
/// <see cref="WindowsCodexEditingTests.HasEditingPrompt"/>.</summary>
public class CodexPromptRecognitionTests
{
    private static TerminalFrame Screen(string vt, int cols = 100, int rows = 28)
    {
        using var terminal = new TerminalEmulator(columns: cols, rows: rows);
        terminal.Parser.Feed(vt);
        return terminal.Buffer.CaptureFrame();
    }

    [Fact]
    public void NewUi_ComposerArrow_WithoutModelLine_IsPrompt()
        // 0.158.x layout captured live: ' >_' header row 2 col 3, composer
        // '› Ask Codex to do anything' on row 10, cursor at col 3 — no model:.
        => Assert.True(WindowsCodexEditingTests.HasEditingPrompt(Screen(
            "\x1b[2J\x1b[H\x1b[2;3H>_ OpenAI Codex (v0.158.0)\x1b[10;1H› Ask Codex to do anything\x1b[10;3H")));

    [Fact]
    public void OldUi_ModelLine_Composer_IsPrompt()
        => Assert.True(WindowsCodexEditingTests.HasEditingPrompt(Screen(
            "\x1b[2J\x1b[H\x1b[2;1HOpenAI Codex\x1b[4;1Hmodel: gpt-5\x1b[10;1H› \x1b[10;3H")));

    [Fact]
    public void SignInComposer_IsNotPrompt()
        => Assert.False(WindowsCodexEditingTests.HasEditingPrompt(Screen(
            "\x1b[2J\x1b[H\x1b[2;3H>_ OpenAI Codex (v0.158.0)\x1b[10;1H› Sign in with ChatGPT\x1b[10;3H")));

    [Fact]
    public void TrustPrompt_IsNotPrompt()
        => Assert.False(WindowsCodexEditingTests.HasEditingPrompt(Screen(
            "\x1b[2J\x1b[H\x1b[2;3H>_ OpenAI Codex (v0.158.0)\x1b[8;1Htrust this workspace?\x1b[10;1H› \x1b[10;3H")));

    [Fact]
    public void CursorOnAnotherRow_IsNotPrompt()
        => Assert.False(WindowsCodexEditingTests.HasEditingPrompt(Screen(
            "\x1b[2J\x1b[H\x1b[2;3H>_ OpenAI Codex (v0.158.0)\x1b[10;1H› Ask Codex to do anything\x1b[5;1H")));
}
