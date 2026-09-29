using Avalonia.Media;
using TerminalHub.Core.Terminal;

namespace TerminalHub.App.Controls;

/// <summary>Campbell-style palette + 256-color cube resolution for terminal cells.</summary>
public static class TerminalPalette
{
    public static Color DefaultFg = Color.FromRgb(0xCC, 0xCC, 0xCC);
    public static Color DefaultBg = Color.FromRgb(0x0C, 0x0C, 0x0C);

    public static void SetTheme(string theme)
    {
        DefaultFg = Color.Parse(theme is "Paper" ? "#3C352B" : theme is "White" ? "#1E2D41" : "#D8DDE7");
        DefaultBg = Color.Parse(theme is "Paper" ? "#FCF8EE" : theme is "White" ? "#FAFCFF" : theme is "Black" ? "#050607" : "#0C1018");
    }

    public static string QueryDefaultColor(bool foreground)
    {
        var color = foreground ? DefaultFg : DefaultBg;
        return $"{color.R * 257:x4}/{color.G * 257:x4}/{color.B * 257:x4}";
    }

    private static readonly Color[] Light16 = Array.ConvertAll(new[] {
        "#20242B", "#B12632", "#187344", "#826014", "#255CAE", "#8550A6", "#167485", "#596573",
        "#707780", "#C03040", "#24763C", "#966200", "#306BC1", "#985AA7", "#157987", "#3E4B5B"
    }, Color.Parse);

    private static readonly Color[] First16 =
    [
        Color.FromRgb(0x0C, 0x0C, 0x0C), Color.FromRgb(0xC5, 0x0F, 0x1F),
        Color.FromRgb(0x13, 0xA1, 0x0E), Color.FromRgb(0xC1, 0x9C, 0x00),
        Color.FromRgb(0x00, 0x37, 0xDA), Color.FromRgb(0x88, 0x17, 0x98),
        Color.FromRgb(0x3A, 0x96, 0xDD), Color.FromRgb(0xCC, 0xCC, 0xCC),
        Color.FromRgb(0x76, 0x76, 0x76), Color.FromRgb(0xE7, 0x48, 0x56),
        Color.FromRgb(0x16, 0xC6, 0x0C), Color.FromRgb(0xF9, 0xF1, 0xA5),
        Color.FromRgb(0x3B, 0x78, 0xFF), Color.FromRgb(0xB4, 0x00, 0x9E),
        Color.FromRgb(0x61, 0xD6, 0xD6), Color.FromRgb(0xF2, 0xF2, 0xF2),
    ];

    public static Color Resolve(TerminalColor color, bool isForeground)
    {
        return color.Kind switch
        {
            TerminalColor.ColorKind.Indexed => ResolveIndexed(color.Value),
            TerminalColor.ColorKind.Rgb => Color.FromRgb(
                (byte)(color.Value >> 16), (byte)(color.Value >> 8), (byte)color.Value),
            _ => isForeground ? DefaultFg : DefaultBg,
        };
    }

    private static Color ResolveIndexed(int i)
    {
        // Malformed SGR (e.g. "38;5;" with an empty color slot → -1) or
        // out-of-range values must never index past the tables.
        i = Math.Clamp(i, 0, 255);
        if (i < 16) return ThemeManager.IsLight ? Light16[i] : First16[i];
        if (i < 232)
        {
            var v = i - 16;
            var r = CubeLevel(v / 36); var g = CubeLevel(v / 6 % 6); var b = CubeLevel(v % 6);
            return Color.FromRgb(r, g, b);
        }
        var gray = (byte)(8 + (i - 232) * 10);
        return Color.FromRgb(gray, gray, gray);

        static byte CubeLevel(int l) => l == 0 ? (byte)0 : (byte)(55 + l * 40);
    }
}
