using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TerminalHub.Extensibility;

namespace TerminalHub.Official;

internal sealed class PluginUi(IPluginContext context)
{
    private readonly List<Action> _translations = [];
    public void Translate() { foreach (var update in _translations) update(); }
    public string T(string key, string fallback) => context.Text(key, fallback);
    public void Languages(params (string Key, string Zh, string En)[] messages)
        => context.RegisterLocalization("zh-CN", new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["zh-CN"] = messages.ToDictionary(m => m.Key, m => m.Zh),
            ["en"] = messages.ToDictionary(m => m.Key, m => m.En)
        });
    public TextBlock Label(string text = "", string brush = "UiInk", double size = 13)
    {
        var label = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
        label.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension(brush)); return label;
    }
    public TextBlock LocalLabel(string key, string fallback, string brush = "UiInk", double size = 13)
    {
        var label = Label(brush: brush, size: size);
        void Update() => label.Text = T(key, fallback);
        _translations.Add(Update); Update(); return label;
    }
    public Button Button(string name, string key, string fallback, Action action, bool primary = false)
    {
        var button = new Button { Name = name, Padding = new(14, 9), CornerRadius = new(7), Margin = new(0, 0, 8, 8) };
        button.Bind(Avalonia.Controls.Button.BackgroundProperty, new DynamicResourceExtension(primary ? "UiAccent" : "UiRaised"));
        button.Bind(Avalonia.Controls.Button.ForegroundProperty, new DynamicResourceExtension(primary ? "UiOnAccent" : "UiInk"));
        button.Bind(Avalonia.Controls.Button.BorderBrushProperty, new DynamicResourceExtension("UiBorder"));
        void Update() => button.Content = T(key, fallback);
        _translations.Add(Update); Update();
        button.Click += (_, _) => { try { action(); } catch (Exception ex) { context.ReportError(ex); } };
        return button;
    }
    public TextBox Editor(string name, bool readOnly = false)
    {
        var editor = new TextBox { Name = name, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            IsReadOnly = readOnly, Padding = new(14), CornerRadius = new(8), VerticalAlignment = VerticalAlignment.Stretch };
        editor.Bind(TextBox.BackgroundProperty, new DynamicResourceExtension("UiInset"));
        editor.Bind(TextBox.ForegroundProperty, new DynamicResourceExtension("UiInk"));
        editor.Bind(TextBox.BorderBrushProperty, new DynamicResourceExtension("UiBorder"));
        return editor;
    }
    public Grid Page(string titleKey, string title, string detailKey, string detail, Control body, Control? actions = null)
    {
        var page = new Grid { Margin = new(18), RowDefinitions = new("Auto,Auto,*"), RowSpacing = 14 };
        var heading = new StackPanel { Spacing = 7 };
        var label = LocalLabel(titleKey, title, size: 23); label.FontWeight = FontWeight.SemiBold;
        heading.Children.Add(label); heading.Children.Add(LocalLabel(detailKey, detail, "UiMuted", 12)); page.Children.Add(heading);
        if (actions is not null) { Grid.SetRow(actions, 1); page.Children.Add(actions); }
        Grid.SetRow(body, 2); page.Children.Add(body); return page;
    }
    public Border Card(Control content) => new() { Child = content, Padding = new(14), CornerRadius = new(8),
        BorderThickness = new(1), [!Border.BackgroundProperty] = new DynamicResourceExtension("UiSurface"),
        [!Border.BorderBrushProperty] = new DynamicResourceExtension("UiBorder") };
}
