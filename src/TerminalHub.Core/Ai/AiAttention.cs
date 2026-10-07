using System.Text;
using TerminalHub.Core.Terminal;

namespace TerminalHub.Core.Ai;

/// <summary>What an AI CLI is doing right now, as far as the terminal can tell
/// without guessing. <see cref="NeedsYou"/> is the only state that should
/// interrupt the user: the CLI is blocked on a decision only they can make.</summary>
public enum AiAttention
{
    /// <summary>No signal either way — treat the session as an ordinary shell.</summary>
    Unknown,
    /// <summary>Working on its own (a progress indicator is up).</summary>
    Working,
    /// <summary>Blocked: a permission, trust or login prompt is on screen.</summary>
    NeedsYou,
}

/// <summary>Turns a screen frame plus the progress sequences the CLI sent into
/// an <see cref="AiAttention"/>. Protocol signals win over screen text, and
/// screen text is matched only against wording captured from real CLI output —
/// a generic "error" or a lone question mark is never enough.</summary>
public static class AiAttentionDetector
{
    /// <summary>Lines observed in Claude Code permission and trust prompts.
    /// Matched as whole lines so prose that merely mentions them doesn't fire.</summary>
    private static readonly string[] ClaudePromptLines =
    [
        "Do you want to proceed?",
        "Do you trust the files in this folder?",
    ];

    public static AiAttention Detect(TerminalFrame frame, int progressState)
    {
        // OSC 9;4: error (2) and warning (4) are the CLI saying it stopped and
        // needs a person. Determinate/indeterminate progress means it is busy.
        if (progressState is 2 or 4) return AiAttention.NeedsYou;
        if (progressState is 1 or 3) return AiAttention.Working;

        if (MatchesLine(frame, ClaudePromptLines)) return AiAttention.NeedsYou;
        if (CodexPrompt.IsBlocked(frame)) return AiAttention.NeedsYou;
        return AiAttention.Unknown;
    }

    private static bool MatchesLine(TerminalFrame frame, string[] lines)
    {
        for (var r = 0; r < frame.Rows; r++)
        {
            var text = ScreenBuffer.FlattenRow(frame.Cells.AsSpan(r * frame.Columns, frame.Columns)).Text.Trim();
            foreach (var line in lines)
                if (text.Equals(line, StringComparison.Ordinal)) return true;
        }
        return false;
    }
}
