using Avalonia.Controls;
using Avalonia.Layout;
using TerminalHub.Extensibility;

namespace TerminalHub.Official.TerminalBroadcast;

/// <summary>Send one line of input to every running session in the active
/// workspace — the cluster-ssh workflow for "restart this on all nodes".
/// Targets are listed before sending so the user sees the blast radius.</summary>
public sealed class TerminalBroadcastPlugin : IWorkbenchPlugin
{
    private IPluginContext? _context;
    private PluginUi _ui = null!;
    private TextBox? _input;
    private CheckBox? _submit;
    private StackPanel? _targets;
    private TextBlock? _status;

    public void Initialize(IPluginContext context)
    {
        _context = context; _ui = new(context);
        _ui.Languages(("broadcast", "输入广播", "Terminal broadcast"),
            ("detail", "把一行输入发到当前工作区的所有运行中会话（含弹出窗口）。", "Send one input line to every running session in this workspace (detached windows included)."),
            ("send", "发送到全部会话", "Send to all sessions"),
            ("submit", "带回车", "With Enter"),
            ("sent", "已发送到 {0} 个会话", "Sent to {0} sessions"),
            ("empty", "输入为空", "Nothing to send"),
            ("none", "当前工作区没有运行中的会话", "No running sessions in this workspace"),
            ("targets", "将送达:", "Targets:"));
        context.RegisterView(new("broadcast", "输入广播", Order: 220,
            IconSvg: "<svg viewBox='0 0 24 24'><path d='M2 11L22 2L13 22L11 13Z M11 13L22 2'/></svg>"), CreateView);
        context.RegisterCommand(new("broadcast", "输入广播", () => { Broadcast(); return Task.CompletedTask; }));
        context.Subscribe(e =>
        {
            if (e.Kind is WorkbenchEventKind.SessionCreated or WorkbenchEventKind.SessionClosed
                or WorkbenchEventKind.WorkspaceChanged or WorkbenchEventKind.LanguageChanged)
            { _ui.Translate(); RefreshTargets(); }
        });
    }

    private Control CreateView()
    {
        _input = new TextBox { Name = "BroadcastInput", Padding = new(10, 8), CornerRadius = new(7), Watermark = "…" };
        _submit = new CheckBox { Name = "BroadcastSubmit", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
        var submit = _submit;
        void UpdateSubmit() => submit.Content = _ui.T("submit", "带回车");
        UpdateSubmit();
        _status = _ui.Label(brush: "UiMuted", size: 11);
        _targets = new StackPanel { Spacing = 4 };
        var row = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 8 };
        row.Children.Add(_input); Grid.SetColumn(submit, 1); row.Children.Add(submit);
        var send = _ui.Button("BroadcastSend", "send", "发送到全部会话", Broadcast, primary: true);
        Grid.SetColumn(send, 2); row.Children.Add(send);
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(row); body.Children.Add(_ui.LocalLabel("targets", "将送达:", "UiMuted", 11));
        body.Children.Add(_targets); body.Children.Add(_status);
        _input.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Broadcast(); };
        RefreshTargets();
        return _ui.Page("broadcast", "输入广播", "detail", "把一行输入发到当前工作区的所有运行中会话（含弹出窗口）。", body);
    }

    private SessionInfo[] RunningTargets()
        => _context!.Host.Sessions.Where(s => s.WorkspaceId == _context.Host.ActiveWorkspaceId && s.Running).ToArray();

    private void RefreshTargets()
    {
        if (_targets is null || _context is null) return;
        _targets.Children.Clear();
        var targets = RunningTargets();
        if (targets.Length == 0)
        {
            _targets.Children.Add(_ui.LocalLabel("none", "当前工作区没有运行中的会话", "UiMuted", 12));
            return;
        }
        foreach (var session in targets) _targets.Children.Add(_ui.Label("· " + session.Name, "UiInk", 12));
    }

    private void Broadcast()
    {
        if (_context is null || _input is null) return;
        var text = _input.Text ?? "";
        if (text.Length == 0) { _status!.Text = _ui.T("empty", "输入为空"); return; }
        var targets = RunningTargets();
        var submit = _submit?.IsChecked != false;
        foreach (var session in targets) _context.Host.SendInput(session.Id, text, submit);
        _status!.Text = string.Format(_ui.T("sent", "已发送到 {0} 个会话"), targets.Length);
        _input.Text = "";
    }

    public void Deactivate() { _context = null; }
}
