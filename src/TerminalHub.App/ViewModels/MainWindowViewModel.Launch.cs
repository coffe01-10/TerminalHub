using CommunityToolkit.Mvvm.ComponentModel;
using TerminalHub.Core.Sessions;
using TerminalHub.Core.Settings;

namespace TerminalHub.App.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty] private string _launchNotice = "";
    [ObservableProperty] private bool _explorerMenuEnabled;
    [ObservableProperty] private string _explorerMenuMessage = "";
    private bool _explorerReady;

    private void LoadExplorerMenu()
    {
        if (OperatingSystem.IsWindows())
            ExplorerMenuEnabled = ExplorerContextMenu.IsInstalled();
        _explorerReady = true;
        ExplorerMenuMessage = ExplorerMenuEnabled ? "资源管理器文件夹右键里已有 Terminal Hub。" : "右键菜单未添加。";
    }

    partial void OnExplorerMenuEnabledChanged(bool value)
    {
        if (!_explorerReady || !OperatingSystem.IsWindows()) return;
        if (value == ExplorerContextMenu.IsInstalled()) return;
        if (value)
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
            {
                ExplorerMenuMessage = "找不到程序路径，无法添加右键菜单。";
                ExplorerMenuEnabled = false;
                return;
            }
            try { ExplorerContextMenu.Install(exe); }
            catch (Exception ex)
            {
                ExplorerMenuMessage = "添加右键菜单失败：" + ex.Message;
                ExplorerMenuEnabled = ExplorerContextMenu.IsInstalled();
                return;
            }
        }
        else ExplorerContextMenu.Remove();
        var installed = ExplorerContextMenu.IsInstalled();
        ExplorerMenuMessage = installed ? "已添加右键菜单。取消勾选即可移除。" : "已移除右键菜单。";
        if (ExplorerMenuEnabled != installed) ExplorerMenuEnabled = installed;
    }

    public void ShowLaunchNotice(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        LaunchNotice = message;
        Dashboard.AppendOutput("warn", message, "ui");
    }

    public async Task OpenLaunchDirectoryAsync(string path)
    {
        var problem = LaunchRequest.CheckDirectory(path);
        if (problem is not null)
        {
            LaunchNotice = problem;
            Dashboard.AppendOutput("warn", problem, "ui");
            return;
        }
        var full = Path.GetFullPath(path);
        LaunchNotice = "";
        var name = new DirectoryInfo(full).Name;
        if (string.IsNullOrEmpty(name)) name = full;
        await CreateSessionAsync(name, SessionTag.None, full);
    }
}
