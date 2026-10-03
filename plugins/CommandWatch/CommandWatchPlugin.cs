using Avalonia.Controls;
using Avalonia.Layout;
using TerminalHub.Core.Terminal;
using TerminalHub.Extensibility;

namespace TerminalHub.Official.CommandWatch;

public sealed record CommandObservation(DateTimeOffset StartedAt, ShellCommandState? Completed = null);
public sealed class CommandWatchPlugin : IWorkbenchPlugin
{
    public sealed record Options(bool CurrentWorkspaceOnly = true);
    private IPluginContext _context = null!;
    private PluginUi _ui = null!;
    private readonly Dictionary<Guid, CommandObservation> _commands = [];
    private Options _options = new();
    private StackPanel? _rows;
    private CheckBox? _filter;
    public void Initialize(IPluginContext context)
    {
        _context = context; _ui = new(context); _options = context.ReadConfiguration<Options>() ?? new();
        _ui.Languages(("watch", "命令看板", "Command watch"),
            ("detail", "查看各终端最近一次命令的耗时和退出码，点击会话回到终端。", "See each terminal's latest command duration and exit code. Click a session to return to it."),
            ("hint", "依赖 Shell 集成标记；未观察到标记时不推断命令状态。启用前的命令不会补录。", "Uses shell integration markers. No status is inferred without a marker; earlier commands are not backfilled."),
            ("filter", "仅当前工作区", "Current workspace only"), ("unobserved", "尚未收到命令标记", "No command marker yet"),
            ("running", "执行中", "Running"), ("success", "完成", "Completed"), ("failed", "失败", "Failed"),
            ("unknown", "完成 · 退出码未知", "Completed · exit code unknown"), ("exit", "退出码", "Exit code"),
            ("closed", "会话已退出", "Session exited"), ("empty", "此工作区暂无会话。", "No sessions in this workspace."));
        context.RegisterView(new("watch", "命令看板", Order: 230,
            IconSvg: "<svg viewBox='0 0 24 24'><path d='M3 3H21V21H3Z M5 5V19H19V5Z M7 13H9V17H7Z M11 9H13V17H11Z M15 7H17V17H15Z'/></svg>"), CreateView);
        context.Subscribe(e =>
        {
            if (e.SessionId is { } id)
            {
                if (e.Kind == WorkbenchEventKind.CommandStarted) _commands[id] = new(DateTimeOffset.Now);
                if (e.Kind == WorkbenchEventKind.CommandCompleted && e.Data is ShellCommandState state)
                    _commands[id] = new(_commands.GetValueOrDefault(id)?.StartedAt ?? DateTimeOffset.Now - state.Duration, state);
                if (e.Kind == WorkbenchEventKind.SessionClosed) _commands.Remove(id);
            }
            if (e.Kind != WorkbenchEventKind.OutputBatch) { _ui.Translate(); Refresh(); }
        });
        context.Schedule(TimeSpan.FromSeconds(1), () => { if (_rows?.IsEffectivelyVisible == true && _commands.Values.Any(c => c.Completed is null)) Refresh(); });
    }
    private Control CreateView()
    {
        _filter = new CheckBox { Name = "WorkspaceFilter", IsChecked = _options.CurrentWorkspaceOnly };
        _filter.IsCheckedChanged += (_, _) => { _options = new(_filter.IsChecked == true); _context.SaveConfiguration(_options); Refresh(); };
        _rows = new StackPanel { Name = "CommandRows", Spacing = 8 };
        var body = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = 12 };
        body.Children.Add(_ui.LocalLabel("hint", "", "UiMuted", 11));
        var scroll = new ScrollViewer { Content = _rows, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); body.Children.Add(scroll); Refresh();
        return _ui.Page("watch", "命令看板", "detail", "", body, _filter);
    }
    private void Refresh()
    {
        if (_filter is not null) _filter.Content = _ui.T("filter", "仅当前工作区");
        if (_rows is null) return;
        _rows.Children.Clear();
        var workspaces = _context.Host.Workspaces.ToDictionary(w => w.Id, w => w.Name);
        foreach (var session in _context.Host.Sessions.Where(s => !_options.CurrentWorkspaceOnly || s.WorkspaceId == _context.Host.ActiveWorkspaceId))
        {
            var labels = new StackPanel { Spacing = 6 };
            labels.Children.Add(_ui.Label(session.Name, size: 15));
            labels.Children.Add(_ui.Label(workspaces.GetValueOrDefault(session.WorkspaceId, ""), "UiMuted", 11));
            var observation = _commands.GetValueOrDefault(session.Id);
            string text; var brush = "UiMuted";
            if (observation is null) text = _ui.T("unobserved", "No command marker yet");
            else if (observation.Completed is not { } completed)
            { text = $"{_ui.T("running", "Running")} · {(DateTimeOffset.Now - observation.StartedAt).TotalSeconds:0.0}s"; brush = "UiAccent"; }
            else
            {
                var code = completed.ExitCode;
                text = $"{_ui.T(code is null ? "unknown" : code == 0 ? "success" : "failed", "Completed")} · {completed.Duration.TotalSeconds:0.0}s";
                if (code is not null) text += $" · {_ui.T("exit", "Exit code")} {code}";
                brush = code is null ? "UiMuted" : code == 0 ? "UiGood" : "UiBad";
            }
            if (!session.Running) { text += " · " + _ui.T("closed", "Session exited"); brush = "UiMuted"; }
            labels.Children.Add(_ui.Label(text, brush, 12));
            var button = new Button { Content = labels, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new(0), Background = Avalonia.Media.Brushes.Transparent, BorderThickness = new(0) };
            button.Click += (_, _) => { try { _context.Host.ActivateSession(session.Id); } catch (Exception ex) { _context.ReportError(ex); } };
            _rows.Children.Add(_ui.Card(button));
        }
        if (_rows.Children.Count == 0) _rows.Children.Add(_ui.Label(_ui.T("empty", "No sessions"), "UiMuted"));
    }
    public void Deactivate() => _commands.Clear();
}
