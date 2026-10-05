using Avalonia.Controls;
using Avalonia.Layout;
using TerminalHub.Extensibility;

namespace TerminalHub.Official.TaskRunner;

public sealed class TaskRunnerOptions
{
    public string Directory { get; set; } = "";
    /// <summary>true pastes into the active session; false starts a one-shot session.</summary>
    public bool Paste { get; set; } = true;
    /// <summary>true follows the selected project / active terminal across workspaces; false pins to the directory chosen by hand.</summary>
    public bool Follow { get; set; } = true;
    /// <summary>Per-workspace directory picked by hand, used when Follow is off so switching workspaces does not leak the previous project.</summary>
    public Dictionary<string, string> PinnedDirectories { get; set; } = new();
}

public sealed class TaskRunnerPlugin : IWorkbenchPlugin
{
    private IPluginContext? _context;
    private PluginUi _ui = null!;
    private TaskRunnerOptions _options = new();
    private readonly List<ProjectTask> _tasks = [];
    private TextBox? _directory;
    private CheckBox? _paste;
    private StackPanel? _list;
    private TextBlock? _status;

    public void Initialize(IPluginContext context)
    {
        _context = context; _ui = new(context);
        _options = context.ReadConfiguration<TaskRunnerOptions>() ?? new();
        _ui.Languages(
            ("tasks", "任务面板", "Task runner"),
            ("detail", "读取 package.json、Makefile、justfile 与 .vscode/tasks.json，点击后粘贴或新建会话执行。", "Read package.json, Makefile, justfile and .vscode/tasks.json, then paste or run in a new session."),
            ("directory", "项目目录", "Project directory"),
            ("refresh", "刷新", "Refresh"),
            ("choose", "选择目录", "Choose folder"),
            ("paste", "粘贴到活动会话", "Paste into the active session"),
            ("run", "运行", "Run"),
            ("missing", "目录不存在，或没有这四个任务文件。", "The directory is missing, or none of the four task files are there."),
            ("empty", "这四个文件里没有可运行的任务。", "Those files have no runnable tasks."),
            ("loaded", "已读取 {0} 个任务", "Loaded {0} tasks"),
            ("noactive", "没有活动会话", "No active session"),
            ("needdir", "先填写项目目录", "Enter a project directory first"),
            ("pasted", "已粘贴，未按回车", "Pasted without pressing Enter"),
            ("started", "已新建会话执行", "Started in a new session"),
            ("follow", "跟随项目／活动终端", "Follow project / active terminal"));
        context.RegisterView(new("tasks", "任务面板", Order: 260,
            IconSvg: "<svg viewBox='0 0 24 24'><path d='M3 4H21V8H3Z M3 10H15V14H3Z M3 16H18V20H3Z'/></svg>"), CreateView);
        context.Subscribe(OnEvent);
    }

    private Control CreateView()
    {
        _directory = new TextBox { Name = "TaskDirectory", Padding = new(10, 8), CornerRadius = new(7) };
        _directory.Text = _options.Directory;
        _directory.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty || _context is null) return;
            var text = _directory.Text ?? "";
            if (text == _options.Directory) return;
            _options.Directory = text;
            _context.SaveConfiguration(_options);
        };
        _paste = _ui.Toggle("TaskPaste", "paste", "粘贴到活动会话", _options.Paste, value =>
        {
            _options.Paste = value;
            _context?.SaveConfiguration(_options);
        });
        var follow = _ui.Toggle("TaskFollow", "follow", "跟随项目／活动终端", _options.Follow, value =>
        {
            _options.Follow = value;
            _context?.SaveConfiguration(_options);
            FollowProject();
            Refresh();
        });
        _status = _ui.Label(brush: "UiMuted", size: 11); _status.Name = "TaskStatus";
        _list = new StackPanel { Spacing = 6, Name = "TaskList" };
        var bar = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 8 };
        bar.Children.Add(_directory);
        var choose = _ui.Button("TaskChoose", "choose", "选择目录", Choose);
        Grid.SetColumn(choose, 1); bar.Children.Add(choose);
        var refresh = _ui.Button("TaskRefresh", "refresh", "刷新", Refresh, primary: true);
        Grid.SetColumn(refresh, 2); bar.Children.Add(refresh);
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(_ui.LocalLabel("directory", "项目目录", "UiMuted", 12));
        body.Children.Add(bar);
        body.Children.Add(follow);
        body.Children.Add(_paste);
        body.Children.Add(_list);
        body.Children.Add(_status);
        var page = _ui.Page("tasks", "任务面板", "detail", "读取 package.json、Makefile、justfile 与 .vscode/tasks.json，点击后粘贴或新建会话执行。", body);
        FollowProject();
        Refresh();
        return page;
    }

    private void OnEvent(WorkbenchEvent e)
    {
        if (_context is null) return;
        if (e.Kind == WorkbenchEventKind.LanguageChanged) { _ui.Translate(); RefreshList(); return; }
        // WorkspaceChanged fires on every switch and must re-resolve the directory;
        // ProjectDirectoryChanged only fires from the navigator's own picks.
        if (e.Kind == WorkbenchEventKind.WorkspaceChanged) { FollowProject(); Refresh(); return; }
        if (!_options.Follow) return;
        if (e.Kind == WorkbenchEventKind.ProjectDirectoryChanged && e.Data is string path && path.Length > 0)
        {
            ApplyDirectory(path);
            Refresh();
        }
        else if (e.Kind is WorkbenchEventKind.ActiveSessionChanged or WorkbenchEventKind.SessionCwdChanged
            && (e.SessionId is null || e.SessionId == _context.Host.ActiveSessionId)) { FollowProject(); Refresh(); }
    }

    private void FollowProject()
    {
        if (_context is null) return;
        // A hand-picked directory pins this workspace and wins over follow.
        if (!_options.Follow && _options.PinnedDirectories.TryGetValue(_context.Host.ActiveWorkspaceId, out var pinned)
            && pinned.Length > 0)
        {
            ApplyDirectory(pinned, pin: false);
            return;
        }
        var selected = (_context.Host as IProjectWorkbenchHost)?.SelectedProjectDirectory;
        if (_options.Follow && !string.IsNullOrWhiteSpace(selected)) { ApplyDirectory(selected); return; }
        // Fall back to the active terminal's working directory so a workspace switch never keeps the old project;
        // an empty workspace clears the field rather than leaking the previous one.
        var path = ProjectPluginUi.Active(_context)?.WorkingDirectory ?? "";
        ApplyDirectory(path);
    }

    private void ApplyDirectory(string path, bool pin = false)
    {
        _options.Directory = path;
        if (_directory is not null && _directory.Text != path) _directory.Text = path;
        if (pin && _context is not null) _options.PinnedDirectories[_context.Host.ActiveWorkspaceId] = path;
        _context?.SaveConfiguration(_options);
    }

    private async void Choose()
    {
        if (_context is null || _directory is null) return;
        var path = await ProjectPluginUi.PickFolder(_directory, _context.Lifetime);
        if (path is null || _context is null) return;
        _options.Follow = false;
        ApplyDirectory(path, pin: true);
        Refresh();
    }

    private void Refresh()
    {
        _tasks.Clear();
        if (_context is null || _status is null) return;
        var directory = (_directory?.Text ?? _options.Directory).Trim();
        if (directory.Length == 0 || !Directory.Exists(directory))
        {
            _status.Text = _ui.T("missing", "目录不存在，或没有这四个任务文件。");
            RefreshList();
            return;
        }
        var files = new (string Path, Func<string, string, IReadOnlyList<ProjectTask>> Parse)[]
        {
            (Path.Combine(directory, "package.json"), TaskSources.ParsePackageJson),
            (Path.Combine(directory, "Makefile"), TaskSources.ParseMakefile),
            (Path.Combine(directory, "justfile"), TaskSources.ParseJustfile),
            (Path.Combine(directory, ".vscode", "tasks.json"), TaskSources.ParseVsCodeTasks)
        };
        var present = false;
        foreach (var file in files)
        {
            if (!File.Exists(file.Path)) continue;
            present = true;
            try { _tasks.AddRange(file.Parse(File.ReadAllText(file.Path), directory)); }
            catch { /* one bad file must not hide the others */ }
        }
        _status.Text = !present
            ? _ui.T("missing", "目录不存在，或没有这四个任务文件。")
            : _tasks.Count == 0
                ? _ui.T("empty", "这四个文件里没有可运行的任务。")
                : string.Format(_ui.T("loaded", "已读取 {0} 个任务"), _tasks.Count);
        RefreshList();
    }

    private void RefreshList()
    {
        if (_list is null) return;
        _list.Children.Clear();
        foreach (var task in _tasks.ToArray())
        {
            var captured = task;
            var row = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 8 };
            var source = _ui.Label(TaskSources.SourceLabel(task.Source), "UiMuted", 11);
            source.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(source);
            var label = _ui.Label(task.Name, "UiInk", 12);
            label.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(label, 1); row.Children.Add(label);
            var run = _ui.Button("TaskRun" + _list.Children.Count, "run", "运行", () => Run(captured));
            Grid.SetColumn(run, 2); row.Children.Add(run);
            _list.Children.Add(row);
        }
    }

    private async void Run(ProjectTask task)
    {
        if (_context is null || _status is null) return;
        var command = TaskSources.FoldNewlines(TaskSources.CommandFor(task));
        if (command.Length == 0) return;
        if (_options.Paste)
        {
            if (_context.Host.ActiveSessionId is not { } id) { _status.Text = _ui.T("noactive", "没有活动会话"); return; }
            _context.Host.SendInput(id, command, submit: false);
            _context.Host.ActivateSession(id);
            _status.Text = _ui.T("pasted", "已粘贴，未按回车");
            return;
        }
        var (shell, arguments) = TaskSources.OneShotSession(command, OperatingSystem.IsWindows());
        var idCreated = await _context.Host.CreateSessionAsync(new NewSessionRequest(task.Name, task.WorkingDirectory, shell, arguments));
        if (_context is null) return;
        if (idCreated is { } created) _context.Host.ActivateSession(created);
        _status.Text = _ui.T("started", "已新建会话执行");
    }

    public void Deactivate() { _context = null; _tasks.Clear(); }
}
