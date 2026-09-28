namespace TerminalHub.Core.Terminal;

[Flags]
public enum CellAttrs : ushort
{
    None = 0,
    Bold = 1,
    Dim = 2,
    Italic = 4,
    Underline = 8,
    Blink = 16,
    Inverse = 32,
    Hidden = 64,
    Strike = 128,
}

/// <summary>One character cell on the terminal grid.</summary>
public struct TerminalCell
{
    public char Char;
    public CellAttrs Attrs;
    public TerminalColor Fg;
    public TerminalColor Bg;
    /// <summary>First cell of a double-width glyph.</summary>
    public bool IsWide;
    /// <summary>Padding cell after a double-width glyph.</summary>
    public bool IsWideContinuation;

    public static TerminalCell Blank(TerminalColor bg) =>
        new() { Char = ' ', Attrs = CellAttrs.None, Fg = TerminalColor.Default, Bg = bg };

    public TerminalCell Clone() => this;
}
