using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHub.Core.Monitoring;
using TerminalHub.Core.Pty;
using TerminalHub.Core.Sessions;
using TerminalHub.Core.Settings;
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
    [ObservableProperty] private bool _assistantMode;
    [ObservableProperty] private int _terminalCount;
    [ObservableProperty] private int _runningCount;
    [ObservableProperty] private string _cpuText = "";
    [ObservableProperty] private string _memText = "";
    [ObservableProperty] private double[] _statusSpark = [];
    [ObservableProperty] private string _commandInput = "";
    [ObservableProperty] private int _selectedRightTab;
    [ObservableProperty] private bool _settingsOpen;

    /// <summary>AssistantMode mirrors the Codex right-rail tab.</summary>
    partial void OnSelectedRightTabChanged(int value) => AssistantMode = value == 4;

    /// <summary>Selection sync: ListBox.SelectedItem drives activation.</summary>
    partial void OnActiveCardChanged(SessionCardViewModel? value)
    {
        if (value is not null && !ReferenceEquals(value.Model, _sessions.Active))
            _sessions.Activate(value.Model);
    }

    private readonly SparklineBuffer _statusCpu = new(40);

    public DashboardViewModel Dashboard { get; }
    public AiPanelViewModel Assistant { get; }

    public MainWindowViewModel(ISystemMonitor? monitor = null, SettingsStore? settingsStore = null)
    {
        _settingsStore = settingsStore ?? new SettingsStore();
        _settings = _settingsStore.Load();
        _workspaceName = _settings.WorkspaceName;

        _monitor = monitor ?? new SystemMonitor();
        _monitor.Sampled += OnSampled;
        _monitor.Start(TimeSpan.FromSeconds(1));

        Dashboard = new DashboardViewModel(_monitor);
        Assistant = new AiPanelViewModel(new TerminalHub.Core.AI.MockAiAssistant());

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

    [RelayCommand]
    private async Task NewSession()
    {
        var tags = new[] { SessionTag.Dev, SessionTag.Test, SessionTag.Deploy };
        var tag = tags[SessionCards.Count % tags.Length];
        await CreateSessionAsync(null, tag, "");
    }

    [RelayCommand]
    private void ActivateSession(SessionCardViewModel? card)
    {
        if (card is null) return;
        _sessions.Activate(card.Model);
    }

    [RelayCommand]
    private void CloseSession(SessionCardViewModel? card)
    {
        if (card is null) return;
        var vm = SessionCards.FirstOrDefault(c => ReferenceEquals(c.Model, card.Model));
        _sessions.Close(card.Model);
        if (vm is not null) SessionCards.Remove(vm);
    }

    [RelayCommand] private void ToggleAssistant() => AssistantMode = !AssistantMode;

    /// <summary>Bottom dock: 0 New 1 Monitor 2 SSH 3 Logs 4 Deploy 5 Settings</summary>
    [RelayCommand]
    private void DockSelect(int index)
    {
        switch (index)
        {
            case 0: _ = NewSession(); break;
            case 1: SelectedRightTab = 0; break;
            case 2: SelectedRightTab = 3; break;
            case 3: SelectedRightTab = 2; break;
            case 4: Dashboard.AppendOutput("info", "Deploy: 部署功能即将上线 (stub)"); break;
            case 5: SettingsOpen = !SettingsOpen; break;
        }
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

    [RelayCommand]
    private void SubmitCommandInput()
    {
        if (string.IsNullOrEmpty(CommandInput) || ActiveSession is null) return;
        ActiveSession.Emulator.SendText(CommandInput + "\r");
        CommandInput = "";
    }

    private void OnSessionAdded(TerminalSessionModel s)
    {
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
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
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
        var cwd = ActiveSession?.WorkingDirectory ?? "";
        Breadcrumb = string.IsNullOrEmpty(cwd)
            ? ""
            : (OperatingSystem.IsWindows() ? cwd.Replace('\\', '〉').Replace("〉", " > ") : "~/" + System.IO.Path.GetFileName(cwd));
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
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            RefreshCounts();
            CpuText = $"CPU {(int)m.Current.CpuPercent}%";
            MemText = $"内存 {FmtGb(m.Current.MemoryUsedBytes)} / {FmtGb(m.Current.MemoryTotalBytes)}";
            StatusSpark = _statusCpu.ToArray();
        });
    }

    public static string FmtGb(double bytes) => $"{bytes / (1024.0 * 1024 * 1024):0.0} GB";

    public void PersistSettings() => _settingsStore.Save(_settings);

    public void Dispose()
    {
        PersistSettings();
        _monitor.Dispose();
        foreach (var c in SessionCards) c.Model.Dispose();
        SessionCards.Clear();
    }
}
