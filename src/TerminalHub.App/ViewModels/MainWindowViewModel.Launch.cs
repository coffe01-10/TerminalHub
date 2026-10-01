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

    public string FileManagerMenuLabel => OperatingSystem.IsWindows()
        ? "在资源管理器文件夹右键中显示 Terminal Hub"
        : "在 Thunar 文件夹右键中显示 Terminal Hub";
    public bool FileManagerMenuAvailable => OperatingSystem.IsWindows()
        || OperatingSystem.IsLinux() && TerminalHub.Core.Pty.ShellDiscovery.Exists("thunar");
    private static bool IsFileManagerMenuInstalled() => OperatingSystem.IsWindows()
        ? ExplorerContextMenu.IsInstalled() : LinuxFileManagerMenu.IsInstalled();

    private void LoadExplorerMenu()
    {
        if (FileManagerMenuAvailable)
            ExplorerMenuEnabled = IsFileManagerMenuInstalled();
        _explorerReady = true;
        ExplorerMenuMessage = !FileManagerMenuAvailable ? "本机未安装 Thunar；仍可用 --cwd 指定启动目录。"
            : ExplorerMenuEnabled ? "文件夹右键里已有 Terminal Hub。" : "右键菜单未添加。";
    }

    partial void OnExplorerMenuEnabledChanged(bool value)
    {
        if (!_explorerReady || !FileManagerMenuAvailable) return;
        if (value == IsFileManagerMenuInstalled()) return;
        if (value)
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
            {
                ExplorerMenuMessage = "找不到程序路径，无法添加右键菜单。";
                ExplorerMenuEnabled = false;
                return;
            }
            try
            {
                if (OperatingSystem.IsWindows()) ExplorerContextMenu.Install(exe);
                else LinuxFileManagerMenu.Install(exe);
            }
            catch (Exception ex)
            {
                ExplorerMenuMessage = "添加右键菜单失败：" + ex.Message;
                ExplorerMenuEnabled = IsFileManagerMenuInstalled();
                return;
            }
        }
        else
        {
            try
            {
                if (OperatingSystem.IsWindows()) ExplorerContextMenu.Remove();
                else LinuxFileManagerMenu.Remove();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                ExplorerMenuMessage = "移除右键菜单失败：" + ex.Message;
                ExplorerMenuEnabled = IsFileManagerMenuInstalled();
                return;
            }
        }
        var installed = IsFileManagerMenuInstalled();
        ExplorerMenuMessage = installed
            ? OperatingSystem.IsWindows() ? "已添加右键菜单。取消勾选即可移除。"
                : "已添加右键菜单。取消勾选即可移除；Thunar 需重新打开窗口。"
            : "已移除右键菜单。";
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
