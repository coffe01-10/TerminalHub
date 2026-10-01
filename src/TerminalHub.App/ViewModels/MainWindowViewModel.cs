using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Platform.Storage;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHub.Core.Deploy;
using TerminalHub.Core.Logging;
using TerminalHub.Core.Monitoring;
using TerminalHub.Core.Pty;
using TerminalHub.Core.Sessions;
using TerminalHub.Core.Settings;
using TerminalHub.App.Views;
using TerminalHub.App.Controls;
using TerminalHub.Pty;

namespace TerminalHub.App.ViewModels;

/// <summary>Shell VM: owns sessions, active session, dock, status bar.</summary>
public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly SessionManager _sessions = new();
    private readonly ISystemMonitor _monitor;
    private readonly AppSettings _settings;
    private readonly SettingsStore _settingsStore;

    public ObservableCollection<SessionCardViewModel> SessionCards { get; } = [];
    public ObservableCollection<SessionShortcutViewModel> SessionShortcuts { get; } = [];
    public List<AvailableShell> AvailableStartupShells { get; private set; } = [];
    [ObservableProperty] private bool _shellSetupOpen;
    [ObservableProperty] private string _shellSetupMessage = "";
    [ObservableProperty] private AvailableShell? _selectedStartupShell;
    [ObservableProperty] private string _startupCustomShell = "";
    private readonly Func<string, bool> _shellAvailable;
    private string? _missingStartupShell;

    [ObservableProperty] private TerminalSessionModel? _activeSession;
    [ObservableProperty] private SessionCardViewModel? _activeCard;
    [ObservableProperty] private string _workspaceName;
    [ObservableProperty] private string _statusLine = "";
    [ObservableProperty] private string _breadcrumb = "";
    [ObservableProperty] private bool _canCwdBack;
    [ObservableProperty] private bool _canCwdForward;
    [ObservableProperty] private int _terminalCount;
    [ObservableProperty] private int _runningCount;
    [ObservableProperty] private string _cpuText = "";
    [ObservableProperty] private string _memText = "";
    [ObservableProperty] private double[] _statusSpark = [];
    [ObservableProperty] private int _selectedRightTab;
    [ObservableProperty] private bool _settingsOpen;
    [ObservableProperty] private bool _inspectorVisible;
    [ObservableProperty] private bool _outputVisible;
    [ObservableProperty] private int _dockVisibilityMode;
    [ObservableProperty] private string _activeWorkingDirectory = "";
    public string ActiveDirectoryName => string.IsNullOrEmpty(ActiveWorkingDirectory) ? "未选择会话" :
        Path.GetFileName(ActiveWorkingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { Length: > 0 } name ? name : ActiveWorkingDirectory;
    partial void OnActiveWorkingDirectoryChanged(string value) => OnPropertyChanged(nameof(ActiveDirectoryName));
    public int ThemeIndex
    {
        get => Math.Max(0, Array.IndexOf(ThemeManager.Names, _settings.Theme));
        set
        {
            if (value < 0 || value >= ThemeManager.Names.Length) return;
            _settings.Theme = ThemeManager.Names[value];
            ThemeManager.Apply(_settings.Theme);
            OnPropertyChanged();
        }
    }

    [RelayCommand]
    private void ShowWorkspace()
    {
        InspectorVisible = true;
        SelectedRightTab = 1;
        // For ssh sessions the cwd is remote — Files only browses local dirs.
        if (ActiveSession?.Tag != SessionTag.Ssh)
            Files.NavigateTo(ActiveWorkingDirectory);
    }

    partial void OnInspectorVisibleChanged(bool value) => _settings.InspectorVisible = value;
    partial void OnOutputVisibleChanged(bool value) => _settings.OutputVisible = value;
    partial void OnDockVisibilityModeChanged(int value) => _settings.DockVisibilityMode = value;

    [RelayCommand] private void ToggleInspector() => InspectorVisible = !InspectorVisible;
    [RelayCommand] private void ToggleOutput() => OutputVisible = !OutputVisible;
    /// <summary>Dock index of the active surface (-1 when a tab has no dock item).</summary>
    [ObservableProperty] private int _dockHighlight = -1;

    /// <summary>True while a dock publish is starting or its session is still running.</summary>
    [ObservableProperty] private bool _isPublishRunning;

    /// <summary>Deploy dock tooltip. Idle text, or a cancel hint while a publish is running.</summary>
    [ObservableProperty] private string _deployDockTip = DeployDockTipIdle;

    private const string DeployDockTipIdle =
        "点击：有产物则打开目录，无产物则按当前配置档开始打包。Ctrl+点击或右键「重新打包」强制重新发布。右键可切换配置档、查看最近产物、打开/复制上次成功产物路径。打包进行中可右键「取消打包」。";

    private const string DeployDockTipRunning =
        "打包进行中。右键「取消打包 Cancel」终止发布进程组。普通点击不会再次启动。";

    /// <summary>UI-thread timer that refreshes the live packing elapsed on the dock (~1s).</summary>
    private DispatcherTimer? _publishElapsedTimer;

    /// <summary>Optional clock for tests; defaults to UTC now. Live elapsed uses the real attempt stamp.</summary>
    internal Func<DateTimeOffset> UtcNow { get; set; } = static () => DateTimeOffset.UtcNow;

    /// <summary>Dock caption: idle reads 部署; a running publish reads 打包中.</summary>
    public string DeployDockCaption => IsPublishRunning ? "打包中" : "部署";

    /// <summary>
    /// Short last-outcome text from the real exit record. Empty until a publish has finished.
    /// Stays available while a later publish is running; the dock binds <see cref="LastPublishBadge"/>.
    /// </summary>
    public string LastPublishSummary => LastPublishResults.FormatBadge(_settings.LastPublishResult);

    /// <summary>
    /// Status line under the Deploy caption. Idle: last outcome badge. Running: live
    /// <c>打包中 · Ns</c> from <see cref="_publishAttemptStartedAt"/> (same duration wording).
    /// </summary>
    public string LastPublishBadge => IsPublishRunning
        ? LastPublishResults.FormatLiveBadge(CurrentPublishElapsedMs())
        : LastPublishSummary;

    public bool HasLastPublishBadge => LastPublishBadge.Length > 0;

    /// <summary>True when the stored last-success folder still exists on disk.</summary>
    public bool CanOpenLastSuccessfulArtifact => LastPublishResults.CanOpen(_settings.LastPublishResult);

    /// <summary>Same gate as open-last: success path exists on disk.</summary>
    public bool CanCopyLastSuccessfulArtifact => CanOpenLastSuccessfulArtifact;

    /// <summary>
    /// Quiet second line under the last-publish badge: active profile name,
    /// optionally with RID (<c>默认 · linux-x64</c>). Empty when none / no name.
    /// </summary>
    public string ActivePublishProfileLabel =>
        PublishProfiles.FormatDockLabel(PublishProfiles.Active(_settings));

    public bool HasActivePublishProfileLabel => ActivePublishProfileLabel.Length > 0;

    /// <summary>True when a stored last outcome exists (badge non-empty when idle), even if the artifact path is gone.</summary>
    public bool CanClearLastPublishResult =>
        LastPublishResults.HasRecord(_settings.LastPublishResult);

    partial void OnIsPublishRunningChanged(bool value)
    {
        if (value) StartPublishElapsedTimer();
        else StopPublishElapsedTimer();
        DeployDockTip = ComposeDeployDockTip(value);
        OnPropertyChanged(nameof(DeployDockCaption));
        OnPropertyChanged(nameof(LastPublishBadge));
        OnPropertyChanged(nameof(HasLastPublishBadge));
    }

    private void StartPublishElapsedTimer()
    {
        if (_publishElapsedTimer is not null) return;
        _publishElapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _publishElapsedTimer.Tick += OnPublishElapsedTick;
        _publishElapsedTimer.Start();
        // Immediate paint so the first second is not blank until the first tick.
        NotifyPublishElapsed();
    }

    private void StopPublishElapsedTimer()
    {
        if (_publishElapsedTimer is null) return;
        _publishElapsedTimer.Stop();
        _publishElapsedTimer.Tick -= OnPublishElapsedTick;
        _publishElapsedTimer = null;
    }

    private void OnPublishElapsedTick(object? sender, EventArgs e) => NotifyPublishElapsed();

    private void NotifyPublishElapsed()
    {
        OnPropertyChanged(nameof(LastPublishBadge));
        OnPropertyChanged(nameof(HasLastPublishBadge));
        DeployDockTip = ComposeDeployDockTip(true);
    }

    private long CurrentPublishElapsedMs()
    {
        DateTimeOffset started;
        lock (_publishLock) started = _publishAttemptStartedAt;
        return ElapsedMs(started, UtcNow());
    }

    private string ComposeDeployDockTip(bool? running = null)
    {
        var isRunning = running ?? IsPublishRunning;
        var head = isRunning ? DeployDockTipRunning : DeployDockTipIdle;
        if (isRunning)
        {
            var live = LastPublishResults.FormatLiveElapsed(CurrentPublishElapsedMs());
            head = $"{head} 已耗时 {live}。";
        }
        var detail = LastPublishResults.FormatTooltip(_settings.LastPublishResult);
        var profile = ActivePublishProfileLabel;
        var profileLine = profile.Length == 0 ? "" : "\n配置档：" + profile;
        if (isRunning && detail == "尚未打包")
            return head + profileLine;
        return head + "\n" + detail + profileLine;
    }

    /// <summary>Title-bar OS label ("Windows System" in the mockup — follows the real OS).</summary>
    public string OsLabel => OperatingSystem.IsWindows() ? "Windows System"
        : OperatingSystem.IsLinux() ? "Linux System"
        : OperatingSystem.IsMacOS() ? "macOS System"
        : "本机";

    /// <summary>Files lazy-inits on first visit.</summary>
    partial void OnSelectedRightTabChanged(int value)
    {
        if (value == 1) Files.EnsureSessionDir();
        if (value == 2) Logs.RefreshSessions();
        // Dock: 1 Monitor→tab0, 2 SSH→tab3, 3 Logs→tab2; Files has no dock item.
        DockHighlight = value switch { 0 => 1, 2 => 3, 3 => 2, _ => SettingsOpen ? 5 : -1 };
    }

    partial void OnSettingsOpenChanged(bool value)
        => DockHighlight = value ? 5 : (SelectedRightTab switch { 0 => 1, 2 => 3, 3 => 2, _ => -1 });

    /// <summary>Selection sync: ListBox.SelectedItem drives activation. In split
    /// mode the picked card is assigned to the focused pane.</summary>
    partial void OnActiveCardChanged(SessionCardViewModel? value)
    {
        if (_rebuildingShelf || value is null) return;
        if (IsSplit && value.Model is { } picked)
        {
            AssignToPane(FocusedPane, picked);
            if (!ReferenceEquals(picked, _sessions.Active))
                _sessions.Activate(picked);
            return;
        }
        if (!ReferenceEquals(value.Model, _sessions.Active))
            _sessions.Activate(value.Model);
    }

    /// <summary>Active-session change arms/disarms popout + Files' terminal button.</summary>
    partial void OnActiveSessionChanged(TerminalSessionModel? value)
    {
        OpenInNewWindowCommand.NotifyCanExecuteChanged();
        Files.NotifySessionAvailability();
    }

    /// <summary>Assign a session to a pane, keeping the two panes distinct.
    /// One remaining session exits split instead of occupying both panes.</summary>
    private void AssignToPane(int pane, TerminalSessionModel s)
    {
        if (!IsSplit) return;
        if (pane == 0)
        {
            LeftPane = s;
            if (ReferenceEquals(RightPane, s))
                RightPane = _sessions.Sessions.FirstOrDefault(o => !ReferenceEquals(o, s));
        }
        else
        {
            RightPane = s;
            if (ReferenceEquals(LeftPane, s))
                LeftPane = _sessions.Sessions.FirstOrDefault(o => !ReferenceEquals(o, s));
        }
        if (LeftPane is null || RightPane is null || ReferenceEquals(LeftPane, RightPane))
            ExitSplit();
    }

    private readonly SparklineBuffer _statusCpu = new(40);

    /// <summary>Set by the Deploy button on Ctrl+pointer-press; consumed by the next dock Deploy.</summary>
    private bool _deployCtrlHeld;

    /// <summary>Publish sessions we spawned, so exit can be reported once.</summary>
    private readonly HashSet<Guid> _publishIds = new();
    /// <summary>Repo root of each publish session, so a successful exit can rescan artifacts.</summary>
    private readonly Dictionary<Guid, string> _publishRoots = new();
    /// <summary>Publish ids the user asked to cancel. Exit then logs cancelled, not a generic failure.</summary>
    private readonly HashSet<Guid> _publishCancelled = new();
    /// <summary>When each publish session was armed, so exit can store duration.</summary>
    private readonly Dictionary<Guid, DateTimeOffset> _publishStartedAt = new();
    /// <summary>Start stamp for the in-flight attempt, including cancel-before-id and spawn failure.</summary>
    private DateTimeOffset _publishAttemptStartedAt;
    /// <summary>Publish sessions whose first script line already focused the Output tab.</summary>
    private readonly HashSet<Guid> _publishOutputArmed = new();
    private readonly object _publishLock = new();
    private int _publishStarting;
    /// <summary>Cancel arrived before the PTY id existed. Applied when the session is created.</summary>
    private bool _cancelPendingStart;
    /// <summary>Idle cancel already logged, so a second click does not repeat the warning.</summary>
    private bool _idleCancelWarned;

    /// <summary>Per-session CWD back/forward stacks (keyed by session id).
    /// Mutated from UI (nav commands), PTY thread (OSC 7) and monitor thread
    /// (poll) — all accesses go under this lock.</summary>
    private readonly Dictionary<Guid, CwdHistory> _cwdHistories = new();
    private readonly object _cwdLock = new();

    /// <summary>Sessions popped out into standalone windows. They are off the main
    /// session list but still owned here for shutdown; each returns via Reattach
    /// when its window closes.</summary>
    public ObservableCollection<TerminalSessionModel> DetachedSessions { get; } = [];
    private readonly List<SessionWindow> _popouts = [];
    /// <summary>Live popout windows, one per detached session (tests drive these).</summary>
    public IReadOnlyList<SessionWindow> Popouts => _popouts;
    private bool _disposed;

    /// <summary>Last real scan of <c>artifacts/publish</c> (dock click, menu, or successful publish).</summary>
    public IReadOnlyList<RecentArtifact> RecentArtifacts { get; private set; } = [];

    public DashboardViewModel Dashboard { get; }
    public FilesViewModel Files { get; }
    public LogsViewModel Logs { get; }
    public SshViewModel Ssh { get; }
    private readonly SessionLogFile _sessionLog = new();
    /// <summary>"Open in file manager" seam — tests stub it so no real Explorer
    /// window pops on a temp artifacts dir the test then deletes (位置不可用).</summary>
    private readonly Action<string> _openFolder;

    public MainWindowViewModel(ISystemMonitor? monitor = null, SettingsStore? settingsStore = null,
        Action<string>? openFolder = null, Func<string, bool>? shellAvailable = null,
        Func<bool>? sshAvailable = null)
    {
        _shellAvailable = shellAvailable ?? (command => PtySessionFactory.UseMock || ShellDiscovery.Exists(command));
        _openFolder = openFolder ?? OpenFolderInFileManager;
        _settingsStore = settingsStore ?? new SettingsStore();
        _settings = _settingsStore.Load();
        if (!_settings.SessionShortcuts.Any(s => s.Action == SessionShortcutAction.CommandPalette))
            _settings.SessionShortcuts.Add(new() { Action = SessionShortcutAction.CommandPalette, Gesture = "Ctrl+Shift+P" });
        LoadGroups();
        LoadFavorites();
        LoadBookmarks();
        LoadExplorerMenu();
        LoadSessionShortcuts();
        foreach (var template in _settings.WorkspaceTemplates.OrderByDescending(t => t.LastUsed).ThenBy(t => t.Name))
            WorkspaceTemplates.Add(template);
        ThemeManager.Apply(_settings.Theme);
        TerminalLinkOpener.EditorPath = _settings.FileEditorPath;
        _inspectorVisible = _settings.InspectorVisible;
        _outputVisible = _settings.OutputVisible;
        _dockVisibilityMode = _settings.DockVisibilityMode;
        DeployDockTip = ComposeDeployDockTip();
        _workspaceName = _settings.WorkspaceName;

        _monitor = monitor ?? new SystemMonitor();
        _monitor.Sampled += OnSampled;
        _monitor.Start(TimeSpan.FromSeconds(1));

        Dashboard = new DashboardViewModel(_monitor);
        Dashboard.BufferSource = () => ActiveSession?.Emulator.Buffer;
        Files = new FilesViewModel(
            // Files browses the LOCAL filesystem — an ssh session's remote cwd
            // must never reach it, and "open terminal here" on a local dir is
            // meaningless for a remote shell.
            () => ActiveSession is { Tag: not SessionTag.Ssh } ? ActiveSession.WorkingDirectory : null,
            openTerminalAt: CdActiveSessionTo,
            copyTextAsync: CopyTextToClipboardAsync,
            hasActiveSession: () => ActiveSession is { Tag: not SessionTag.Ssh });
        Logs = new LogsViewModel(Dashboard, _sessionLog,
            () => SessionCards.Select(c => c.Name).ToList(),
            _settings.SessionLogToFile,
            v => _settings.SessionLogToFile = v,
            bufferCapacity: LogsViewModel.NormalizeSavedBufferCapacity(_settings.LogsBufferCapacity),
            copyToClipboard: CopyTextToClipboardAsync,
            promptExportPath: PromptExportPathAsync,
            persistFilters: PersistLogsFilters,
            activateSession: TryActivateSessionByName);
        // Replay the filters saved from the previous run (never writes back):
        // the global combo for「全部会话」, plus each named session's own memory.
        Logs.ApplyPersistedFilters(_settings.LogsFilterText, _settings.LogsUseRegex,
            _settings.LogsLevelFilterIndex, _settings.LogsRetainHistoryOnClear,
            _settings.LogsUseRelativeTimestamps, _settings.LogsWrapLines,
            _settings.LogsCompactDensity);
        Logs.ApplySessionFilterMap(_settings.LogsSessionFilters);
        Ssh = new SshViewModel(_settings.SshHosts, ConnectSsh, SaveSettingsInternal, sshAvailable);

        // Logs' session filter follows card adds/removes live, not just on tab open.
        SessionCards.CollectionChanged += OnSessionCardsChanged;

        _sessions.SessionAdded += OnSessionAdded;
        _sessions.SessionRemoved += OnSessionRemoved;
        _sessions.ActiveChanged += s => Avalonia.Threading.Dispatcher.UIThread.Post(() => SyncActive());
        _sessions.SessionStateChanged += _ => Avalonia.Threading.Dispatcher.UIThread.Post(RefreshCounts);
    }

    public AppSettings Settings => _settings;
    public ISystemMonitor Monitor => _monitor;

    /// <summary>Called once at startup to spawn configured sessions. A saved
    /// workspace layout wins over the static startup-session list — it is the
    /// same shape the user closed the app with (fresh processes each time).</summary>
    public async Task SpawnStartupSessionsAsync()
    {
        var configuredShell = _settings.ResolveShellCommand();
        var usesConfiguredShell = _settings.Workspace.Sessions.Count > 0
            ? _settings.Workspace.Sessions.Any(s => string.IsNullOrWhiteSpace(s.Shell) || s.Shell == configuredShell)
            : _settings.StartupSessions.Count == 0
                || _settings.StartupSessions.Any(s => _settings.ResolveShellCommand(s.Shell) == configuredShell);
        if (usesConfiguredShell && !_shellAvailable(configuredShell))
        {
            _missingStartupShell = configuredShell;
            AvailableStartupShells = ShellDiscovery.Find(_shellAvailable);
            OnPropertyChanged(nameof(AvailableStartupShells));
            SelectedStartupShell = AvailableStartupShells.FirstOrDefault();
            ShellSetupMessage = $"未找到 {configuredShell}。请选择本机可用的 Shell，或填写已安装程序的路径。";
            ShellSetupOpen = true;
            return;
        }
        if (_settings.Workspace.Sessions.Count > 0)
        {
            await RestoreWorkspaceAsync(_settings.Workspace);
            return;
        }
        if (_settings.StartupSessions.Count == 0)
        {
            await NewSession();
            return;
        }
        foreach (var s in _settings.StartupSessions)
            await CreateSessionAsync(s.Name, ParseTag(s.Tag), s.WorkingDirectory, s.Shell); // failures log to Output, next session still spawns
    }

    [RelayCommand]
    private async Task ContinueShellSetup()
    {
        var command = string.IsNullOrWhiteSpace(StartupCustomShell)
            ? SelectedStartupShell?.Command : StartupCustomShell.Trim().Trim('"');
        if (string.IsNullOrEmpty(command) || !_shellAvailable(command))
        {
            ShellSetupMessage = "未找到所选 Shell，请选择可用程序或填写正确的路径。";
            return;
        }
        var kind = string.IsNullOrWhiteSpace(StartupCustomShell) ? SelectedStartupShell!.Kind : ShellKind.Custom;
        _settings.Shell = kind;
        if (kind == ShellKind.Custom) _settings.CustomShellPath = command;
        OnPropertyChanged(nameof(ShellIndex));
        OnPropertyChanged(nameof(Settings));
        // Do not snapshot an empty stage over the workspace we are about to restore.
        _settingsStore.Save(_settings);
        ShellSetupOpen = false;
        await SpawnStartupSessionsAsync();
    }

    /// <summary>Respawn the saved workspace: session order/names/cwd/shell, then
    /// the split layout and active session. A spawn failure drops that index only.</summary>
    private async Task RestoreWorkspaceAsync(WorkspaceState ws, bool runStartupCommands = false)
    {
        var byIndex = new Dictionary<int, TerminalSessionModel>();
        for (var i = 0; i < ws.Sessions.Count; i++)
        {
            var s = ws.Sessions[i];
            var model = await CreateSessionAsync(
                string.IsNullOrWhiteSpace(s.Name) ? null : s.Name,
                ParseTag(s.Tag), s.WorkingDirectory,
                shellCommand: string.IsNullOrWhiteSpace(s.Shell) ? null : s.Shell,
                arguments: s.Arguments);
            if (model is not null)
            {
                byIndex[i] = model;
                RememberSessionLayout(model, s);
                _startupCommands[model.Id] = (s.StartupCommand, s.RunStartupCommand);
                if (runStartupCommands && s.RunStartupCommand && !string.IsNullOrWhiteSpace(s.StartupCommand)
                    && !model.IsRemote)
                {
                    model.Emulator.PasteText(s.StartupCommand);
                    model.Emulator.SendText("\r");
                }
            }
        }
        if (byIndex.Count == 0)
        {
            await NewSession();
            return;
        }

        if (ws.IsSplit
            && byIndex.TryGetValue(ws.LeftIndex, out var left)
            && byIndex.TryGetValue(ws.RightIndex, out var right)
            && !ReferenceEquals(left, right))
        {
            LeftPane = left;
            RightPane = right;
            FocusedPane = ws.FocusedPane == 1 ? 1 : 0;
            IsSplit = true;
            _sessions.Activate(FocusedPane == 1 ? right : left);
        }
        else if (byIndex.TryGetValue(ws.ActiveIndex, out var active))
        {
            IsSplit = false;
            LeftPane = RightPane = null;
            _sessions.Activate(active);
        }
    }

    private static SessionTag ParseTag(string tag) => tag switch
    {
        "开发环境" or "dev" or "Dev" => SessionTag.Dev,
        "测试环境" or "test" or "Test" => SessionTag.Test,
        "部署控制" or "deploy" or "Deploy" => SessionTag.Deploy,
        "Codex" => SessionTag.Codex,
        "SSH" or "ssh" => SessionTag.Ssh,
        _ => SessionTag.None,
    };

    private async Task<TerminalSessionModel?> CreateSessionAsync(
        string? name, SessionTag tag, string cwd,
        ShellKind? shell = null, string? shellCommand = null, string? arguments = null)
    {
        var shellCmd = !string.IsNullOrWhiteSpace(shellCommand)
            ? shellCommand
            : _settings.ResolveShellCommand(shell);
        if (_missingStartupShell is not null && shellCmd == _missingStartupShell)
        {
            shellCmd = _settings.ResolveShellCommand();
            arguments = null; // arguments for the missing shell don't belong to its replacement
        }
        if (string.IsNullOrEmpty(cwd))
            cwd = OperatingSystem.IsWindows() ? "C:\\" : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (ShellIntegration.IsPowerShell(shellCmd)
            && (string.IsNullOrWhiteSpace(arguments) || arguments == ShellIntegration.LegacyPowerShellArguments))
            arguments = ShellIntegration.PowerShellArguments;
        // Same idea on Linux: bash gets OSC 133 (command + exit code) and OSC 7
        // (cwd) via a session-only --rcfile. Custom arguments mean a
        // non-interactive/script run — marks would be noise there. An unwritable
        // config dir must not block spawning — plain bash still works.
        else if (OperatingSystem.IsLinux() && ShellIntegration.IsBash(shellCmd)
            && string.IsNullOrWhiteSpace(arguments))
        {
            try { arguments = ShellIntegration.BashArguments; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        try
        {
            return await _sessions.CreateAsync(
                PtySessionFactory.Create,
                new PtyOptions { Shell = shellCmd, WorkingDirectory = cwd,
                    Arguments = arguments ?? "",
                    Environment = OperatingSystem.IsWindows() && shellCmd == "cmd.exe"
                        ? new Dictionary<string, string> { ["PROMPT"] = "$E]9;9;$P$E\\$P$G" }
                        : new Dictionary<string, string>() },
                name, tag);
        }
        catch (Exception ex)
        {
            // PTY spawn failures must be visible — a silent exception leaves the
            // user staring at an empty stage wondering where the terminal went.
            Dashboard.AppendOutput("error",
                $"创建会话失败 / failed to spawn terminal — {ex.Message}", "terminal");
            return null;
        }
    }

    /// <summary>SSH tab → spawn a session running the local ssh binary.</summary>
    private void ConnectSsh(TerminalHub.Core.Ssh.SshHost host)
    {
        Dashboard.AppendOutput("info", $"SSH 连接: {host.CommandLine}", "ssh");
        _ = SpawnSshAsync(host);
    }

    /// <summary>Awaits on the caller's context (UI) — Sessions mutations stay on the UI thread.</summary>
    private async Task SpawnSshAsync(TerminalHub.Core.Ssh.SshHost host)
    {
        try
        {
            await _sessions.CreateAsync(
                PtySessionFactory.Create,
                new PtyOptions
                {
                    Shell = "ssh",
                    Arguments = host.SshArguments,
                    WorkingDirectory = OperatingSystem.IsWindows()
                        ? "C:\\" : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                },
                host.DisplayName, SessionTag.Ssh);
        }
        catch (Exception ex)
        {
            Dashboard.AppendOutput("error",
                $"SSH 会话创建失败 / failed to spawn ssh — {ex.Message}", "ssh");
        }
    }

    [RelayCommand]
    private async Task NewSession()
    {
        var tags = new[] { SessionTag.Dev, SessionTag.Test, SessionTag.Deploy };
        var tag = tags[SessionCards.Count % tags.Length];
        await CreateSessionAsync(null, tag, "");
    }

    /// <summary>Toolbar「◫ 分屏」: side-by-side panes, each with its own PTY/Emulator.
    /// Left pane = the session that was active when split opened; right pane =
    /// the next session (a new one is spawned if only one exists). Pane clicks
    /// move focus (and thus ActiveSession/Output/Search) to that pane's session;
    /// clicking a sidebar card assigns it to the focused pane.</summary>
    [ObservableProperty] private bool _isSplit;
    [ObservableProperty] private TerminalSessionModel? _leftPane;
    [ObservableProperty] private TerminalSessionModel? _rightPane;
    /// <summary>0 = left pane focused, 1 = right.</summary>
    [ObservableProperty] private int _focusedPane;
    public string LeftPaneName => LeftPane?.Name ?? "未分配会话";
    public string RightPaneName => RightPane?.Name ?? "未分配会话";
    partial void OnLeftPaneChanged(TerminalSessionModel? value)
    {
        OnPropertyChanged(nameof(LeftPaneName));
        UpdateDisplayedCards();
    }
    partial void OnRightPaneChanged(TerminalSessionModel? value)
    {
        OnPropertyChanged(nameof(RightPaneName));
        UpdateDisplayedCards();
    }
    partial void OnIsSplitChanged(bool value) => UpdateDisplayedCards();

    [RelayCommand]
    private void ToggleSplit()
    {
        if (IsSplit) { ExitSplit(); return; }
        _ = EnterSplitAsync();
    }

    private async Task EnterSplitAsync()
    {
        // Read _sessions.Active directly — the ActiveSession property lags one
        // UI-thread post behind Activate() and would give us the stale session.
        var left = _sessions.Active ?? _sessions.Sessions.FirstOrDefault();
        var other = _sessions.Sessions.FirstOrDefault(s => !ReferenceEquals(s, left));
        if (left is null)
        {
            await NewSession();
            left = _sessions.Active ?? _sessions.Sessions.FirstOrDefault();
        }
        if (other is null && left is not null)
        {
            Dashboard.AppendOutput("info", "分屏: 只有一个会话,为右栏新建一个…", "split");
            await NewSession();                       // activates the new session
            // Re-pick from the list instead of trusting _sessions.Active: during
            // the await the user can activate/close sessions, so Active may still
            // be `left` (→ both panes one session) or a dead session.
            other = _sessions.Sessions.FirstOrDefault(s => !ReferenceEquals(s, left));
            if (_sessions.Sessions.Contains(left)) _sessions.Activate(left); // keep focus on the left pane
        }
        // Both panes must be live, distinct sessions — captured values can be
        // closed mid-await, and a null left would leave a dead pane behind.
        if (left is null || other is null
            || !_sessions.Sessions.Contains(left) || !_sessions.Sessions.Contains(other))
        {
            Dashboard.AppendOutput("warn", "分屏: 无法创建第二个会话", "split");
            return;
        }
        LeftPane = left;
        RightPane = other;
        FocusedPane = 0;
        IsSplit = true;
        Dashboard.AppendOutput("info",
            $"分屏: 左 {left.Name} ｜ 右 {other.Name} · 点击窗格聚焦,点会话卡分配到该窗格", "split");
    }

    private void ExitSplit()
    {
        IsSplit = false;
        LeftPane = RightPane = null;
        FocusedPane = 0;
        Dashboard.AppendOutput("info", "分屏: 已退出,回到单视图", "split");
    }

    /// <summary>Pane pointer-press → that pane's session becomes active (its
    /// output feeds Output/Logs/Search; the middle input targets it too).</summary>
    public void FocusPane(int pane)
    {
        FocusedPane = pane;
        var s = pane == 0 ? LeftPane : RightPane;
        if (s is not null && !ReferenceEquals(s, _sessions.Active))
            _sessions.Activate(s);
    }

    [RelayCommand]
    private void ActivateSession(SessionCardViewModel? card)
    {
        if (card is null) return;
        _sessions.Activate(card.Model);
    }

    /// <summary>Logs jump-to-session: activate the card whose name matches
    /// <paramref name="name"/>. Keeps the Logs tab open (does not touch
    /// <see cref="SelectedRightTab"/>). Unknown / missing → false.</summary>
    private bool TryActivateSessionByName(string name)
    {
        var card = SessionCards.FirstOrDefault(c =>
            string.Equals(c.Name, name, StringComparison.Ordinal));
        if (card is null) return false;
        _sessions.Activate(card.Model);
        return true;
    }

    [RelayCommand]
    private void CloseSession(SessionCardViewModel? card)
    {
        if (card is null) return;
        var vm = SessionCards.FirstOrDefault(c => ReferenceEquals(c.Model, card.Model));
        _sessions.Close(card.Model);
        if (vm is not null) SessionCards.Remove(vm);
    }

    /// <summary>Ctrl+W / 「••• → 关闭会话」: close the currently active session.</summary>
    [RelayCommand]
    private void CloseActiveSession() => CloseSession(ActiveCard);

    /// <summary>Ctrl+Tab / Ctrl+Shift+Tab: cycle session cards (wraps).</summary>
    public void CycleSession(int direction)
    {
        if (SessionCards.Count == 0) return;
        var idx = ActiveCard is null ? -1 : SessionCards.IndexOf(ActiveCard);
        var next = SessionCards[(idx + direction + SessionCards.Count) % SessionCards.Count];
        // Goes through the normal activation path (syncs ActiveSession,
        // split-pane assignment to the focused pane, Output/Logs switch).
        ActiveCard = next;
    }

    [RelayCommand] private void CycleSessionNext() => CycleSession(+1);
    [RelayCommand] private void CycleSessionPrev() => CycleSession(-1);

    private void LoadSessionShortcuts()
    {
        SessionShortcuts.Clear();
        foreach (var binding in _settings.SessionShortcuts)
            SessionShortcuts.Add(new SessionShortcutViewModel(binding, ValidateSessionShortcuts));
        ValidateSessionShortcuts();
    }

    private void ValidateSessionShortcuts()
    {
        foreach (var shortcut in SessionShortcuts)
        {
            shortcut.Error = "";
            shortcut.ParsedGesture = null;
            if (string.IsNullOrWhiteSpace(shortcut.Gesture)) continue;
            try
            {
                var gesture = ParseSessionGesture(shortcut.Gesture);
                if ((gesture.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) == 0
                    && gesture.Key is not (>= Key.F1 and <= Key.F24))
                    shortcut.Error = "请使用 Ctrl、Alt 或功能键，避免占用普通输入。";
                else if (IsReservedShortcut(gesture))
                    shortcut.Error = "与复制、粘贴或现有应用操作冲突。";
                else shortcut.ParsedGesture = gesture;
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            { shortcut.Error = "无法识别这个快捷键，请重新按键录入。"; }
        }
        foreach (var group in SessionShortcuts.Where(s => s.ParsedGesture is not null)
            .GroupBy(s => (s.ParsedGesture!.Key, s.ParsedGesture.KeyModifiers)).Where(g => g.Count() > 1))
            foreach (var shortcut in group) shortcut.Error = "与其他终端切换快捷键重复。";
        ValidateFavoriteGestures();
        OnPropertyChanged(nameof(NextSessionMenuText));
        OnPropertyChanged(nameof(PreviousSessionMenuText));
    }

    public string NextSessionMenuText => SessionMenuText(SessionShortcutAction.Next, "下一个会话");
    public string PreviousSessionMenuText => SessionMenuText(SessionShortcutAction.Previous, "上一个会话");
    private string SessionMenuText(SessionShortcutAction action, string label)
    {
        var binding = SessionShortcuts.FirstOrDefault(s => s.Binding.Action == action && s.Error.Length == 0 && s.ParsedGesture is not null);
        return binding is null ? label : $"{label}      {binding.Gesture}";
    }

    private static KeyGesture ParseSessionGesture(string text)
    {
        // KeyGesture.Parse treats bare digits as enum values ("1" becomes Cancel),
        // so translate the visible digit to the corresponding keyboard key.
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        if (parts[^1].Length == 1 && parts[^1][0] is >= '0' and <= '9') parts[^1] = "D" + parts[^1];
        return KeyGesture.Parse(string.Join('+', parts));
    }

    private static bool IsReservedShortcut(KeyGesture gesture)
    {
        string[] reserved = ["Ctrl+C", "Ctrl+V", "Ctrl+Shift+C", "Ctrl+Shift+N", "Ctrl+Shift+W",
            "Ctrl+Shift+B", "Ctrl+Shift+J", "Ctrl+OemPlus", "Ctrl+OemMinus", "Ctrl+0"];
        return reserved.Select(ParseSessionGesture).Any(item =>
            item.Matches(new KeyEventArgs { Key = gesture.Key, KeyModifiers = gesture.KeyModifiers }));
    }

    public bool HandleSessionShortcut(Avalonia.Input.KeyEventArgs e)
    {
        var shortcut = SessionShortcuts.FirstOrDefault(s => s.Error.Length == 0 && s.ParsedGesture?.Matches(e) == true);
        if (shortcut is null)
        {
            if (e.Source is Avalonia.Controls.TextBox) return false;
            var favorite = FavoriteCommands.FirstOrDefault(item => item.Error.Length == 0 && item.ParsedGesture?.Matches(e) == true);
            if (favorite is null) return false;
            InsertFavorite(favorite.Model);
            return true;
        }
        switch (shortcut.Binding.Action)
        {
            case SessionShortcutAction.CommandPalette: PaletteRequested?.Invoke(); break;
            case SessionShortcutAction.Next: CycleSession(1); break;
            case SessionShortcutAction.Previous: CycleSession(-1); break;
            case SessionShortcutAction.Select:
                if (shortcut.Binding.SessionIndex >= 0 && shortcut.Binding.SessionIndex < SessionCards.Count)
                    ActivateCard(SessionCards[shortcut.Binding.SessionIndex]);
                break;
        }
        return true;
    }

    [RelayCommand]
    private void ResetSessionShortcuts()
    {
        _settings.SessionShortcuts = SessionShortcutBinding.Defaults();
        LoadSessionShortcuts();
    }

    [RelayCommand] private void IncreaseFontSize() => AdjustFontSize(1);
    [RelayCommand] private void DecreaseFontSize() => AdjustFontSize(-1);
    [RelayCommand] private void ResetTerminalFontSize() => ResetFontSize();

    /// <summary>「••• → 复制 CWD」: copy the active session's real working directory.</summary>
    [RelayCommand]
    private void CopyActiveCwd()
    {
        var cwd = ActiveSession?.WorkingDirectory;
        if (string.IsNullOrEmpty(cwd)) return;
        _ = CopyTextToClipboardAsync(cwd);
        Dashboard.AppendOutput("info", $"已复制 CWD: {cwd}", "ui");
    }

    /// <summary>「↗ 在新窗口打开」: detach the given (or active) session into a
    /// standalone <see cref="SessionWindow"/>. The PTY/emulator keep running —
    /// the card leaves the main list and comes back when the popout closes.</summary>
    [RelayCommand(CanExecute = nameof(HasPopoutTarget))]
    private void OpenInNewWindow(SessionCardViewModel? card)
    {
        var model = card?.Model ?? ActiveSession;
        if (model is null) return;
        // Mark before detaching — SessionRemoved fires synchronously inside
        // Detach and must already see this as a popout, not a close.
        model.Detached = true;
        var session = _sessions.Detach(model);
        if (session is null)
        {
            model.Detached = false;
            return;
        }

        DetachedSessions.Add(session);
        var win = new SessionWindow(session, FontSize, TerminalFont);
        _popouts.Add(win);
        PositionPopout(win);
        win.Closed += (_, _) => OnPopoutClosed(win, session);
        Dashboard.AppendOutput("info",
            $"会话「{session.Name}」已弹出为独立窗口（关闭子窗即收回）", "window");
        win.Show();
    }

    private bool HasPopoutTarget(SessionCardViewModel? card)
        => (card?.Model ?? ActiveSession) is not null;

    /// <summary>Popout closed → return the session to the main window's list.
    /// During shutdown (<see cref="_disposed"/>) the session is already disposed —
    /// skip the reattach.</summary>
    private void OnPopoutClosed(SessionWindow win, TerminalSessionModel session)
    {
        _popouts.Remove(win);
        DetachedSessions.Remove(session);
        if (_disposed) return;
        _sessions.Reattach(session);
        Dashboard.AppendOutput("info", $"会话「{session.Name}」已收回主窗口", "window");
    }

    /// <summary>Cascade the popout over the main window so both stay visible.</summary>
    private static void PositionPopout(SessionWindow win)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime
                { MainWindow: { } main })
            win.Position = new Avalonia.PixelPoint(main.Position.X + 160, main.Position.Y + 110);
    }

    /// <summary>Rename a session (tab title); keeps Logs' session filter and output source names in sync.</summary>
    [RelayCommand]
    public void RenameSession((SessionCardViewModel Card, string Name) args)
    {
        var (card, name) = args;
        if (card is null || string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        // Startup entries have no session identity — rename every match instead
        // of the first, so duplicate names can't rewrite the wrong slot.
        for (var i = 0; i < _settings.StartupSessions.Count; i++)
            if (_settings.StartupSessions[i].Name == card.Name)
                _settings.StartupSessions[i] = _settings.StartupSessions[i] with { Name = name };
        var oldName = card.Name;
        _sessions.Rename(card.Model, name);
        _sessionNames[card.Model.Id] = name; // future output lines carry the new name
        // The filter combo belongs to the session — move it so the freed
        // auto-name doesn't hand it to a later recycled session.
        Logs.RenameSessionFilter(oldName, name);
        card.Refresh();
        Logs.RefreshSessions();
        OnPropertyChanged(nameof(LeftPaneName));
        OnPropertyChanged(nameof(RightPaneName));
    }

    private void OnSessionCardsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Logs.RefreshSessions();
        if (!_disposed) RebuildShelf();
    }

    /// <summary>Best-effort clipboard copy. False when headless (no window) or
    /// the clipboard is unavailable — callers use it to toast failure instead of
    /// success. Avalonia's Win32 clipboard throws TimeoutException while another
    /// process holds it open, so catch broadly like the window-level helper.</summary>
    private static async Task<bool> CopyTextToClipboardAsync(string text)
    {
        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime
                    is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow is { Clipboard: { } clipboard })
            {
                await clipboard.SetTextAsync(text);
                return true;
            }
        }
        catch (Exception ex)
        {
            // Clipboard unavailable (headless / locked desktop) — copy stays best-effort.
            System.Diagnostics.Trace.WriteLine($"Clipboard copy failed: {ex.Message}");
        }
        return false;
    }

    /// <summary>Save dialog for the Logs one-shot export. No window (headless/automation),
    /// a broken picker (Linux X11 without a desktop portal), or a picker that never
    /// answers → fall back to the default export path so the file still lands on disk;
    /// a dismissed dialog returns null and the VM reports "已取消导出".</summary>
    private async Task<string?> PromptExportPathAsync()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime
            { MainWindow: { StorageProvider: { } picker } })
        {
            try
            {
                var pick = picker.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "导出 Logs 可见行",
                    SuggestedFileName = Path.GetFileName(Logs.DefaultExportPath()),
                    FileTypeChoices =
                    [
                        new FilePickerFileType("日志文件") { Patterns = ["*.log"] },
                        new FilePickerFileType("文本文件") { Patterns = ["*.txt"] },
                    ],
                });
                // Without xdg-desktop-portal the DBus call can hang forever — don't let
                // that swallow the export; time out into the default path.
                var done = await Task.WhenAny(pick, Task.Delay(TimeSpan.FromSeconds(10)));
                if (done != pick) return Logs.DefaultExportPath();
                var file = await pick;
                return file?.TryGetLocalPath();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Logs.DefaultExportPath(); // picker broken on this platform — still export
            }
        }
        return Logs.DefaultExportPath();
    }


    /// <summary>Bottom dock: 0 New 1 Monitor 2 SSH 3 Logs 4 Deploy 5 Settings.
    /// Parameter arrives as a string from XAML — parse it (int also accepted).</summary>
    [RelayCommand]
    private void DockSelect(object? parameter)
    {
        if (!int.TryParse(parameter?.ToString(), out var index)) return;
        switch (index)
        {
            case 0: _ = NewSession(); break;
            case 1: InspectorVisible = true; DockHighlight = 1; SelectedRightTab = 0; break;
            case 2: InspectorVisible = true; DockHighlight = 2; SelectedRightTab = 3; break;
            case 3: InspectorVisible = true; DockHighlight = 3; SelectedRightTab = 2; break;
            case 4:
                var force = _deployCtrlHeld;
                _deployCtrlHeld = false;
                DeployFromDock(force);
                break;
            case 5: SettingsOpen = !SettingsOpen; break;
        }
    }

    /// <summary>One-shot arm from Ctrl+pointer-press on the Deploy button. Cleared when the click runs.</summary>
    public void ArmDeployCtrl(bool force) => _deployCtrlHeld = force;

    /// <summary>
    /// Deploy dock.
    /// Plain click opens <c>artifacts/publish</c> when it has files, and starts the
    /// platform publish script when it does not. <paramref name="forceRepublish"/>
    /// (Ctrl+click, or the dock menu「重新打包」) always starts the script.
    /// A non-empty <paramref name="startDirectory"/> is an explicit walk-up start and
    /// skips the active publish profile (tests / programmatic callers).
    /// The dock button passes null, so the active profile applies: its repo root
    /// (or the process working directory when that is empty) and its RID
    /// (<c>linux-x64</c> / <c>win-x64</c>, or the host platform when empty).
    /// </summary>
    public void DeployFromDock(bool forceRepublish, string? startDirectory = null)
    {
        Dashboard.SelectedBottomTab = 0;
        if (forceRepublish)
            Dashboard.AppendOutput("info", "Deploy: 重新打包 / force republish", "deploy");

        var profileDriven = string.IsNullOrWhiteSpace(startDirectory);
        if (profileDriven && PublishProfiles.TouchActive(_settings) is { } used)
        {
            SaveSettingsInternal();
            NotifyActivePublishProfile();
            Dashboard.AppendOutput("info", DescribeProfile(used, "使用配置档 / profile"), "deploy");
        }

        var start = profileDriven
            ? PublishProfiles.ResolveStartDirectory(_settings, null, Directory.GetCurrentDirectory())
            : startDirectory!;
        var platform = profileDriven
            ? PublishProfiles.ResolvePlatform(_settings, PublishPlanner.CurrentPlatform)
            : PublishPlanner.CurrentPlatform;

        RefreshRecentArtifacts(start);

        List<ArtifactLocator.ArtifactDir> found;
        try
        {
            found = ArtifactLocator.Find(start);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            Dashboard.AppendOutput("warn", $"Deploy: 无法读取产物目录 — {ex.Message}", "deploy");
            found = [];
        }

        if (PublishPlanner.Decide(found.Count > 0, forceRepublish) == DeployAction.OpenArtifacts)
            OpenArtifactFolder(found);
        else
            StartPublish(start, platform);
    }

    public string ActivePublishProfileId => _settings.ActivePublishProfileId;
    public string ActivePublishProfileName => PublishProfiles.Active(_settings)?.Name ?? "";

    public IReadOnlyList<PublishProfile> ListPublishProfiles() => PublishProfiles.List(_settings);

    public bool IsActivePublishProfile(string? id) =>
        !string.IsNullOrEmpty(id)
        && string.Equals(PublishProfiles.Active(_settings)?.Id, id, StringComparison.Ordinal);

    /// <summary>Dialog seed: active profile, or a suggested name plus the repo discovered from CWD.</summary>
    public PublishProfileDraft CurrentPublishDraft()
    {
        var active = PublishProfiles.Active(_settings);
        var cwd = Directory.GetCurrentDirectory();
        var discovered = PublishPlanner.FindRepoRoot(
            !string.IsNullOrWhiteSpace(active?.RepoRoot) ? active!.RepoRoot : cwd) ?? "";
        var name = string.IsNullOrWhiteSpace(active?.Name)
            ? PublishProfiles.SuggestName(_settings, PublishProfiles.HostRid(PublishPlanner.CurrentPlatform))
            : active!.Name;
        var root = active is null ? discovered : active.RepoRoot;
        return new PublishProfileDraft(name, root ?? "", active?.Rid ?? "", active?.Note ?? "");
    }

    /// <summary>Upsert by name, persist, and make it the profile the next dock Deploy uses.</summary>
    public bool SavePublishProfile(string? name, string? repoRoot, string? rid, string? note)
    {
        var saved = PublishProfiles.Save(_settings, name, repoRoot, rid, note);
        if (saved is null)
        {
            Dashboard.AppendOutput("warn", "Deploy: 配置档名称不能为空 / profile name required", "deploy");
            return false;
        }
        SaveSettingsInternal();
        NotifyActivePublishProfile();
        Dashboard.AppendOutput("info",
            DescribeProfile(saved, "已保存配置档 / profile saved") + " 下次 Deploy 使用该配置。",
            "deploy");
        return true;
    }

    /// <summary>Switch the active profile. The next dock Deploy uses its root and RID.</summary>
    public bool ActivatePublishProfile(string? idOrName)
    {
        if (!PublishProfiles.Activate(_settings, idOrName))
        {
            Dashboard.AppendOutput("warn", $"Deploy: 找不到配置档 / profile not found {idOrName}", "deploy");
            return false;
        }
        SaveSettingsInternal();
        NotifyActivePublishProfile();
        var active = PublishProfiles.Active(_settings)!;
        Dashboard.AppendOutput("info",
            DescribeProfile(active, "已切换配置档 / profile active") + " 下次 Deploy 使用该配置。",
            "deploy");
        return true;
    }

    public bool DeletePublishProfile(string? idOrName)
    {
        var existing = PublishProfiles.Find(_settings, idOrName);
        if (existing is null || !PublishProfiles.Delete(_settings, idOrName))
        {
            Dashboard.AppendOutput("warn", $"Deploy: 找不到配置档 / profile not found {idOrName}", "deploy");
            return false;
        }
        SaveSettingsInternal();
        NotifyActivePublishProfile();
        Dashboard.AppendOutput("info", $"Deploy: 已删除配置档 / profile deleted {existing.Name}", "deploy");
        return true;
    }

    /// <summary>Rescan <c>artifacts/publish</c> from the active profile root, or CWD when it has none.</summary>
    public IReadOnlyList<RecentArtifact> QueryRecentArtifacts()
    {
        var start = PublishProfiles.ResolveStartDirectory(_settings, null, Directory.GetCurrentDirectory());
        return RefreshRecentArtifacts(start);
    }

    /// <summary>Open one recent artifact directory with the same file-manager path as a dock click.</summary>
    public void OpenRecentArtifact(string? path)
    {
        Dashboard.SelectedBottomTab = 0;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            Dashboard.AppendOutput("warn", "Deploy: 产物目录不存在 / artifact folder missing", "deploy");
            QueryRecentArtifacts();
            return;
        }

        string[] files;
        try { files = Directory.GetFiles(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dashboard.AppendOutput("warn", $"Deploy: 无法读取产物目录 — {ex.Message}", "deploy");
            return;
        }
        OpenArtifactFolder([new ArtifactLocator.ArtifactDir(path, files)]);
    }

    /// <summary>
    /// Open the last successful publish folder with the same file-manager path as a recent-artifact click.
    /// A missing directory stays stored and is reported; the menu item is disabled in that case.
    /// </summary>
    public void OpenLastSuccessfulArtifact()
    {
        Dashboard.SelectedBottomTab = 0;
        var path = _settings.LastPublishResult?.ArtifactPath;
        if (!CanOpenLastSuccessfulArtifact)
        {
            Dashboard.AppendOutput("warn",
                "Deploy: 上次成功产物不存在 / last successful artifact missing", "deploy");
            OnPropertyChanged(nameof(CanOpenLastSuccessfulArtifact));
            OnPropertyChanged(nameof(CanCopyLastSuccessfulArtifact));
            return;
        }
        OpenRecentArtifact(path);
    }

    /// <summary>
    /// Copy the last successful artifact directory path. Same enable gate as open-last;
    /// vanishes between menu open and click → Output warn, no throw.
    /// </summary>
    public void CopyLastSuccessfulArtifactPath()
    {
        Dashboard.SelectedBottomTab = 0;
        var path = _settings.LastPublishResult?.ArtifactPath;
        if (!CanCopyLastSuccessfulArtifact || string.IsNullOrWhiteSpace(path))
        {
            Dashboard.AppendOutput("warn",
                "Deploy: 上次成功产物不存在 / last successful artifact missing", "deploy");
            OnPropertyChanged(nameof(CanOpenLastSuccessfulArtifact));
            OnPropertyChanged(nameof(CanCopyLastSuccessfulArtifact));
            return;
        }
        _ = CopyTextToClipboardAsync(path);
        Dashboard.AppendOutput("info", $"已复制产物路径 {path}", "deploy");
    }

    /// <summary>
    /// Clear the persisted last-publish outcome. Enabled whenever a known result exists
    /// (even if the artifact folder is gone). Refreshes badge / open / copy / clear gates.
    /// </summary>
    public void ClearLastPublishResult()
    {
        Dashboard.SelectedBottomTab = 0;
        if (!CanClearLastPublishResult)
        {
            NotifyLastPublish();
            return;
        }
        LastPublishResults.Clear(_settings);
        PersistSettings();
        NotifyLastPublish();
        Dashboard.AppendOutput("info", "已清除上次发布结果", "deploy");
    }

    private IReadOnlyList<RecentArtifact> RefreshRecentArtifacts(string? start)
    {
        RecentArtifacts = RecentArtifactList.List(start);
        return RecentArtifacts;
    }

    private void AnnounceRecentArtifacts(string start)
    {
        var rows = RefreshRecentArtifacts(start);
        Dashboard.AppendOutput("info",
            $"Deploy: 最近产物已刷新 / recent artifacts ({rows.Count})", "deploy");
        foreach (var row in rows)
            Dashboard.AppendOutput("info",
                $"  {row.Rid}  {FmtBytes(row.SizeBytes)}  {row.Modified.ToLocalTime():yyyy-MM-dd HH:mm}  {row.Path}",
                "deploy");
    }

    private static string DescribeProfile(PublishProfile profile, string verb)
    {
        var root = string.IsNullOrWhiteSpace(profile.RepoRoot) ? "cwd" : profile.RepoRoot;
        var rid = string.IsNullOrWhiteSpace(profile.Rid) ? "host" : profile.Rid;
        var note = string.IsNullOrWhiteSpace(profile.Note) ? "" : $" · {profile.Note}";
        return $"Deploy: {verb} {profile.Name} — root {root} · rid {rid}{note}.";
    }

    private void OpenArtifactFolder(IReadOnlyList<ArtifactLocator.ArtifactDir> found)
    {
        foreach (var d in found)
        {
            Dashboard.AppendOutput("info", $"Deploy: 产物目录 {d.Path}", "deploy");
            foreach (var f in d.Files)
            {
                var fi = new FileInfo(f);
                Dashboard.AppendOutput("info", $"  {fi.Name}  ({FmtBytes(fi.Length)})", "deploy");
            }
        }
        try
        {
            _openFolder(found[0].Path);
            Dashboard.AppendOutput("info", "Deploy: 已在文件管理器中打开产物目录", "deploy");
        }
        catch
        {
            Dashboard.AppendOutput("warn", "Deploy: 无法打开文件管理器 — 请手动访问上面目录", "deploy");
        }
        Dashboard.AppendOutput("info",
            "Deploy: 重新发布请按住 Ctrl 再点 Deploy，或右键菜单「重新打包」。", "deploy");
    }

    private static void OpenFolderInFileManager(string path)
        => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path)
            { UseShellExecute = true });

    private void StartPublish(string startDir, PublishPlatform platform)
    {
        if (PublishBusy())
        {
            Dashboard.AppendOutput("warn",
                "Deploy: 打包进行中 / publish already running — 请等待当前 Publish 会话结束", "deploy");
            return;
        }

        var winShell = platform == PublishPlatform.Windows ? ResolveWindowsPublishShell() : null;
        var plan = PublishPlanner.TryPlan(startDir, platform, winShell);
        if (plan is null)
        {
            PrintPublishHints("Deploy: 找不到发布脚本 / publish script not found");
            return;
        }

        Dashboard.AppendOutput("info", $"Deploy: 开始打包 / publish start — {plan.DisplayCommand}", "deploy");
        Dashboard.SelectedBottomTab = 0;
        lock (_publishLock)
        {
            _publishStarting++;
            _idleCancelWarned = false;
            _publishAttemptStartedAt = DateTimeOffset.UtcNow;
        }
        RefreshPublishRunning();
        _ = SpawnPublishAsync(plan);
    }

    private async Task SpawnPublishAsync(PublishPlan plan)
    {
        var spawned = Guid.Empty;
        try
        {
            var session = await _sessions.CreateAsync(
                () =>
                {
                    // Register the id before StartAsync so the first subscribed
                    // script line is already known to be a publish stream.
                    var pty = PtySessionFactory.Create();
                    lock (_publishLock)
                    {
                        spawned = pty.Id;
                        _publishIds.Add(pty.Id);
                        _publishRoots[pty.Id] = plan.RepoRoot;
                        _publishStartedAt[pty.Id] = _publishAttemptStartedAt;
                        if (_cancelPendingStart)
                        {
                            _publishCancelled.Add(pty.Id);
                            _cancelPendingStart = false;
                        }
                    }
                    return pty;
                },
                new PtyOptions
                {
                    Shell = plan.Shell,
                    Arguments = plan.Arguments,
                    WorkingDirectory = plan.WorkingDirectory,
                },
                "Publish",
                SessionTag.Deploy);
            // One-shot task session: the workspace snapshot must skip it or the
            // publish script re-runs on every restart.
            session.ExcludeFromWorkspace = true;
            session.Pty.Exited += (_, code) => RunOnUi(() => ReportPublishExit(session.Id, code));
            if (!session.IsRunning)
                ReportPublishExit(session.Id, session.Pty.ExitCode ?? -1);
            Dashboard.SelectedBottomTab = 0;
            Dashboard.AppendOutput("info",
                "Deploy: 已启动会话 Publish · 标签 部署控制 (Deploy)。结束时 Output 会显示成功或失败。",
                "deploy");

            bool cancelNow;
            lock (_publishLock) cancelNow = _publishCancelled.Contains(session.Id);
            if (cancelNow && session.IsRunning)
                session.Pty.Kill();
        }
        catch (Exception ex)
        {
            bool cancelled;
            bool record;
            string? root = null;
            DateTimeOffset started;
            lock (_publishLock)
            {
                cancelled = _cancelPendingStart
                    || (spawned != Guid.Empty && _publishCancelled.Contains(spawned));
                if (spawned != Guid.Empty)
                {
                    // False when ReportPublishExit already consumed this id.
                    record = _publishIds.Remove(spawned);
                    _publishRoots.TryGetValue(spawned, out root);
                    _publishRoots.Remove(spawned);
                    _publishCancelled.Remove(spawned);
                    _publishOutputArmed.Remove(spawned);
                    if (!_publishStartedAt.Remove(spawned, out started))
                        started = _publishAttemptStartedAt;
                }
                else
                {
                    record = true;
                    started = _publishAttemptStartedAt;
                }
                _cancelPendingStart = false;
            }
            if (cancelled)
                Dashboard.AppendOutput("warn",
                    "Deploy: 已取消打包 / publish cancelled", "deploy");
            else
                Dashboard.AppendOutput("error",
                    $"Deploy: 启动失败 / publish failed to start — {ex.Message}", "deploy");
            if (record)
            {
                var outcome = cancelled ? LastPublishResults.Cancelled : LastPublishResults.Fail;
                var rootCopy = root;
                var startedCopy = started;
                RunOnUi(() => RecordPublishOutcome(outcome, -1, rootCopy, null, startedCopy));
            }
        }
        finally
        {
            lock (_publishLock) _publishStarting--;
            RunOnUi(RefreshPublishRunning);
        }
    }

    /// <summary>True while a publish is starting or its session is still alive.</summary>
    public bool PublishBusy()
    {
        lock (_publishLock)
        {
            if (_publishStarting > 0) return true;
            return _sessions.Sessions.Any(s => _publishIds.Contains(s.Id) && s.IsRunning);
        }
    }

    private void RefreshPublishRunning()
    {
        var running = PublishBusy();
        if (IsPublishRunning != running)
            IsPublishRunning = running;
    }

    /// <summary>
    /// Stop the running publish. Signals the session PTY (process group on Linux)
    /// and lets the exit handler log the cancel. Idle and double-cancel are safe.
    /// </summary>
    [RelayCommand]
    private void CancelPublish()
    {
        List<TerminalSessionModel> toKill = [];
        var warnIdle = false;
        lock (_publishLock)
        {
            var ids = _publishIds.ToArray();
            var pending = _publishStarting > 0;
            if (ids.Length == 0 && !pending)
            {
                if (!_idleCancelWarned)
                {
                    _idleCancelWarned = true;
                    warnIdle = true;
                }
            }
            else
            {
                var newly = false;
                foreach (var id in ids)
                    if (_publishCancelled.Add(id)) newly = true;
                if (pending && !_cancelPendingStart)
                {
                    _cancelPendingStart = true;
                    newly = true;
                }
                if (!newly) return;
                foreach (var id in ids)
                {
                    var session = _sessions.Sessions.FirstOrDefault(s => s.Id == id && s.IsRunning);
                    if (session is not null) toKill.Add(session);
                }
            }
        }

        if (warnIdle)
        {
            Dashboard.SelectedBottomTab = 0;
            Dashboard.AppendOutput("warn",
                "Deploy: 没有进行中的打包 / publish is not running", "deploy");
            return;
        }

        foreach (var session in toKill)
            session.Pty.Kill();
    }

    private void ReportPublishExit(Guid id, int code)
    {
        bool cancelled;
        string? root;
        DateTimeOffset started;
        lock (_publishLock)
        {
            if (!_publishIds.Remove(id)) return;
            cancelled = _publishCancelled.Remove(id);
            _publishOutputArmed.Remove(id);
            _publishRoots.TryGetValue(id, out root);
            _publishRoots.Remove(id);
            if (!_publishStartedAt.Remove(id, out started))
                started = _publishAttemptStartedAt;
        }
        RefreshPublishRunning();
        Dashboard.SelectedBottomTab = 0;
        string? artifact = null;
        if (cancelled)
            Dashboard.AppendOutput("warn",
                "Deploy: 已取消打包 / publish cancelled", "deploy");
        else if (code == 0)
        {
            Dashboard.AppendOutput("info",
                "Deploy: 打包成功 / publish succeeded。再次点击 Deploy 打开产物目录 artifacts/publish。",
                "deploy");
            if (!string.IsNullOrWhiteSpace(root))
            {
                AnnounceRecentArtifacts(root);
                artifact = RecentArtifacts.FirstOrDefault()?.Path;
            }
        }
        else
            Dashboard.AppendOutput("error",
                $"Deploy: 打包失败 / publish failed (exit {code})。请查看 Publish 终端输出。",
                "deploy");

        RecordPublishOutcome(
            cancelled ? LastPublishResults.Cancelled : code == 0 ? LastPublishResults.Success : LastPublishResults.Fail,
            code,
            root,
            artifact,
            started);
    }

    /// <summary>Persist a real terminal outcome and refresh dock bindings. No invented success.</summary>
    private void RecordPublishOutcome(
        string outcome, int exitCode, string? repoRoot, string? artifactPath, DateTimeOffset started)
    {
        var finished = DateTimeOffset.UtcNow;
        LastPublishResults.Record(
            _settings, outcome, exitCode, finished, ElapsedMs(started, finished), repoRoot, artifactPath);
        PersistSettings();
        NotifyLastPublish();
    }

    private static long ElapsedMs(DateTimeOffset started, DateTimeOffset finished)
    {
        if (started == default) return 0;
        var ms = (finished - started).TotalMilliseconds;
        if (double.IsNaN(ms) || ms <= 0) return 0;
        return ms >= long.MaxValue ? long.MaxValue : (long)ms;
    }

    private void NotifyLastPublish()
    {
        OnPropertyChanged(nameof(LastPublishSummary));
        OnPropertyChanged(nameof(LastPublishBadge));
        OnPropertyChanged(nameof(HasLastPublishBadge));
        OnPropertyChanged(nameof(CanOpenLastSuccessfulArtifact));
        OnPropertyChanged(nameof(CanCopyLastSuccessfulArtifact));
        OnPropertyChanged(nameof(CanClearLastPublishResult));
        DeployDockTip = ComposeDeployDockTip();
    }

    private void NotifyActivePublishProfile()
    {
        OnPropertyChanged(nameof(ActivePublishProfileId));
        OnPropertyChanged(nameof(ActivePublishProfileName));
        OnPropertyChanged(nameof(ActivePublishProfileLabel));
        OnPropertyChanged(nameof(HasActivePublishProfileLabel));
        DeployDockTip = ComposeDeployDockTip();
    }

    private void PrintPublishHints(string reason)
    {
        Dashboard.AppendOutput("warn", reason, "deploy");
        Dashboard.AppendOutput("info", OperatingSystem.IsWindows()
            ? "  打包 Windows: scripts\\publish-windows.ps1 (需 Inno Setup)"
            : "  打包 Linux:   ./scripts/publish-linux.sh  → artifacts/publish/linux-x64/", "deploy");
        Dashboard.AppendOutput("info", OperatingSystem.IsWindows()
            ? "  打包 Linux:   ./scripts/publish-linux.sh"
            : "  打包 Windows: scripts\\publish-windows.ps1 (需在 Windows + Inno Setup)", "deploy");
        Dashboard.AppendOutput("info", "  在仓库根目录运行上述命令，完成后再次点击 Deploy 打开产物目录", "deploy");
    }

    private static string ResolveWindowsPublishShell() =>
        PublishPlanner.ResolveWindowsShell(name =>
            PublishPlanner.NameOnPath(name, Environment.GetEnvironmentVariable("PATH"), windows: true));

    private static void RunOnUi(Action action)
    {
        var ui = Avalonia.Threading.Dispatcher.UIThread;
        if (ui.CheckAccess()) action();
        else ui.Post(action);
    }

    /// <summary>Shell ComboBox index ⇄ ShellKind.</summary>
    public int ShellIndex
    {
        get => _settings.Shell switch
        {
            ShellKind.PowerShell => 0,
            ShellKind.Cmd => 1,
            ShellKind.Wsl => 2,
            ShellKind.Bash => 3,
            ShellKind.Custom => 4,
            _ => 0,
        };
        set
        {
            _settings.Shell = value switch
            {
                1 => ShellKind.Cmd,
                2 => ShellKind.Wsl,
                3 => ShellKind.Bash,
                4 => ShellKind.Custom,
                _ => ShellKind.PowerShell,
            };
            OnPropertyChanged();
        }
    }

    /// <summary>Live-bound font size → writes through to settings + notifies TerminalView.</summary>
    public double FontSize
    {
        get => _settings.FontSize;
        set
        {
            if (_settings.FontSize == value) return;
            _settings.FontSize = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Ctrl+= / Ctrl+- zoom, persisted with the rest of the workspace.</summary>
    public void AdjustFontSize(double delta)
        => FontSize = Math.Clamp(Math.Round(FontSize + delta), 8, 32);

    /// <summary>Ctrl+0 — back to the default 13pt.</summary>
    public void ResetFontSize() => FontSize = 13;

    /// <summary>Terminal font family; an empty setting falls back to the bundled stack.</summary>
    public Avalonia.Media.FontFamily TerminalFont =>
        string.IsNullOrWhiteSpace(_settings.FontFamily)
            ? new Avalonia.Media.FontFamily("Cascadia Code, Consolas, Menlo, DejaVu Sans Mono, monospace")
            : new Avalonia.Media.FontFamily(_settings.FontFamily);

    /// <summary>Settings-panel text for <see cref="AppSettings.FontFamily"/>.</summary>
    public string TerminalFontName
    {
        get => _settings.FontFamily;
        set
        {
            value ??= "";
            if (_settings.FontFamily == value) return;
            _settings.FontFamily = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TerminalFont));
        }
    }

    /// <summary>Shelf drag: drop <paramref name="card"/> onto <paramref name="target"/>'s
    /// slot. Card order is the workspace order saved by <see cref="SnapshotWorkspace"/>.</summary>
    public void MoveSessionCard(SessionCardViewModel? card, SessionCardViewModel? target)
    {
        if (card is null || target is null || ReferenceEquals(card, target)) return;
        var from = SessionCards.IndexOf(card);
        var to = SessionCards.IndexOf(target);
        if (from < 0 || to < 0 || from == to) return;
        card.Model.GroupId = target.Model.GroupId;
        card.Model.Pinned = target.Model.Pinned;
        SessionCards.Move(from, to);
        SyncActive();
    }

    public string WorkspaceNameLive
    {
        get => _settings.WorkspaceName;
        set { _settings.WorkspaceName = value; WorkspaceName = value; OnPropertyChanged(); }
    }

    [RelayCommand]
    private void SaveSettings()
    {
        SaveSettingsInternal();
        SettingsOpen = false;
    }

    /// <summary>Every save also snapshots the live workspace (session order,
    /// names, cwd, shell, split, active) so a later launch restores this shape
    /// as fresh processes.</summary>
    private void SaveSettingsInternal()
    {
        TerminalLinkOpener.EditorPath = _settings.FileEditorPath;
        _settings.SessionGroups = SessionGroups.ToList();
        _settings.FavoriteCommands = FavoriteCommands.Select(favorite => favorite.Model).ToList();
        if (!ShellSetupOpen) SnapshotWorkspace();
        _settingsStore.Save(_settings);
    }

    /// <summary>Copy the live layout into <see cref="AppSettings.Workspace"/>.
    /// Order follows the session shelf (the user-visible order after drag
    /// reorder); popout sessions go last so they come back as normal cards.</summary>
    private void SnapshotWorkspace()
        => _settings.Workspace = CaptureWorkspace();

    private readonly Dictionary<Guid, (string Command, bool Run)> _startupCommands = new();
    private WorkspaceState CaptureWorkspace()
    {
        var ordered = SessionCards.Select(c => c.Model)
            .Concat(DetachedSessions)
            .Where(m => !m.ExcludeFromWorkspace)
            .ToList();
        var ws = new WorkspaceState();
        ws.Sessions = ordered.Select(m => new WorkspaceSession
        {
            Name = m.Name,
            Tag = m.Tag.DisplayName(),
            // An ssh session's cwd is a REMOTE path — saving it would make the
            // respawned ssh try to start in a nonexistent local directory.
            WorkingDirectory = m.Tag == SessionTag.Ssh ? "" : m.WorkingDirectory,
            Shell = m.Shell,
            Arguments = m.ShellArguments,
            StartupCommand = _startupCommands.GetValueOrDefault(m.Id).Command ?? "",
            RunStartupCommand = _startupCommands.GetValueOrDefault(m.Id).Run,
            GroupId = m.GroupId,
            GroupName = SessionGroups.FirstOrDefault(group => group.Id == m.GroupId)?.Name ?? "",
            Pinned = m.Pinned,
            ColorScheme = m.Emulator.ColorScheme,
        }).ToList();
        ws.ActiveIndex = IndexOf(_sessions.Active);
        ws.IsSplit = IsSplit;
        ws.LeftIndex = IndexOf(LeftPane);
        ws.RightIndex = IndexOf(RightPane);
        ws.FocusedPane = FocusedPane;
        return ws;
        int IndexOf(TerminalSessionModel? m) => m is null ? -1 : ordered.IndexOf(m);
    }

    /// <summary>Logs filter changed → copy into <see cref="_settings"/> and save
    /// (small JSON; every change is fine, no debounce needed). The global fields keep
    /// 「全部会话」's last known combo; named sessions persist in their own map.</summary>
    private void PersistLogsFilters()
    {
        var global = Logs.SnapshotGlobalFilters();
        _settings.LogsFilterText = global.FilterText;
        _settings.LogsUseRegex = global.UseRegex;
        _settings.LogsLevelFilterIndex = global.LevelFilterIndex;
        _settings.LogsRetainHistoryOnClear = global.RetainHistoryOnClear;
        _settings.LogsUseRelativeTimestamps = Logs.UseRelativeTimestamps;
        _settings.LogsWrapLines = Logs.WrapLines;
        _settings.LogsCompactDensity = Logs.CompactDensity;
        _settings.LogsBufferCapacity = Logs.BufferCapacity;
        _settings.LogsSessionFilters = Logs.SnapshotSessionFilters();
        SaveSettingsInternal();
    }


    private CwdHistory HistoryFor(TerminalSessionModel s)
    {
        lock (_cwdLock)
        {
            if (!_cwdHistories.TryGetValue(s.Id, out var h))
            {
                h = new CwdHistory();
                _cwdHistories[s.Id] = h;
            }
            return h;
        }
    }

    private void UpdateBreadcrumbFrom(string? cwd)
    {
        ActiveWorkingDirectory = cwd ?? "";
        Breadcrumb = string.IsNullOrEmpty(cwd)
            ? ""
            : (OperatingSystem.IsWindows()
                ? cwd.Replace('\\', '〉').Replace("〉", " > ")
                : "~/" + System.IO.Path.GetFileName(cwd));
    }

    private void UpdateCwdNavFlags()
    {
        if (ActiveSession is null)
        {
            CanCwdBack = CanCwdForward = false;
            return;
        }
        var h = HistoryFor(ActiveSession);
        lock (_cwdLock)
        {
            CanCwdBack = h.CanGoBack;
            CanCwdForward = h.CanGoForward;
        }
    }

    /// <summary>Session-cwd normalize: a remote (ssh) path stays verbatim — the
    /// local <see cref="CwdHistory.Normalize"/> would anchor "/home/u" at
    /// "C:\home\u", which is neither a real local dir nor a valid remote cd.</summary>
    private static string NormalizeSessionPath(TerminalSessionModel s, string path)
    {
        if (s.Tag != SessionTag.Ssh) return CwdHistory.Normalize(path);
        var t = path.Trim();
        return t.Length > 1 ? t.TrimEnd('/') : t;
    }

    private void OnSessionCwdReported(TerminalSessionModel s, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        path = NormalizeSessionPath(s, path);
        if (path.Length == 0) return;
        lock (_cwdLock)
        {
            s.WorkingDirectory = path;
            if (s.Tag == SessionTag.Ssh) HistoryFor(s).PushRaw(path);
            else HistoryFor(s).Push(path);
        }
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            SessionCards.FirstOrDefault(c => ReferenceEquals(c.Model, s))?.Refresh();
            if (!ReferenceEquals(ActiveSession, s)) return;
            UpdateBreadcrumbFrom(path);
            UpdateCwdNavFlags();
            // Files browses the LOCAL filesystem — never navigate it to a remote path.
            if (s.Tag != SessionTag.Ssh && InspectorVisible && SelectedRightTab == 1)
                Files.NavigateTo(path);
        });
    }

    /// <summary>Re-read the active session CWD (Linux /proc if possible) and sync toolbar + Files.</summary>
    [RelayCommand]
    private void RefreshCwd()
    {
        if (ActiveSession is null) return;
        // /proc probing reads the LOCAL ssh client's cwd, not the remote shell's.
        var probed = ActiveSession.Tag == SessionTag.Ssh ? null : ProcessCwd.TryRead(ActiveSession.Pty);
        var path = !string.IsNullOrEmpty(probed) ? probed! : ActiveSession.WorkingDirectory;
        if (string.IsNullOrEmpty(path)) return;
        ApplyDisplayedCwd(path, sendCd: false, recordHistory: true);
    }

    [RelayCommand]
    private void CwdBack()
    {
        if (ActiveSession is null) return;
        string? path;
        lock (_cwdLock) path = HistoryFor(ActiveSession).Back();
        if (path is null) return;
        ApplyDisplayedCwd(path, sendCd: true, recordHistory: false);
    }

    [RelayCommand]
    private void CwdForward()
    {
        if (ActiveSession is null) return;
        string? path;
        lock (_cwdLock) path = HistoryFor(ActiveSession).Forward();
        if (path is null) return;
        ApplyDisplayedCwd(path, sendCd: true, recordHistory: false);
    }

    private void ApplyDisplayedCwd(string path, bool sendCd, bool recordHistory)
    {
        if (ActiveSession is null) return;
        var ssh = ActiveSession.Tag == SessionTag.Ssh;
        path = NormalizeSessionPath(ActiveSession, path);
        if (path.Length == 0) return;
        lock (_cwdLock)
        {
            ActiveSession.WorkingDirectory = path;
            if (recordHistory)
            {
                if (ssh) HistoryFor(ActiveSession).PushRaw(path);
                else HistoryFor(ActiveSession).Push(path);
            }
        }
        UpdateBreadcrumbFrom(path);
        if (!ssh) Files.NavigateTo(path);
        UpdateCwdNavFlags();
        if (sendCd && ActiveSession.IsRunning)
            ActiveSession.Emulator.SendText($"cd {QuoteForShell(path)}\r");
    }

    /// <summary>Files「在此打开终端」target: real `cd` into the active PTY and
    /// sync the CWD chrome (path bar / history / Files) via the same path the
    /// toolbar ←/→ uses.</summary>
    private void CdActiveSessionTo(string path)
    {
        if (ActiveSession is null) return;
        ApplyDisplayedCwd(path, sendCd: true, recordHistory: true);
    }

    private string QuoteForShell(string path)
    {
        var shell = ActiveSession?.Shell;
        if (string.IsNullOrWhiteSpace(shell))
            shell = OperatingSystem.IsWindows() ? "powershell" : "sh";
        return ShellPathInput.Format(new[] { path }, shell);
    }

    /// <summary>Per-session UTF-8 line decoders feeding the real Output log.
    /// Read/written on PTY read threads — concurrent collections only.</summary>
    private readonly ConcurrentDictionary<Guid, Utf8LineDecoder> _lineDecoders = new();
    private readonly ConcurrentDictionary<Guid, string> _sessionNames = new();

    private void OnPtyOutput(IPtySession pty, ReadOnlyMemory<byte> data)
    {
        var dec = _lineDecoders.GetOrAdd(pty.Id, id =>
        {
            var d = new Utf8LineDecoder();
            d.LineReceived += line =>
            {
                var level = ClassifyLine(line);
                var name = _sessionNames.GetValueOrDefault(id, "session");
                bool publish;
                bool focusOutput;
                lock (_publishLock)
                {
                    publish = _publishIds.Contains(id);
                    focusOutput = publish && _publishOutputArmed.Add(id);
                }
                // Publish script bytes are real PTY output. Show them as deploy
                // without inventing progress text. Other sessions keep their name.
                if (focusOutput)
                    RunOnUi(() => Dashboard.SelectedBottomTab = 0);
                Dashboard.AppendOutput(level, line, publish ? "deploy" : name);
                _sessionLog.Write(name, level, line);
            };
            d.RawLineReceived += raw =>
                Dashboard.AppendDebug(
                    TerminalHub.Core.Logging.AnsiText.DebugEscape(raw),
                    _sessionNames.GetValueOrDefault(id, "session"));
            return d;
        });
        dec.Feed(data.Span);
    }

    private static string ClassifyLine(string line) => LineClassifier.Classify(line);

    private void OnSessionAdded(TerminalSessionModel s)
    {
        _sessionNames[s.Id] = s.Name;
        // Reattach after a popout: the PTY wiring and cwd history were kept
        // alive through the detach — re-subscribing would double every output
        // line and every cwd report.
        var reattach = s.Detached;
        s.Detached = false;
        if (!reattach)
        {
            s.Pty.OutputReceived += OnPtyOutput;
            // For ssh the spawn dir is the local client's cwd, not the remote
            // shell's — pushing it would seed the history with a path that
            // `cd` back fails on remotely.
            if (s.Tag != SessionTag.Ssh && !string.IsNullOrEmpty(s.WorkingDirectory))
                lock (_cwdLock) HistoryFor(s).Push(s.WorkingDirectory);
            s.Emulator.CwdChanged += path => OnSessionCwdReported(s, path);
            s.Emulator.CommandCompleted += command => OnCommandCompleted(s, command);
        }
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            // The window can close between SessionAdded and this post — no card
            // would ever be created and the spawned PTY would leak.
            if (_disposed) { s.Dispose(); return; }
            var card = new SessionCardViewModel(s);
            card.Refresh();
            SessionCards.Add(card);
            RefreshCounts();
            SyncActive();
        });
    }

    private void OnSessionRemoved(TerminalSessionModel s)
    {
        // Detach (popout) keeps the session alive off-list: PTY output keeps
        // feeding Output/Logs, cwd history survives, and CwdChanged stays wired —
        // everything resumes seamlessly on Reattach. A real close tears it down.
        // Capture now — Reattach can reset Detached before the posted lambda runs.
        var detached = s.Detached;
        if (!detached)
        {
            s.Pty.OutputReceived -= OnPtyOutput;
            _lineDecoders.TryRemove(s.Id, out _);
            _sessionNames.TryRemove(s.Id, out _);
            lock (_cwdLock) _cwdHistories.Remove(s.Id);
        }
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (IsSplit)
            {
                if (ReferenceEquals(RightPane, s))
                    RightPane = PaneFallbackFor(s, LeftPane);
                if (ReferenceEquals(LeftPane, s))
                    LeftPane = PaneFallbackFor(s, RightPane);
                // A null side used to stay split; the later active-card sync then
                // assigned the only remaining session to both panes.
                if (LeftPane is null || RightPane is null || ReferenceEquals(LeftPane, RightPane))
                    ExitSplit();
            }
            var vm = SessionCards.FirstOrDefault(c => ReferenceEquals(c.Model, s));
            if (vm is not null) SessionCards.Remove(vm);
            if (!detached)
            {
                // Session names recycle ("Terminal 01" → the next auto-named
                // session). The Logs filter map is keyed by name — drop the dead
                // session's combo so a recycled name doesn't inherit it.
                Logs.ForgetSessionFilter(s.Name);
                _settings.LogsSessionFilters.Remove(s.Name);
                OnBookmarkSessionClosed(s);
            }
            RefreshCounts();
        });
    }

    /// <summary>Pane fallback after a session closes: prefer the removed card's
    /// shelf neighbor; never hand both panes the same session.</summary>
    private TerminalSessionModel? PaneFallbackFor(TerminalSessionModel removed, TerminalSessionModel? otherPane)
    {
        var card = SessionCards.FirstOrDefault(c => ReferenceEquals(c.Model, removed));
        var idx = card is null ? -1 : SessionCards.IndexOf(card);
        for (var i = idx + 1; i < SessionCards.Count; i++)
            if (!ReferenceEquals(SessionCards[i].Model, otherPane))
                return SessionCards[i].Model;
        for (var i = idx - 1; i >= 0; i--)
            if (!ReferenceEquals(SessionCards[i].Model, otherPane))
                return SessionCards[i].Model;
        return _sessions.Sessions.FirstOrDefault(o =>
            !ReferenceEquals(o, removed) && !ReferenceEquals(o, otherPane));
    }

    private void SyncActive()
    {
        ActiveSession = _sessions.Active;
        var target = SessionCards.FirstOrDefault(c => ReferenceEquals(c.Model, ActiveSession));
        var activeIndex = target is null ? 0 : SessionCards.IndexOf(target);
        for (var i = 0; i < SessionCards.Count; i++)
        {
            SessionCards[i].IsActive = ReferenceEquals(SessionCards[i], target);
            SessionCards[i].StageDistance = i - activeIndex;
        }
        if (!ReferenceEquals(ActiveCard, target))
            ActiveCard = target;
        BindCommandSession(ActiveSession);
        UpdateDisplayedCards();
        Dashboard.RefreshSearch();
        UpdateBreadcrumbFrom(ActiveSession?.WorkingDirectory);
        if (InspectorVisible && SelectedRightTab == 1
            && ActiveSession?.Tag != SessionTag.Ssh)
            Files.NavigateTo(ActiveWorkingDirectory);
        UpdateCwdNavFlags();
    }

    private void RefreshCounts()
    {
        UpdateDisplayedCards();
        TerminalCount = SessionCards.Count;
        RunningCount = SessionCards.Count(c => c.Model.IsRunning);
        foreach (var c in SessionCards) c.Refresh();
        UpdateGroupActivity();
        StatusLine = $"工作空间  {WorkspaceName}      {TerminalCount} 个终端      {RunningCount} 个运行中";
    }

    private void UpdateDisplayedCards()
    {
        foreach (var card in SessionCards)
            card.SetDisplayed(IsSplit
                ? ReferenceEquals(card.Model, LeftPane) || ReferenceEquals(card.Model, RightPane)
                : ReferenceEquals(card.Model, ActiveSession));
    }

    private void OnSampled(ISystemMonitor m)
    {
        _statusCpu.Add(m.Current.CpuPercent);
        PollCwdChanges();
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            RefreshCounts();
            CpuText = $"CPU {(int)m.Current.CpuPercent}%";
            MemText = $"内存 {FmtGb(m.Current.MemoryUsedBytes)} / {FmtGb(m.Current.MemoryTotalBytes)}";
            StatusSpark = _statusCpu.ToArray();
        });
    }

    /// <summary>Poll every session's real CWD (Linux /proc/&lt;pid&gt;/cwd) once per
    /// monitor tick — bash doesn't emit OSC 7 by default, so this is how a plain
    /// `cd` keeps the toolbar path + per-session history fresh.</summary>
    private void PollCwdChanges()
    {
        foreach (var s in _sessions.Snapshot)
        {
            // The probe reads the local process's cwd — for ssh that is the ssh
            // client's dir and would stomp the remote path reported via OSC 7.
            if (s.Tag == SessionTag.Ssh) continue;
            var probed = ProcessCwd.TryRead(s.Pty);
            if (string.IsNullOrEmpty(probed)) continue;
            var norm = CwdHistory.Normalize(probed);
            if (norm.Length == 0) continue;
            lock (_cwdLock)
            {
                if (string.Equals(CwdHistory.Normalize(s.WorkingDirectory), norm,
                        StringComparison.Ordinal))
                    continue;
            }
            OnSessionCwdReported(s, norm);
        }
    }

    public static string FmtGb(double bytes) => $"{bytes / (1024.0 * 1024 * 1024):0.0} GB";

    /// <summary>"512 MB"-style byte formatting for file sizes.</summary>
    public static string FmtBytes(long bytes)
    {
        if (bytes >= 1L << 30) return $"{bytes / (double)(1L << 30):0.0} GB";
        if (bytes >= 1L << 20) return $"{bytes / (double)(1L << 20):0.0} MB";
        if (bytes >= 1L << 10) return $"{bytes / 1024.0:0} KB";
        return $"{bytes} B";
    }

    public void PersistSettings() => SaveSettingsInternal();

    public void Dispose()
    {
        _disposed = true;   // popout Closed handlers must not reattach anymore
        StopTemplatePreview();
        BindCommandSession(null);
        StopPublishElapsedTimer();
        // Kill detached PTYs before closing their windows — OnPopoutClosed removes
        // them from DetachedSessions, so disposing after the closes would leak them.
        foreach (var s in DetachedSessions) s.Dispose();
        DetachedSessions.Clear();
        foreach (var w in _popouts.ToArray()) w.Close();
        _popouts.Clear();
        lock (_publishLock)
        {
            _publishIds.Clear();
            _publishRoots.Clear();
            _publishCancelled.Clear();
            _publishStartedAt.Clear();
            _publishOutputArmed.Clear();
            _cancelPendingStart = false;
        }
        PersistSettings();
        Logs.Dispose();
        Files.Dispose();
        _sessionLog.Dispose();
        _monitor.Dispose();
        // SessionCards lags _sessions.Sessions for a session whose posted
        // card-add hasn't run yet — dispose the union so no PTY leaks.
        var live = new HashSet<TerminalSessionModel>(SessionCards.Select(c => c.Model));
        live.UnionWith(_sessions.Sessions);
        SessionCards.Clear();
        foreach (var s in live) s.Dispose();
    }
}
