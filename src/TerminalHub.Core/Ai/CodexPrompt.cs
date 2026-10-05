using System.Text;
using TerminalHub.Core.Terminal;

namespace TerminalHub.Core.Ai;

/// <summary>Frame-based recognition of the Codex editing composer — the
/// detector the task panel uses to report "awaiting input". Ported from the
/// test helper in WindowsCodexEditingTests so the product can use it.</summary>
public static class CodexPrompt
{
    /// <summary>The editing composer is up when the screen shows the Codex
    /// header, carries no login/trust blocker, and the visible VT cursor sits
    /// on the '› ' composer row in the cell right after the arrow — with the
    /// row still empty or holding only the placeholder text. Works for both
    /// the old 'model:'-era UI and the 0.158.x layout without a model line.</summary>
    public static bool IsEditing(TerminalFrame frame)
    {
        var screen = ScreenText(frame);
        if (!screen.Contains("OpenAI Codex", StringComparison.Ordinal)) return false;
        if (screen.Contains("Sign in with ChatGPT", StringComparison.OrdinalIgnoreCase)) return false;
        if (screen.Contains("trust this", StringComparison.OrdinalIgnoreCase)) return false;
        if (!frame.CursorVisible) return false;
        var row = ScreenBuffer.FlattenRow(frame.Cells.AsSpan(frame.CursorY * frame.Columns, frame.Columns)).Text;
        var arrow = row.IndexOf('›');
        if (arrow < 0) return false;
        if (frame.CursorX != arrow + 2) return false;
        var tail = row[(arrow + 1)..].Trim();
        return tail.Length == 0 || tail == "Ask Codex to do anything";
    }

    private static string ScreenText(TerminalFrame frame)
    {
        var all = new StringBuilder();
        for (var r = 0; r < frame.Rows; r++)
            all.Append(ScreenBuffer.FlattenRow(frame.Cells.AsSpan(r * frame.Columns, frame.Columns)).Text).Append('\n');
        return all.ToString();
    }
}
