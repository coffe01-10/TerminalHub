using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TerminalHub.Extensibility;

namespace TerminalHub.Official.PortGuard;

public sealed class PortGuardPlugin : IWorkbenchPlugin
{
    private IPluginContext? _context;
    private PluginUi _ui = null!;
    private Control? _page;
        private ListBox _rows = null!;
        private TextBlock _header = null!;
    private TextBox _filter = null!;
    private TextBox _port = null!;
    private TextBlock _status = null!;
    private TextBlock _matches = null!;
    private IReadOnlyList<PortRow> _snapshot = [];
    private string _source = "";
    private int _exitCode;
    private string _error = "";
    private int _refresh;
    private bool _busy;

    public void Initialize(IPluginContext context)
    {
        _context = context;
        _ui = new(context);
        _ui.Languages(
            ("title", "端口看板", "Port board"),
            ("detail", "查看本机正在监听的 TCP 端口和 UDP 绑定。结束进程会关掉整个进程，而不只是这一个端口。", "See local TCP listeners and UDP binds. Ending a process stops the whole process, not just one port."),
            ("refresh", "刷新", "Refresh"),
            ("filter", "按端口、进程名或 PID 过滤", "Filter by port, process, or PID"),
            ("lookup", "查看占用", "Who uses it"),
            ("port", "端口号", "Port"),
            ("end", "结束进程", "End process"),
            ("diagnose", "诊断", "Diagnose"),
            ("empty", "没有匹配的端口。", "No matching ports."),
            ("matches", "占用该端口的记录", "Rows on this port"),
            ("none", "快照里没有这个端口。", "No row in the snapshot uses this port."),
            ("busy", "正在读取端口…", "Reading ports…"),
            ("noconfirm", "没有父窗口，已取消。", "No parent window, cancelled."),
            ("cancel", "已取消。", "Cancelled."),
            ("refused", "这个 PID 不在快照里，或是 0、4、当前进程。", "That PID is not in the snapshot, or it is 0, 4, or this process."),
            ("ended", "已请求结束进程。", "End process requested."),
            ("protocol", "协议", "Protocol"),
            ("local", "本地地址", "Local address"),
            ("state", "状态", "State"),
            ("remote", "对端", "Remote"),
            ("pid", "PID", "PID"),
            ("process", "进程名", "Process"),
            ("udp", "UDP 绑定", "UDP bind"),
            ("listening", "LISTENING", "LISTENING"),
            ("confirm-title", "结束进程", "End process"),
            ("confirm-yes", "结束", "End"),
            ("confirm-no", "取消", "Cancel"));
        context.RegisterView(new("ports", "端口看板", Order: 260,
            IconSvg: "<svg viewBox='0 0 24 24'><path d='M3 4H21V8H3Z M3 10H21V14H3Z M3 16H21V20H3Z'/></svg>"), CreateView);
    }

    private Control CreateView()
    {
        _filter = _ui.Editor("PortFilter");
        _filter.AcceptsReturn = false;
        _filter.TextWrapping = TextWrapping.NoWrap;
        _filter.Watermark = _ui.T("filter", "按端口、进程名或 PID 过滤");
        _filter.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) Show(); };
        _port = _ui.Editor("PortQuery");
        _port.AcceptsReturn = false;
        _port.TextWrapping = TextWrapping.NoWrap;
        _port.Width = 120;
        _port.Watermark = _ui.T("port", "端口号");
        _rows = new ListBox { Name = "PortRows", MinHeight = 160 };
        _ui.StyleList(_rows);
        _header = _ui.Label(brush: "UiMuted", size: 11);
        _header.Name = "PortHeader";
        _status = _ui.Label(brush: "UiMuted", size: 12);
        _status.Name = "PortStatus";
        _matches = _ui.Label(brush: "UiMuted", size: 12);
        _matches.Name = "PortMatches";
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var control in new Control[]
        {
            _ui.Button("PortRefresh", "refresh", "刷新", () => _ = RefreshAsync(), primary: true),
            _filter, _port,
            _ui.Button("PortLookup", "lookup", "查看占用", Lookup),
            _ui.Button("PortEnd", "end", "结束进程", () => _ = EndAsync()),
            _ui.Button("PortDiagnose", "diagnose", "诊断", () => _ = DiagnoseAsync())
        }) actions.Children.Add(control);
        var body = new Grid { RowDefinitions = new("Auto,*,Auto,Auto"), RowSpacing = 8 };
        body.Children.Add(_header);
        Grid.SetRow(_rows, 1); body.Children.Add(_rows);
        Grid.SetRow(_matches, 2); body.Children.Add(_matches);
        Grid.SetRow(_status, 3); body.Children.Add(_status);
        _page = _ui.Page("title", "端口看板", "detail", "查看本机正在监听的 TCP 端口和 UDP 绑定。", new ScrollViewer { Content = body }, actions);
        _context!.Subscribe(e =>
        {
            if (e.Kind == WorkbenchEventKind.LanguageChanged)
            {
                _ui.Translate();
                _filter.Watermark = _ui.T("filter", "按端口、进程名或 PID 过滤");
                _port.Watermark = _ui.T("port", "端口号");
                Show();
            }
        });
        Show();
        return _page;
    }

    private async Task RefreshAsync()
    {
        if (_context is null || _busy) return;
        _busy = true;
        var version = ++_refresh;
        var context = _context;
        _status.Text = _ui.T("busy", "正在读取端口…");
        try
        {
            var windows = OperatingSystem.IsWindows();
            var directory = PortSnapshotParser.ProbeDirectory();
            if (windows)
            {
                var net = await OemProcess.RunAsync("netstat", ["-ano"], directory, context.Lifetime);
                var tasks = await OemProcess.RunAsync("tasklist", ["/fo", "csv"], directory, context.Lifetime);
                if (_context is null || version != _refresh) return;
                var rows = PortSnapshotParser.AttachNames(PortSnapshotParser.ParseNetstat(net.Output), PortSnapshotParser.ParseTasklist(tasks.Output));
                _snapshot = rows;
                _source = "netstat -ano + tasklist /fo csv";
                _exitCode = net.ExitCode != 0 ? net.ExitCode : tasks.ExitCode;
                _error = Join(net.Error, tasks.Error);
            }
            else
            {
                var ss = await OemProcess.RunAsync("ss", ["-tulnp"], directory, context.Lifetime);
                if (_context is null || version != _refresh) return;
                if (ss.ExitCode == 0 && PortSnapshotParser.ParseSs(ss.Output).Count > 0)
                {
                    _snapshot = PortSnapshotParser.ParseSs(ss.Output);
                    _source = "ss -tulnp";
                    _exitCode = ss.ExitCode;
                    _error = ss.Error;
                }
                else
                {
                    var lsof = await OemProcess.RunAsync("lsof", ["-i", "-P", "-n"], directory, context.Lifetime);
                    if (_context is null || version != _refresh) return;
                    _snapshot = PortSnapshotParser.ParseLsof(lsof.Output);
                    _source = "lsof -i -P -n";
                    _exitCode = lsof.ExitCode;
                    _error = Join(ss.Error, lsof.Error);
                }
            }
            Show();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (_context is null || version != _refresh) return;
            _error = ex.Message;
            _exitCode = -1;
            Show();
            context.ReportError(ex);
        }
        finally { _busy = false; }
    }

    private void Lookup()
    {
        if (_context is null) return;
        var text = (_port.Text ?? "").Trim();
        if (!PortSnapshotParser.TryParsePortQuery(text, out var port))
        {
            if (SelectedRow() is PortRow selected) port = selected.Port;
            else { _matches.Text = _ui.T("none", "快照里没有这个端口。"); return; }
        }
        var found = PortSnapshotParser.MatchingPort(_snapshot, port);
        _matches.Text = _ui.T("matches", "占用该端口的记录") + " " + port.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ": " + (found.Count == 0 ? _ui.T("none", "快照里没有这个端口。") : string.Join(" | ", found.Select(Format)));
    }

    private async Task EndAsync()
    {
        if (_context is null) return;
        if (SelectedRow() is not PortRow row || !PortSnapshotParser.CanEndProcess(row, _snapshot, Environment.ProcessId))
        {
            _status.Text = _ui.T("refused", "这个 PID 不在快照里，或是 0、4、当前进程。");
            return;
        }
        var owner = TopLevel.GetTopLevel(_page);
        if (owner is not Window parent)
        {
            _status.Text = _ui.T("noconfirm", "没有父窗口，已取消。");
            return;
        }
        var dialog = new ConfirmEndWindow(_ui.T("confirm-title", "结束进程"),
            PortSnapshotParser.ConfirmText(row, OperatingSystem.IsWindows(), _context.Language == "en"),
            _ui.T("confirm-yes", "结束"), _ui.T("confirm-no", "取消"));
        if (await dialog.ShowDialog<bool>(parent) != true) { _status.Text = _ui.T("cancel", "已取消。"); return; }
        if (_context is null || !PortSnapshotParser.CanEndProcess(row, _snapshot, Environment.ProcessId)) return;
        try
        {
            var windows = OperatingSystem.IsWindows();
            var result = windows
                ? await OemProcess.RunAsync("taskkill", ["/PID", row.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture), "/F"], PortSnapshotParser.ProbeDirectory(), _context.Lifetime)
                : await OemProcess.RunAsync("kill", [row.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture)], PortSnapshotParser.ProbeDirectory(), _context.Lifetime);
            _exitCode = result.ExitCode;
            _error = Join(result.Error, result.Output);
            _source = windows ? "taskkill /PID /F" : "kill";
            _status.Text = StatusLine();
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            _status.Text = StatusLine();
            _context.ReportError(ex);
        }
    }

    private async Task DiagnoseAsync()
    {
        if (_context is null || SelectedRow() is not PortRow row) return;
        var (shell, arguments) = PortSnapshotParser.DiagnosticSession(row, OperatingSystem.IsWindows());
        var title = "port " + row.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await _context.Host.CreateSessionAsync(new NewSessionRequest(title, "", shell, arguments));
    }

    private void Show()
    {
        if (_rows is null) return;
        _header.Text = _ui.T("protocol", "协议") + "  " + _ui.T("local", "本地地址") + "  " + _ui.T("state", "状态")
            + "  " + _ui.T("remote", "对端") + "  " + _ui.T("pid", "PID") + "  " + _ui.T("process", "进程名");
        var visible = PortSnapshotParser.Filter(_snapshot, _filter?.Text ?? "");
        _rows.ItemsSource = visible.Select(row => new PortListItem(row, _ui.T("udp", "UDP 绑定"), _ui.T("listening", "LISTENING"))).ToList();
        if (visible.Count == 0) _matches.Text = _snapshot.Count == 0 ? "" : _ui.T("empty", "没有匹配的端口。");
        _status.Text = StatusLine();
    }

    private string StatusLine()
    {
        var platform = OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" : "Linux";
        var line = platform + " · " + (_source.Length == 0 ? "-" : _source) + " · exit " + _exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (_error.Length > 0) line += " · " + _error.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return line;
    }

    private PortRow? SelectedRow() => (_rows.SelectedItem as PortListItem)?.Row;

    private static string Format(PortRow row)
        => row.Protocol + " " + row.LocalAddress + ":" + row.Port + " " + (row.Udp ? "UDP 绑定" : row.RemoteAddress) + " " + row.Pid + " " + row.ProcessName;

    private static string Join(string left, string right)
    {
        left = (left ?? "").Trim();
        right = (right ?? "").Trim();
        if (left.Length == 0) return right;
        if (right.Length == 0) return left;
        return left + " " + right;
    }

    public void Deactivate() { _context = null; _refresh++; }
}

internal sealed class PortListItem(PortRow row, string udpLabel, string listeningLabel)
{
    public PortRow Row => row;
    public override string ToString()
        => row.Protocol + "  " + row.LocalAddress + ":" + row.Port + "  " + (row.Udp ? udpLabel : listeningLabel)
            + "  " + row.RemoteAddress + "  " + row.Pid + "  " + row.ProcessName;
}

internal sealed class ConfirmEndWindow : Window
{
    private bool _answered;

    // Only the explicit "End" button may close with true. The title-bar X and the cancel
    // button both land in OnClosing as WindowClosing; cancel is re-Closed as false so the
    // caller's `!= true` check cannot be bypassed. The _answered flag stops the recursion.
    public ConfirmEndWindow(string title, string message, string yes, string no)
    {
        Title = title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 16) };
        var ok = new Button { Content = yes, IsDefault = true, Margin = new(0, 0, 8, 0), MinWidth = 88 };
        var cancel = new Button { Content = no, IsCancel = true, MinWidth = 88 };
        ok.Click += (_, _) => { _answered = true; Close(true); };
        cancel.Click += (_, _) => { _answered = true; Close(false); };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        Content = new StackPanel { Margin = new(18), Children = { text, buttons } };
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Any close that did not come from a button (the title-bar X) counts as cancel.
        if (!_answered) { _answered = true; Close(false); }
        base.OnClosing(e);
    }
}

/// <summary>Own runner. PluginProcess pins stdout to UTF-8, which garbles Chinese netstat/tasklist/taskkill on OEM code page 936 while still exiting 0.</summary>
internal static class OemProcess
{
    public static async Task<OemResult> RunAsync(string executable, IReadOnlyList<string> arguments, string directory, CancellationToken ct)
    {
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        ct.ThrowIfCancellationRequested();
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Cannot start " + executable);
        using var registration = ct.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        });
        var stdout = ReadAllAsync(process.StandardOutput.BaseStream, ct);
        var stderr = ReadAllAsync(process.StandardError.BaseStream, ct);
        await process.WaitForExitAsync(ct);
        var windows = OperatingSystem.IsWindows();
        return new(process.ExitCode, PortSnapshotParser.DecodeCommandOutput(await stdout, windows), PortSnapshotParser.DecodeCommandOutput(await stderr, windows));
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, ct);
            if (read == 0) break;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}

internal readonly record struct OemResult(int ExitCode, string Output, string Error);
