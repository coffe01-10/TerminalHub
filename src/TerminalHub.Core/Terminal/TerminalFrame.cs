namespace TerminalHub.Core.Terminal;

/// <summary>A stable screen image. Rendering never reads cells while the PTY rewrites them.
/// <paramref name="BaseLine"/> is the absolute buffer line shown at visual row 0
/// (scrollbackCount - view offset at capture time).</summary>
public sealed record TerminalFrame(int Columns, int Rows, TerminalCell[] Cells,
    int CursorX, int CursorY, bool CursorVisible, bool AlternateScreen, int BaseLine = 0);
