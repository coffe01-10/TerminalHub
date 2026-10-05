using TerminalHub.Core.Localization;

namespace TerminalHub.App.Plugins;

public sealed record OfficialPluginListing(string Id, string Folder, string Name, string EnglishName,
    string Description, string EnglishDescription, string Category, string EnglishCategory)
{
    public string Title => Localizer.Current.Language == "en" ? EnglishName : Name;
    public string Detail => Localizer.Current.Language == "en" ? EnglishDescription : Description;
    public string Group => Localizer.Current.Language == "en" ? EnglishCategory : Category;
    public string PackageDirectory(string root) => Path.Combine(root, Folder);
}

/// <summary>The offline catalog shipped with the app; packages are installed only on request.</summary>
public static class OfficialPluginCatalog
{
    public static IReadOnlyList<OfficialPluginListing> All { get; } = new OfficialPluginListing[]
    {
        new("official.project-navigator", "ProjectNavigator", "项目导航", "Project navigator",
            "浏览与收藏项目目录，切换终端路径，在指定目录打开终端。", "Browse and bookmark project folders, change directories, and open terminals.", "项目", "Projects"),
        new("official.git-workbench", "GitWorkbench", "Git 工作台", "Git workbench",
            "查看差异、提交与推送，管理分支及 GitHub PR、Issue。", "Review diffs, commit and push, manage branches and GitHub PRs and issues.", "开发", "Development"),
        new("official.workspace-notes", "WorkspaceNotes", "工作区笔记", "Workspace notes",
            "为每个工作区保留独立笔记，切换时自动保存。", "Keep separate notes for each workspace, saved as you switch.", "记录", "Notes"),
        new("official.screen-clips", "ScreenClips", "屏幕摘录", "Screen clips",
            "收藏终端屏幕片段，查看和复制最近的输出。", "Capture terminal screen snippets, review and copy recent output.", "记录", "Notes"),
        new("official.command-watch", "CommandWatch", "命令看板", "Command watch",
            "根据 Shell 集成展示最近命令、耗时和退出码。", "Track recent commands, duration and exit codes from shell integration.", "开发", "Development"),
        new("official.terminal-broadcast", "TerminalBroadcast", "终端广播", "Terminal broadcast",
            "选择目标会话，将输入广播给多个终端。", "Choose target sessions and broadcast input to multiple terminals.", "终端", "Terminal"),
        new("official.snippets", "Snippets", "命令片段", "Command snippets",
            "保存常用命令，搜索后粘贴到指定会话。", "Save reusable commands, search and paste into a selected session.", "终端", "Terminal")
    };
}
