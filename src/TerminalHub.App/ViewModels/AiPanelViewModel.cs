using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHub.Core.Ai;
using TerminalHub.Core.Sessions;
using TerminalHub.Core.Terminal;

namespace TerminalHub.App.ViewModels;

/// <summary>One task the panel drove into an AI CLI session.</summary>
public sealed partial class AiTaskViewModel : ObservableObject
{
    public required string Title { get; init; }
    public required AiCli Cli { get; init; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Active))]
    private TerminalSessionModel? _session;
    public DateTimeOffset Started { get; init; } = DateTimeOffset.Now;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Active))] private string _status = "启动中";
    [ObservableProperty] private bool _awaitingInput;
    public bool Active => Session is { IsRunning: true };
}

/// <summary>AI 任务面板：检测本机 AI CLI、用任务描述驱动一个会话（任务作为
/// CLI 的初始提示词参数），并按帧识别聚合每个会话的状态（运行中/等待输入/已
/// 退出）。状态切换走已有的横幅通知。"等待输入"目前只有 Codex 有验证过的帧
/// 识别器，其余 CLI 只报运行/退出。</summary>
public sealed partial class AiPanelViewModel : ObservableObject, IDisposable
{
    private readonly Func<AiCli, string, Task<TerminalSessionModel?>> _spawn;
    private readonly Func<IReadOnlyList<TerminalSessionModel>>? _sessions;
    private readonly Action? _sessionsChanged;
    private readonly Action<TerminalSessionModel>? _activate;
    private readonly Action<TerminalSessionModel, string>? _notify;
    private readonly Func<TerminalSessionModel, bool>? _isVisible;
    private readonly DispatcherTimer? _timer;
    private bool _disposed;

    public ObservableCollection<AiCli> Clis { get; } = [];
    public ObservableCollection<AiTaskViewModel> Tasks { get; } = [];
    [ObservableProperty] private string _taskDraft = "";
    [ObservableProperty] private AiCli? _selectedCli;
    [ObservableProperty] private string _notice = "";

    /// <summary>All collaborators are injectable so tests can drive the panel
    /// with a mock PTY and captured callbacks. <paramref name="autoScan"/> off
    /// leaves <see cref="Scan"/> for the test to call by hand.</summary>
    public AiPanelViewModel(
        IEnumerable<AiCli>? clis = null,
        Func<AiCli, string, Task<TerminalSessionModel?>>? spawn = null,
        Func<IReadOnlyList<TerminalSessionModel>>? sessions = null,
        Action? sessionsChanged = null,
        Action<TerminalSessionModel>? activate = null,
        Action<TerminalSessionModel, string>? notify = null,
        Func<TerminalSessionModel, bool>? isVisible = null,
        bool autoScan = true)
    {
        _spawn = spawn ?? ((_, _) => Task.FromResult<TerminalSessionModel?>(null));
        _sessions = sessions;
        _sessionsChanged = sessionsChanged;
        _activate = activate;
        _notify = notify;
        _isVisible = isVisible;
        foreach (var cli in clis ?? AiCliCatalog.Detect()) Clis.Add(cli);
        SelectedCli = Clis.FirstOrDefault();
        if (autoScan)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            _timer.Tick += (_, _) => Scan();
            _timer.Start();
        }
    }

    public bool HasClis => Clis.Count > 0;

    [RelayCommand]
    private async Task StartTask()
    {
        var title = TaskDraft.Trim();
        if (title.Length == 0) { Notice = "请先描述任务。"; return; }
        if (SelectedCli is not { } cli) { Notice = "未检测到 AI CLI。"; return; }
        var task = new AiTaskViewModel { Title = title, Cli = cli };
        Tasks.Add(task);
        TaskDraft = "";
        Notice = "";
        try { task.Session = await _spawn(cli, title); }
        catch (Exception ex) { task.Status = "启动失败"; Notice = ex.Message; return; }
        task.Status = task.Session is { IsRunning: true } ? "运行中" : "启动失败";
    }

    [RelayCommand]
    private void ShowTask(AiTaskViewModel? task)
    {
        if (task?.Session is { } session) _activate?.Invoke(session);
    }

    [RelayCommand]
    private void RemoveTask(AiTaskViewModel? task)
    {
        if (task is { Active: false }) Tasks.Remove(task);
    }

    /// <summary>One pass over live tasks: exited sessions get their code, and
    /// CLIs with a frame detector flip between 运行中 and 等待输入. State
    /// transitions notify only when the session isn't on screen — a banner for
    /// the pane the user is already watching is noise. The same pass updates
    /// every session's "needs you" attention, panel-spawned or not.</summary>
    public void Scan()
    {
        if (_disposed) return;
        if (_sessions is not null && ScanAttention(_sessions())) _sessionsChanged?.Invoke();
        foreach (var task in Tasks.ToArray())
        {
            var session = task.Session;
            if (session is null) continue;
            if (!session.IsRunning)
            {
                var exited = $"已退出（{session.Pty.ExitCode?.ToString() ?? "?"}）";
                if (task.Status != exited)
                {
                    task.Status = exited;
                    task.AwaitingInput = false;
                    if (_isVisible?.Invoke(session) != true)
                        _notify?.Invoke(session, $"{session.Name} · AI 任务{exited}");
                }
                continue;
            }
            if (!task.Cli.DetectsAwaitingInput)
            {
                if (task.Status.Length == 0) task.Status = "运行中";
                continue;
            }
            TerminalFrame frame;
            lock (session.Emulator.Buffer.SyncRoot) frame = session.Emulator.Buffer.CaptureFrame();
            var awaiting = AiCliCatalog.IsAwaitingInput(task.Cli, frame);
            if (awaiting != task.AwaitingInput)
            {
                task.AwaitingInput = awaiting;
                task.Status = awaiting ? "等待输入" : "运行中";
                if (awaiting && _isVisible?.Invoke(session) != true)
                    _notify?.Invoke(session, $"{session.Name} · 等待输入");
            }
        }
    }

    /// <summary>Recompute every AI session's attention. Returns whether any
    /// session changed, so the caller can repaint cards that show the badge.</summary>
    private bool ScanAttention(IReadOnlyList<TerminalSessionModel> sessions)
    {
        var changed = false;
        foreach (var session in sessions)
            if (RefreshAttention(session)) changed = true;
        return changed;
    }

    /// <summary>Recompute whether an AI CLI in this session is blocked on the
    /// user. Only AI-tagged sessions are scanned, and only a transition into
    /// "needs you" notifies — a session the user is already looking at stays quiet.</summary>
    private bool RefreshAttention(TerminalSessionModel session)
    {
        if (session.Tag != SessionTag.Ai || !session.IsRunning) return false;
        TerminalFrame frame;
        int progress;
        lock (session.Emulator.Buffer.SyncRoot)
        {
            frame = session.Emulator.Buffer.CaptureFrame();
            progress = session.Emulator.Buffer.ProgressState;
        }
        var attention = AiAttentionDetector.Detect(frame, progress);
        if (attention == session.Attention) return false;
        session.Attention = attention;
        if (attention == AiAttention.NeedsYou && _isVisible?.Invoke(session) != true)
            _notify?.Invoke(session, $"{session.Name} · 需要你确认");
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer?.Stop();
    }
}
