using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Controls.Primitives;
using Avalonia.Platform.Storage;
using TerminalHub.App.Localization;
using TerminalHub.App.Plugins;
using TerminalHub.Extensibility;
using TerminalHub.Core.Localization;

namespace TerminalHub.App.Views;

public partial class MainWindow
{
    private PluginManager _plugins = null!;
    private readonly List<(Grid Host, Control Original, StackPanel Extras, ExtensionSurface Surface)> _extensionSlots = [];
    private readonly Dictionary<string, Window> _pluginWindows = [];
    private Window? _pluginManagerWindow;
    private ListBox? _toolModulesList;
    private ContentControl? _toolModulePage;
    private string? _selectedToolModule;
    private readonly List<MenuItem> _pluginMenuItems = [];
    private bool _refreshingExtensions, _closingPluginWindows;

    private void InitializePlugins()
    {
        _plugins = new(Vm, styles: Styles);
        InstallExtensionSlot(TerminalChrome,ExtensionSurface.Toolbar);
        InstallExtensionSlot(WorkspaceTabs,ExtensionSurface.WorkspaceTabs);
        InstallExtensionSlot(SessionShelf,ExtensionSurface.Sidebar,true);
        InstallExtensionSlot(StatusContents,ExtensionSurface.StatusBar);
        _plugins.Changed += RefreshExtensionUi;
        Opened += (_, _) => { _plugins.Discover(); RefreshExtensionUi(); };
        RefreshExtensionUi();
    }
    private void InstallExtensionSlot(Control original, ExtensionSurface surface, bool vertical = false)
    {
        var wrapper = new Grid { HorizontalAlignment = original.HorizontalAlignment, VerticalAlignment = original.VerticalAlignment };
        Grid.SetRow(wrapper,Grid.GetRow(original)); Grid.SetColumn(wrapper,Grid.GetColumn(original));
        Grid.SetRowSpan(wrapper,Grid.GetRowSpan(original)); Grid.SetColumnSpan(wrapper,Grid.GetColumnSpan(original));
        if (original.Parent is Panel panel)
        { var index = panel.Children.IndexOf(original); panel.Children.RemoveAt(index); panel.Children.Insert(index,wrapper); }
        else if (original.Parent is ContentControl content) { content.Content = null; content.Content = wrapper; }
        else if (original.Parent is Decorator decorator) { decorator.Child = null; decorator.Child = wrapper; }
        else return;
        Grid.SetRow(original,vertical ? 1 : 0); Grid.SetColumn(original,0); Grid.SetRowSpan(original,1); Grid.SetColumnSpan(original,1);
        var extras = new StackPanel { Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal, Spacing = 6 };
        if (vertical) wrapper.RowDefinitions = new("Auto,*"); else { wrapper.ColumnDefinitions = new("*,Auto"); Grid.SetColumn(extras,1); }
        wrapper.Children.Add(original); wrapper.Children.Add(extras); _extensionSlots.Add((wrapper,original,extras,surface));
    }

    private void RefreshExtensionUi()
    {
        if (_refreshingExtensions || _plugins is null) return;
        _refreshingExtensions = true;
        try
        {
            PluginNotice.Text = _plugins.LastError; PluginNotice.IsVisible = _plugins.LastError.Length > 0;
            ToolTip.SetTip(PluginNotice,_plugins.LastError);
            foreach (var slot in _extensionSlots)
            {
                slot.Extras.Children.Clear();
                var modules = _plugins.Visible(slot.Surface).ToArray();
                var replacement = modules.LastOrDefault(m => m.Definition.Replace);
                slot.Original.IsVisible = replacement is null;
                if (slot.Host.ColumnDefinitions.Count > 0)
                {
                    slot.Host.ColumnDefinitions[0].Width = replacement is null ? new(1,GridUnitType.Star) : new(0);
                    slot.Host.ColumnDefinitions[1].Width = replacement is null ? GridLength.Auto : new(1,GridUnitType.Star);
                }
                if (slot.Host.RowDefinitions.Count > 0)
                {
                    slot.Host.RowDefinitions[0].Height = replacement is null ? GridLength.Auto : new(1,GridUnitType.Star);
                    slot.Host.RowDefinitions[1].Height = replacement is null ? new(1,GridUnitType.Star) : new(0);
                }
                foreach (var module in modules.Where(m => !m.Definition.Replace || m == replacement)) slot.Extras.Children.Add(module.GetView());
            }
            PopulateExtensionPanel(PluginSidePanel,ExtensionSurface.SidePanel);
            PopulateExtensionPanel(PluginBottomPanel,ExtensionSurface.BottomPanel);
            if (SessionMenuButton.ContextFlyout is MenuFlyout menu)
            {
                foreach (var old in _pluginMenuItems) menu.Items.Remove(old); _pluginMenuItems.Clear();
                foreach (var (owner,command) in _plugins.Commands.Where(c => c.Command.ShowInMenu).ToArray())
                {
                    var item = new MenuItem { Header = Localizer.Current.GetModuleText(owner,command.Id,Localizer.Current.Translate(command.Title)) };
                    item.Click += async (_, _) => await command.Execute(); menu.Items.Add(item); _pluginMenuItems.Add(item);
                }
                foreach (var module in _plugins.Visible(ExtensionSurface.Menu))
                {
                    var item = new MenuItem { Header = module.GetView() }; menu.Items.Add(item); _pluginMenuItems.Add(item);
                }
                var manage = new MenuItem { Header = Localizer.Current.Translate("管理插件和模块") };
                manage.Click += (_, _) => OpenPluginManager(); menu.Items.Add(manage); _pluginMenuItems.Add(manage);
            }
            var tools = _plugins.Visible(ExtensionSurface.ToolWindow).ToArray();
            _closingPluginWindows = true;
            foreach (var id in _pluginWindows.Keys.Where(id => !tools.Any(m => m.Id == id)).ToArray())
            { _pluginWindows[id].Close(); _pluginWindows.Remove(id); }
            _closingPluginWindows = false;
            foreach (var module in tools)
            {
                if (_pluginWindows.ContainsKey(module.Id)) continue;
                var window = new Window { Title = module.ToString(), Width = 640, Height = 480, DataContext = Vm, Content = module.GetView() };
                window.Closed += (_, _) =>
                {
                    window.Content = null; _pluginWindows.Remove(module.Id);
                    if (!_closingPluginWindows && _plugins.Modules.Contains(module))
                    { _plugins.Settings(module).Visible = false; _plugins.SaveModules(); }
                };
                _pluginWindows[module.Id] = window; window.Show(this);
            }
            RefreshToolModules();
        }
        finally { _refreshingExtensions = false; }
    }
    private void PopulateExtensionPanel(StackPanel panel, ExtensionSurface surface)
    {
        panel.Children.Clear();
        foreach (var module in _plugins.Visible(surface)) panel.Children.Add(module.GetView());
        panel.IsVisible = panel.Children.Count > 0;
    }

    private void EnsureToolModules()
    {
        if (_projectToolsView is not null) return;
        _projectToolsView = new(this,Vm);
        var titles = new[] { "输出规则", "广播输入", "项目任务", "远程文件", "录制回放" };
        for (var i = 0; i < titles.Length; i++)
        {
            var page = _projectToolsView.DetachModule(i)!; page.DataContext = Vm;
            var scroll = new ScrollViewer { Content = page, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
            page.MinWidth = 480;
            _plugins.RegisterBuiltin(new("tools-" + i,titles[i],Order:i),() => scroll);
        }
        _toolModulesList = new ListBox { Width = 144, Margin = new(6) };
        _toolModulesList.ItemTemplate = new FuncDataTemplate<ModuleRegistration>((module, _) =>
        {
            var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            if (module?.Definition.IconSvg is { } icon)
            {
                try
                {
                    var svg = icon.TrimStart().StartsWith("<") ? icon : File.ReadAllText(Path.Combine(_plugins.Plugins.First(p => p.Manifest.Id == module.Owner).Directory,icon));
                    var view = new SvgIcon(svg); view.Bind(SvgIcon.ForegroundProperty,new DynamicResourceExtension("UiAccent")); heading.Children.Add(view);
                }
                catch (Exception ex) { if (_plugins.Plugins.FirstOrDefault(p => p.Manifest.Id == module.Owner) is { } plugin) Dispatcher.UIThread.Post(() => _plugins.Fail(plugin,ex)); }
            }
            heading.Children.Add(new TextBlock { Text = module?.ToString(), TextWrapping = TextWrapping.Wrap }); return heading;
        });
        _toolModulePage = new ContentControl { Margin = new(8), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        _toolModulesList.SelectionChanged += (_, _) =>
        {
            if (_toolModulesList.SelectedItem is not ModuleRegistration module) { _toolModulePage.Content = null; return; }
            _selectedToolModule = module.Id; _toolModulePage.Content = module.GetView();
        };
        var board = new Grid { ColumnDefinitions = new("Auto,*") };
        board.Children.Add(_toolModulesList); Grid.SetColumn(_toolModulePage,1); board.Children.Add(_toolModulePage);
        ProjectToolsContent.Content = board;
    }
    private void RefreshToolModules()
    {
        if (_toolModulesList is null) return;
        var modules = _plugins.Visible(ExtensionSurface.WorkspaceTools).ToArray();
        if (_toolModulePage is not null) _toolModulePage.Content = null;
        _toolModulesList.ItemsSource = modules;
        _toolModulesList.SelectedItem = modules.FirstOrDefault(m => m.Id == _selectedToolModule) ?? modules.FirstOrDefault();
        if (_toolModulePage is not null && _toolModulesList.SelectedItem is ModuleRegistration selected) _toolModulePage.Content = selected.GetView();
    }
    private void OnManagePlugins(object? sender, RoutedEventArgs e) => OpenPluginManager();

    private void OpenPluginManager()
    {
        if (_pluginManagerWindow is not null) { _pluginManagerWindow.Activate(); return; }
        EnsureToolModules();
        var window = new Window { Width = 840, Height = 600, MinWidth = 600, MinHeight = 420, DataContext = Vm };
        window.Bind(Window.TitleProperty,UiText.Binding("管理插件和模块"));
        var rows = new StackPanel { Spacing = 12, Margin = new(16) };
        var notice = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var tabs = new TabControl();
        var pluginsPage = new ScrollViewer { Content = rows };
        var modulesRows = new StackPanel { Spacing = 8, Margin = new(16) };
        var settingsRows = new StackPanel { Spacing = 10, Margin = new(16) };
        tabs.Items.Add(new TabItem { [!HeaderedContentControl.HeaderProperty] = UiText.Binding("插件"), Content = pluginsPage });
        tabs.Items.Add(new TabItem { [!HeaderedContentControl.HeaderProperty] = UiText.Binding("模块"), Content = new ScrollViewer { Content = modulesRows } });
        tabs.Items.Add(new TabItem { [!HeaderedContentControl.HeaderProperty] = UiText.Binding("插件设置"), Content = new ScrollViewer { Content = settingsRows } });
        window.Content = tabs;
        Button Button(string label, Action action)
        { var button = new Button { [!ContentControl.ContentProperty] = UiText.Binding(label), Padding = new(10,5) }; button.Click += (_, _) => { try { action(); } catch (Exception ex) { notice.Text = ex.Message; } }; return button; }
        void Render()
        {
            if (_plugins is null) return;
            rows.Children.Clear(); modulesRows.Children.Clear(); settingsRows.Children.Clear();
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var import = new Button { [!ContentControl.ContentProperty] = UiText.Binding("导入插件") };
            import.Click += async (_, _) =>
            {
                try
                {
                    var folders = await StorageProvider.OpenFolderPickerAsync(new() { Title = Localizer.Current.Translate("选择包含 plugin.json 的插件目录"), AllowMultiple = false });
                    if (folders.FirstOrDefault()?.TryGetLocalPath() is { } source) _plugins.Import(source);
                }
                catch (Exception ex) { notice.Text = ex.Message; }
            };
            actions.Children.Add(import);
            actions.Children.Add(Button("刷新",_plugins.Discover));
            actions.Children.Add(Button("打开插件目录",() => { Directory.CreateDirectory(_plugins.DirectoryPath); OpenPluginDirectory(_plugins.DirectoryPath); }));
            rows.Children.Add(actions); rows.Children.Add(notice);
            foreach (var plugin in _plugins.Plugins)
            {
                var line = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new(0,8) };
                var description = new StackPanel { Spacing = 4 };
                description.Children.Add(new TextBlock { Text = plugin.Manifest.Name + " · " + plugin.Manifest.Version, FontWeight = FontWeight.SemiBold });
                description.Children.Add(new TextBlock { Text = plugin.Manifest.Id, Opacity = .6 });
                description.Children.Add(new TextBlock { Text = Localizer.Current.Translate(plugin.Error), TextWrapping = TextWrapping.Wrap });
                line.Children.Add(description); var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                controls.Children.Add(Button(plugin.Enabled ? "禁用" : "启用",() => { if (plugin.Enabled) _plugins.Disable(plugin); else _plugins.Enable(plugin); }));
                controls.Children.Add(Button("移除",() => _plugins.Remove(plugin)));
                Grid.SetColumn(controls,1); line.Children.Add(controls); rows.Children.Add(line);
            }
            foreach (var module in _plugins.Modules.OrderBy(m => _plugins.Settings(m).Order ?? m.Definition.Order))
            {
                var state = _plugins.Settings(module); var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                var visible = new CheckBox { Content = module.ToString(), IsChecked = state.Visible, MinWidth = 150 };
                visible.IsCheckedChanged += (_, _) => { state.Visible = visible.IsChecked == true; _plugins.SaveModules(); }; line.Children.Add(visible);
                var workspace = new CheckBox { [!ContentControl.ContentProperty] = UiText.Binding("在当前工作区启用"), IsChecked = state.Workspaces.GetValueOrDefault(Vm.ActiveWorkspace.Id,true) };
                workspace.IsCheckedChanged += (_, _) => { state.Workspaces[Vm.ActiveWorkspace.Id] = workspace.IsChecked == true; _plugins.SaveModules(); }; line.Children.Add(workspace);
                var order = new NumericUpDown { Value = state.Order ?? module.Definition.Order, Width = 88, Increment = 1 };
                order.ValueChanged += (_, _) => { state.Order = (int)(order.Value ?? 0); _plugins.SaveModules(); }; line.Children.Add(order);
                modulesRows.Children.Add(line);
            }
            foreach (var module in _plugins.Visible(ExtensionSurface.Settings)) settingsRows.Children.Add(module.GetView());
        }
        _plugins.Changed += Render; Render();
        window.Closed += (_, _) => { _plugins.Changed -= Render; settingsRows.Children.Clear(); _pluginManagerWindow = null; };
        _pluginManagerWindow = window; window.Show(this);
    }
    private static void OpenPluginDirectory(string path)
        => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
}
