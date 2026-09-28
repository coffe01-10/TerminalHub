namespace TerminalHub.Core.Terminal;

/// <summary>Cell foreground/background color: default, 256-palette index, or 24-bit RGB.</summary>
public readonly record struct TerminalColor
{
    public enum ColorKind : byte { Default, Indexed, Rgb }

    public ColorKind Kind { get; }
    /// <summary>Palette index when Kind=Indexed; packed 0xRRGGBB when Kind=Rgb.</summary>
    public int Value { get; }

    private TerminalColor(ColorKind kind, int value) => (Kind, Value) = (kind, value);

    public static readonly TerminalColor Default = new(ColorKind.Default, 0);
    public static TerminalColor Indexed(int index) => new(ColorKind.Indexed, index);
    public static TerminalColor Rgb(int r, int g, int b) => new(ColorKind.Rgb, (r << 16) | (g << 8) | b);

    public bool IsDefault => Kind == ColorKind.Default;
}
