using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TerminalHub.Extensibility;
using Avalonia;
using Avalonia.Styling;

namespace TerminalHub.Official.GitWorkbench;

public sealed class GitWorkbenchOptions
{
    public bool Follow { get; set; } = true;
    public Dictionary<string, string> PinnedDirectories { get; set; } = new();
    public int HistoryLimit { get; set; } = 20;
    public int OutputLimit { get; set; } = 30;
    public string DefaultRemote { get; set; } = "origin";
    public string DefaultBaseBranch { get; set; } = "main";
    public bool RefreshOnCommandCompleted { get; set; } = true;
    public bool WrapDiff { get; set; }
    public bool CompactLayout { get; set; } = true;
}
public sealed record GitFileRow(GitChange Change, bool Staged, string Label)
{
    public override string ToString() => Label + "  " + Change.Path + (Change.OriginalPath is { } original ? " ← " + original : "");
}

public sealed class GitWorkbenchPlugin : IWorkbenchPlugin
{
    private IPluginContext? _context;
    private PluginUi _ui = null!;
    private readonly GitWorkbenchClient _client = new();
    private GitWorkbenchOptions _options = new();
    private GitSnapshot? _state;
    private string _directory = "";
    private bool _busy, _changingItems, _issues, _pendingRefresh;
    private string? _pendingDirectory;
    private CancellationTokenSource? _operation;
    private Control? _page;
    private readonly List<Control> _interactive = [];
    private readonly Queue<string> _log = new();
    private TextBox _path = null!, _message = null!, _diff = null!, _branchName = null!, _targetBranch = null!, _historyPreview = null!, _output = null!;
    private TextBox _prTitle = null!, _prBody = null!, _baseBranch = null!, _issueBranch = null!, _ghPreview = null!;
    private TextBlock _meta = null!, _status = null!, _staged = null!, _conflicts = null!;
    private CheckBox _follow = null!;
    private CheckBox? _settingsFollow;
    private ComboBox _branches = null!, _remotes = null!;
    private ListBox _files = null!, _history = null!, _github = null!, _checks = null!;
    private Button _cancel = null!;
    private TabControl _tabs = null!;

    public void Initialize(IPluginContext context)
    {
        _context = context; _ui = new(context); _options = context.ReadConfiguration<GitWorkbenchOptions>() ?? new();
        _ui.Languages(
            ("git", "Git 工作台", "Git workbench"),
            ("detail", "查看差异、暂存、提交与推送；GitHub 功能复用 gh 登录。", "Review changes, commit, push, and work with GitHub using your gh login."),
            ("open", "打开仓库", "Open repository"), ("choose", "选择目录", "Choose folder"),
            ("refresh", "刷新", "Refresh"), ("init", "初始化仓库", "Initialize repository"),
            ("follow", "跟随项目／活动终端", "Follow project / active terminal"),
            ("changes", "改动", "Changes"), ("history", "提交历史", "History"), ("output", "操作输出", "Output"),
            ("branchesremotes", "分支", "Branches"), ("remotetarget", "远端与目标分支", "Remote and target branch"),
            ("stage", "暂存所选", "Stage selected"), ("unstage", "取消暂存", "Unstage selected"),
            ("stageall", "全部暂存", "Stage all"), ("unstageall", "全部取消暂存", "Unstage all"),
            ("commit", "提交", "Commit"), ("commitpush", "提交并推送", "Commit & push"),
            ("fetch", "获取 Fetch", "Fetch"), ("pull", "拉取 Pull", "Pull"), ("push", "推送 Push", "Push"),
            ("switch", "切换分支", "Switch branch"), ("create", "新建分支", "Create branch"),
            ("terminal", "打开终端", "Open terminal"), ("folder", "打开文件夹", "Open folder"),
            ("file", "打开所选文件", "Open selected file"), ("cancel", "取消操作", "Cancel"),
            ("message", "提交信息（仅提交已暂存文件）", "Commit message (staged files only)"),
            ("branchname", "新分支名称", "New branch name"), ("target", "远端目标分支", "Remote target branch"),
            ("staged", "已暂存", "Staged"), ("unstaged", "未暂存", "Unstaged"), ("untracked", "未跟踪", "Untracked"),
            ("conflict", "冲突", "Conflict"), ("detached", "游离 HEAD", "Detached HEAD"),
            ("norepo", "未打开 Git 仓库，可选择目录或在此初始化。", "No Git repository selected. Choose a folder or initialize here."),
            ("remote", "当前为远程会话，请选择一个本地项目目录。", "Remote session selected. Choose a local project folder."),
            ("running", "操作进行中…", "Working…"), ("done", "操作完成", "Completed"), ("canceled", "操作已取消，请刷新查看实际状态。", "Canceled. Refresh to inspect the actual state."),
            ("selectfile", "请先选择文件。", "Select a file first."), ("selectbranch", "请先选择或填写分支。", "Select or enter a branch."),
            ("needremote", "请选择远端并填写目标分支。", "Select a remote and enter the target branch."),
            ("conflicthelp", "冲突文件可用默认编辑器打开；处理后暂存，再到终端继续 merge／rebase。", "Open conflicted files in your editor, resolve and stage them, then continue merge / rebase in the terminal."),
            ("prs", "加载 PR", "Load PRs"), ("issues", "加载 Issue", "Load issues"),
            ("checks", "检查状态", "Checks"), ("repo", "仓库主页", "Repository page"), ("currentpr", "当前分支 PR", "Current branch PR"),
            ("login", "GitHub 登录", "GitHub login"), ("web", "打开所选页面", "Open selected page"),
            ("details", "查看详情", "View details"), ("draft", "创建 PR 草稿", "Create draft PR"),
            ("title", "PR 标题", "PR title"), ("body", "PR 描述", "PR description"),
            ("base", "目标／基础分支", "Base branch"), ("issuebranch", "Issue 分支名称", "Issue branch name"),
            ("develop", "从所选 Issue 创建分支", "Create branch from issue"),
            ("ghhelp", "首次使用请登录 gh；PR 草稿使用当前分支，请先推送。登录缺失不影响本地 Git 功能。", "Log in to gh first. Push the current branch before creating a draft PR. Local Git works without a GitHub login."),
            ("ahead", "领先／落后", "Ahead / behind"), ("stagedfiles", "本次提交文件", "Files to commit"),
            ("git-settings", "Git 工作台设置", "Git workbench settings"), ("historylimit", "提交历史数量", "Commit history limit"),
            ("outputlimit", "操作输出保留数量", "Command output limit"), ("defaultremote", "默认远端", "Preferred remote"),
            ("defaultbase", "默认 PR／Issue 基础分支", "Default PR / issue base branch"),
            ("autorefresh", "终端命令完成后自动刷新", "Refresh after terminal commands complete"),
            ("wrapdiff", "差异文本自动换行", "Wrap diff text"), ("compact", "紧凑布局", "Compact layout"));
        context.RegisterView(new("git", "Git 工作台", Order: 250,
            IconSvg: "<svg viewBox='0 0 24 24'><path d='M12 1L23 12L12 23L1 12Z M11 7V16H13V12H17V10H13V7Z'/></svg>"), CreateView);
        context.Subscribe(Observe);
        context.RegisterView(new("git-settings", "Git 工作台设置", ExtensionSurface.Settings), CreateSettings);
        _client.CommandCompleted += result =>
        {
            if (_context is null || _output is null) return;
            _log.Enqueue(result.Display); while (_log.Count > _options.OutputLimit) _log.Dequeue();
            _output.Text = string.Join("\n\n", _log);
        };
    }
    private Button B(string name, string key, string text, Func<CancellationToken, Task> action)
    {
        var button = _ui.Button(name, key, text, () => Start(action)); _interactive.Add(button); return button;
    }
    private TextBox Input(string name, bool multiline = false, bool readOnly = false)
    {
        var editor = _ui.Editor(name, readOnly); editor.AcceptsReturn = multiline;
        if (!multiline || readOnly) editor.TextWrapping = TextWrapping.NoWrap;
        editor.Padding = new(9, 7);
        if (readOnly) { editor.FontFamily = new FontFamily("Consolas, monospace"); editor.FontSize = 12; }
        else _interactive.Add(editor);
        return editor;
    }
    private Control CreateView()
    {
        _path = Input("GitPath"); _message = Input("GitMessage", true); _message.Height = 72;
        _diff = Input("GitDiff", true, true); _historyPreview = Input("GitHistoryPreview", true, true);
        _branchName = Input("GitBranchName"); _branchName.Width = 150;
        _targetBranch = Input("GitTargetBranch"); _targetBranch.Width = 150;
        _output = Input("GitOutput", true, true); _ghPreview = Input("GitHubPreview", true, true); _ghPreview.Height = 110;
        _prTitle = Input("GitPrTitle"); _prBody = Input("GitPrBody", true); _prBody.Height = 80;
        _baseBranch = Input("GitPrBase"); _baseBranch.Width = 160;
        _issueBranch = Input("GitIssueBranch"); _issueBranch.Width = 180;
        _meta = _ui.Label(brush: "UiMuted", size: 12); _meta.Name = "GitRepositoryInfo";
        _status = _ui.Label(size: 12); _status.Name = "GitStatus";
        _staged = _ui.Label(brush: "UiMuted", size: 12); _conflicts = _ui.Label(brush: "UiMuted", size: 12);
        _files = new ListBox { Name = "GitFiles", MinHeight = 100 };
        _history = new ListBox { Name = "GitHistory", MinHeight = 100 };
        _github = new ListBox { Name = "GitHubItems", Height = 130 };
        _checks = new ListBox { Name = "GitHubChecks", Height = 90 };
        foreach (var list in new[] { _files, _history, _github, _checks }) _ui.StyleList(list);
        _branches = new ComboBox { Name = "GitBranches", Width = 150 };
        _remotes = new ComboBox { Name = "GitRemotes", Width = 110 };
        _interactive.AddRange([_files, _history, _github, _checks, _branches, _remotes]);
        _follow = new CheckBox { Name = "GitFollow", IsChecked = _options.Follow };
        _interactive.Add(_follow);
        _follow.IsCheckedChanged += (_, _) =>
        {
            _options.Follow = _follow.IsChecked == true; if (_settingsFollow is not null) _settingsFollow.IsChecked = _options.Follow;
            SaveOptions(); if (_options.Follow) Follow();
        };
        _files.SelectionChanged += (_, _) =>
        {
            if (_busy || _changingItems || _files.SelectedItem is not GitFileRow row || _state is null) return;
            var root = _state.Root;
            Start(async ct => _diff.Text = await _client.Diff(root, row.Change, row.Staged, ct));
        };
        _history.SelectionChanged += (_, _) =>
        {
            if (_busy || _changingItems || _history.SelectedItem is not GitCommit commit || _state is null) return;
            var root = _state.Root;
            Start(async ct => _historyPreview.Text = (await _client.Git(root, ct, "show", "--no-ext-diff", "--no-textconv", commit.Id, "--")).RequireSuccess().Output);
        };
        _remotes.SelectionChanged += (_, _) =>
        {
            if (_changingItems || _state is null) return;
            _targetBranch.Text = Equals(_remotes.SelectedItem, _state.UpstreamRemote) && _state.UpstreamBranch.Length > 0 ? _state.UpstreamBranch : _state.Branch;
        };
        _github.SelectionChanged += (_, _) =>
        {
            if (_github.SelectedItem is GitHubItem issue && _issues) _issueBranch.Text = "issue-" + issue.Number;
        };
        _checks.DoubleTapped += (_, _) => { if (_checks.SelectedItem is GitHubCheck check && check.Link.Length > 0) OpenUrl(check.Link); };
        var pathbar = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
        pathbar.Children.Add(_path);
        var open = B("GitOpen", "open", "打开仓库", async ct =>
        {
            _options.Follow = false; _follow.IsChecked = false; _directory = System.IO.Path.GetFullPath(_path.Text ?? "");
            _options.PinnedDirectories[_context!.Host.ActiveWorkspaceId] = _directory; SaveOptions(); await Refresh(ct);
        });
        Grid.SetColumn(open, 1); pathbar.Children.Add(open);
        _cancel = _ui.Button("GitCancel", "cancel", "取消操作", () => _operation?.Cancel()); _cancel.IsEnabled = false;
        var topActions = ProjectPluginUi.Actions(
            B("GitChoose", "choose", "选择目录", async ct =>
            {
                var path = await ProjectPluginUi.PickFolder(_page!, ct); if (path is null) return;
                _options.Follow = false; _follow.IsChecked = false; _directory = path;
                _options.PinnedDirectories[_context!.Host.ActiveWorkspaceId] = path; SaveOptions(); await Refresh(ct);
            }), B("GitRefresh", "refresh", "刷新", Refresh), _cancel, _follow);
        var branchActions = ProjectPluginUi.Actions(_branches,
            B("GitSwitch", "switch", "切换分支", async ct =>
            { var state = RequireState(); var branch = _branches.SelectedItem as string ?? throw new InvalidOperationException(_ui.T("selectbranch", "请先选择或填写分支。")); await _client.Switch(state.Root, branch, false, ct); await Refresh(ct); }),
            _branchName, B("GitCreateBranch", "create", "新建分支", async ct =>
            { var state = RequireState(); var branch = (_branchName.Text ?? "").Trim(); if (branch.Length == 0) throw new InvalidOperationException(_ui.T("selectbranch", "请先选择或填写分支。")); await _client.Switch(state.Root, branch, true, ct); await Refresh(ct); }));
        var remoteActions = ProjectPluginUi.Actions(_remotes, _targetBranch,
            B("GitFetch", "fetch", "获取 Fetch", async ct => { var state = RequireState(); await _client.Fetch(state.Root, Remote(), ct); await Refresh(ct); }),
            B("GitPull", "pull", "拉取 Pull", async ct => { var state = RequireState(); await _client.Pull(state.Root, Remote(), Target(), ct); await Refresh(ct); }),
            B("GitPush", "push", "推送 Push", async ct => { await _client.Push(RequireState(), Remote(), Target(), ct); await Refresh(ct); }));
        var fileActions = ProjectPluginUi.Actions(
            B("GitStage", "stage", "暂存所选", async ct => { var state = RequireState(); await _client.Stage(state.Root, SelectedFile().Change, ct); await Refresh(ct); }),
            B("GitUnstage", "unstage", "取消暂存", async ct => { await _client.Unstage(RequireState(), SelectedFile().Change, ct); await Refresh(ct); }),
            B("GitStageAll", "stageall", "全部暂存", async ct => { var state = RequireState(); await _client.Stage(state.Root, null, ct); await Refresh(ct); }),
            B("GitUnstageAll", "unstageall", "全部取消暂存", async ct => { await _client.Unstage(RequireState(), null, ct); await Refresh(ct); }),
            B("GitOpenFile", "file", "打开所选文件", ct => { ProjectPluginUi.OpenPath(System.IO.Path.Combine(RequireState().Root, SelectedFile().Change.Path), false); return Task.CompletedTask; }));
        var review = new Grid { ColumnDefinitions = new("2*,3*"), ColumnSpacing = 10 };
        review.MinHeight = 160;
        review.Children.Add(_files); Grid.SetColumn(_diff, 1); review.Children.Add(_diff);
        bool? stacked = null;
        review.SizeChanged += (_, _) =>
        {
            var narrow = review.Bounds.Width < 480;
            if (stacked == narrow) return; // Changing rows invalidates layout; apply each arrangement only once.
            stacked = narrow;
            review.ColumnDefinitions = new(narrow ? "*" : "2*,3*"); review.RowDefinitions = new(narrow ? "140,220" : "*");
            Grid.SetColumn(_diff, narrow ? 0 : 1); Grid.SetRow(_diff, narrow ? 1 : 0); review.MinHeight = narrow ? 360 : 160;
        };
        var commitForm = new StackPanel { Spacing = 6 };
        commitForm.Children.Add(new ScrollViewer { Content = _staged, MaxHeight = 55 }); commitForm.Children.Add(_conflicts); commitForm.Children.Add(_message);
        commitForm.Children.Add(ProjectPluginUi.Actions(B("GitCommit", "commit", "提交", ct => Commit(false, ct)), B("GitCommitPush", "commitpush", "提交并推送", ct => Commit(true, ct))));
        var changes = new Grid { RowDefinitions = new("Auto,*,Auto"), RowSpacing = 8 };
        changes.Children.Add(fileActions); Grid.SetRow(review, 1); changes.Children.Add(review); Grid.SetRow(commitForm, 2); changes.Children.Add(commitForm);
        var history = new Grid { ColumnDefinitions = new("2*,3*"), ColumnSpacing = 10 };
        history.Children.Add(_history); Grid.SetColumn(_historyPreview, 1); history.Children.Add(_historyPreview);
        var gh = CreateGitHubView();
        _tabs = new TabControl { Name = "GitTabs" };
        _tabs.Styles.Add(new Style(s => s.OfType<TabItem>()) { Setters = { new Setter(TabItem.PaddingProperty, new Thickness(8,6)) } });
        _tabs.MinHeight = 380;
        _tabs.Items.Add(new TabItem { Header = _ui.LocalLabel("changes", "改动", size: 12), Content = changes });
        _tabs.Items.Add(new TabItem { Header = _ui.LocalLabel("history", "提交历史", size: 12), Content = history });
        var repositoryActions = new StackPanel { Spacing = 12 };
        repositoryActions.Children.Add(ProjectPluginUi.Actions(
            B("GitInit", "init", "初始化仓库", async ct => { await _client.Init(_directory, ct); await Refresh(ct); }),
            B("GitTerminal", "terminal", "打开终端", OpenTerminal),
            B("GitFolder", "folder", "打开文件夹", ct => { ProjectPluginUi.OpenPath(_directory, true); return Task.CompletedTask; })));
        repositoryActions.Children.Add(branchActions); repositoryActions.Children.Add(_ui.LocalLabel("remotetarget", "远端与目标分支", "UiMuted", 12)); repositoryActions.Children.Add(remoteActions);
        _tabs.Items.Add(new TabItem { Header = _ui.LocalLabel("branchesremotes", "分支", size: 12), Content = new ScrollViewer { Content = repositoryActions } });
        _tabs.Items.Add(new TabItem { Header = _ui.Label("GitHub", size: 12), Content = gh });
        _tabs.Items.Add(new TabItem { Header = _ui.LocalLabel("output", "操作输出", size: 12), Content = _output });
        var body = new Grid { RowDefinitions = new("Auto,Auto,Auto,*,Auto"), RowSpacing = 6 };
        body.Children.Add(pathbar);
        foreach (var (control, row) in new (Control, int)[] { (topActions, 1), (_meta, 2), (_tabs, 3), (_status, 4) })
        { Grid.SetRow(control, row); body.Children.Add(control); }
        // The page can be taller than the minimum tool window; keep all actions reachable by scrolling.
        body.MinHeight = 620;
        _page = _ui.Page("git", "Git 工作台", "detail", "查看差异、暂存、提交与推送；GitHub 功能复用 gh 登录。", new ScrollViewer { Content = body });
        ApplyAppearance(); Translate(); Follow(); return _page;
    }

    private Control CreateSettings()
    {
        var panel = new StackPanel { Spacing = 8 };
        _settingsFollow = _ui.Toggle("GitFollowSetting", "follow", "跟随项目／活动终端", _options.Follow, value =>
        { _options.Follow = value; SaveOptions(); if (_follow is not null) _follow.IsChecked = value; }); panel.Children.Add(_settingsFollow);
        panel.Children.Add(_ui.Field("defaultremote", "默认远端", _ui.PreferenceText("GitDefaultRemote", _options.DefaultRemote,
            value => { _options.DefaultRemote = value.Trim(); SaveOptions(); })));
        panel.Children.Add(_ui.Field("defaultbase", "默认 PR／Issue 基础分支", _ui.PreferenceText("GitDefaultBase", _options.DefaultBaseBranch,
            value => { _options.DefaultBaseBranch = value.Trim(); SaveOptions(); })));
        panel.Children.Add(_ui.Field("historylimit", "提交历史数量", _ui.Number("GitHistoryLimit", _options.HistoryLimit, 1, 200,
            value => { _options.HistoryLimit = value; SaveOptions(); })));
        panel.Children.Add(_ui.Field("outputlimit", "操作输出保留数量", _ui.Number("GitOutputLimit", _options.OutputLimit, 1, 100, value =>
        { _options.OutputLimit = value; while (_log.Count > value) _log.Dequeue(); if (_output is not null) _output.Text = string.Join("\n\n", _log); SaveOptions(); })));
        panel.Children.Add(_ui.Toggle("GitRefreshSetting", "autorefresh", "终端命令完成后自动刷新", _options.RefreshOnCommandCompleted,
            value => { _options.RefreshOnCommandCompleted = value; SaveOptions(); }));
        panel.Children.Add(_ui.Toggle("GitWrapSetting", "wrapdiff", "差异文本自动换行", _options.WrapDiff,
            value => { _options.WrapDiff = value; SaveOptions(); ApplyAppearance(); }));
        panel.Children.Add(_ui.Toggle("GitCompactSetting", "compact", "紧凑布局", _options.CompactLayout,
            value => { _options.CompactLayout = value; SaveOptions(); ApplyAppearance(); }));
        return panel;
    }
    private void ApplyAppearance()
    {
        if (_page is null) return;
        _ui.Compact(_page, _options.CompactLayout);
        _diff.TextWrapping = _historyPreview.TextWrapping = _options.WrapDiff ? TextWrapping.Wrap : TextWrapping.NoWrap;
    }

    private Control CreateGitHubView()
    {
        var panel = new StackPanel { Spacing = 7 };
        panel.Children.Add(_ui.LocalLabel("ghhelp", "首次使用请登录 gh；PR 草稿使用当前分支，请先推送。登录缺失不影响本地 Git 功能。", "UiMuted", 12));
        panel.Children.Add(ProjectPluginUi.Actions(
            B("GitHubLogin", "login", "GitHub 登录", async ct =>
            {
                var context = _context!;
                var id = await context.Host.CreateSessionAsync(new("GitHub login", _directory, "gh", "auth login") { Transient = true });
                if (_context is not null && id is { } session) context.Host.ActivateSession(session);
            }),
            B("GitHubRepo", "repo", "仓库主页", async ct => OpenUrl(await _client.RepoUrl(RequireState().Root, ct))),
            B("GitHubCurrentPr", "currentpr", "当前分支 PR", async ct => OpenUrl(await _client.CurrentPrUrl(RequireState().Root, ct))),
            B("GitHubPrList", "prs", "加载 PR", ct => LoadGitHub(false, ct)),
            B("GitHubIssueList", "issues", "加载 Issue", ct => LoadGitHub(true, ct)),
            B("GitHubLoadChecks", "checks", "检查状态", async ct =>
            {
                var number = !_issues && _github.SelectedItem is GitHubItem pr ? pr.Number.ToString() : null;
                _checks.ItemsSource = await _client.Checks(RequireState().Root, number, ct);
            })));
        panel.Children.Add(_github);
        panel.Children.Add(ProjectPluginUi.Actions(
            B("GitHubOpenItem", "web", "打开所选页面", ct => { if (_github.SelectedItem is GitHubItem item) OpenUrl(item.Url); return Task.CompletedTask; }),
            B("GitHubDetails", "details", "查看详情", async ct =>
            {
                if (_github.SelectedItem is not GitHubItem item) return;
                var result = (await _client.Gh(RequireState().Root, ct, _issues ? "issue" : "pr", "view", item.Number.ToString(), "--json", "title,body,url,state")).RequireSuccess();
                using var json = System.Text.Json.JsonDocument.Parse(result.Output); var e = json.RootElement;
                _ghPreview.Text = e.GetProperty("title").GetString() + "\n" + e.GetProperty("url").GetString() + "\n\n" + e.GetProperty("body").GetString();
            })));
        panel.Children.Add(_ghPreview); panel.Children.Add(_checks);
        panel.Children.Add(_prTitle); panel.Children.Add(_prBody);
        panel.Children.Add(ProjectPluginUi.Actions(_baseBranch,
            B("GitHubCreateDraft", "draft", "创建 PR 草稿", async ct =>
            {
                var url = await _client.CreateDraft(RequireState(), _prTitle.Text ?? "", _prBody.Text ?? "", _baseBranch.Text ?? "", ct);
                _ghPreview.Text = url; _prTitle.Text = ""; _prBody.Text = ""; await LoadGitHub(false, ct);
            })));
        panel.Children.Add(ProjectPluginUi.Actions(_issueBranch,
            B("GitHubIssueBranch", "develop", "从所选 Issue 创建分支", async ct =>
            {
                if (!_issues || _github.SelectedItem is not GitHubItem issue) throw new InvalidOperationException("Select an issue first.");
                if (string.IsNullOrWhiteSpace(_issueBranch.Text) || string.IsNullOrWhiteSpace(_baseBranch.Text)) throw new InvalidOperationException("Branch name and base branch are required.");
                await _client.IssueBranch(RequireState().Root, issue.Number, _issueBranch.Text, _baseBranch.Text, ct); await Refresh(ct);
            })));
        return new ScrollViewer { Content = panel };
    }
    private void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) throw new InvalidOperationException("Invalid page URL");
        ProjectPluginUi.OpenPath(url, false);
    }
    private async Task LoadGitHub(bool issues, CancellationToken ct)
    {
        var items = await _client.ListGitHub(RequireState().Root, issues, ct);
        _issues = issues; _github.ItemsSource = items; _ghPreview.Text = ""; _checks.ItemsSource = null;
    }
    private GitFileRow SelectedFile() => _files.SelectedItem as GitFileRow ?? throw new InvalidOperationException(_ui.T("selectfile", "请先选择文件。"));
    private GitSnapshot RequireState() => _state ?? throw new InvalidOperationException(_ui.T("norepo", "未打开 Git 仓库，可选择目录或在此初始化。"));
    private string Remote() => _remotes.SelectedItem as string ?? throw new InvalidOperationException(_ui.T("needremote", "请选择远端并填写目标分支。"));
    private string Target() => !string.IsNullOrWhiteSpace(_targetBranch.Text) ? _targetBranch.Text.Trim() : throw new InvalidOperationException(_ui.T("needremote", "请选择远端并填写目标分支。"));
    private async Task Commit(bool push, CancellationToken ct)
    {
        var state = RequireState();
        var remote = push ? Remote() : ""; var target = push ? Target() : "";
        await _client.Commit(state.Root, _message.Text ?? "", ct); _message.Text = "";
        if (push) await _client.Push(state, remote, target, ct);
        await Refresh(ct);
    }
    private async Task OpenTerminal(CancellationToken ct)
    {
        var context = _context!; var path = _directory;
        var id = await context.Host.CreateSessionAsync(new(Name: System.IO.Path.GetFileName(path), WorkingDirectory: path));
        if (_context is not null && id is { } session) context.Host.ActivateSession(session);
    }
    private async Task Refresh(CancellationToken ct)
    {
        _path.Text = _directory;
        var state = await _client.Read(_directory, ct);
        if (_state?.Root != state.Root)
        {
            _github.ItemsSource = null; _checks.ItemsSource = null; _ghPreview.Text = "";
            _message.Text = ""; _prTitle.Text = ""; _prBody.Text = ""; _baseBranch.Text = _options.DefaultBaseBranch; _issueBranch.Text = "";
        }
        _state = state; _directory = System.IO.Path.GetFullPath(state.Root); _path.Text = _directory;
        var remote = _remotes.SelectedItem as string;
        _changingItems = true;
        try
        {
            _branches.ItemsSource = state.Branches; _branches.SelectedItem = state.Branch;
            _remotes.ItemsSource = state.Remotes;
            _remotes.SelectedItem = state.Remotes.Contains(remote) ? remote : state.Remotes.Contains(state.UpstreamRemote) ? state.UpstreamRemote
                : state.Remotes.Contains(_options.DefaultRemote) ? _options.DefaultRemote : state.Remotes.FirstOrDefault();
            _targetBranch.Text = Equals(_remotes.SelectedItem, state.UpstreamRemote) && state.UpstreamBranch.Length > 0 ? state.UpstreamBranch : state.Branch;
            _files.ItemsSource = FileRows(state).ToArray(); _diff.Text = "";
            _history.ItemsSource = await _client.History(state, ct, _options.HistoryLimit); _historyPreview.Text = "";
        }
        finally { _changingItems = false; }
        RenderMeta();
    }
    private IEnumerable<GitFileRow> FileRows(GitSnapshot state)
    {
        foreach (var c in state.Changes.Where(c => c.Conflict)) yield return new(c, false, _ui.T("conflict", "冲突"));
        foreach (var c in state.Changes.Where(c => c.Staged)) yield return new(c, true, _ui.T("staged", "已暂存"));
        foreach (var c in state.Changes.Where(c => c.Unstaged)) yield return new(c, false, _ui.T("unstaged", "未暂存"));
        foreach (var c in state.Changes.Where(c => c.Untracked)) yield return new(c, false, _ui.T("untracked", "未跟踪"));
    }
    private void RenderMeta()
    {
        if (_state is null) { _meta.Text = _ui.T("norepo", "未打开 Git 仓库，可选择目录或在此初始化。"); _staged.Text = ""; _conflicts.Text = ""; return; }
        var state = _state;
        ToolTip.SetTip(_path, state.Root);
        _meta.Text = (state.Branch.Length > 0 ? state.Branch : _ui.T("detached", "游离 HEAD"))
            + (state.UpstreamRemote.Length > 0 ? " → " + state.UpstreamRemote + "/" + state.UpstreamBranch : "")
            + (state.AheadBehind.Length > 0 ? " · " + _ui.T("ahead", "领先／落后") + " " + state.AheadBehind.Replace('\t', '/') : "");
        _staged.Text = _ui.T("stagedfiles", "本次提交文件") + ": " + string.Join(", ", state.Changes.Where(c => c.Staged).Select(c => c.Path));
        _conflicts.Text = state.Changes.Any(c => c.Conflict) ? _ui.T("conflicthelp", "冲突文件可用默认编辑器打开；处理后暂存，再到终端继续 merge／rebase。") : "";
    }
    private async void Start(Func<CancellationToken, Task> action)
    {
        if (_busy || _context is null) return;
        var context = _context; _operation = CancellationTokenSource.CreateLinkedTokenSource(context.Lifetime); SetBusy(true);
        _status.Text = _ui.T("running", "操作进行中…");
        try { await action(_operation.Token); if (_context is not null) _status.Text = _ui.T("done", "操作完成"); }
        catch (OperationCanceledException) { if (_context is not null) _status.Text = _ui.T("canceled", "操作已取消，请刷新查看实际状态。"); }
        catch (Exception ex)
        {
            if (_context is not null)
            {
                _status.Text = ex.Message;
                // A failed pull can leave conflict files, and a failed push can follow a successful commit.
                try { if (_state is not null && Directory.Exists(_directory)) await Refresh(context.Lifetime); else ClearRepository(); }
                catch { ClearRepository(); }
            }
        }
        finally
        {
            _operation.Dispose(); _operation = null;
            if (_context is not null)
            {
                SetBusy(false);
                var refresh = _pendingRefresh; _pendingRefresh = false;
                if (_pendingDirectory is { } path) { _pendingDirectory = null; LoadDirectory(path); }
                if (!_busy && refresh && _state is not null) Start(Refresh);
            }
        }
    }
    private void SetBusy(bool busy)
    {
        _busy = busy; foreach (var control in _interactive) control.IsEnabled = !busy; _cancel.IsEnabled = busy;
    }
    private void LoadDirectory(string path)
    {
        if (_page is null || _context is null) return;
        if (_busy) { _pendingDirectory = path; return; }
        if (path.Length == 0) { _directory = ""; _path.Text = ""; ClearRepository(); _status.Text = _ui.T("remote", "当前为远程会话，请选择一个本地项目目录。"); return; }
        path = System.IO.Path.GetFullPath(path);
        if (string.Equals(path, _directory, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) && _state is not null) return;
        _directory = path;
        Start(Refresh);
    }
    private void Follow()
    {
        if (_page is null || _context is null) return;
        if (!_options.Follow && _options.PinnedDirectories.TryGetValue(_context.Host.ActiveWorkspaceId, out var pinned)) { LoadDirectory(pinned); return; }
        var selected = (_context.Host as IProjectWorkbenchHost)?.SelectedProjectDirectory;
        if (_options.Follow && selected is not null) { LoadDirectory(selected); return; }
        FollowSession();
    }
    private void FollowSession()
    {
        if (_context is null) return;
        var session = ProjectPluginUi.Active(_context);
        if (session?.IsRemote == true) { LoadDirectory(""); return; }
        var path = session?.WorkingDirectory;
        LoadDirectory(!string.IsNullOrWhiteSpace(path) ? path : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }
    private void Observe(WorkbenchEvent e)
    {
        if (_context is null) return;
        if (e.Kind == WorkbenchEventKind.LanguageChanged) { _ui.Translate(); if (_page is not null) Translate(); return; }
        if (_page is null) return;
        if (e.Kind == WorkbenchEventKind.WorkspaceChanged) { Follow(); return; }
        if (!_options.Follow) return;
        if (e.Kind == WorkbenchEventKind.ProjectDirectoryChanged && e.Data is string path) LoadDirectory(path);
        else if (e.Kind is WorkbenchEventKind.ActiveSessionChanged or WorkbenchEventKind.SessionCwdChanged
            && (e.SessionId is null || e.SessionId == _context.Host.ActiveSessionId)) FollowSession();
        else if (_options.RefreshOnCommandCompleted && e.Kind == WorkbenchEventKind.CommandCompleted && e.SessionId == _context.Host.ActiveSessionId)
        {
            if (_busy) _pendingRefresh = true; else Start(Refresh);
        }
    }
    private void SaveOptions() => _context?.SaveConfiguration(_options);
    private void ClearRepository()
    {
        _state = null; _files.ItemsSource = null; _history.ItemsSource = null; _diff.Text = ""; _historyPreview.Text = "";
        _branches.ItemsSource = null; _remotes.ItemsSource = null; _targetBranch.Text = "";
        _github.ItemsSource = null; _checks.ItemsSource = null; _ghPreview.Text = ""; RenderMeta();
    }
    private void Translate()
    {
        _ui.Translate(); _follow.Content = _ui.T("follow", "跟随项目／活动终端");
        foreach (var (editor, key, fallback) in new[] { (_message, "message", "提交信息（仅提交已暂存文件）"), (_branchName, "branchname", "新分支名称"),
            (_targetBranch, "target", "远端目标分支"), (_prTitle, "title", "PR 标题"), (_prBody, "body", "PR 描述"),
            (_baseBranch, "base", "目标／基础分支"), (_issueBranch, "issuebranch", "Issue 分支名称") }) editor.Watermark = _ui.T(key, fallback);
        RenderMeta();
        if (_state is not null) { _changingItems = true; _files.ItemsSource = FileRows(_state).ToArray(); _changingItems = false; }
    }
    public void Deactivate() { _operation?.Cancel(); _context = null; }
}
