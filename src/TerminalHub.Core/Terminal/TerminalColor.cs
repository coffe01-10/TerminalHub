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

    /// <summary>
    /// CSS-style "#RRGGBB" for previews; <c>null</c> for the terminal default color
    /// (the caller falls back to its own muted text brush).
    /// </summary>
    public string? ToRgbHex() => Kind switch
    {
        ColorKind.Default => null,
        ColorKind.Rgb => $"#{(Value & 0xFFFFFF):X6}",
        _ => $"#{IndexToRgb(Value):X6}",
    };

    /// <summary>xterm 256-palette: 0-15 standard, 16-231 color cube, 232-255 grayscale.</summary>
    private static int IndexToRgb(int i)
    {
        if (i < 0) i = 0;
        if (i < 16) return Standard16[Math.Min(i, 15)];
        if (i >= 232) { var v = 8 + (Math.Min(i, 255) - 232) * 10; return (v << 16) | (v << 8) | v; }
        var n = i - 16;
        var r = n / 36 % 6; var g = n / 6 % 6; var b = n % 6;
        return (Cube(r) << 16) | (Cube(g) << 8) | Cube(b);
        static int Cube(int c) => c == 0 ? 0 : 55 + 40 * c;
    }

    private static readonly int[] Standard16 =
    [
        0x000000, 0xCD0000, 0x00CD00, 0xCDCD00, 0x0000EE, 0xCD00CD, 0x00CDCD, 0xE5E5E5,
        0x7F7F7F, 0xFF0000, 0x00FF00, 0xFFFF00, 0x5C5CFF, 0xFF00FF, 0x00FFFF, 0xFFFFFF,
    ];
}
