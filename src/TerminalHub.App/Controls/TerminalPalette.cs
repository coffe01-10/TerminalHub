using Avalonia.Media;
using TerminalHub.Core.Terminal;

namespace TerminalHub.App.Controls;

public sealed record TerminalColors(Color Foreground, Color Background, Color Cursor,
    Color Selection, Color Match, Color CurrentMatch, bool IsLight);

/// <summary>Campbell-style palette + 256-color cube resolution for terminal cells.</summary>
public static class TerminalPalette
{
    public static TerminalColors ThemeColors => new(DefaultFg, DefaultBg, CursorColor,
        SelectionColor, MatchColor, MatchCurrentColor, Brightness(DefaultBg) >= 128);
    private static readonly TerminalColors DarkColors = new(Color.Parse("#D8DDE7"), Color.Parse("#0C1018"),
        Color.FromArgb(0xB4, 0x38, 0xBD, 0xF8), Color.FromArgb(0x48, 0x38, 0xBD, 0xF8),
        Color.FromArgb(0x50, 0xF5, 0x9E, 0x0B), Color.FromArgb(0xA0, 0xF5, 0x9E, 0x0B), false);
    private static readonly TerminalColors LightColors = new(Color.Parse("#1E2D41"), Color.Parse("#FAFCFF"),
        Color.FromArgb(0xDC, 0x1D, 0x4E, 0xD8), Color.FromArgb(0x55, 0x1D, 0x4E, 0xD8),
        Color.FromArgb(0x60, 0xD9, 0x77, 0x06), Color.FromArgb(0xB0, 0xB4, 0x53, 0x09), true);

    public static TerminalColors ForFrame(TerminalFrame frame, TerminalColorScheme scheme)
    {
        if (scheme == TerminalColorScheme.Dark) return DarkColors;
        if (scheme == TerminalColorScheme.Light) return LightColors;
        if (scheme == TerminalColorScheme.FollowTheme) return ThemeColors;
        // Grok's actual ConPTY output paints #141414 over the full screen.
        // A single colored prompt/log line must not change the whole session.
        var backgrounds = new Dictionary<TerminalColor, int>();
        foreach (var cell in frame.Cells)
        {
            if (cell.Bg.IsDefault || cell.Attrs.HasFlag(CellAttrs.Inverse)) continue;
            backgrounds[cell.Bg] = backgrounds.GetValueOrDefault(cell.Bg) + 1;
        }
        foreach (var (background, count) in backgrounds)
            if (count > frame.Cells.Length * 3 / 4)
            {
                var color = Resolve(background, false, DarkColors);
                return (Brightness(color) < 128 ? DarkColors : LightColors) with { Background = color };
            }
        return ThemeColors;
    }

    private static int Brightness(Color color) => (color.R * 3 + color.G * 6 + color.B) / 10;
    public static Color DefaultFg = Color.FromRgb(0xCC, 0xCC, 0xCC);
    public static Color DefaultBg = Color.FromRgb(0x0C, 0x0C, 0x0C);

    /// <summary>Block-cursor fill / IME anchor accent.</summary>
    public static Color CursorColor { get; private set; } = Color.FromArgb(0xB4, 0x38, 0xBD, 0xF8);
    /// <summary>Translucent fill under a mouse selection.</summary>
    public static Color SelectionColor { get; private set; } = Color.FromArgb(0x48, 0x38, 0xBD, 0xF8);
    /// <summary>Search-match highlight inside the terminal viewport.</summary>
    public static Color MatchColor { get; private set; } = Color.FromArgb(0x50, 0xF5, 0x9E, 0x0B);
    /// <summary>The current search hit (navigated to).</summary>
    public static Color MatchCurrentColor { get; private set; } = Color.FromArgb(0xA0, 0xF5, 0x9E, 0x0B);

    public static void SetTheme(string theme)
    {
        var light = theme is "Paper" or "White";
        DefaultFg = Color.Parse(theme is "Paper" ? "#3C352B" : theme is "White" ? "#1E2D41" : "#D8DDE7");
        DefaultBg = Color.Parse(theme is "Paper" ? "#FCF8EE" : theme is "White" ? "#FAFCFF" : theme is "Black" ? "#050607" : "#0C1018");
        // Light themes need a deeper accent or cursor/selection wash out on paper.
        CursorColor = light ? Color.FromArgb(0xDC, 0x1D, 0x4E, 0xD8) : Color.FromArgb(0xB4, 0x38, 0xBD, 0xF8);
        SelectionColor = light ? Color.FromArgb(0x55, 0x1D, 0x4E, 0xD8) : Color.FromArgb(0x48, 0x38, 0xBD, 0xF8);
        MatchColor = light ? Color.FromArgb(0x60, 0xD9, 0x77, 0x06) : Color.FromArgb(0x50, 0xF5, 0x9E, 0x0B);
        MatchCurrentColor = light ? Color.FromArgb(0xB0, 0xB4, 0x53, 0x09) : Color.FromArgb(0xA0, 0xF5, 0x9E, 0x0B);
    }

    public static string QueryDefaultColor(bool foreground) => QueryDefaultColor(foreground, ThemeColors);

    public static string QueryDefaultColor(bool foreground, TerminalColors colors)
    {
        var color = foreground ? colors.Foreground : colors.Background;
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

    public static Color Resolve(TerminalColor color, bool isForeground, TerminalColors? colors = null)
    {
        colors ??= ThemeColors;
        return color.Kind switch
        {
            TerminalColor.ColorKind.Indexed => ResolveIndexed(color.Value, colors.IsLight),
            TerminalColor.ColorKind.Rgb => Color.FromRgb(
                (byte)(color.Value >> 16), (byte)(color.Value >> 8), (byte)color.Value),
            _ => isForeground ? colors.Foreground : colors.Background,
        };
    }

    private static Color ResolveIndexed(int i, bool light)
    {
        // Malformed SGR (e.g. "38;5;" with an empty color slot → -1) or
        // out-of-range values must never index past the tables.
        i = Math.Clamp(i, 0, 255);
        if (i < 16) return light ? Light16[i] : First16[i];
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
