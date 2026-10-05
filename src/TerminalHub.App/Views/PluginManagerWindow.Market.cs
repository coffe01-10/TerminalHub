using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using TerminalHub.App.Localization;
using TerminalHub.App.Plugins;
using TerminalHub.Core.Localization;
using TerminalHub.Core.Settings;
using TerminalHub.Extensibility;

namespace TerminalHub.App.Views;

public partial class PluginManagerWindow
{
    private string? _settingsOwner;
    private sealed record MarketItem(string Id, string Title, string Description, string Category,
        PluginEntry? Plugin = null, ModuleRegistration? Builtin = null, OfficialPluginListing? Listing = null)
    {
        public bool Installed => Plugin is not null || Builtin is not null;
    }
    private string Translate(string text) => Localizer.Current.Translate(text);
    private IEnumerable<MarketItem> MarketItems()
    {
        var builtinDetails = new[]
        {
            ("匹配终端输出，标记关键字并定位结果。", "Match terminal output, highlight keywords and locate results."),
            ("向选定会话同步发送输入。", "Send input to selected sessions together."),
            ("启动、停止和查看项目任务。", "Run, stop and inspect project tasks."),
            ("管理 SSH 会话中的 SFTP 文件。", "Manage SFTP files for SSH sessions."),
            ("录制终端，并按时间回放。", "Record terminal sessions and replay them over time.")
        };
        foreach (var listing in OfficialPluginCatalog.All)
            yield return new(listing.Id, listing.Title, listing.Detail, Translate("官方扩展") + " · " + listing.Group,
                _manager!.Plugins.FirstOrDefault(p => p.Manifest.Id == listing.Id), Listing: listing);
        foreach (var module in _manager!.Modules.Where(m => m.Owner == "builtin"))
        {
            var detail = int.TryParse(module.Definition.Id.Replace("tools-", ""), out var index) && index >= 0 && index < builtinDetails.Length
                ? Localizer.Current.Language == "en" ? builtinDetails[index].Item2 : builtinDetails[index].Item1 : module.ToString();
            yield return new(module.Id, module.ToString(), detail, Translate("内置基础工具"), Builtin: module);
        }
        foreach (var plugin in _manager.Plugins.Where(p => !OfficialPluginCatalog.All.Any(l => l.Id == p.Manifest.Id)))
            yield return new(plugin.Manifest.Id, plugin.Manifest.Name, plugin.Manifest.Id, Translate("自定义插件"), plugin);
    }
    private void RenderMarket()
    {
        var search = MarketSearch.Text?.Trim() ?? "";
        var items = MarketItems().Where(i => (i.Title + " " + i.Description + " " + i.Id).Contains(search, StringComparison.OrdinalIgnoreCase));
        items = MarketFilter.SelectedIndex switch
        {
            1 => items.Where(i => i.Installed), 2 => items.Where(i => !i.Installed),
            3 => items.Where(i => i.Builtin is not null), 4 => items.Where(i => i.Listing is not null), _ => items
        };
        foreach (var item in items) PluginCards.Children.Add(MarketCard(item));
        if (PluginCards.Children.Count == 0) PluginCards.Children.Add(Empty("没有匹配的插件", "调整搜索或筛选，查看其他工具。"));
        PluginCount.Text = Localizer.Current.Format("{0} 个内置 · {1} 个已安装扩展", _manager!.Modules.Count(m => m.Owner == "builtin"), _manager.Plugins.Count);
    }
    private Control MarketCard(MarketItem item)
    {
        var panel = new StackPanel { Spacing = 10 };
        var identity = new Grid { ColumnDefinitions = new("42,*,Auto"), ColumnSpacing = 12 };
        var glyph = Text(item.Title.Length > 0 ? item.Title[..1].ToUpperInvariant() : "◇", "UiAccent", 20, false);
        glyph.HorizontalAlignment = HorizontalAlignment.Center; glyph.VerticalAlignment = VerticalAlignment.Center;
        var icon = new Border { Child = glyph, CornerRadius = new(9), Height = 42 };
        icon.Bind(Border.BackgroundProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("UiAccentSoft"));
        identity.Children.Add(icon);
        var title = new StackPanel { Spacing = 4 }; var name = Text(item.Title, size: 15, localized: false); name.FontWeight = Avalonia.Media.FontWeight.SemiBold;
        title.Children.Add(name); title.Children.Add(Text(item.Category, "UiMuted", 11, false)); Grid.SetColumn(title, 1); identity.Children.Add(title);
        var enabled = item.Plugin?.Enabled ?? (item.Builtin is { } builtin && _manager!.Settings(builtin).Visible);
        var status = Text(!item.Installed ? "未安装" : item.Plugin?.Error.Length > 0 ? "加载失败" : enabled ? "已启用" : "已停用",
            item.Plugin?.Error.Length > 0 ? "UiBad" : enabled ? "UiGood" : "UiMuted", 11);
        Grid.SetColumn(status, 2); identity.Children.Add(status); panel.Children.Add(identity);
        panel.Children.Add(Text(item.Description, "UiMuted", 12, false));
        if (item.Plugin?.Error.Length > 0) panel.Children.Add(Text(item.Plugin.Error, "UiBad", 12, false));
        var actions = new WrapPanel();
        void Add(Button button) { button.Margin = new(0,0,8,4); actions.Children.Add(button); }
        if (!item.Installed)
        {
            var install = Action("安装", () => _manager!.InstallOfficial(item.Id), "primary"); install.Name = "Install-" + item.Id;
            install.IsEnabled = File.Exists(Path.Combine(item.Listing!.PackageDirectory(_manager!.OfficialPackageRoot), "plugin.json"));
            Add(install);
            if (!install.IsEnabled) panel.Children.Add(Text("此构建未附带安装资源，请导入插件目录。", "UiMuted", 11));
        }
        else
        {
            var module = item.Builtin ?? _manager!.Modules.FirstOrDefault(m => m.Owner == item.Id && m.Definition.Surface == ExtensionSurface.WorkspaceTools);
            if (module is not null)
            {
                var open = Action("打开", () => _main!.OpenWorkbenchModule(module), "primary"); open.Name = "Open-" + item.Id; Add(open);
                var placement = new ComboBox { Name = "MarketPlacement-" + module.Id, Width = 180,
                    ItemsSource = new[] { "独立窗口", "底部工具栏", "右侧边栏" }.Select(Translate).ToArray(),
                    SelectedIndex = (int)_manager!.Settings(module).Placement };
                placement.SelectionChanged += (_, _) => _main!.MoveWorkbenchModule(module, (ToolPlacement)placement.SelectedIndex);
                var location = new WrapPanel { Margin = new(0,0,0,2) };
                var caption = Text("打开位置", "UiMuted", 11); caption.VerticalAlignment = VerticalAlignment.Center; caption.Margin = new(0,0,12,0);
                location.Children.Add(caption); location.Children.Add(placement); panel.Children.Add(location);
            }
            Add(Action(enabled ? "停用" : "启用", () =>
            {
                if (item.Plugin is { } plugin) { if (plugin.Enabled) _manager!.Disable(plugin); else _manager!.Enable(plugin); }
                else { Save(() => _manager!.Settings(item.Builtin!).Visible = !enabled); Render(); }
            }));
            Add(Action("设置", () => ShowSettings(item.Builtin?.Owner ?? item.Id)));
            if (item.Plugin is { } installed) Add(Action("卸载", () => _manager!.Remove(installed), "danger"));
        }
        panel.Children.Add(actions); var card = Card(panel); card.Name = "Market-" + item.Id; return card;
    }
    private Control ModulePreferences(ModuleRegistration module)
    {
        var state = _manager!.Settings(module); var workspaceId = _vm!.ActiveWorkspace.Id;
        var panel = new StackPanel { Spacing = 10 };
        var title = Text(module.ToString(), size: 14, localized: false); title.FontWeight = Avalonia.Media.FontWeight.SemiBold; panel.Children.Add(title);
        panel.Children.Add(Text(module.Owner == "builtin" ? "内置基础工具" : _manager.Plugins.First(p => p.Manifest.Id == module.Owner).Manifest.Name, "UiMuted", 11, module.Owner == "builtin"));
        var checks = new WrapPanel();
        CheckBox Check(string label, bool flag, Action<bool> update)
        {
            var check = new CheckBox { [!ContentControl.ContentProperty] = UiText.Binding(label), IsChecked = flag, Margin = new(0,0,16,0) };
            check.IsCheckedChanged += (_, _) => Save(() => update(check.IsChecked == true)); checks.Children.Add(check); return check;
        }
        Check("显示模块", state.Visible, value => state.Visible = value);
        Check("当前工作区", state.Workspaces.GetValueOrDefault(workspaceId, true), value => state.Workspaces[workspaceId] = value);
        var launcher = module.Definition.Surface == ExtensionSurface.WorkspaceTools
            ? Check("显示快捷入口", state.ShowLauncher, value => state.ShowLauncher = value) : null;
        if (launcher is not null) launcher.IsEnabled = state.Placement != ToolPlacement.Window;
        panel.Children.Add(checks);
        var fields = new WrapPanel();
        void Field(string caption, Control editor)
        {
            var field = new StackPanel { Spacing = 5, Margin = new(0,0,12,8) };
            field.Children.Add(Text(caption, "UiMuted", 11)); field.Children.Add(editor); fields.Children.Add(field);
        }
        if (module.Definition.Surface == ExtensionSurface.WorkspaceTools)
        {
            var position = new ComboBox { Name = "Placement-" + module.Id, Width = 180,
                ItemsSource = new[] { "独立窗口", "底部工具栏", "右侧边栏" }.Select(Translate).ToArray(), SelectedIndex = (int)state.Placement };
            position.SelectionChanged += (_, _) =>
            {
                var reopen = _main!.IsWorkbenchModuleOpen(module);
                Save(() => state.Placement = (ToolPlacement)position.SelectedIndex);
                launcher!.IsEnabled = state.Placement != ToolPlacement.Window;
                if (reopen) _main.OpenWorkbenchModule(module);
            };
            Field("打开位置", position);
            var startup = new ComboBox { Name = "Startup-" + module.Id, Width = 200,
                ItemsSource = new[] { "手动打开", "启动时打开", "恢复上次打开" }.Select(Translate).ToArray(), SelectedIndex = (int)state.Startup };
            startup.SelectionChanged += (_, _) => Save(() => state.Startup = (ToolStartup)startup.SelectedIndex);
            Field("启动方式", startup);
        }
        var order = new NumericUpDown { Name = "Order-" + module.Id, Value = state.Order ?? module.Definition.Order, Width = 96, Increment = 1, FormatString = "0" };
        order.ValueChanged += (_, _) => Save(() => state.Order = (int)(order.Value ?? 0)); Field("排序", order);
        panel.Children.Add(fields); return Card(panel);
    }
    private void RenderModulePreferences()
    {
        foreach (var module in _manager!.Modules.OrderBy(m => _manager.Settings(m).Order ?? m.Definition.Order)) ModuleCards.Children.Add(ModulePreferences(module));
    }
    private void RenderPluginPreferences()
    {
        SettingsScopeTitle.Text = _settingsOwner is null ? Translate("全部插件与工具") : _settingsOwner == "builtin" ? Translate("内置基础工具")
            : _manager!.Plugins.FirstOrDefault(p => p.Manifest.Id == _settingsOwner)?.Manifest.Name ?? _settingsOwner;
        foreach (var module in _manager!.Visible(ExtensionSurface.Settings).Where(m => _settingsOwner is null || m.Owner == _settingsOwner))
        {
            var panel = new StackPanel { Spacing = 10 }; panel.Children.Add(Text(module.ToString(), size: 15, localized: false));
            panel.Children.Add(module.GetView()); SettingCards.Children.Add(Card(panel));
        }
        foreach (var module in _manager!.Modules.Where(m => m.Definition.Surface == ExtensionSurface.WorkspaceTools && (_settingsOwner is null || m.Owner == _settingsOwner)))
            SettingCards.Children.Add(ModulePreferences(module));
        foreach (var plugin in _manager.Plugins.Where(p => _settingsOwner is null || p.Manifest.Id == _settingsOwner))
        {
            var panel = new StackPanel { Spacing = 10 }; panel.Children.Add(Text(plugin.Manifest.Name, size: 14, localized: false));
            panel.Children.Add(Text(plugin.Manifest.Id + " · v" + plugin.Manifest.Version, "UiMuted", 11, false));
            var actions = new WrapPanel();
            if (plugin.Enabled && !plugin.IsScript) actions.Children.Add(Action("重载", () => _manager.Reload(plugin)));
            if (!plugin.IsScript)
            {
                var auto = new CheckBox { [!ContentControl.ContentProperty] = UiText.Binding("改动自动重载"), IsChecked = _manager.Preferences(plugin).AutoReload, Margin = new(12,0) };
                auto.IsCheckedChanged += (_, _) => Save(() =>
                {
                    _manager.Preferences(plugin).AutoReload = auto.IsChecked == true;
                    if (auto.IsChecked == true && plugin.Enabled) _manager.StartWatch(plugin); else _manager.StopWatch(plugin);
                }); actions.Children.Add(auto);
            }
            panel.Children.Add(actions); panel.Children.Add(Text("卸载会清除该插件保存的配置。", "UiMuted", 11)); SettingCards.Children.Add(Card(panel));
        }
        if (SettingCards.Children.Count == 0) SettingCards.Children.Add(Empty("暂无插件设置", "支持设置的插件启用后，会在这里显示配置选项。"));
    }
    private void ShowSettings(string owner) { _settingsOwner = owner; Render(); ManagerTabs.SelectedIndex = 2; }
    private void OnAllSettings(object? sender, RoutedEventArgs e) { _settingsOwner = null; Render(); }
    private void OnSearch(object? sender, TextChangedEventArgs e) => RefreshMarketOnly();
    private void OnFilter(object? sender, SelectionChangedEventArgs e) => RefreshMarketOnly();
    private void RefreshMarketOnly()
    {
        if (_manager is null || _rendering) return;
        PluginCards.Children.Clear(); RenderMarket();
    }
}
