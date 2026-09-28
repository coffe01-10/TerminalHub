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
    /// <summary>When true, session output is also written to a local log file.</summary>
    public bool SessionLogToFile { get; set; }
    /// <summary>Logs panel: last text filter ("" = none). Survives restarts.</summary>
    public string LogsFilterText { get; set; } = "";
    /// <summary>Logs panel: treat <see cref="LogsFilterText"/> as a regex.</summary>
    public bool LogsUseRegex { get; set; }
    /// <summary>Logs panel: 0=全部, 1=info, 2=warn, 3=error (clamped on load).</summary>
    public int LogsLevelFilterIndex { get; set; }
    /// <summary>Logs panel: keep the buffered history when the Output panel is cleared.</summary>
    public bool LogsRetainHistoryOnClear { get; set; }
    /// <summary>Saved SSH connections for the right-rail SSH tab.</summary>
    public List<TerminalHub.Core.Ssh.SshHost> SshHosts { get; set; } = [];

    /// <summary>Named Deploy publish profiles. Persisted in settings.json.</summary>
    public List<TerminalHub.Core.Deploy.PublishProfile> PublishProfiles
    {
        get => _publishProfiles;
        set => _publishProfiles = value ?? [];
    }
    private List<TerminalHub.Core.Deploy.PublishProfile> _publishProfiles = [];

    /// <summary>
    /// Id of the publish profile applied to the next dock Deploy.
    /// Empty = host platform and the process working directory.
    /// </summary>
    public string ActivePublishProfileId
    {
        get => _activePublishProfileId;
        set => _activePublishProfileId = value ?? "";
    }
    private string _activePublishProfileId = "";
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
