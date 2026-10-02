using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using TerminalHub.App.ViewModels;
using TerminalHub.Core.Terminal;

namespace TerminalHub.App.Views;

public partial class ProjectToolsWindow : Window
{
    public static readonly FuncValueConverter<int, bool> IsEmpty = new(count => count == 0);
    private readonly MainWindow? _main;
    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;
    public ProjectToolsWindow() => InitializeComponent();
    public ProjectToolsWindow(MainWindow main, MainWindowViewModel vm) : this()
    {
        _main = main; DataContext = vm;
        Closed += (_, _) => vm.SaveProjectToolsCommand.Execute(null);
        AddHandler(InputElement.KeyDownEvent, (_, e) => { if (vm.BroadcastEnabled && e.Key == Key.Escape) { vm.StopBroadcast(); e.Handled = true; } }, RoutingStrategies.Tunnel);
    }
    private async void OnRuleLocate(object? sender, TappedEventArgs e)
    { if ((e.Source as Control)?.DataContext is OutputRuleResult result && _main is not null) { await _main.LocateSearchResultAsync(result.Result); _main.Activate(); } }
    private async void OnRemoteDirectory(object? sender, TappedEventArgs e) => await Vm.RemoteFiles.OpenDirectory();
    private async void OnUpload(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "上传到当前远程目录", AllowMultiple = false });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) await Vm.RemoteFiles.UploadAsync(path);
    }
    private async void OnDownload(object? sender, RoutedEventArgs e)
    {
        if (Vm.RemoteFiles.Selected is not { IsDirectory: false } file) return;
        var target = await StorageProvider.SaveFilePickerAsync(new() { Title = "保存远程文件", SuggestedFileName = file.Name });
        if (target?.TryGetLocalPath() is { } path) await Vm.RemoteFiles.DownloadAsync(path);
    }
    private static FilePickerFileType RecordingType => new("Terminal Hub 录制") { Patterns = ["*.threc"] };
    private async void OnRecord(object? sender, RoutedEventArgs e)
    {
        var target = await StorageProvider.SaveFilePickerAsync(new() { Title = "保存终端录制", SuggestedFileName = $"terminal-{DateTime.Now:yyyyMMdd-HHmmss}.threc", FileTypeChoices = [RecordingType] });
        if (target?.TryGetLocalPath() is { } path) Vm.StartRecording(path);
    }
    private async void OnStopRecord(object? sender, RoutedEventArgs e) => await Vm.StopRecordingAsync();
    private async void OnPlayback(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "打开终端录制", FileTypeFilter = [RecordingType] });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        try { new PlaybackWindow(await TerminalPlayback.LoadAsync(path)).Show(this); }
        catch (Exception ex) { Vm.RecordingStatus = "无法打开录制：" + ex.Message; }
    }
}
