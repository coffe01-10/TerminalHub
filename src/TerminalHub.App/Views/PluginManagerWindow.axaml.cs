using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using TerminalHub.App.Localization;
using TerminalHub.App.Plugins;
using TerminalHub.App.ViewModels;
using TerminalHub.Core.Localization;
using TerminalHub.Extensibility;

namespace TerminalHub.App.Views;

public partial class PluginManagerWindow : Window
{
    private readonly MainWindow? _main;
    private readonly MainWindowViewModel? _vm;
    private readonly PluginManager? _manager;
    private bool _saving, _rendering;
    public PluginManagerWindow() => InitializeComponent();
    public PluginManagerWindow(MainWindow main, MainWindowViewModel vm, PluginManager manager) : this()
    {
        _main = main; _vm = vm; _manager = manager; DataContext = vm;
        manager.Changed += Render;
        Closed += (_, _) => { manager.Changed -= Render; ClearSettings(); };
        Render();
    }
    private static TextBlock Text(string text, string brush = "UiInk", double size = 13, bool localized = true)
    {
        var label = new TextBlock { FontSize = size, TextWrapping = TextWrapping.Wrap };
        label.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension(brush));
        if (localized) label.Bind(TextBlock.TextProperty, UiText.Binding(text)); else label.Text = text;
        return label;
    }
    private static Border Card(Control content) => new() { Classes = { "module-card" }, Child = content };
    private static Control Empty(string title, string detail)
    {
        var panel = new StackPanel { Spacing = 10, Margin = new(16,36), MaxWidth = 480, HorizontalAlignment = HorizontalAlignment.Center };
        var icon = Text("◇", "UiFaint", 32, false); icon.HorizontalAlignment = HorizontalAlignment.Center;
        var heading = Text(title, size: 16); heading.FontWeight = FontWeight.SemiBold; heading.HorizontalAlignment = HorizontalAlignment.Center;
        var caption = Text(detail, "UiMuted", 12); caption.TextAlignment = TextAlignment.Center;
        panel.Children.Add(icon); panel.Children.Add(heading); panel.Children.Add(caption); return Card(panel);
    }
    private Button Action(string label, Action callback, string? style = null)
    {
        var button = new Button { [!ContentControl.ContentProperty] = UiText.Binding(label) };
        if (style is not null) button.Classes.Add(style);
        button.Click += (_, _) => { try { callback(); Notice(_manager!.LastError); } catch (Exception ex) { Notice(ex.Message); } };
        return button;
    }
    private void Notice(string text) { ErrorText.Text = text; ErrorNotice.IsVisible = text.Length > 0; }
    private void ClearSettings()
    {
        foreach (var card in SettingCards.Children.OfType<Border>())
            if (card.Child is StackPanel panel) panel.Children.Clear();
        SettingCards.Children.Clear();
    }
    private void Save(Action change)
    {
        // Saving a field must not rebuild the focused editor while the user types.
        _saving = true;
        try { change(); _manager!.SaveModules(); }
        finally { _saving = false; }
    }
    private void Render()
    {
        if (_manager is null || _vm is null || _saving || _rendering) return;
        _rendering = true;
        try
        {
            PluginCards.Children.Clear(); ModuleCards.Children.Clear(); ClearSettings();
            var filter = Math.Max(0, MarketFilter.SelectedIndex);
            MarketFilter.ItemsSource = new[] { "全部", "已安装", "未安装", "内置基础工具", "官方扩展" }.Select(Translate).ToArray();
            MarketFilter.SelectedIndex = filter;
            Notice(_manager.LastError);
            RenderMarket(); RenderModulePreferences(); RenderPluginPreferences();
        }
        finally { _rendering = false; }
    }
    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        try
        {
            var folders = await _main!.StorageProvider.OpenFolderPickerAsync(new() { Title = Localizer.Current.Translate("选择包含 plugin.json 的插件目录"), AllowMultiple = false });
            if (folders.FirstOrDefault()?.TryGetLocalPath() is { } source) _manager!.Import(source);
        }
        catch (Exception ex) { Notice(ex.Message); }
    }
    private void OnRefresh(object? sender, RoutedEventArgs e) => _manager!.Discover();
    private async void OnDevelopmentDocs(object? sender, RoutedEventArgs e) => await OpenDocumentation("develop");
    private async void OnOfficialPlugins(object? sender, RoutedEventArgs e) => await OpenDocumentation("official");
    private async Task OpenDocumentation(string section)
    {
        try
        {
            var development = section == "develop";
            var english = Localizer.Current.Language == "en";
            var tutorial = english ? "development-tutorial.en" : "development-tutorial";
            var manual = english ? "index.en" : "index";
            var path = Path.Combine(AppContext.BaseDirectory, "docs", "plugins", (development ? tutorial : manual) + ".html");
            var uri = File.Exists(path) ? new Uri(new Uri(path).AbsoluteUri + "#" + (development ? "start" : section))
                : new Uri(development
                    ? "https://github.com/coffe01-10/TerminalHub/blob/main/docs/plugins/" + tutorial + ".md"
                    : "https://github.com/coffe01-10/TerminalHub/blob/main/docs/plugins/" + (english ? "README.en.md" : "README.md"));
            if (!await Launcher.LaunchUriAsync(uri)) Notice(uri.AbsoluteUri);
        }
        catch (Exception ex) { Notice(ex.Message); }
    }
    private void OnOpenDirectory(object? sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_manager!.DirectoryPath);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_manager.DirectoryPath) { UseShellExecute = true });
        }
        catch (Exception ex) { Notice(ex.Message); }
    }
}
