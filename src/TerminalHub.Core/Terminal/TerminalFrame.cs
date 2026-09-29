namespace TerminalHub.Core.Terminal;

/// <summary>A stable screen image. Rendering never reads cells while the PTY rewrites them.</summary>
public sealed record TerminalFrame(int Columns, int Rows, TerminalCell[] Cells,
    int CursorX, int CursorY, bool CursorVisible, bool AlternateScreen);
