using Avalonia.Controls;
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
    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;
    public ProjectToolsView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => ToolsLayout.ColumnDefinitions[0].Width = new(Bounds.Width < 680 ? 112 : 156);
    }
    public ProjectToolsView(MainWindow main, MainWindowViewModel vm) : this()
    {
        _main = main; DataContext = vm;
        AddHandler(InputElement.KeyDownEvent, (_, e) => { if (vm.BroadcastEnabled && e.Key == Key.Escape) { vm.StopBroadcast(); e.Handled = true; } }, RoutingStrategies.Tunnel);
    }
    public void SelectModule(int index) => ToolsTabs.SelectedIndex = index;
    public Control? DetachModule(int index)
    {
        if (ToolsTabs.Items[index] is not TabItem tab || tab.Content is not Control page) return null;
        tab.Content = null;
        // Detached pages need their original style scope in the embedded module host.
        var host = new UserControl { Content = page, FontFamily = FontFamily, FontSize = FontSize };
        host.Bind(ForegroundProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("UiInk"));
        host.Bind(BackgroundProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("UiCanvas"));
        foreach (var original in Styles.OfType<Avalonia.Styling.Style>())
        {
            var style = new Avalonia.Styling.Style(_ => original.Selector!);
            foreach (var setter in original.Setters) style.Setters.Add(setter);
            host.Styles.Add(style);
        }
        return host;
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
