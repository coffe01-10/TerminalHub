using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHub.Core.Deploy;
using TerminalHub.Core.Logging;
using TerminalHub.Core.Monitoring;
using TerminalHub.Core.Pty;
using TerminalHub.Core.Sessions;
using TerminalHub.Core.Settings;
using TerminalHub.App.Views;
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

    [ObservableProperty] private TerminalSessionModel? _activeSession;
    [ObservableProperty] private SessionCardViewModel? _activeCard;
    [ObservableProperty] private string _workspaceName;
    [ObservableProperty] private string _statusLine = "";
    [ObservableProperty] private string _breadcrumb = "";
    [ObservableProperty] private bool _canCwdBack;
    [ObservableProperty] private bool _canCwdForward;
    [ObservableProperty] private bool _assistantMode;
    [ObservableProperty] private int _terminalCount;
    [ObservableProperty] private int _runningCount;
    [ObservableProperty] private string _cpuText = "";
    [ObservableProperty] private string _memText = "";
    [ObservableProperty] private double[] _statusSpark = [];
    [ObservableProperty] private string _commandInput = "";
    [ObservableProperty] private int _selectedRightTab;
    [ObservableProperty] private bool _settingsOpen;
    /// <summary>Dock index of the active surface (-1 when a tab has no dock item).</summary>
    [ObservableProperty] private int _dockHighlight = -1;

    /// <summary>True while a dock publish is starting or its session is still running.</summary>
    [ObservableProperty] private bool _isPublishRunning;

    /// <summary>Deploy dock tooltip. Idle text, or a cancel hint while a publish is running.</summary>
    [ObservableProperty] private string _deployDockTip = DeployDockTipIdle;

    private const string DeployDockTipIdle =
        "点击：有产物则打开目录，无产物则按当前配置档开始打包。Ctrl+点击或右键「重新打包」强制重新发布。右键可切换配置档、查看最近产物、打开上次成功产物。打包进行中可右键「取消打包」。";

    private const string DeployDockTipRunning =
        "打包进行中。右键「取消打包 Cancel」终止发布进程组。普通点击不会再次启动。";

    /// <summary>Dock caption: idle reads Deploy; a running publish reads 打包中.</summary>
    public string DeployDockCaption => IsPublishRunning ? "打包中" : "Deploy";

    /// <summary>
    /// Short last-outcome text from the real exit record. Empty until a publish has finished.
    /// Stays visible in the property while a later publish is running; the dock binds
    /// <see cref="LastPublishBadge"/>, which hides during that run.
    /// </summary>
    public string LastPublishSummary => LastPublishResults.FormatBadge(_settings.LastPublishResult);

    /// <summary>Status line under the Deploy caption. Empty while a publish is running.</summary>
    public string LastPublishBadge => IsPublishRunning ? "" : LastPublishSummary;

    public bool HasLastPublishBadge => LastPublishBadge.Length > 0;

    /// <summary>True when the stored last-success folder still exists on disk.</summary>
    public bool CanOpenLastSuccessfulArtifact => LastPublishResults.CanOpen(_settings.LastPublishResult);

    partial void OnIsPublishRunningChanged(bool value)
    {
        DeployDockTip = ComposeDeployDockTip(value);
        OnPropertyChanged(nameof(DeployDockCaption));
        OnPropertyChanged(nameof(LastPublishBadge));
        OnPropertyChanged(nameof(HasLastPublishBadge));
    }

    private string ComposeDeployDockTip(bool? running = null)
    {
        var isRunning = running ?? IsPublishRunning;
        var head = isRunning ? DeployDockTipRunning : DeployDockTipIdle;
        var detail = LastPublishResults.FormatTooltip(_settings.LastPublishResult);
        if (isRunning && detail == "尚未打包") return head;
        return head + "\n" + detail;
    }

    /// <summary>Title-bar OS label ("Windows System" in the mockup — follows the real OS).</summary>
    public string OsLabel => OperatingSystem.IsWindows() ? "Windows System"
        : OperatingSystem.IsLinux() ? "Linux System"
        : OperatingSystem.IsMacOS() ? "macOS System"
        : "本机";

    /// <summary>AssistantMode mirrors the Codex right-rail tab; Files lazy-inits on first visit.</summary>
    partial void OnSelectedRightTabChanged(int value)
    {
        AssistantMode = value == 4;
        if (value == 1) Files.EnsureSessionDir();
        if (value == 2) Logs.RefreshSessions();
        // Dock: 1 Monitor→tab0, 2 SSH→tab3, 3 Logs→tab2; Files/Codex have no dock item.
        DockHighlight = value switch { 0 => 1, 2 => 3, 3 => 2, _ => SettingsOpen ? 5 : -1 };
    }

    partial void OnSettingsOpenChanged(bool value)
        => DockHighlight = value ? 5 : (SelectedRightTab switch { 0 => 1, 2 => 3, 3 => 2, _ => -1 });

    /// <summary>Selection sync: ListBox.SelectedItem drives activation. In split
    /// mode the picked card is assigned to the focused pane.</summary>
    partial void OnActiveCardChanged(SessionCardViewModel? value)
    {
        if (value is null) return;
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

    /// <summary>Assign a session to a pane, keeping the two panes distinct.</summary>
    private void AssignToPane(int pane, TerminalSessionModel s)
    {
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
                LeftPane = _sessions.Sessions.FirstOrDefault(o => !ReferenceEquals(o, s))
                           ?? s; // only one session exists — left keeps it
        }
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
    public AiPanelViewModel Assistant { get; }
    public FilesViewModel Files { get; }
    public LogsViewModel Logs { get; }
    public SshViewModel Ssh { get; }
    private readonly SessionLogFile _sessionLog = new();

    public MainWindowViewModel(ISystemMonitor? monitor = null, SettingsStore? settingsStore = null)
    {
        _settingsStore = settingsStore ?? new SettingsStore();
        _settings = _settingsStore.Load();
        DeployDockTip = ComposeDeployDockTip();
        _workspaceName = _settings.WorkspaceName;

        _monitor = monitor ?? new SystemMonitor();
        _monitor.Sampled += OnSampled;
        _monitor.Start(TimeSpan.FromSeconds(1));

        Dashboard = new DashboardViewModel(_monitor);
        Dashboard.BufferSource = () => ActiveSession?.Emulator.Buffer;
        Assistant = new AiPanelViewModel(new TerminalHub.Core.AI.LocalAiAssistant(),
            msg => Dashboard.AppendOutput("info", msg, "codex"));
        Files = new FilesViewModel(() => ActiveSession?.WorkingDirectory,
            openTerminalAt: CdActiveSessionTo,
            copyTextAsync: CopyTextToClipboardAsync,
            hasActiveSession: () => ActiveSession is not null);
        Logs = new LogsViewModel(Dashboard, _sessionLog,
            () => SessionCards.Select(c => c.Name).ToList(),
            _settings.SessionLogToFile,
            v => _settings.SessionLogToFile = v,
            copyToClipboard: CopyTextToClipboardAsync,
            promptExportPath: PromptExportPathAsync,
            persistFilters: PersistLogsFilters,
            activateSession: TryActivateSessionByName);
        // Replay the filters saved from the previous run (never writes back):
        // the global combo for「全部会话」, plus each named session's own memory.
        Logs.ApplyPersistedFilters(_settings.LogsFilterText, _settings.LogsUseRegex,
            _settings.LogsLevelFilterIndex, _settings.LogsRetainHistoryOnClear,
            _settings.LogsUseRelativeTimestamps, _settings.LogsWrapLines);
        Logs.ApplySessionFilterMap(_settings.LogsSessionFilters);
        Ssh = new SshViewModel(_settings.SshHosts, ConnectSsh, SaveSettingsInternal);

        // Logs' session filter follows card adds/removes live, not just on tab open.
        SessionCards.CollectionChanged += OnSessionCardsChanged;

        _sessions.SessionAdded += OnSessionAdded;
        _sessions.SessionRemoved += OnSessionRemoved;
        _sessions.ActiveChanged += s => Avalonia.Threading.Dispatcher.UIThread.Post(() => SyncActive());
        _sessions.SessionStateChanged += _ => Avalonia.Threading.Dispatcher.UIThread.Post(RefreshCounts);
    }

    public AppSettings Settings => _settings;
    public ISystemMonitor Monitor => _monitor;

    /// <summary>Called once at startup to spawn configured sessions.</summary>
    public async Task SpawnStartupSessionsAsync()
    {
        if (_settings.StartupSessions.Count == 0)
        {
            await NewSession();
            return;
        }
        foreach (var s in _settings.StartupSessions)
            await CreateSessionAsync(s.Name, ParseTag(s.Tag), s.WorkingDirectory, s.Shell);
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

    private Task<TerminalSessionModel> CreateSessionAsync(string? name, SessionTag tag, string cwd, ShellKind? shell = null)
    {
        var shellCmd = (shell ?? _settings.Shell) switch
        {
            ShellKind.Cmd => OperatingSystem.IsWindows() ? "cmd.exe" : "bash",
            ShellKind.Wsl => OperatingSystem.IsWindows() ? "wsl.exe" : "bash",
            ShellKind.Bash => "bash",
            ShellKind.Custom when !string.IsNullOrWhiteSpace(_settings.CustomShellPath) => _settings.CustomShellPath,
            ShellKind.PowerShell when OperatingSystem.IsWindows() => "pwsh",
            _ => OperatingSystem.IsWindows() ? "pwsh" : "bash",
        };
        if (string.IsNullOrEmpty(cwd))
            cwd = OperatingSystem.IsWindows() ? "C:\\" : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return _sessions.CreateAsync(
            PtySessionFactory.Create,
            new PtyOptions { Shell = shellCmd, WorkingDirectory = cwd },
            name, tag);
    }

    /// <summary>SSH tab → spawn a session running the local ssh binary.</summary>
    private void ConnectSsh(TerminalHub.Core.Ssh.SshHost host)
    {
        Dashboard.AppendOutput("info", $"SSH 连接: {host.CommandLine}", "ssh");
        _ = _sessions.CreateAsync(
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
        if (left is null && other is null)
        {
            await NewSession();
            left = _sessions.Active;
        }
        if (other is null)
        {
            Dashboard.AppendOutput("info", "分屏: 只有一个会话,为右栏新建一个…", "split");
            var prev = left;
            await NewSession();                       // activates the new session
            other = _sessions.Active;
            if (prev is not null) _sessions.Activate(prev); // keep focus on the left pane
        }
        if (other is null)
        {
            Dashboard.AppendOutput("warn", "分屏: 无法创建第二个会话", "split");
            return;
        }
        LeftPane = left;
        RightPane = other;
        FocusedPane = 0;
        IsSplit = true;
        Dashboard.AppendOutput("info",
            $"分屏: 左 {left?.Name ?? "—"} ｜ 右 {other.Name} · 点击窗格聚焦,点会话卡分配到该窗格", "split");
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
        var session = _sessions.Detach(model);
        if (session is null) return;

        DetachedSessions.Add(session);
        var win = new SessionWindow(session, FontSize);
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
        _sessions.Rename(card.Model, name);
        _sessionNames[card.Model.Id] = name; // future output lines carry the new name
        card.Refresh();
        Logs.RefreshSessions();
    }

    private void OnSessionCardsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => Logs.RefreshSessions();

    /// <summary>Best-effort clipboard copy (no-op when headless / clipboard locked).</summary>
    private static async Task CopyTextToClipboardAsync(string text)
    {
        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime
                    is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow is { Clipboard: { } clipboard })
                await clipboard.SetTextAsync(text);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            // Clipboard unavailable (headless / locked desktop) — copy stays best-effort.
        }
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

    [RelayCommand] private void ToggleAssistant() => AssistantMode = !AssistantMode;

    /// <summary>Bottom dock: 0 New 1 Monitor 2 SSH 3 Logs 4 Deploy 5 Settings.
    /// Parameter arrives as a string from XAML — parse it (int also accepted).</summary>
    [RelayCommand]
    private void DockSelect(object? parameter)
    {
        if (!int.TryParse(parameter?.ToString(), out var index)) return;
        switch (index)
        {
            case 0: _ = NewSession(); break;
            case 1: DockHighlight = 1; SelectedRightTab = 0; break;
            case 2: DockHighlight = 2; SelectedRightTab = 3; break;
            case 3: DockHighlight = 3; SelectedRightTab = 2; break;
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
            return;
        }
        OpenRecentArtifact(path);
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
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(found[0].Path)
                { UseShellExecute = true });
            Dashboard.AppendOutput("info", "Deploy: 已在文件管理器中打开产物目录", "deploy");
        }
        catch
        {
            Dashboard.AppendOutput("warn", "Deploy: 无法打开文件管理器 — 请手动访问上面目录", "deploy");
        }
        Dashboard.AppendOutput("info",
            "Deploy: 重新发布请按住 Ctrl 再点 Deploy，或右键菜单「重新打包」。", "deploy");
    }

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
            _ => 0,
        };
        set
        {
            _settings.Shell = value switch
            {
                1 => ShellKind.Cmd,
                2 => ShellKind.Wsl,
                3 => ShellKind.Bash,
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

    private void SaveSettingsInternal() => _settingsStore.Save(_settings);

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

    private void OnSessionCwdReported(TerminalSessionModel s, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        path = CwdHistory.Normalize(path);
        if (path.Length == 0) return;
        lock (_cwdLock)
        {
            s.WorkingDirectory = path;
            HistoryFor(s).Push(path);
        }
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(ActiveSession, s)) return;
            UpdateBreadcrumbFrom(path);
            UpdateCwdNavFlags();
        });
    }

    /// <summary>Re-read the active session CWD (Linux /proc if possible) and sync toolbar + Files.</summary>
    [RelayCommand]
    private void RefreshCwd()
    {
        if (ActiveSession is null) return;
        var probed = ProcessCwd.TryRead(ActiveSession.Pty);
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
        path = CwdHistory.Normalize(path);
        if (path.Length == 0) return;
        lock (_cwdLock)
        {
            ActiveSession.WorkingDirectory = path;
            if (recordHistory)
                HistoryFor(ActiveSession).Push(path);
        }
        UpdateBreadcrumbFrom(path);
        Files.NavigateTo(path);
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

    private static string QuoteForShell(string path)
    {
        if (OperatingSystem.IsWindows())
            return "\"" + path.Replace("\"", "\"\"") + "\"";
        return "'" + path.Replace("'", "'\\''") + "'";
    }

    [RelayCommand]
    private void SubmitCommandInput()
    {
        if (string.IsNullOrEmpty(CommandInput)) return;
        var text = CommandInput;
        CommandInput = "";
        if (text.StartsWith('?') || text.StartsWith("ai:", StringComparison.OrdinalIgnoreCase))
        {
            var query = text.TrimStart('?').Trim();
            if (query.StartsWith("ai:", StringComparison.OrdinalIgnoreCase))
                query = query[3..].TrimStart();
            if (query.Length == 0) return;
            _ = Assistant.SubmitQueryAsync(query);
            return;
        }
        if (ActiveSession is null) return;
        ActiveSession.Emulator.SendText(text + "\r");
    }

    /// <summary>Per-session UTF-8 line decoders feeding the real Output log.</summary>
    private readonly Dictionary<Guid, Utf8LineDecoder> _lineDecoders = new();
    private readonly Dictionary<Guid, string> _sessionNames = new();

    private void OnPtyOutput(IPtySession pty, ReadOnlyMemory<byte> data)
    {
        if (!_lineDecoders.TryGetValue(pty.Id, out var dec))
        {
            dec = new Utf8LineDecoder();
            dec.LineReceived += line =>
            {
                var level = ClassifyLine(line);
                var name = _sessionNames.GetValueOrDefault(pty.Id, "session");
                bool publish;
                bool focusOutput;
                lock (_publishLock)
                {
                    publish = _publishIds.Contains(pty.Id);
                    focusOutput = publish && _publishOutputArmed.Add(pty.Id);
                }
                // Publish script bytes are real PTY output. Show them as deploy
                // without inventing progress text. Other sessions keep their name.
                if (focusOutput)
                    RunOnUi(() => Dashboard.SelectedBottomTab = 0);
                Dashboard.AppendOutput(level, line, publish ? "deploy" : name);
                _sessionLog.Write(name, level, line);
            };
            dec.RawLineReceived += raw =>
                Dashboard.AppendDebug(
                    TerminalHub.Core.Logging.AnsiText.DebugEscape(raw),
                    _sessionNames.GetValueOrDefault(pty.Id, "session"));
            _lineDecoders[pty.Id] = dec;
        }
        dec.Feed(data.Span);
    }

    private static string ClassifyLine(string line) => LineClassifier.Classify(line);

    private void OnSessionAdded(TerminalSessionModel s)
    {
        _sessionNames[s.Id] = s.Name;
        s.Pty.OutputReceived += OnPtyOutput;
        if (!string.IsNullOrEmpty(s.WorkingDirectory))
            HistoryFor(s).Push(s.WorkingDirectory);
        s.Emulator.CwdChanged += path => OnSessionCwdReported(s, path);
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var card = new SessionCardViewModel(s);
            card.Refresh();
            SessionCards.Add(card);
            RefreshCounts();
            SyncActive();
        });
    }

    private void OnSessionRemoved(TerminalSessionModel s)
    {
        s.Pty.OutputReceived -= OnPtyOutput;
        _lineDecoders.Remove(s.Id);
        _sessionNames.Remove(s.Id);
        _cwdHistories.Remove(s.Id);
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (IsSplit)
            {
                if (ReferenceEquals(RightPane, s))
                    RightPane = _sessions.Sessions.FirstOrDefault(o => !ReferenceEquals(o, LeftPane));
                if (ReferenceEquals(LeftPane, s))
                    LeftPane = _sessions.Sessions.FirstOrDefault();
                if (LeftPane is null && RightPane is null)
                {
                    IsSplit = false;
                    FocusedPane = 0;
                }
            }
            var vm = SessionCards.FirstOrDefault(c => ReferenceEquals(c.Model, s));
            if (vm is not null) SessionCards.Remove(vm);
            RefreshCounts();
        });
    }

    private void SyncActive()
    {
        ActiveSession = _sessions.Active;
        foreach (var c in SessionCards)
            c.IsActive = ReferenceEquals(c.Model, ActiveSession);
        var target = SessionCards.FirstOrDefault(c => ReferenceEquals(c.Model, ActiveSession));
        if (!ReferenceEquals(ActiveCard, target))
            ActiveCard = target;
        Dashboard.RefreshSearch();
        UpdateBreadcrumbFrom(ActiveSession?.WorkingDirectory);
        UpdateCwdNavFlags();
    }

    private void RefreshCounts()
    {
        TerminalCount = SessionCards.Count;
        RunningCount = SessionCards.Count(c => c.Model.IsRunning);
        foreach (var c in SessionCards) c.Refresh();
        StatusLine = $"工作空间  {WorkspaceName}      {TerminalCount} 个终端      {RunningCount} 个运行中";
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
        foreach (var s in _sessions.Sessions)
        {
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

    public void PersistSettings() => _settingsStore.Save(_settings);

    public void Dispose()
    {
        _disposed = true;   // popout Closed handlers must not reattach anymore
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
        _sessionLog.Dispose();
        _monitor.Dispose();
        foreach (var c in SessionCards) c.Model.Dispose();
        SessionCards.Clear();
    }
}
