using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace TerminalHub.App.Controls;

public static class ThemeManager
{
    public static readonly string[] Names = ["DarkGlass", "Black", "White", "Paper"];
    public static string Current { get; private set; } = "DarkGlass";
    public static bool IsLight => Current is "White" or "Paper";
    public static event Action? Changed;
    private static readonly Dictionary<string, SolidColorBrush> Brushes = new();

    public static IBrush Brush(string role) => Brushes.TryGetValue(role, out var brush) ? brush : Avalonia.Media.Brushes.Gray;

    public static void Apply(string name)
    {
        Current = Names.Contains(name) ? name : "DarkGlass";
        var app = Application.Current;
        if (app is null) return;
        app.RequestedThemeVariant = IsLight ? ThemeVariant.Light : ThemeVariant.Dark;
        var colors = Current switch
        {
            "Black" => new[] { "#08090B", "#101114", "#1B1D21", "#050607", "#303238", "#E8E9ED", "#A0A3AC", "#757985", "#A9C8F5", "#242D3A", "#0A1423", "#67C69C", "#F17C82", "#DDAD62", "#B29BD9" },
            "White" => new[] { "#E9EEF4", "#FFFFFF", "#F0F4F9", "#FAFCFF", "#CCD6E2", "#1E2D41", "#526278", "#64758B", "#245AB5", "#E2EDFF", "#FFFFFF", "#19734B", "#B3293A", "#8A5A12", "#7755AA" },
            "Paper" => new[] { "#E7E0D1", "#F8F3E8", "#EEE6D7", "#FCF8EE", "#B4A58C", "#3C352B", "#625545", "#72634F", "#8C5132", "#EBDAC4", "#FFFAF0", "#466035", "#A44234", "#82621F", "#766184" },
            _ => new[] { "#0B111E", "#F0111926", "#1C2C3F", "#0C1018", "#52657B", "#E2E8F0", "#A8B9CC", "#7C91AA", "#65ACED", "#243E59", "#0A1524", "#34D399", "#F87171", "#FBBF24", "#A78BFA" }
        };
        string[] roles = ["Canvas", "Surface", "Raised", "Inset", "Border", "Ink", "Muted", "Faint", "Accent", "AccentSoft", "OnAccent", "Good", "Bad", "Warm", "Violet"];
        for (var i = 0; i < roles.Length; i++)
        {
            if (!Brushes.TryGetValue(roles[i], out var brush)) Brushes[roles[i]] = brush = new SolidColorBrush();
            brush.Color = Color.Parse(colors[i]);
            app.Resources["Ui" + roles[i]] = brush;
        }
        // Fluent popup/hover templates use their own resource keys. Map those
        // keys too, otherwise a Paper menu reverts to a cold grey Windows panel.
        void Map(string role, params string[] keys)
        {
            foreach (var key in keys) app.Resources[key] = Brushes[role];
        }
        Map("Surface", "MenuFlyoutPresenterBackground", "ComboBoxDropDownBackground");
        Map("Border", "MenuFlyoutPresenterBorderBrush", "ComboBoxDropDownBorderBrush");
        Map("Ink", "MenuFlyoutItemForeground", "MenuFlyoutItemForegroundPointerOver", "MenuFlyoutItemForegroundPressed",
            "ComboBoxItemForeground", "ComboBoxItemForegroundSelected", "ComboBoxItemForegroundSelectedPointerOver",
            "ComboBoxItemForegroundPointerOver", "ComboBoxItemForegroundPressed", "ToggleSwitchContentForeground");
        Map("AccentSoft", "MenuFlyoutItemBackgroundPointerOver", "MenuFlyoutItemBackgroundPressed",
            "ComboBoxItemBackgroundSelected", "ComboBoxItemBackgroundSelectedPointerOver", "ComboBoxItemBackgroundPointerOver");
        foreach (var state in new[] { "", "PointerOver", "Pressed" })
        {
            Map("Accent", "ToggleSwitchFillOn" + state, "ToggleSwitchStrokeOn" + state);
            Map("OnAccent", "ToggleSwitchKnobFillOn" + state);
            Map("Raised", "ToggleSwitchFillOff" + state);
            Map("Border", "ToggleSwitchStrokeOff" + state);
            Map("Muted", "ToggleSwitchKnobFillOff" + state);
            Map("Ink", "ButtonForeground" + state);
            Map(state == "" ? "Raised" : "AccentSoft", "ButtonBackground" + state, "ComboBoxBackground" + state);
            Map("Border", "ButtonBorderBrush" + state, "ComboBoxBorderBrush" + state);
            if (state != "Pressed")
            {
                Map("Ink", "TextControlForeground" + state);
                Map("Border", "TextControlBorderBrush" + state);
                Map("Inset", "TextControlBackground" + state);
            }
        }
        Map("Ink", "TextControlForegroundFocused", "ComboBoxForeground", "ComboBoxForegroundFocused", "ComboBoxForegroundFocusedPressed");
        Map("Raised", "ButtonBackgroundDisabled");
        Map("Border", "ButtonBorderBrushDisabled");
        Map("Faint", "ButtonForegroundDisabled");
        Map("Inset", "TextControlBackgroundFocused");
        Map("Accent", "TextControlBorderBrushFocused");
        Map("Ink", "TabItemHeaderForeground", "TabItemHeaderForegroundSelected", "TabItemHeaderForegroundPointerOver",
            "ListBoxItemForeground", "ListBoxItemForegroundSelected", "ListBoxItemForegroundSelectedPointerOver");
        Map("AccentSoft", "ListBoxItemBackgroundSelected", "ListBoxItemBackgroundSelectedPointerOver", "ListBoxItemBackgroundPointerOver");
        Map("Muted", "TextControlPlaceholderForeground", "TextControlPlaceholderForegroundFocused",
            "ComboBoxDropDownGlyphForeground", "ComboBoxPlaceHolderForeground");
        app.Resources["SurfaceCorner"] = new CornerRadius(Current == "Paper" ? 4 : Current == "Black" ? 10 : 18);
        app.Resources["CardCorner"] = new CornerRadius(Current == "Paper" ? 3 : Current == "Black" ? 7 : 13);
        app.Resources["DockCorner"] = new CornerRadius(Current == "Paper" ? 8 : 20);
        app.Resources["TerminalCorner"] = new CornerRadius(Current == "Paper" ? 2 : 10);
        app.Resources["SurfaceShadow"] = BoxShadows.Parse(Current switch
        {
            "Paper" => "0 3 12 0 #18816D50",
            "White" => "0 12 36 0 #20314766",
            "Black" => "0 8 24 0 #80000000",
            _ => "0 18 42 0 #60000000"
        });
        app.Resources["CardShadow"] = BoxShadows.Parse(Current switch
        {
            "Paper" => "1 4 5 0 #24816D50",
            "White" => "0 8 18 -3 #30314766, 0 1 3 0 #18314766",
            "Black" => "0 5 10 0 #90000000",
            _ => "0 10 20 -3 #80000000, 0 1 0 0 #305EB6FF"
        });
        app.Resources["ActiveCardShadow"] = BoxShadows.Parse(Current switch
        {
            "Paper" => "0 2 3 0 #28816D50",
            "White" => "0 3 8 0 #20314766",
            "Black" => "0 1 4 0 #60000000",
            _ => "0 3 12 0 #40258ED6"
        });
        app.Resources["HeadingFont"] = new FontFamily(Current == "Paper" ? "Georgia, Noto Serif, Microsoft YaHei UI, serif" : "Segoe UI, Noto Sans, sans-serif");
        TerminalPalette.SetTheme(Current);
        Changed?.Invoke();
    }
}
