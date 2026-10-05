using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TerminalHub.Core.Sessions;
using TerminalHub.Extensibility;

namespace TerminalHub.Official.ProjectNavigator;

public sealed record ProjectBookmark(string Name, string Path, string Group)
{
    public override string ToString() => (Group.Length > 0 ? "[" + Group + "] " : "") + Name + "\n" + Path;
}
public sealed class ProjectNavigatorOptions
{
    public List<ProjectBookmark> Bookmarks { get; set; } = [];
    public List<string> Recent { get; set; } = [];
    public bool Follow { get; set; } = true;
    public string DefaultDirectory { get; set; } = "";
    public bool ShowHiddenDirectories { get; set; }
    public bool ShowFullPaths { get; set; }
    public bool CompactLayout { get; set; } = true;
    public int RecentLimit { get; set; } = 12;
}

public sealed class ProjectNavigatorPlugin : IWorkbenchPlugin
{
    private IPluginContext? _context;
    private PluginUi _ui = null!;
    private ProjectNavigatorOptions _options = new();
    private readonly Dictionary<string, CwdHistory> _histories = new();
    private readonly Dictionary<string, string> _workspacePaths = new();
    private string _directory = "", _workspace = "";
    private int _navigation;
    private Control? _page;
    private TextBox _path = null!, _name = null!, _group = null!, _search = null!;
    private TextBlock _status = null!, _target = null!;
    private ListBox _folders = null!, _bookmarks = null!, _recent = null!;
    private WrapPanel _breadcrumbs = null!;
    private CheckBox _follow = null!;
    private CheckBox? _settingsFollow;
    private Button _back = null!, _forward = null!;

    public void Initialize(IPluginContext context)
    {
        _context = context; _ui = new(context);
        _options = context.ReadConfiguration<ProjectNavigatorOptions>() ?? new();
        _workspace = context.Host.ActiveWorkspaceId;
        _ui.Languages(
            ("navigator", "项目导航", "Project navigator"),
            ("detail", "收藏常用项目，浏览目录，在指定位置切换或新建终端。", "Browse folders, save projects, and open terminals where you need them."),
            ("browse", "浏览目录", "Folders"), ("saved", "收藏与最近", "Saved & recent"),
            ("open", "进入", "Go"), ("pick", "选择文件夹", "Choose folder"),
            ("up", "上级", "Parent"), ("back", "后退", "Back"), ("forward", "前进", "Forward"),
            ("new", "在此新建终端", "New terminal here"), ("cd", "终端切到此处", "Change terminal directory"),
            ("pastecd", "粘贴目录命令", "Paste directory command"),
            ("explorer", "打开文件夹", "Open folder"), ("follow", "跟随当前终端", "Follow active terminal"),
            ("save", "收藏当前目录", "Bookmark current folder"), ("update", "更新所选收藏", "Update selected bookmark"), ("remove", "删除收藏", "Remove bookmark"),
            ("moveup", "上移", "Move up"), ("movedown", "下移", "Move down"),
            ("import", "导入收藏", "Import bookmarks"), ("export", "导出收藏", "Export bookmarks"),
            ("name", "项目名称", "Project name"), ("group", "分组（可选）", "Group (optional)"),
            ("search", "搜索名称、分组或路径", "Search name, group, or path"),
            ("recent", "最近访问", "Recently opened"), ("favorite", "收藏项目", "Bookmarks"),
            ("waiting", "已发送切换命令，等待 Shell 报告目录。", "Directory command sent; waiting for the shell to report its directory."),
            ("busy", "当前会话没有确认的空闲 Shell 提示符，请在此新建终端。", "The session has no confirmed idle shell prompt. Open a new terminal here."),
            ("remote", "当前为远程会话；这里浏览本地目录，可在此新建本地终端。", "Remote session selected. These folders are local; open a local terminal here."),
            ("noactive", "未选择终端", "No active terminal"),
            ("needname", "请填写项目名称。", "Enter a project name."),
            ("savedok", "收藏已保存", "Bookmark saved"), ("imported", "收藏已导入", "Bookmarks imported"),
            ("exported", "收藏已导出", "Bookmarks exported"),
            ("navigator-settings", "项目导航设置", "Project navigator settings"),
            ("defaultdir", "默认目录（留空使用用户目录）", "Default folder (empty uses your home folder)"),
            ("hidden", "显示隐藏目录", "Show hidden folders"), ("fullpaths", "列表显示完整路径", "Show full paths in the list"),
            ("compact", "紧凑布局", "Compact layout"), ("recentlimit", "最近访问数量", "Recent folder limit"));
        context.RegisterView(new("navigator", "项目导航", Order: 240,
            IconSvg: "<svg viewBox='0 0 24 24'><path d='M2 5H10L12 7H22V20H2Z M4 9V18H20V9Z'/></svg>"), CreateView);
        context.Subscribe(e => Observe(e));
        context.RegisterView(new("navigator-settings", "项目导航设置", ExtensionSurface.Settings), CreateSettings);
    }

    private Control CreateView()
    {
        _path = new TextBox { Name = "ProjectPath" };
        _name = new TextBox { Name = "ProjectName" };
        _group = new TextBox { Name = "ProjectGroup" };
        _search = new TextBox { Name = "ProjectSearch" };
        _status = _ui.Label(brush: "UiMuted", size: 12); _status.Name = "ProjectStatus";
        _target = _ui.Label(brush: "UiMuted", size: 12);
        _folders = new ListBox { Name = "ProjectFolders", MinHeight = 140, ItemTemplate = FolderTemplate() };
        _bookmarks = new ListBox { Name = "ProjectBookmarks", MinHeight = 140 };
        _recent = new ListBox { Name = "ProjectRecent", MinHeight = 90 };
        foreach (var list in new[] { _folders, _bookmarks, _recent }) _ui.StyleList(list);
        _breadcrumbs = new WrapPanel();
        _follow = new CheckBox { Name = "ProjectFollow", IsChecked = _options.Follow };
        _follow.IsCheckedChanged += (_, _) =>
        { _options.Follow = _follow.IsChecked == true; if (_settingsFollow is not null) _settingsFollow.IsChecked = _options.Follow; Save(); if (_options.Follow) Follow(); };
        _search.TextChanged += (_, _) => RefreshSaved();
        _path.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Safe(() => Navigate(_path.Text ?? "", user: true)); } };
        _folders.DoubleTapped += (_, _) => { if (_folders.SelectedItem is string path) Safe(() => Navigate(path, user: true)); };
        _bookmarks.SelectionChanged += (_, _) =>
        {
            if (_bookmarks.SelectedItem is ProjectBookmark bookmark) { _name.Text = bookmark.Name; _group.Text = bookmark.Group; }
        };
        _bookmarks.DoubleTapped += (_, _) => { if (_bookmarks.SelectedItem is ProjectBookmark bookmark) Safe(() => Navigate(bookmark.Path, user: true)); };
        _recent.DoubleTapped += (_, _) => { if (_recent.SelectedItem is string path) Safe(() => Navigate(path, user: true)); };
        _back = B("ProjectBack", "back", "后退", () => Safe(() => Travel(false)));
        _forward = B("ProjectForward", "forward", "前进", () => Safe(() => Travel(true)));
        var pathbar = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
        pathbar.Children.Add(_path);
        var go = B("ProjectGo", "open", "进入", () => Safe(() => Navigate(_path.Text ?? "", user: true))); Grid.SetColumn(go, 1); pathbar.Children.Add(go);
        var browse = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = 8 };
        browse.Children.Add(_breadcrumbs); Grid.SetRow(_folders, 1); browse.Children.Add(_folders);
        var saved = new StackPanel { Spacing = 8 };
        saved.Children.Add(_search); saved.Children.Add(_bookmarks);
        var editors = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 8 };
        editors.Children.Add(_name); Grid.SetColumn(_group, 1); editors.Children.Add(_group); saved.Children.Add(editors);
        saved.Children.Add(ProjectPluginUi.Actions(
            B("ProjectSave", "save", "收藏当前目录", () => AddBookmark(false)),
            B("ProjectUpdate", "update", "更新所选收藏", () => AddBookmark(true)),
            B("ProjectRemove", "remove", "删除收藏", RemoveBookmark),
            B("ProjectMoveUp", "moveup", "上移", () => MoveBookmark(-1)),
            B("ProjectMoveDown", "movedown", "下移", () => MoveBookmark(1)),
            B("ProjectImport", "import", "导入收藏", () => Safe(Import)),
            B("ProjectExport", "export", "导出收藏", () => Safe(Export))));
        saved.Children.Add(_ui.LocalLabel("recent", "最近访问", "UiMuted", 12)); saved.Children.Add(_recent);
        var tabs = new TabControl { Name = "ProjectTabs", MinHeight = 300 };
        tabs.Items.Add(new TabItem { Header = _ui.LocalLabel("browse", "浏览目录"), Content = browse });
        tabs.Items.Add(new TabItem { Header = _ui.LocalLabel("saved", "收藏与最近"), Content = new ScrollViewer { Content = saved } });
        var body = new Grid { RowDefinitions = new("Auto,Auto,Auto,*,Auto"), RowSpacing = 8 };
        body.Children.Add(pathbar);
        var navigation = ProjectPluginUi.Actions(_back, _forward, B("ProjectUp", "up", "上级", () => Safe(() => Navigate(Directory.GetParent(_directory)?.FullName ?? _directory, user: true))),
            B("ProjectChoose", "pick", "选择文件夹", () => Safe(Pick)), _follow);
        Grid.SetRow(navigation, 1); body.Children.Add(navigation);
        var actions = ProjectPluginUi.Actions(
            B("ProjectNewTerminal", "new", "在此新建终端", () => Safe(NewTerminal)),
            B("ProjectCd", "cd", "终端切到此处", ChangeDirectory),
            B("ProjectPasteCd", "pastecd", "粘贴目录命令", PasteDirectoryCommand),
            B("ProjectExplorer", "explorer", "打开文件夹", () => ProjectPluginUi.OpenPath(_directory, true)));
        Grid.SetRow(actions, 2); body.Children.Add(actions); Grid.SetRow(tabs, 3); body.Children.Add(tabs);
        var footer = new StackPanel { Spacing = 4 }; footer.Children.Add(_target); footer.Children.Add(_status);
        Grid.SetRow(footer, 4); body.Children.Add(footer);
        body.MinHeight = 650;
        _page = _ui.Page("navigator", "项目导航", "detail", "收藏常用项目，浏览目录，在指定位置切换或新建终端。", new ScrollViewer { Content = body });
        _ui.Compact(_page, _options.CompactLayout); Translate(); RefreshSaved();
        var session = ProjectPluginUi.Active(_context!);
        Safe(() => Navigate(_options.Follow && session is { IsRemote: false } && Directory.Exists(session.WorkingDirectory)
            ? session.WorkingDirectory : DefaultDirectory()));
        return _page;
    }

    private string DefaultDirectory() => Directory.Exists(_options.DefaultDirectory) ? _options.DefaultDirectory : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private FuncDataTemplate<string> FolderTemplate() => new((path, _) =>
    {
        var label = _ui.Label(_options.ShowFullPaths ? path ?? "" : Path.GetFileName(path) ?? "");
        label.TextWrapping = TextWrapping.NoWrap; label.TextTrimming = TextTrimming.CharacterEllipsis;
        ToolTip.SetTip(label, path); return label;
    });
    private Control CreateSettings()
    {
        var panel = new StackPanel { Spacing = 8 };
        _settingsFollow = _ui.Toggle("NavigatorFollowSetting", "follow", "跟随当前终端", _options.Follow, value =>
        { _options.Follow = value; Save(); if (_follow is not null) _follow.IsChecked = value; }); panel.Children.Add(_settingsFollow);
        panel.Children.Add(_ui.Field("defaultdir", "默认目录（留空使用用户目录）", _ui.PreferenceText("NavigatorDefaultDirectory", _options.DefaultDirectory,
            value => { _options.DefaultDirectory = value; Save(); })));
        panel.Children.Add(_ui.Toggle("NavigatorHiddenSetting", "hidden", "显示隐藏目录", _options.ShowHiddenDirectories, value =>
        { _options.ShowHiddenDirectories = value; Save(); if (_page is not null && _directory.Length > 0) Safe(() => Navigate(_directory, history: false)); }));
        panel.Children.Add(_ui.Toggle("NavigatorFullPathsSetting", "fullpaths", "列表显示完整路径", _options.ShowFullPaths, value =>
        { _options.ShowFullPaths = value; Save(); if (_page is not null) _folders.ItemTemplate = FolderTemplate(); }));
        panel.Children.Add(_ui.Field("recentlimit", "最近访问数量", _ui.Number("NavigatorRecentLimit", _options.RecentLimit, 1, 50, value =>
        { _options.RecentLimit = value; TrimRecent(); Save(); if (_page is not null) RefreshSaved(); })));
        panel.Children.Add(_ui.Toggle("NavigatorCompactSetting", "compact", "紧凑布局", _options.CompactLayout, value =>
        { _options.CompactLayout = value; Save(); if (_page is not null) _ui.Compact(_page, value); }));
        return panel;
    }
    private void TrimRecent() { if (_options.Recent.Count > _options.RecentLimit) _options.Recent.RemoveRange(_options.RecentLimit, _options.Recent.Count - _options.RecentLimit); }

    private Button B(string name, string key, string text, Action action) => _ui.Button(name, key, text, action);
    private void Translate()
    {
        _ui.Translate(); _name.Watermark = _ui.T("name", "项目名称"); _group.Watermark = _ui.T("group", "分组（可选）");
        _search.Watermark = _ui.T("search", "搜索名称、分组或路径"); _follow.Content = _ui.T("follow", "跟随当前终端"); UpdateTarget();
    }
    private async void Safe(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (_context is not null) _status.Text = ex.Message; }
    }
    private void Save() => _context?.SaveConfiguration(_options);
    private async Task Navigate(string path, bool history = true, bool user = false)
    {
        if (_context is null) return;
        var context = _context; var version = ++_navigation; var workspace = context.Host.ActiveWorkspaceId;
        path = Path.GetFullPath(path);
        var folders = await Task.Run(() => Directory.EnumerateDirectories(path)
            .Where(p => _options.ShowHiddenDirectories || (!Path.GetFileName(p).StartsWith('.') && (File.GetAttributes(p) & FileAttributes.Hidden) == 0))
            .Order(StringComparer.OrdinalIgnoreCase).ToArray(), context.Lifetime);
        if (_context is null || context.Lifetime.IsCancellationRequested || version != _navigation || workspace != context.Host.ActiveWorkspaceId) return;
        _directory = path; _workspacePaths[workspace] = path; _path.Text = path; _folders.ItemsSource = folders;
        var cwdHistory = History(workspace);
        if (history) cwdHistory.Push(path);
        _back.IsEnabled = cwdHistory.CanGoBack; _forward.IsEnabled = cwdHistory.CanGoForward;
        _breadcrumbs.Children.Clear();
        var parents = new Stack<DirectoryInfo>();
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent) parents.Push(directory);
        foreach (var parent in parents)
        {
            var target = parent.FullName;
            var button = new Button { Content = parent.Name.Length > 0 ? parent.Name : target, Margin = new(0, 0, 4, 4) };
            button.Click += (_, _) => Safe(() => Navigate(target, user: true)); _breadcrumbs.Children.Add(button);
        }
        if (user)
        {
            _options.Recent.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            _options.Recent.Insert(0, path); TrimRecent();
            Save(); RefreshSaved();
            (context.Host as IProjectWorkbenchHost)?.SelectProjectDirectory(path);
        }
        _status.Text = path; UpdateTarget();
    }
    private CwdHistory History(string workspace)
    {
        if (!_histories.TryGetValue(workspace, out var history)) _histories[workspace] = history = new();
        return history;
    }
    private Task Travel(bool forward)
    {
        var history = History(_workspace);
        var path = forward ? history.Forward() : history.Back();
        return path is null ? Task.CompletedTask : Navigate(path, history: false, user: true);
    }
    private async Task Pick()
    {
        if (_context is null) return;
        var path = await ProjectPluginUi.PickFolder(_page!, _context.Lifetime);
        if (path is not null) await Navigate(path, user: true);
    }
    private async Task NewTerminal()
    {
        var context = _context; if (context is null || !Directory.Exists(_directory)) return;
        var path = _directory;
        var id = await context.Host.CreateSessionAsync(new(Name: new DirectoryInfo(path).Name, WorkingDirectory: path));
        if (_context is not null && id is { } session) context.Host.ActivateSession(session);
    }
    private void ChangeDirectory()
    {
        if (_context is null) return;
        var session = ProjectPluginUi.Active(_context);
        if (session is null) { _status.Text = _ui.T("noactive", "未选择终端"); return; }
        if (session.IsRemote) { _status.Text = _ui.T("remote", "当前为远程会话；这里浏览本地目录，可在此新建本地终端。"); return; }
        var sent = (_context.Host as IProjectWorkbenchHost)?.ChangeSessionDirectory(session.Id, _directory) == true;
        _status.Text = sent ? _ui.T("waiting", "已发送切换命令，等待 Shell 报告目录。") : _ui.T("busy", "当前会话没有确认的空闲 Shell 提示符，请在此新建终端。");
    }
    private void PasteDirectoryCommand()
    {
        if (_context is null || ProjectPluginUi.Active(_context) is not { IsRemote: false, Running: true } session) return;
        _context.Host.SendInput(session.Id, TerminalHub.Core.Pty.ShellDirectoryCommand.Build(_directory, session.Shell));
        _context.Host.ActivateSession(session.Id);
    }
    private void AddBookmark(bool update)
    {
        var name = (_name.Text ?? "").Trim(); if (name.Length == 0) { _status.Text = _ui.T("needname", "请填写项目名称。"); return; }
        if (update && _bookmarks.SelectedItem is not ProjectBookmark) return;
        var path = update ? ((ProjectBookmark)_bookmarks.SelectedItem!).Path : _directory;
        if (!Directory.Exists(path)) return;
        var bookmark = new ProjectBookmark(name, path, (_group.Text ?? "").Trim());
        var existing = _options.Bookmarks.FindIndex(b => b.Path == path);
        if (existing >= 0) _options.Bookmarks[existing] = bookmark; else _options.Bookmarks.Add(bookmark);
        Save(); RefreshSaved(); _bookmarks.SelectedItem = bookmark; _status.Text = _ui.T("savedok", "收藏已保存");
    }
    private void RemoveBookmark()
    {
        if (_bookmarks.SelectedItem is ProjectBookmark bookmark) { _options.Bookmarks.Remove(bookmark); Save(); RefreshSaved(); }
    }
    private void MoveBookmark(int delta)
    {
        if (_bookmarks.SelectedItem is not ProjectBookmark bookmark) return;
        var index = _options.Bookmarks.IndexOf(bookmark); var next = index + delta;
        if (next < 0 || next >= _options.Bookmarks.Count) return;
        (_options.Bookmarks[index], _options.Bookmarks[next]) = (_options.Bookmarks[next], _options.Bookmarks[index]);
        Save(); RefreshSaved(); _bookmarks.SelectedItem = bookmark;
    }
    private void RefreshSaved()
    {
        var filter = _search.Text ?? "";
        _bookmarks.ItemsSource = _options.Bookmarks.Where(b => b.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
        _recent.ItemsSource = _options.Recent.Where(p => p.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
    }
    public void ImportBookmarks(string json)
    {
        var items = JsonSerializer.Deserialize<List<ProjectBookmark>>(json) ?? throw new InvalidDataException("Empty bookmarks");
        var bookmarks = items.Select(b => new ProjectBookmark(b.Name, Path.GetFullPath(b.Path), b.Group ?? "")).ToArray();
        foreach (var bookmark in bookmarks)
        {
            _options.Bookmarks.RemoveAll(b => b.Path == bookmark.Path); _options.Bookmarks.Add(bookmark);
        }
        Save(); if (_page is not null) RefreshSaved();
    }
    public string ExportBookmarks() => JsonSerializer.Serialize(_options.Bookmarks, new JsonSerializerOptions { WriteIndented = true });
    private async Task Import()
    {
        if (_context is null) return;
        var path = await ProjectPluginUi.PickFile(_page!, false, _context.Lifetime);
        if (path is null || _context is null) return;
        ImportBookmarks(await File.ReadAllTextAsync(path, _context.Lifetime)); _status.Text = _ui.T("imported", "收藏已导入");
    }
    private async Task Export()
    {
        if (_context is null) return;
        var json = ExportBookmarks(); var path = await ProjectPluginUi.PickFile(_page!, true, _context.Lifetime);
        if (path is null || _context is null) return;
        await File.WriteAllTextAsync(path, json, _context.Lifetime); _status.Text = _ui.T("exported", "收藏已导出");
    }
    private void UpdateTarget()
    {
        if (_page is null || _context is null) return;
        var session = ProjectPluginUi.Active(_context);
        _target.Text = session is null ? _ui.T("noactive", "未选择终端") : session.Name + " · " + session.WorkingDirectory;
        if (session?.IsRemote == true) _status.Text = _ui.T("remote", "当前为远程会话；这里浏览本地目录，可在此新建本地终端。");
    }
    private void Follow()
    {
        if (_page is null || _context is null) return;
        UpdateTarget();
        var session = ProjectPluginUi.Active(_context);
        if (session is { IsRemote: false } && Directory.Exists(session.WorkingDirectory)) Safe(() => Navigate(session.WorkingDirectory));
    }
    private void Observe(WorkbenchEvent e)
    {
        if (_context is null) return;
        if (e.Kind == WorkbenchEventKind.LanguageChanged) { _ui.Translate(); if (_page is not null) Translate(); return; }
        if (_page is null) return;
        if (e.Kind == WorkbenchEventKind.WorkspaceChanged)
        {
            _workspace = _context.Host.ActiveWorkspaceId;
            if (_options.Follow) Follow();
            else if (_workspacePaths.TryGetValue(_workspace, out var path)) Safe(() => Navigate(path));
            else Safe(() => Navigate(DefaultDirectory()));
        }
        if (e.Kind is WorkbenchEventKind.ActiveSessionChanged or WorkbenchEventKind.SessionCwdChanged)
        {
            if (e.SessionId is not null && e.SessionId != _context.Host.ActiveSessionId) return;
            if (_options.Follow) Follow(); else UpdateTarget();
        }
    }
    public void Deactivate() { ++_navigation; _context = null; }
}
