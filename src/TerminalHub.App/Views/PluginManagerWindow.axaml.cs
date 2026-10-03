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
            PluginCount.Text = $"{_manager.Plugins.Count(p => p.Enabled)} / {_manager.Plugins.Count}";
            ToolTip.SetTip(PluginCount, Localizer.Current.Translate("已启用 / 已安装"));
            Notice(_manager.LastError);
            if (_manager.Plugins.Count == 0) PluginCards.Children.Add(Empty("让工作台更合你的习惯", "导入本地插件，添加工具或调整工作台界面。"));
            foreach (var plugin in _manager.Plugins)
            {
                var body = new Grid { RowDefinitions = new("Auto,Auto"), RowSpacing = 14 };
                var top = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 16 };
                var identity = new StackPanel { Spacing = 5 };
                var title = Text(plugin.Manifest.Name, size: 15, localized: false); title.FontWeight = FontWeight.SemiBold;
                identity.Children.Add(title); identity.Children.Add(Text(plugin.Manifest.Id + "  ·  v" + plugin.Manifest.Version, "UiMuted", 11, false));
                top.Children.Add(identity);
                var status = Text(plugin.Error.Length > 0 ? "加载失败" : plugin.Enabled ? "已启用" : "已停用", plugin.Error.Length > 0 ? "UiBad" : plugin.Enabled ? "UiGood" : "UiMuted", 11);
                var badge = new Border { Background = Brushes.Transparent, CornerRadius = new(6), Padding = new(9,5), Child = status, VerticalAlignment = VerticalAlignment.Top };
                badge.Bind(Border.BackgroundProperty, new DynamicResourceExtension("UiRaised")); Grid.SetColumn(badge,1); top.Children.Add(badge); body.Children.Add(top);
                var bottom = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 16 };
                if (plugin.Error.Length > 0) bottom.Children.Add(Text(plugin.Error, "UiBad", 12, false));
                var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                actions.Children.Add(Action(plugin.Enabled ? "禁用" : "启用", () => { if (plugin.Enabled) _manager.Disable(plugin); else _manager.Enable(plugin); }, plugin.Enabled ? null : "primary"));
                actions.Children.Add(Action("移除", () => _manager.Remove(plugin), "danger"));
                Grid.SetColumn(actions,1); bottom.Children.Add(actions); Grid.SetRow(bottom,1); body.Children.Add(bottom); PluginCards.Children.Add(Card(body));
            }
            foreach (var module in _manager.Modules.OrderBy(m => _manager.Settings(m).Order ?? m.Definition.Order))
            {
                var state = _manager.Settings(module); var workspaceId = _vm.ActiveWorkspace.Id;
                var body = new Grid { RowDefinitions = new("Auto,Auto"), RowSpacing = 12 };
                var identity = new StackPanel { Spacing = 5 };
                var title = Text(module.ToString(), size: 14, localized: false); title.FontWeight = FontWeight.SemiBold; identity.Children.Add(title);
                var owner = module.Owner == "builtin" ? Localizer.Current.Translate("内置工具")
                    : _manager.Plugins.First(p => p.Manifest.Id == module.Owner).Manifest.Name;
                identity.Children.Add(Text(owner, "UiMuted", 11, false)); body.Children.Add(identity);
                ToolTip.SetTip(title, module.Id);
                var controls = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 20 };
                var checks = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24 };
                var visible = new CheckBox { [!ContentControl.ContentProperty] = UiText.Binding("显示模块"), IsChecked = state.Visible };
                visible.IsCheckedChanged += (_, _) => Save(() => state.Visible = visible.IsChecked == true); checks.Children.Add(visible);
                var workspace = new CheckBox { [!ContentControl.ContentProperty] = UiText.Binding("当前工作区"), IsChecked = state.Workspaces.GetValueOrDefault(workspaceId,true) };
                ToolTip.SetTip(workspace, _vm.ActiveWorkspace.Name);
                workspace.IsCheckedChanged += (_, _) => Save(() => state.Workspaces[workspaceId] = workspace.IsChecked == true); checks.Children.Add(workspace); controls.Children.Add(checks);
                var ordering = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
                var caption = Text("排序", "UiMuted", 12); caption.VerticalAlignment = VerticalAlignment.Center; ordering.Children.Add(caption);
                var order = new NumericUpDown { Value = state.Order ?? module.Definition.Order, Width = 112, Increment = 1, FormatString = "0" };
                order.ValueChanged += (_, _) => Save(() => state.Order = (int)(order.Value ?? 0)); ordering.Children.Add(order);
                Grid.SetColumn(ordering,1); controls.Children.Add(ordering); Grid.SetRow(controls,1); body.Children.Add(controls); ModuleCards.Children.Add(Card(body));
            }
            if (_manager.Modules.Count == 0) ModuleCards.Children.Add(Empty("暂无可用模块", "启用插件后，可在这里调整模块的显示范围和顺序。"));
            foreach (var module in _manager.Visible(ExtensionSurface.Settings))
            {
                var body = new StackPanel { Spacing = 14 };
                var title = Text(module.ToString(), size: 15, localized: false); title.FontWeight = FontWeight.SemiBold;
                body.Children.Add(title); body.Children.Add(module.GetView()); SettingCards.Children.Add(Card(body));
            }
            if (SettingCards.Children.Count == 0) SettingCards.Children.Add(Empty("暂无插件设置", "支持设置的插件启用后，会在这里显示配置选项。"));
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
            var tutorial = Localizer.Current.Language == "en" ? "development-tutorial.en" : "development-tutorial";
            var path = Path.Combine(AppContext.BaseDirectory, "docs", "plugins", development ? tutorial + ".html" : "index.html");
            var uri = File.Exists(path) ? new Uri(new Uri(path).AbsoluteUri + "#" + (development ? "start" : section))
                : new Uri(development
                    ? "https://github.com/coffe01-10/TerminalHub/blob/main/docs/plugins/" + tutorial + ".md"
                    : "https://github.com/coffe01-10/TerminalHub/blob/main/docs/plugins/README.md");
            if (!await Launcher.LaunchUriAsync(uri)) Notice(uri.AbsoluteUri);
        }
        catch (Exception ex) { Notice(ex.Message); }
    }
    private void OnOpenDirectory(object? sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_manager!.DirectoryPath);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_manager.DirectoryPath) { UseShellExecute = true });
    }
}
