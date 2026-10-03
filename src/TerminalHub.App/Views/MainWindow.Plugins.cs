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
        _projectToolsView = new(this, Vm);
        var titles = new[] { "输出规则", "广播输入", "项目任务", "远程文件", "录制回放" };
        for (var i = 0; i < titles.Length; i++)
        {
            var page = _projectToolsView.GetBuiltinPage(i);
            _plugins.RegisterBuiltin(new("tools-" + i, titles[i], Order: i), () => page);
        }
        RefreshToolModules();
    }
    private void RefreshToolModules()
        => _projectToolsView?.UpdateModules(_plugins);
    private void OnManagePlugins(object? sender, RoutedEventArgs e) => OpenPluginManager();
    private void OpenPluginManager()
    {
        if (_pluginManagerWindow is not null) { _pluginManagerWindow.Activate(); return; }
        EnsureToolModules();
        var window = new PluginManagerWindow(this, Vm, _plugins);
        window.Closed += (_, _) => _pluginManagerWindow = null;
        _pluginManagerWindow = window; window.Show(this);
    }
}
