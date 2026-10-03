using Avalonia.Controls;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TerminalHub.App.Plugins;
using TerminalHub.Extensibility;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using TerminalHub.App.ViewModels;
using TerminalHub.Core.Terminal;

namespace TerminalHub.App.Views;

public partial class ProjectToolsView : UserControl
{
    public static readonly FuncValueConverter<int, bool> IsEmpty = new(count => count == 0);
    private readonly MainWindow? _main;
    private readonly TabItem[] _builtinTabs;
    private readonly ListBoxItem[] _builtinNavigation;
    private readonly Dictionary<string, (ModuleRegistration Module, TabItem Tab, ListBoxItem Navigation)> _extraModules = [];
    private bool _updatingModules;
    private string? _selectedModuleId;
    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;
    public ProjectToolsView()
    {
        InitializeComponent();
        _builtinTabs = ToolsTabs.Items.Cast<TabItem>().ToArray();
        _builtinNavigation = ToolNavigation.Items.Cast<ListBoxItem>().ToArray();
        for (var i = 0; i < _builtinNavigation.Length; i++) _builtinNavigation[i].Tag = "builtin:tools-" + i;
        ToolNavigation.SelectionChanged += (_, _) =>
        {
            if (_updatingModules || ToolNavigation.SelectedItem is not ListBoxItem item) return;
            _selectedModuleId = item.Tag as string;
            if (_selectedModuleId is not null && _extraModules.TryGetValue(_selectedModuleId, out var extra))
                extra.Tab.Content ??= extra.Module.GetView();
        };
        SizeChanged += (_, _) => ToolsLayout.ColumnDefinitions[0].Width = new(Bounds.Width < 680 ? 112 : 156);
    }
    public ProjectToolsView(MainWindow main, MainWindowViewModel vm) : this()
    {
        _main = main; DataContext = vm;
        AddHandler(InputElement.KeyDownEvent, (_, e) => { if (vm.BroadcastEnabled && e.Key == Key.Escape) { vm.StopBroadcast(); e.Handled = true; } }, RoutingStrategies.Tunnel);
    }
    public void SelectModule(int index) => ToolsTabs.SelectedIndex = index;
    public Control GetBuiltinPage(int index) => (Control)_builtinTabs[index].Content!;
    public void UpdateModules(PluginManager manager)
    {
        if (_updatingModules) return;
        var modules = manager.Visible(ExtensionSurface.WorkspaceTools).ToArray();
        _updatingModules = true;
        try
        {
            _selectedModuleId = (ToolNavigation.SelectedItem as ListBoxItem)?.Tag as string ?? _selectedModuleId;
            foreach (var id in _extraModules.Keys.Where(id => !modules.Any(m => m.Id == id)).ToArray())
            { _extraModules[id].Tab.Content = null; _extraModules.Remove(id); }
            ToolNavigation.Items.Clear(); ToolsTabs.Items.Clear();
            foreach (var module in modules)
            {
                if (module.Owner == "builtin" && int.TryParse(module.Definition.Id.Replace("tools-", ""), out var index))
                {
                    ToolsTabs.Items.Add(_builtinTabs[index]); ToolNavigation.Items.Add(_builtinNavigation[index]);
                    continue;
                }
                if (!_extraModules.TryGetValue(module.Id, out var extra))
                {
                    var heading = new Grid { ColumnDefinitions = new("16,*"), ColumnSpacing = 12 };
                    Control icon = new TextBlock { Text = "◇", FontSize = 16, VerticalAlignment = VerticalAlignment.Center };
                    if (module.Definition.IconSvg is { } svg)
                    {
                        try
                        {
                            var vector = svg.TrimStart().StartsWith('<') ? new SvgIcon(svg)
                                : SvgIcon.FromFile(Path.Combine(manager.Plugins.First(p => p.Manifest.Id == module.Owner).Directory, svg));
                            vector.Width = vector.Height = 16;
                            vector.Bind(SvgIcon.ForegroundProperty, new DynamicResourceExtension("UiInk"));
                            icon = vector;
                        }
                        catch (Exception ex) { ToolTip.SetTip(icon, ex.Message); }
                    }
                    heading.Children.Add(icon);
                    var title = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(title, 1); heading.Children.Add(title);
                    extra = (module, new TabItem(), new ListBoxItem { Tag = module.Id, Content = heading });
                    _extraModules[module.Id] = extra;
                }
                ((TextBlock)((Grid)extra.Navigation.Content!).Children[1]).Text = module.ToString();
                extra.Tab.Header = module.ToString();
                ToolsTabs.Items.Add(extra.Tab); ToolNavigation.Items.Add(extra.Navigation);
            }
            var selected = ToolNavigation.Items.Cast<ListBoxItem>().ToList().FindIndex(n => Equals(n.Tag, _selectedModuleId));
            ToolNavigation.SelectedIndex = modules.Length == 0 ? -1 : Math.Max(0, selected);
            if (ToolNavigation.SelectedItem is ListBoxItem current)
            {
                _selectedModuleId = current.Tag as string;
                if (_selectedModuleId is not null && _extraModules.TryGetValue(_selectedModuleId, out var extra))
                    extra.Tab.Content ??= extra.Module.GetView();
            }
        }
        finally { _updatingModules = false; }
    }
    private async void OnRuleLocate(object? sender, TappedEventArgs e)
    { if ((e.Source as Control)?.DataContext is OutputRuleResult result && _main is not null) { await _main.LocateSearchResultAsync(result.Result); _main.CollapseProjectTools(); _main.Activate(); } }
    private async void OnRemoteDirectory(object? sender, TappedEventArgs e) => await Vm.RemoteFiles.OpenDirectory();
    private async void OnUpload(object? sender, RoutedEventArgs e)
    {
        var files = await _main!.StorageProvider.OpenFilePickerAsync(new() { Title = TerminalHub.Core.Localization.Localizer.Current.Translate("上传到当前远程目录"), AllowMultiple = false });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) await Vm.RemoteFiles.UploadAsync(path);
    }
    private async void OnDownload(object? sender, RoutedEventArgs e)
    {
        if (Vm.RemoteFiles.Selected is not { IsDirectory: false } file) return;
        var target = await _main!.StorageProvider.SaveFilePickerAsync(new() { Title = TerminalHub.Core.Localization.Localizer.Current.Translate("保存远程文件"), SuggestedFileName = file.Name });
        if (target?.TryGetLocalPath() is { } path) await Vm.RemoteFiles.DownloadAsync(path);
    }
    private static FilePickerFileType RecordingType => new(TerminalHub.Core.Localization.Localizer.Current.Translate("Terminal Hub 录制")) { Patterns = ["*.threc"] };
    private async void OnRecord(object? sender, RoutedEventArgs e)
    {
        var target = await _main!.StorageProvider.SaveFilePickerAsync(new() { Title = TerminalHub.Core.Localization.Localizer.Current.Translate("保存终端录制"), SuggestedFileName = $"terminal-{DateTime.Now:yyyyMMdd-HHmmss}.threc", FileTypeChoices = [RecordingType] });
        if (target?.TryGetLocalPath() is { } path) Vm.StartRecording(path);
    }
    private async void OnStopRecord(object? sender, RoutedEventArgs e) => await Vm.StopRecordingAsync();
    private async void OnPlayback(object? sender, RoutedEventArgs e)
    {
        var files = await _main!.StorageProvider.OpenFilePickerAsync(new() { Title = TerminalHub.Core.Localization.Localizer.Current.Translate("打开终端录制"), FileTypeFilter = [RecordingType] });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        try { new PlaybackWindow(await TerminalPlayback.LoadAsync(path)).Show(_main!); }
        catch (Exception ex) { Vm.RecordingStatus = "无法打开录制：" + ex.Message; }
    }
}
