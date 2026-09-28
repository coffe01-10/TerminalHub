namespace TerminalHub.Core.Settings;

public enum ShellKind
{
    PowerShell,
    Cmd,
    Wsl,
    Bash,
    Custom,
}

/// <summary>One session to create automatically on startup.</summary>
public sealed record StartupSession
{
    public string Name { get; init; } = "Terminal 01";
    public string Tag { get; init; } = "";
    public string WorkingDirectory { get; init; } = "";
    public ShellKind Shell { get; init; } = ShellKind.PowerShell;
}

/// <summary>Persisted application settings (JSON on disk).</summary>
public sealed class AppSettings
{
    public const string AppName = "Terminal Hub";
    public const string AppNameZh = "终端控制中心";

    public ShellKind Shell { get; set; } = ShellKind.PowerShell;
    public string CustomShellPath { get; set; } = "";
    public string FontFamily { get; set; } = "Cascadia Code, Consolas, Monospace";
    public double FontSize { get; set; } = 13;
    public string Theme { get; set; } = "DarkGlass";
    public string WorkspaceName { get; set; } = "MangaFlow";
    public List<StartupSession> StartupSessions { get; set; } =
    [
        new StartupSession { Name = "Terminal 01", Tag = "开发环境" },
        new StartupSession { Name = "Terminal 02", Tag = "测试环境" },
        new StartupSession { Name = "Terminal 03", Tag = "部署控制" },
    ];

    /// <summary>Resolved shell command line for the current platform.</summary>
    public string ResolveShellCommand()
    {
        if (Shell == ShellKind.Custom && !string.IsNullOrWhiteSpace(CustomShellPath))
            return CustomShellPath;

        var isWindows = OperatingSystem.IsWindows();
        return Shell switch
        {
            ShellKind.PowerShell => isWindows ? "pwsh" : "pwsh",
            ShellKind.Cmd => isWindows ? "cmd.exe" : "bash",
            ShellKind.Wsl => isWindows ? "wsl.exe" : "bash",
            ShellKind.Bash => "bash",
            _ => isWindows ? "pwsh" : Environment.GetEnvironmentVariable("SHELL") ?? "bash",
        };
    }
}
