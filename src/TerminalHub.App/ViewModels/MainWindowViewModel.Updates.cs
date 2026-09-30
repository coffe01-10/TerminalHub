using System.Diagnostics;
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

    [RelayCommand]
    private async Task CheckForUpdates()
    {
        UpdateMessage = "正在检查更新…";
        HasUpdate = false;
        try
        {
            var latest = await new ReleaseUpdates(UpdateHttp).LatestAsync();
            _availableRelease = latest;
            HasUpdate = latest.Version > typeof(MainWindowViewModel).Assembly.GetName().Version!;
            UpdateMessage = HasUpdate ? $"有新版本 {latest.Tag}，点击下载查看安装包与便携版。"
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
