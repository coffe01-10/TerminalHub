using System.Diagnostics;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHub.Core.Settings;

namespace TerminalHub.App.ViewModels;

public partial class MainWindowViewModel
{
    private static readonly HttpClient UpdateHttp = new() { Timeout = TimeSpan.FromSeconds(15) };
    public string AppVersionText => "版本 " + typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3);
    [ObservableProperty] private string _updateMessage = "手动检查新版；更新不会关闭运行中的终端。";
    [ObservableProperty] private bool _hasUpdate;
    private AvailableRelease? _availableRelease;
    public ObservableCollection<ReleaseAsset> UpdateAssets { get; } = [];
    [ObservableProperty] private ReleaseAsset? _selectedUpdateAsset;
    [ObservableProperty] private string _updateNotes = "";
    [ObservableProperty] private bool _isDownloadingUpdate;
    [ObservableProperty] private double _updateDownloadPercent;
    [ObservableProperty] private bool _updateDownloadIndeterminate;
    [ObservableProperty] private string _downloadedUpdatePath = "";
    public bool HasDownloadedUpdate => File.Exists(DownloadedUpdatePath);
    private CancellationTokenSource? _updateDownloadCancellation;
    private static readonly HttpClient DownloadHttp = new() { Timeout = TimeSpan.FromMinutes(30) };
    internal ReleaseUpdates UpdateService { get; set; } = new(UpdateHttp);
    partial void OnDownloadedUpdatePathChanged(string value) => OnPropertyChanged(nameof(HasDownloadedUpdate));

    public async Task DownloadUpdateAsync(string destination)
    {
        if (SelectedUpdateAsset is not { } asset || IsDownloadingUpdate) return;
        IsDownloadingUpdate = true;
        DownloadedUpdatePath = "";
        UpdateDownloadPercent = 0;
        UpdateDownloadIndeterminate = asset.Size <= 0;
        using var cancellation = new CancellationTokenSource();
        _updateDownloadCancellation = cancellation;
        var progress = new Progress<DownloadProgress>(p =>
        {
            if (!ReferenceEquals(_updateDownloadCancellation, cancellation) || _disposed) return;
            UpdateDownloadPercent = p.Percent;
            UpdateDownloadIndeterminate = p.Total is not > 0;
            UpdateMessage = p.Total is > 0
                ? $"下载 {asset.Label}：{p.Received / 1048576.0:F1} / {p.Total.Value / 1048576.0:F1} MB"
                : $"下载 {asset.Label}：{p.Received / 1048576.0:F1} MB";
        });
        try
        {
            await new ReleaseUpdates(DownloadHttp).DownloadAsync(asset, destination, progress, cancellation.Token);
            DownloadedUpdatePath = destination;
            UpdateDownloadPercent = 100;
            UpdateDownloadIndeterminate = false;
            UpdateMessage = "下载完成，可打开文件或所在目录；升级由你手动执行。";
        }
        catch (OperationCanceledException) { UpdateMessage = "下载已取消。"; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        { UpdateMessage = "下载失败：" + ex.Message; }
        finally { _updateDownloadCancellation = null; IsDownloadingUpdate = false; }
    }
    [RelayCommand] private void CancelUpdateDownload() => _updateDownloadCancellation?.Cancel();
    [RelayCommand] private void OpenDownloadedUpdate() { if (HasDownloadedUpdate) OpenWeb(DownloadedUpdatePath); }
    [RelayCommand] private void OpenUpdateFolder() { if (HasDownloadedUpdate) _openFolder(Path.GetDirectoryName(DownloadedUpdatePath)!); }

    [RelayCommand]
    private async Task CheckForUpdates()
    {
        UpdateMessage = "正在检查更新…";
        HasUpdate = false;
        UpdateAssets.Clear();
        SelectedUpdateAsset = null;
        UpdateNotes = "";
        try
        {
            var latest = await UpdateService.LatestAsync();
            _availableRelease = latest;
            UpdateNotes = latest.Notes;
            var platform = OperatingSystem.IsWindows() ? UpdatePlatform.Windows : OperatingSystem.IsLinux() ? UpdatePlatform.Linux : UpdatePlatform.Other;
            foreach (var asset in latest.ForPlatform(platform)) UpdateAssets.Add(asset);
            SelectedUpdateAsset = UpdateAssets.FirstOrDefault();
            HasUpdate = latest.Version > typeof(MainWindowViewModel).Assembly.GetName().Version!;
            UpdateMessage = HasUpdate ? $"有新版本 {latest.Tag}，选择适合本机的安装包下载。"
                : $"当前无需更新，最新公开版本为 {latest.Tag}。";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or FormatException or System.Text.Json.JsonException)
        { UpdateMessage = "暂时无法检查更新，可在 GitHub 发布页手动下载。"; }
    }
    [RelayCommand] private void OpenUpdateDownload() => OpenWeb(_availableRelease?.DownloadPage.AbsoluteUri ?? ReleaseUpdates.ReleasesUrl);
    [RelayCommand] private void OpenReleaseNotes() => OpenWeb(ReleaseUpdates.ReleasesUrl + "/tag/v" + typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3));
    [RelayCommand] private void OpenProjectReleases() => OpenWeb(ReleaseUpdates.ReleasesUrl);
    private static void OpenWeb(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception ex) { Trace.WriteLine(ex.Message); }
    }
}
