using System.Text;

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

/// <summary>One character cell on the terminal grid. A cell holds a full
/// grapheme cluster: <see cref="Char"/> is the first UTF-16 unit and
/// <see cref="Tail"/> carries the rest (surrogate low half, combining marks,
/// ZWJ sequence members). Single-unit chars leave Tail null.</summary>
public struct TerminalCell
{
    public char Char;
    /// <summary>Remaining UTF-16 units of this cluster after <see cref="Char"/>.</summary>
    public string? Tail;
    public CellAttrs Attrs;
    public TerminalColor Fg;
    public TerminalColor Bg;
    /// <summary>First cell of a double-width glyph.</summary>
    public bool IsWide;
    /// <summary>Padding cell after a double-width glyph.</summary>
    public bool IsWideContinuation;

    /// <summary>Full cluster text of this cell.</summary>
    public readonly string Text => Tail is null ? Char.ToString() : Char + Tail;

    /// <summary>Append this cell's cluster text (' ' for an unwritten cell).</summary>
    public readonly void AppendText(StringBuilder sb)
    {
        if (Char == '\0') { sb.Append(' '); return; }
        sb.Append(Char);
        if (Tail is not null) sb.Append(Tail);
    }

    public static TerminalCell Blank(TerminalColor bg) =>
        new() { Char = ' ', Attrs = CellAttrs.None, Fg = TerminalColor.Default, Bg = bg };

    public TerminalCell Clone() => this;
}
