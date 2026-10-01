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

/// <summary>One session in the saved workspace layout. Recreated as a NEW
/// process on launch — the old PTY process is gone, this only restores shape.</summary>
public sealed record WorkspaceSession
{
    public string Name { get; init; } = "Terminal 01";
    public string Tag { get; init; } = "";
    public string WorkingDirectory { get; init; } = "";
    /// <summary>Resolved shell command line (pwsh, cmd.exe, ssh…), not a ShellKind.</summary>
    public string Shell { get; init; } = "";
    public string Arguments { get; init; } = "";
    public string StartupCommand { get; init; } = "";
    public bool RunStartupCommand { get; init; }
    public string GroupId { get; init; } = "";
    public string GroupName { get; init; } = "";
    public bool Pinned { get; init; }
    public TerminalHub.Core.Terminal.TerminalColorScheme ColorScheme { get; init; }
}

/// <summary>Saved workspace layout: session order + active session + split panes.
/// Indices point into <see cref="Sessions"/>; -1 = none.</summary>
public sealed class WorkspaceState
{
    public List<WorkspaceSession> Sessions { get; set; } = [];
    public int ActiveIndex { get; set; } = -1;
    public bool IsSplit { get; set; }
    public int LeftIndex { get; set; } = -1;
    public int RightIndex { get; set; } = -1;
    public int FocusedPane { get; set; }
}

/// <summary>Logs panel: one named session's remembered filter combo (per-session filter memory).</summary>
public sealed class LogsSessionFilterState
{
    /// <summary>Last text filter for this session ("" = none).</summary>
    public string FilterText { get; set; } = "";
    public bool UseRegex { get; set; }
    /// <summary>0=全部, 1=info, 2=warn, 3=error (clamped on use).</summary>
    public int LevelFilterIndex { get; set; }
    public bool RetainHistoryOnClear { get; set; }
}

/// <summary>Persisted application settings (JSON on disk).</summary>
public sealed class AppSettings
{
    public const string AppName = "Terminal Hub";
    public const string AppNameZh = "终端控制中心";

    public ShellKind Shell { get; set; } = ShellKind.PowerShell;
    public string CustomShellPath { get; set; } = "";
    public double FontSize { get; set; } = 13;
    /// <summary>Terminal font family list ("" = built-in monospace stack).</summary>
    public string FontFamily { get; set; } = "";
    public string Theme { get; set; } = "DarkGlass";
    public List<SessionShortcutBinding> SessionShortcuts
    {
        get => _sessionShortcuts;
        set => _sessionShortcuts = value ?? SessionShortcutBinding.Defaults();
    }
    private List<SessionShortcutBinding> _sessionShortcuts = SessionShortcutBinding.Defaults();
    public bool InspectorVisible { get; set; }
    public bool OutputVisible { get; set; } = true;
    /// <summary>0: reveal near bottom, 1: always visible, 2: hidden.</summary>
    public int DockVisibilityMode { get; set; } = 1;
    public string WorkspaceName { get; set; } = "MangaFlow";
    public List<WorkspaceTemplate> WorkspaceTemplates { get; set; } = [];
    public List<SessionGroup> SessionGroups { get; set; } = [];
    public List<FavoriteCommand> FavoriteCommands { get; set; } = [];
    public bool NotifyCommandCompletion { get; set; }
    public string FileEditorPath { get; set; } = "";
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

    /// <summary>Logs panel: show relative "ago from now" timestamps (刚刚/12s/3m/…) instead of absolute HH:mm:ss. Export always stays absolute.</summary>
    public bool LogsUseRelativeTimestamps { get; set; }

    /// <summary>Logs panel: wrap long message lines in the list (default on). Off = NoWrap for dense one-line scanning; horizontal scroll when needed.</summary>
    public bool LogsWrapLines { get; set; } = true;

    /// <summary>Logs panel: compact list density (default off). On = smaller FontSize (~8.5) and tighter Padding (~2,0) for denser scanning.</summary>
    public bool LogsCompactDensity { get; set; }

    /// <summary>Logs panel: ring-buffer capacity (UI presets 500 / 2000 / 5000; default 2000). Survives restarts.</summary>
    public int LogsBufferCapacity { get; set; } = 2000;

    /// <summary>Logs panel: per-session filter memory keyed by session name — each named
    /// session's last filter combo. 「全部会话」(dropdown index 0) is NOT in the map;
    /// it uses the global <see cref="LogsFilterText"/>-family fields above as its slot.</summary>
    public Dictionary<string, LogsSessionFilterState> LogsSessionFilters
    {
        get => _logsSessionFilters;
        set => _logsSessionFilters = value ?? new Dictionary<string, LogsSessionFilterState>();
    }
    private Dictionary<string, LogsSessionFilterState> _logsSessionFilters = new();

    /// <summary>Saved output bookmarks (name, session name, time, text snapshot).
    /// Runtime buffer anchors are never persisted.</summary>
    public List<OutputBookmark> OutputBookmarks
    {
        get => _outputBookmarks;
        set => _outputBookmarks = value ?? [];
    }
    private List<OutputBookmark> _outputBookmarks = [];

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

    /// <summary>
    /// Last real publish exit (success / fail / cancelled), including exit code and duration.
    /// Null until a publish has finished. ArtifactPath on the result keeps the last successful
    /// folder when a later run fails or is cancelled.
    /// Missing or null in <c>settings.json</c> loads as null.
    /// </summary>
    public TerminalHub.Core.Deploy.LastPublishResult? LastPublishResult
    {
        get => _lastPublishResult;
        set => _lastPublishResult = value;
    }
    private TerminalHub.Core.Deploy.LastPublishResult? _lastPublishResult;

    public List<StartupSession> StartupSessions { get; set; } =
    [
        new StartupSession { Name = "Terminal 01", Tag = "开发环境" },
        new StartupSession { Name = "Terminal 02", Tag = "测试环境" },
        new StartupSession { Name = "Terminal 03", Tag = "部署控制" },
    ];

    /// <summary>Last closed window layout. When it has sessions it wins over
    /// <see cref="StartupSessions"/> — restored as fresh processes on launch.
    /// Null/empty until the app has run once.</summary>
    public WorkspaceState Workspace
    {
        get => _workspace;
        set => _workspace = value ?? new WorkspaceState();
    }
    private WorkspaceState _workspace = new();

    /// <summary>Resolved shell command line for the current platform.
    /// <paramref name="shell"/> overrides the configured kind (per-session choice).</summary>
    public string ResolveShellCommand(ShellKind? shell = null)
    {
        var kind = shell ?? Shell;
        if (kind == ShellKind.Custom && !string.IsNullOrWhiteSpace(CustomShellPath))
            return CustomShellPath;

        var isWindows = OperatingSystem.IsWindows();
        return kind switch
        {
            ShellKind.PowerShell => isWindows ? "pwsh" : "pwsh",
            ShellKind.Cmd => isWindows ? "cmd.exe" : "bash",
            ShellKind.Wsl => isWindows ? "wsl.exe" : "bash",
            ShellKind.Bash => "bash",
            _ => isWindows ? "pwsh" : Environment.GetEnvironmentVariable("SHELL") ?? "bash",
        };
    }
}
