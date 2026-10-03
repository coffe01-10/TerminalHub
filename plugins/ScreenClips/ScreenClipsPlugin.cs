using System.Text;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using TerminalHub.Core.Terminal;
using TerminalHub.Extensibility;

namespace TerminalHub.Official.ScreenClips;

public sealed record ScreenClip(string SessionName, DateTimeOffset CapturedAt, string Text)
{
    public override string ToString() => $"{CapturedAt:HH:mm:ss}  ·  {SessionName}";
}

public sealed class ScreenClipsPlugin : IWorkbenchPlugin
{
    private IPluginContext _context = null!;
    private PluginUi _ui = null!;
    private readonly List<ScreenClip> _clips = [];
    private ListBox? _list;
    private TextBox? _preview;
    private TextBlock? _status;
    private Button? _copy, _export;
    private ScreenClip? _selected;
    private bool _noSession;
    public void Initialize(IPluginContext context)
    {
        _context = context; _ui = new(context);
        _ui.Languages(("clips", "屏幕摘录", "Screen clips"),
            ("detail", "固定当前终端屏幕为纯文本，方便比对错误和分享排查结果。", "Capture the current terminal screen as plain text for comparing errors and sharing findings."),
            ("capture", "摘录当前屏幕", "Capture screen"), ("copy", "复制", "Copy"), ("export", "导出文本", "Export text"),
            ("clear", "清空摘录", "Clear clips"), ("empty", "点击摘录保存当前屏幕；最多保留最近 20 份，停用插件后清空。", "Capture a screen to start. Keeps the latest 20 clips in memory; disabling clears them."),
            ("no-session", "没有可摘录的活动会话。", "No active session to capture."), ("count", "{0} 份摘录 · 仅保存在内存", "{0} clips · stored in memory only"));
        context.RegisterView(new("clips", "屏幕摘录", Order: 220,
            IconSvg: "<svg viewBox='0 0 24 24'><path d='M2 3H22V18H2Z M4 5V16H20V5Z M8 20H16V22H8Z M6 8H18V10H6Z M6 12H14V14H6Z'/></svg>"), CreateView);
        context.RegisterCommand(new("capture", "摘录当前屏幕", () => { Capture(); return Task.CompletedTask; }));
        context.Subscribe(e => { if (e.Kind == WorkbenchEventKind.LanguageChanged) { _ui.Translate(); RefreshStatus(); } });
    }
    // This is the screen grid, not logical lines: API 1 has no soft-wrap metadata.
    public static string ExtractText(TerminalFrame frame)
    {
        var lines = new List<string>();
        for (var row = 0; row < frame.Rows; row++)
        {
            var line = new StringBuilder();
            for (var column = 0; column < frame.Columns; column++)
            {
                var cell = frame.Cells[row * frame.Columns + column];
                if (cell.IsWideContinuation) continue;
                if ((cell.Attrs & CellAttrs.Hidden) != 0) line.Append(' ', cell.IsWide ? 2 : 1);
                else cell.AppendText(line);
            }
            lines.Add(line.ToString().TrimEnd(' '));
        }
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return string.Join(Environment.NewLine, lines);
    }
    private void Capture()
    {
        var session = _context.Host.Sessions.FirstOrDefault(s => s.Id == _context.Host.ActiveSessionId);
        if (session is null || _context.Host.ReadFrame(session.Id) is not { } frame) { _noSession = true; RefreshStatus(); return; }
        _noSession = false; _selected = new(session.Name, DateTimeOffset.Now, ExtractText(frame));
        _clips.Insert(0, _selected); if (_clips.Count > 20) _clips.RemoveAt(20);
        RefreshList();
    }
    private void RefreshList()
    {
        if (_list is not null) { var selected = _selected; _list.ItemsSource = _clips.ToArray(); _list.SelectedItem = selected; }
        if (_preview is not null) _preview.Text = _selected?.Text ?? "";
        RefreshStatus();
    }
    private void RefreshStatus()
    {
        if (_status is not null) _status.Text = _noSession ? _ui.T("no-session", "没有活动会话") : _clips.Count == 0 ? _ui.T("empty", "") : string.Format(_ui.T("count", "{0} clips"), _clips.Count);
        if (_copy is not null) _copy.IsEnabled = _selected is not null;
        if (_export is not null) _export.IsEnabled = _selected is not null;
    }
    private Control CreateView()
    {
        _list = new ListBox { Name = "ClipList", MaxHeight = 110 };
        _list.SelectionChanged += (_, _) => { _selected = _list.SelectedItem as ScreenClip; if (_preview is not null) _preview.Text = _selected?.Text ?? ""; RefreshStatus(); };
        _preview = _ui.Editor("ClipPreview", true); _status = _ui.Label(brush: "UiMuted", size: 11);
        var body = new Grid { RowDefinitions = new("Auto,*,Auto"), RowSpacing = 10 };
        body.Children.Add(_list); Grid.SetRow(_preview, 1); body.Children.Add(_preview); Grid.SetRow(_status, 2); body.Children.Add(_status);
        var actions = new WrapPanel();
        actions.Children.Add(_ui.Button("CaptureScreen", "capture", "摘录当前屏幕", Capture, true));
        _copy = _ui.Button("CopyClip", "copy", "复制", () => _ = CopyAsync()); actions.Children.Add(_copy);
        _export = _ui.Button("ExportClip", "export", "导出文本", () => _ = ExportAsync()); actions.Children.Add(_export);
        actions.Children.Add(_ui.Button("ClearClips", "clear", "清空摘录", () => { _clips.Clear(); _selected = null; _noSession = false; RefreshList(); }));
        RefreshList(); return _ui.Page("clips", "屏幕摘录", "detail", "", body, actions);
    }
    private async Task CopyAsync()
    {
        try { if (_selected is { } clip && TopLevel.GetTopLevel(_preview!)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(clip.Text); }
        catch (Exception ex) { _context.ReportError(ex); }
    }
    private async Task ExportAsync()
    {
        try
        {
            if (_selected is not { } clip || TopLevel.GetTopLevel(_preview!) is not { } window) return;
            var file = await window.StorageProvider.SaveFilePickerAsync(new() { Title = _ui.T("export", "Export text"), SuggestedFileName = $"terminal-clip-{clip.CapturedAt:yyyyMMdd-HHmmss}.txt", DefaultExtension = "txt" });
            if (file is null || _context.Lifetime.IsCancellationRequested) return;
            using (file)
            await using (var stream = await file.OpenWriteAsync())
            { stream.SetLength(0); await using var writer = new StreamWriter(stream, new UTF8Encoding(false)); await writer.WriteAsync(clip.Text); }
        }
        catch (OperationCanceledException) when (_context.Lifetime.IsCancellationRequested) { }
        catch (Exception ex) { if (!_context.Lifetime.IsCancellationRequested) _context.ReportError(ex); }
    }
    public void Deactivate() => _clips.Clear();
}
