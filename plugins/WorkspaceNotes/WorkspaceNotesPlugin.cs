using Avalonia.Controls;
using TerminalHub.Extensibility;

namespace TerminalHub.Official.WorkspaceNotes;

public sealed class WorkspaceNotesPlugin : IWorkbenchPlugin
{
    public sealed record Options(Dictionary<string, string> Notes);
    private IPluginContext? _context;
    private PluginUi _ui = null!;
    private Options _options = new([]);
    private TextBox? _editor;
    private TextBlock? _workspace, _status;
    private string _workspaceId = "";
    private bool _dirty, _loading;
    public void Initialize(IPluginContext context)
    {
        _context = context; _ui = new(context); _options = context.ReadConfiguration<Options>() ?? new([]);
        _workspaceId = context.Host.ActiveWorkspaceId;
        _ui.Languages(("notes", "工作区笔记", "Workspace notes"),
            ("detail", "记录交接信息、待办和排查结论；笔记按工作区独立保存。", "Keep handoff notes, to-dos and findings separately for each workspace."),
            ("save", "保存笔记", "Save notes"), ("saved", "已保存到本机", "Saved locally"), ("dirty", "等待保存…", "Saving soon…"));
        context.RegisterView(new("notes", "工作区笔记", Order: 210,
            IconSvg: "<svg viewBox='0 0 24 24'><path d='M4 2H20V22H4Z M6 4V20H18V4Z M8 7H16V9H8Z M8 11H16V13H8Z M8 15H14V17H8Z'/></svg>"), CreateView);
        context.RegisterCommand(new("save", "保存笔记", () => { Save(); return Task.CompletedTask; }));
        context.Schedule(TimeSpan.FromSeconds(1), Save);
        context.Subscribe(e =>
        {
            if (e.Kind == WorkbenchEventKind.WorkspaceChanged && _workspaceId != context.Host.ActiveWorkspaceId)
            {
                Save(); _workspaceId = context.Host.ActiveWorkspaceId;
                _loading = true;
                try { if (_editor is not null) _editor.Text = _options.Notes.GetValueOrDefault(_workspaceId, ""); }
                finally { _loading = false; }
            }
            if (e.Kind is WorkbenchEventKind.WorkspaceChanged or WorkbenchEventKind.LanguageChanged) { _ui.Translate(); Refresh(); }
        });
    }
    private Control CreateView()
    {
        _workspace = _ui.Label(brush: "UiMuted", size: 12); _status = _ui.Label(brush: "UiMuted", size: 11);
        _editor = _ui.Editor("NotesEditor"); _editor.Text = _options.Notes.GetValueOrDefault(_workspaceId, "");
        // TextChanged is queued by Avalonia; a workspace switch can happen before it arrives.
        // Observe the property synchronously so Save still sees the departing workspace's text.
        _editor.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty && !_loading) { _dirty = true; Refresh(); } };
        var body = new Grid { RowDefinitions = new("Auto,*,Auto"), RowSpacing = 10 };
        body.Children.Add(_workspace); Grid.SetRow(_editor, 1); body.Children.Add(_editor); Grid.SetRow(_status, 2); body.Children.Add(_status);
        Refresh(); return _ui.Page("notes", "工作区笔记", "detail", "", body,
            _ui.Button("SaveNotes", "save", "保存笔记", Save, true));
    }
    private void Refresh()
    {
        if (_workspace is not null) _workspace.Text = _context!.Host.Workspaces.FirstOrDefault(w => w.Id == _workspaceId)?.Name ?? _workspaceId;
        if (_status is not null) _status.Text = _ui.T(_dirty ? "dirty" : "saved", _dirty ? "等待保存…" : "已保存到本机");
    }
    private void Save()
    {
        if (!_dirty || _context is null || _editor is null) return;
        _options.Notes[_workspaceId] = _editor.Text ?? "";
        _context.SaveConfiguration(_options); _dirty = false; Refresh();
    }
    public void Deactivate() { Save(); _context = null; }
}
