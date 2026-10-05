using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using TerminalHub.App.Plugins;
using TerminalHub.Core.Pty;
using TerminalHub.Core.Sessions;
using TerminalHub.Extensibility;
using TerminalHub.App.Views;
using TerminalHub.Pty;
using TerminalHub.Official.GitWorkbench;
using Xunit;

namespace TerminalHub.Tests;

public sealed class GitPluginRepository : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "terminalhub-git-" + Guid.NewGuid());
    public string Work => Path.Combine(Root, "项目 [work]");
    public GitWorkbenchClient Client { get; } = new();
    public GitPluginRepository() { Directory.CreateDirectory(Work); }
    public async Task Initialize()
    {
        (await Client.Git(Work, default, "init", "--initial-branch=main")).RequireSuccess();
        await Identity(Work);
    }
    public async Task Identity(string path)
    {
        (await Client.Git(path, default, "config", "user.name", "TerminalHub regression")).RequireSuccess();
        (await Client.Git(path, default, "config", "user.email", "terminalhub@example.invalid")).RequireSuccess();
        (await Client.Git(path, default, "config", "pull.rebase", "false")).RequireSuccess();
    }
    public async Task Commit(string name, string text, string message, string? directory = null)
    {
        var root = directory ?? Work;
        await File.WriteAllTextAsync(Path.Combine(root, name), text);
        await Client.Stage(root, null, default); await Client.Commit(root, message, default);
    }
    public void Dispose()
    {
        // Git marks loose objects read-only on Windows; clear that attribute in this fixture only.
        foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
        Directory.Delete(Root, true);
    }
}

public class GitWorkbenchClientTests
{
    [Fact]
    public void PorcelainZ_PreservesUnicodeSpacesRenameAndTwoAreas_AndClassifiesConflicts()
    {
        var changes = GitWorkbenchClient.ParseStatus("MM 中文 [1].txt\0R  new -> name.txt\0old name.txt\0?? -new.txt\0UU conflict.txt\0");
        Assert.Equal(4, changes.Count); Assert.True(changes[0].Staged); Assert.True(changes[0].Unstaged);
        Assert.Equal("中文 [1].txt", changes[0].Path); Assert.Equal("new -> name.txt", changes[1].Path);
        Assert.Equal("old name.txt", changes[1].OriginalPath); Assert.True(changes[2].Untracked);
        Assert.True(changes[3].Conflict); Assert.False(changes[3].Staged);
    }

    [Fact]
    public async Task RealGit_UnbornUnstageLiteralPathsRenameAndMultilineCommit()
    {
        using var f = new GitPluginRepository(); await f.Initialize(); var client = f.Client;
        var name = "中文 [1] & dollar.txt";
        await File.WriteAllTextAsync(Path.Combine(f.Work, name), "first\n");
        var state = await client.Read(f.Work, default); Assert.False(state.HasHead); Assert.Equal("main", state.Branch);
        await client.Stage(f.Work, Assert.Single(state.Changes), default);
        state = await client.Read(f.Work, default); Assert.True(Assert.Single(state.Changes).Staged);
        await client.Unstage(state, null, default); Assert.True(File.Exists(Path.Combine(f.Work, name)));
        Assert.True(Assert.Single((await client.Read(f.Work, default)).Changes).Untracked);
        await client.Stage(f.Work, null, default);
        var message = "中文提交 `literal` $literal & quotes\n\nSecond paragraph";
        await client.Commit(f.Work, message, default);
        Assert.Equal(message, (await client.Git(f.Work, default, "log", "-1", "--format=%B")).Output.TrimEnd('\r', '\n'));
        await File.WriteAllTextAsync(Path.Combine(f.Work, name), "staged\n"); await client.Stage(f.Work, null, default);
        await File.WriteAllTextAsync(Path.Combine(f.Work, name), "unstaged\n");
        var mixed = Assert.Single((await client.Read(f.Work, default)).Changes);
        Assert.True(mixed.Staged && mixed.Unstaged);
        Assert.Contains("+staged", await client.Diff(f.Work, mixed, true, default));
        Assert.Contains("+unstaged", await client.Diff(f.Work, mixed, false, default));
        await client.Stage(f.Work, null, default); await client.Commit(f.Work, "content", default);
        (await client.Git(f.Work, default, "mv", "--", name, "renamed [2].txt")).RequireSuccess();
        state = await client.Read(f.Work, default); var renamed = Assert.Single(state.Changes);
        Assert.Equal(name, renamed.OriginalPath); await client.Unstage(state, renamed, default);
        Assert.True(File.Exists(Path.Combine(f.Work, "renamed [2].txt")));
        await client.Stage(f.Work, null, default); await client.Commit(f.Work, "rename", default);
        Assert.Empty((await client.Read(f.Work, default)).Changes);
    }

    [Fact]
    public async Task RealGit_LocalBareRemotePushFetchPullRejectionAndConflict()
    {
        using var f = new GitPluginRepository(); await f.Initialize(); var client = f.Client;
        var remote = Path.Combine(f.Root, "remote.git"); var clone = Path.Combine(f.Root, "other");
        (await client.Git(f.Root, default, "init", "--bare", "--initial-branch=main", remote)).RequireSuccess();
        (await client.Git(f.Work, default, "remote", "add", "origin", remote)).RequireSuccess();
        await f.Commit("file.txt", "initial\n", "initial");
        await client.Push(await client.Read(f.Work, default), "origin", "main", default);
        var pushed = await client.Read(f.Work, default); Assert.Equal("origin", pushed.UpstreamRemote); Assert.Equal("main", pushed.UpstreamBranch);
        (await client.Git(f.Root, default, "clone", remote, clone)).RequireSuccess(); await f.Identity(clone);
        await f.Commit("file.txt", "remote\n", "remote", clone);
        await client.Push(await client.Read(clone, default), "origin", "main", default);
        await client.Fetch(f.Work, "origin", default); await client.Pull(f.Work, "origin", "main", default);
        Assert.Equal("remote", (await File.ReadAllTextAsync(Path.Combine(f.Work, "file.txt"))).TrimEnd('\r', '\n'));
        await f.Commit("file.txt", "local diverged\n", "local committed");
        await f.Commit("file.txt", "remote diverged\n", "remote diverged", clone);
        await client.Push(await client.Read(clone, default), "origin", "main", default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.Push(clientState(), "origin", "main", default));
        var local = await client.Read(f.Work, default); Assert.Empty(local.Changes);
        Assert.Equal("local committed", (await client.History(local, default))[0].Subject);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.Pull(f.Work, "origin", "main", default));
        Assert.True(Assert.Single((await client.Read(f.Work, default)).Changes).Conflict);
        GitSnapshot clientState() => new(f.Work, "main", true, ["main"], ["origin"], "origin", "main", "", []);
    }

    [Fact]
    public async Task RealGit_WorktreeAndDetachedHeadAreRecognized_AndNonRepoFailsClearly()
    {
        using var f = new GitPluginRepository(); await f.Initialize();
        await f.Commit("file.txt", "text", "initial");
        var worktree = Path.Combine(f.Root, "linked worktree");
        (await f.Client.Git(f.Work, default, "worktree", "add", "-b", "feature", worktree)).RequireSuccess();
        var state = await f.Client.Read(worktree, default); Assert.Equal("feature", state.Branch); Assert.Equal(Path.GetFullPath(worktree), Path.GetFullPath(state.Root));
        (await f.Client.Git(worktree, default, "switch", "--detach", "HEAD")).RequireSuccess();
        state = await f.Client.Read(worktree, default); Assert.Equal("", state.Branch); Assert.True(state.HasHead);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Client.Read(f.Root, default));
    }

    [Fact]
    public async Task GitHub_ReplaysListsPendingAndFailedChecksDraftStdinAndIssueBranch_WithoutPublishing()
    {
        var calls = new List<(string[] Args, string? Input)>();
        var client = new GitWorkbenchClient((exe, args, dir, ct, input) =>
        {
            var a = args.ToArray(); calls.Add((a, input));
            var output = a.Contains("checks") ? "[{\"name\":\"build\",\"state\":\"IN_PROGRESS\",\"bucket\":\"pending\",\"link\":\"https://github.com/example/repo/actions\"}]"
                : a.Contains("list") ? "[{\"number\":7,\"title\":\"中文 title\",\"url\":\"https://github.com/example/repo/pull/7\",\"state\":\"OPEN\"}]" : "https://github.com/example/repo/pull/8";
            return Task.FromResult(new TerminalHub.Official.CommandResult(exe, a, dir, a.Contains("checks") ? 8 : 0, output, ""));
        });
        Assert.Equal(7, Assert.Single(await client.ListGitHub("repo", false, default)).Number);
        Assert.Equal("pending", Assert.Single(await client.Checks("repo", "7", default)).Bucket);
        var body = "line 1\nline 2 `literal` $(literal)";
        var state = new GitSnapshot("repo", "feature", true, ["feature"], ["origin"], "origin", "feature", "", []);
        await client.CreateDraft(state, "Title", body, "main", default);
        Assert.Equal(body, calls.Last().Input); Assert.Contains("--head", calls.Last().Args); Assert.Contains("--body-file", calls.Last().Args);
        await client.IssueBranch("repo", 7, "issue-7", "main", default);
        Assert.Equal(new[] { "issue", "develop", "7", "--name", "issue-7", "--base", "main", "--checkout" }, calls.Last().Args);
        var failed = new GitWorkbenchClient((exe, args, dir, ct, input) => Task.FromResult(
            new TerminalHub.Official.CommandResult(exe, args.ToArray(), dir, 1, "[{\"name\":\"build\",\"state\":\"FAILURE\",\"bucket\":\"fail\",\"link\":\"\"}]", "")));
        Assert.Equal("fail", Assert.Single(await failed.Checks("repo", null, default)).Bucket);
    }

    [Fact]
    public async Task OwnedProcess_CancellationEndsLongRunningCommand()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TerminalHub.Official.PluginProcess.RunAsync(
            OperatingSystem.IsWindows() ? "powershell.exe" : "sh",
            OperatingSystem.IsWindows() ? new[] { "-NoProfile", "-Command", "Start-Sleep -Seconds 30" } : ["-c", "sleep 30"],
            Environment.CurrentDirectory, cancel.Token));
    }
}

public class ProjectGitPluginUiTests
{
    private static T Named<T>(Control root, string name) where T : Control => root.GetLogicalDescendants().OfType<T>().First(c => c.Name == name);
    private static void Click(Control page, string name) => Named<Button>(page, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static PluginManager Manager(StageLayoutTests.StageFixture f)
        => (PluginManager)f.Window.GetType().GetField("_plugins", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f.Window)!;
    private static PluginEntry Import(PluginManager manager, string name)
    {
        manager.Import(Path.GetDirectoryName(Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "OfficialPlugins", name), "plugin.json", SearchOption.AllDirectories).Single())!);
        var plugin = manager.Plugins.Last(); Assert.True(plugin.Enabled, plugin.Error); return plugin;
    }
    private static Control Page(PluginManager manager, PluginEntry plugin) => manager.Modules.Single(m => m.Owner == plugin.Manifest.Id && m.Definition.Surface == ExtensionSurface.WorkspaceTools).GetView();
    private static async Task Until(Func<bool> predicate, string message)
    {
        var deadline = Environment.TickCount64 + 12000;
        while (!predicate() && Environment.TickCount64 < deadline) await Task.Delay(25);
        Assert.True(predicate(), message);
    }
    private static Task GitReady(Control page) => Until(() => Named<Button>(page, "GitRefresh").IsEnabled, "Git operation did not finish: " + Named<TextBlock>(page, "GitStatus").Text);

    [AvaloniaFact]
    public async Task Navigator_ActualDllBookmarksFollowNewTerminalAndRemoteIsolation_PersistsOnDisable()
    {
        using var repo = new GitPluginRepository(); using var f = new StageLayoutTests.StageFixture(); await f.ReadyAsync();
        var manager = Manager(f);
        try
        {
            var plugin = Import(manager, "ProjectNavigator"); var page = Page(manager, plugin);
            Named<CheckBox>(page, "ProjectFollow").IsChecked = false;
            Named<TextBox>(page, "ProjectPath").Text = repo.Work; Click(page, "ProjectGo");
            await Until(() => Named<TextBlock>(page, "ProjectStatus").Text == repo.Work, "Folder navigation did not finish");
            Named<TextBox>(page, "ProjectName").Text = "我的项目"; Named<TextBox>(page, "ProjectGroup").Text = "开发"; Click(page, "ProjectSave");
            Assert.Equal(1, Named<ListBox>(page, "ProjectBookmarks").ItemCount);
            var before = f.Vm.SessionCards.Count; Click(page, "ProjectNewTerminal");
            await Until(() => f.Vm.SessionCards.Count > before, "New terminal not created");
            Assert.Equal(repo.Work, f.Vm.ActiveSession!.WorkingDirectory);
            Named<CheckBox>(page, "ProjectFollow").IsChecked = true;
            var child = Path.Combine(repo.Work, "子目录 [test]"); Directory.CreateDirectory(child);
            f.Vm.ActiveSession.Emulator.Parser.Feed("\x1b]9;9;" + child + "\x07");
            await Until(() => Named<TextBox>(page, "ProjectPath").Text == child, "CWD event did not update navigator");
            f.Vm.ActiveSession.Tag = SessionTag.Ssh;
            f.Vm.ActiveSession.Emulator.Parser.Feed("\x1b]9;9;/remote/project\x07");
            await Task.Delay(60); Assert.Equal(child, Named<TextBox>(page, "ProjectPath").Text);
            Click(page, "ProjectCd"); Assert.Contains("远程", Named<TextBlock>(page, "ProjectStatus").Text);
            manager.Disable(plugin); manager.Enable(plugin); Assert.True(plugin.Enabled, plugin.Error);
            page = Page(manager, plugin); Assert.Equal(1, Named<ListBox>(page, "ProjectBookmarks").ItemCount);
            Assert.All(f.Vm.SessionCards, s => Assert.True(s.Model.IsRunning)); Assert.Empty(manager.LastError);
        }
        finally { foreach (var plugin in manager.Plugins.ToArray()) manager.Remove(plugin); }
    }

    [AvaloniaFact]
    public async Task Host_DirectoryCommandUsesLiteralPathWaitsForShellReport_AndRejectsBusyRemoteAndAlternate()
    {
        using var repo = new GitPluginRepository(); using var f = new StageLayoutTests.StageFixture(); await f.ReadyAsync();
        var session = f.Vm.ActiveSession!; session.Shell = "pwsh.exe";
        session.Emulator.Parser.Feed("\x1b]9;9;" + Environment.CurrentDirectory + "\x07");
        var host = (IProjectWorkbenchHost)f.Vm; var input = ((MockPtySession)session.Pty).RawInput;
        Assert.True(host.ChangeSessionDirectory(session.Id, repo.Work)); Assert.Contains("Set-Location -LiteralPath", input.ToString());
        Assert.Contains("[work]", input.ToString()); Assert.NotEqual(repo.Work, session.WorkingDirectory);
        session.Emulator.Parser.Feed("\x1b]133;C\x07"); var before = input.Length;
        Assert.False(host.ChangeSessionDirectory(session.Id, repo.Work)); Assert.Equal(before, input.Length);
        session.Emulator.Parser.Feed("\x1b]133;D;0\x07\x1b[?1049h"); Assert.False(host.ChangeSessionDirectory(session.Id, repo.Work));
        session.Emulator.Parser.Feed("\x1b[?1049l"); session.Tag = SessionTag.Ssh; Assert.False(host.ChangeSessionDirectory(session.Id, repo.Work));
        Assert.True(((IWorkbenchHost)f.Vm).Sessions.Single(s => s.Id == session.Id).IsRemote);
    }

    [AvaloniaFact]
    public async Task Git_ActualDllStageDiffCommitHistoryAndNonRepo_LeavesUserRepositoryUntouched()
    {
        using var repo = new GitPluginRepository(); await repo.Initialize();
        await File.WriteAllTextAsync(Path.Combine(repo.Work, "中文 [file].txt"), "hello\n");
        using var f = new StageLayoutTests.StageFixture(); await f.ReadyAsync(); var manager = Manager(f);
        try
        {
            var plugin = Import(manager, "GitWorkbench"); var page = Page(manager, plugin); await GitReady(page);
            Named<TextBox>(page, "GitPath").Text = repo.Work; Click(page, "GitOpen"); await GitReady(page);
            Assert.Equal(1, Named<ListBox>(page, "GitFiles").ItemCount);
            Named<ListBox>(page, "GitFiles").SelectedIndex = 0; await GitReady(page);
            Assert.Contains("hello", Named<TextBox>(page, "GitDiff").Text);
            Click(page, "GitStageAll"); await GitReady(page); Assert.Contains("已暂存", Named<ListBox>(page, "GitFiles").Items[0]!.ToString());
            Named<TextBox>(page, "GitMessage").Text = "从插件提交中文文件"; Click(page, "GitCommit"); await GitReady(page);
            Assert.Equal(0, Named<ListBox>(page, "GitFiles").ItemCount); Assert.Equal("", Named<TextBox>(page, "GitMessage").Text);
            Assert.Equal(1, Named<ListBox>(page, "GitHistory").ItemCount);
            Named<ListBox>(page, "GitHistory").SelectedIndex = 0; await GitReady(page);
            Assert.Contains("从插件提交中文文件", Named<TextBox>(page, "GitHistoryPreview").Text);
            manager.Disable(plugin); manager.Enable(plugin); page = Page(manager, plugin); await GitReady(page);
            Assert.Equal(repo.Work.Replace('\\', '/'), Named<TextBox>(page, "GitPath").Text?.Replace('\\', '/'));
            Named<TextBox>(page, "GitPath").Text = repo.Root; Click(page, "GitOpen"); await GitReady(page);
            Assert.Contains("未打开", Named<TextBlock>(page, "GitRepositoryInfo").Text);
            Assert.Contains("not a git repository", Named<TextBlock>(page, "GitStatus").Text);
            Assert.Empty(manager.LastError); Assert.True(f.Vm.ActiveSession!.IsRunning);
        }
        finally { foreach (var plugin in manager.Plugins.ToArray()) manager.Remove(plugin); }
    }

    [AvaloniaFact]
    public async Task Navigator_ImportExportGroupingReorderSearchAndWorkspaceSelection()
    {
        using var repo = new GitPluginRepository(); using var f = new StageLayoutTests.StageFixture(); await f.ReadyAsync();
        var second = Path.Combine(repo.Root, "second"); Directory.CreateDirectory(second); var manager = Manager(f);
        try
        {
            var plugin = Import(manager, "ProjectNavigator"); var page = Page(manager, plugin);
            var instance = typeof(PluginEntry).GetField("Instance", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(plugin)!;
            instance.GetType().GetMethod("ImportBookmarks")!.Invoke(instance, [JsonSerializer.Serialize(new[]
            {
                new { Name = "项目一", Path = repo.Work, Group = "开发" }, new { Name = "项目二", Path = second, Group = "测试" }
            })]);
            var list = Named<ListBox>(page, "ProjectBookmarks"); Assert.Equal(2, list.ItemCount);
            list.SelectedIndex = 1; Click(page, "ProjectMoveUp"); Assert.Contains("项目二", list.Items[0]!.ToString());
            Named<TextBox>(page, "ProjectName").Text = "改名后的项目二"; Click(page, "ProjectUpdate");
            var exported = (string)instance.GetType().GetMethod("ExportBookmarks")!.Invoke(instance, null)!;
            using var json = JsonDocument.Parse(exported); Assert.Equal(second, json.RootElement[0].GetProperty("Path").GetString());
            Assert.Equal("改名后的项目二", json.RootElement[0].GetProperty("Name").GetString());
            Named<TextBox>(page, "ProjectSearch").Text = "开发";
            await Until(() => list.ItemCount == 1, "Bookmark search did not update");
            Named<CheckBox>(page, "ProjectFollow").IsChecked = false;
            async Task Go(string path)
            {
                Named<TextBox>(page, "ProjectPath").Text = path; Click(page, "ProjectGo");
                await Until(() => Named<TextBlock>(page, "ProjectStatus").Text == path, "Folder navigation did not finish");
            }
            await Go(repo.Root); await Go(repo.Work);
            var projectHost = (IProjectWorkbenchHost)f.Vm; var workspace = f.Vm.ActiveWorkspace;
            projectHost.SelectProjectDirectory(repo.Work); await f.Vm.NewProjectWorkspaceCommand.ExecuteAsync(null);
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            await Until(() => Named<TextBlock>(page, "ProjectStatus").Text == home, "New workspace did not load its own folder");
            await Go(second); Assert.Equal(second, projectHost.SelectedProjectDirectory);
            Click(page, "ProjectBack");
            await Until(() => Named<TextBlock>(page, "ProjectStatus").Text == home, "Back did not return to this workspace's folder");
            Assert.False(Named<Button>(page, "ProjectBack").IsEnabled); // Must not reach the other workspace's history.
            Click(page, "ProjectForward");
            await Until(() => Named<TextBlock>(page, "ProjectStatus").Text == second, "Forward did not restore this workspace's folder");
            ((IWorkbenchHost)f.Vm).SwitchWorkspace(workspace.Id); Assert.Equal(repo.Work, projectHost.SelectedProjectDirectory);
            await Until(() => Named<TextBlock>(page, "ProjectStatus").Text == repo.Work, "Original workspace folder was not restored");
            Click(page, "ProjectBack");
            await Until(() => Named<TextBlock>(page, "ProjectStatus").Text == repo.Root, "Original workspace history was overwritten");
            Assert.Equal(2, JsonDocument.Parse(f.Vm.PluginPreferences[plugin.Manifest.Id].Configuration).RootElement.GetProperty("Bookmarks").GetArrayLength());
        }
        finally { foreach (var plugin in manager.Plugins.ToArray()) manager.Remove(plugin); }
    }

    [AvaloniaFact]
    public async Task Git_CommitPushFailureKeepsCommitAndAllowsRetry_AndFollowRemoteClearsLocalRepository()
    {
        using var repo = new GitPluginRepository(); await repo.Initialize();
        var missing = Path.Combine(repo.Root, "missing.git");
        (await repo.Client.Git(repo.Work, default, "remote", "add", "origin", missing)).RequireSuccess();
        await File.WriteAllTextAsync(Path.Combine(repo.Work, "file.txt"), "text");
        using var f = new StageLayoutTests.StageFixture(); await f.ReadyAsync(); var manager = Manager(f);
        try
        {
            ((IProjectWorkbenchHost)f.Vm).SelectProjectDirectory(repo.Work);
            var plugin = Import(manager, "GitWorkbench"); var page = Page(manager, plugin); await GitReady(page);
            Click(page, "GitStageAll"); await GitReady(page);
            Named<TextBox>(page, "GitMessage").Text = "commit survives failed push"; Click(page, "GitCommitPush"); await GitReady(page);
            Assert.Equal("", Named<TextBox>(page, "GitMessage").Text); Assert.Equal(0, Named<ListBox>(page, "GitFiles").ItemCount);
            Assert.Contains("does not appear to be a git repository", Named<TextBlock>(page, "GitStatus").Text);
            Assert.Contains("Exit code: 128", Named<TextBox>(page, "GitOutput").Text);
            (await repo.Client.Git(repo.Root, default, "init", "--bare", "--initial-branch=main", missing)).RequireSuccess();
            Click(page, "GitPush"); await GitReady(page);
            Assert.Equal("commit survives failed push", (await repo.Client.Git(missing, default, "log", "-1", "--format=%s")).RequireSuccess().Output.Trim());
            f.Vm.ActiveSession!.Tag = SessionTag.Ssh;
            f.Vm.ActiveSession.Emulator.Parser.Feed("\x1b]9;9;/remote\x07");
            await Until(() => Named<TextBox>(page, "GitPath").Text == "", "Remote selection retained local repository");
            Assert.Equal(0, Named<ListBox>(page, "GitFiles").ItemCount); Assert.Contains("远程", Named<TextBlock>(page, "GitStatus").Text);
        }
        finally { foreach (var plugin in manager.Plugins.ToArray()) manager.Remove(plugin); }
    }

    [AvaloniaTheory]
    [InlineData(0)] [InlineData(3)]
    public async Task Pages_EnglishAtMinimumToolsWindow_KeepActionsInsideContent(int theme)
    {
        using var repo = new GitPluginRepository(); await repo.Initialize(); await repo.Commit("file.txt", "hello", "initial");
        using var f = new StageLayoutTests.StageFixture(); await f.ReadyAsync(); var manager = Manager(f);
        f.Vm.ThemeIndex = theme; f.Vm.LanguageIndex = 2;
        try
        {
            ((IProjectWorkbenchHost)f.Vm).SelectProjectDirectory(repo.Work); f.Vm.ActiveSession!.WorkingDirectory = repo.Work;
            Import(manager, "ProjectNavigator"); var git = Import(manager, "GitWorkbench"); await GitReady(Page(manager, git));
            f.Window.GetType().GetMethod("ToggleProjectTools", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(f.Window, null);
            var tools = (ProjectToolsWindow)f.Window.GetType().GetField("_projectToolsWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Window)!;
            tools.Width = tools.MinWidth; tools.Height = tools.MinHeight;
            var view = (ProjectToolsView)tools.Content!; var nav = view.FindControl<ListBox>("ToolNavigation")!;
            foreach (var module in manager.Modules.Where(m => m.Definition.Surface == ExtensionSurface.WorkspaceTools && m.Owner is "official.project-navigator" or "official.git-workbench"))
            {
                nav.SelectedItem = nav.Items.Cast<ListBoxItem>().Single(i => Equals(i.Tag, module.Id)); await Task.Delay(80);
                var page = module.GetView();
                var scroll = ((Grid)page).Children.OfType<ScrollViewer>().Single();
                Assert.True(scroll.Extent.Height > scroll.Viewport.Height, "Page must scroll at minimum window height");
                foreach (var button in page.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b.Bounds.Width > 0))
                {
                    var point = button.TranslatePoint(default, tools)!.Value;
                    Assert.True(point.X >= 0 && point.X + button.Bounds.Width <= tools.Bounds.Width + 1, "Action overflows minimum tool width: " + button.Name);
                }
                if (Environment.GetEnvironmentVariable("TERMINALHUB_PROJECT_PLUGIN_CAPTURES") is { } captures)
                {
                    Directory.CreateDirectory(captures); tools.CaptureRenderedFrame()!.Save(Path.Combine(captures, $"{module.Definition.Id}-{theme}.png"));
                    scroll.Offset = new Vector(0, scroll.Extent.Height); await Task.Delay(50);
                    tools.CaptureRenderedFrame()!.Save(Path.Combine(captures, $"{module.Definition.Id}-{theme}-scrolled.png"));
                }
                Assert.True(Named<ListBox>(page, module.Definition.Id == "git" ? "GitFiles" : "ProjectFolders").Bounds.Height >= 100,
                    "Directory and change lists must have usable height");
            }
            Assert.Empty(manager.LastError);
        }
        finally { foreach (var plugin in manager.Plugins.ToArray()) manager.Remove(plugin); f.Vm.LanguageIndex = 0; }
    }
}

[Collection("ProcessWide")]
public class ProjectPluginNativeTests
{
    [AvaloniaFact]
    public async Task RealConPty_LiteralChineseDirectoryAndLocalGitCommitPush()
    {
        if (!OperatingSystem.IsWindows() || !InlineDataIfShellInstalledAttribute.OnPath("pwsh")) return;
        using var repo = new GitPluginRepository(); await repo.Initialize();
        var target = Path.Combine(repo.Work, "中文 [目录] 'quote"); Directory.CreateDirectory(target);
        var bare = Path.Combine(repo.Root, "remote.git");
        (await repo.Client.Git(repo.Root, default, "init", "--bare", "--initial-branch=main", bare)).RequireSuccess();
        (await repo.Client.Git(repo.Work, default, "remote", "add", "origin", bare)).RequireSuccess();
        await File.WriteAllTextAsync(Path.Combine(target, "native.txt"), "native");
        using var fixture = new StageLayoutTests.StageFixture(); await fixture.ReadyAsync();
        var host = (IWorkbenchHost)fixture.Vm;
        var ids = new[] { -10, -11, -12 }; var handles = ids.Select(GetStdHandle).ToArray();
        Guid? id;
        try
        {
            PtySessionFactory.UseMock = false;
            foreach (var std in ids) SetStdHandle(std, IntPtr.Zero);
            id = await host.CreateSessionAsync(new("Native project regression", repo.Work, "pwsh") { Transient = true });
        }
        finally
        {
            PtySessionFactory.UseMock = true;
            for (var i = 0; i < ids.Length; i++) SetStdHandle(ids[i], handles[i]);
        }
        Assert.NotNull(id); host.ActivateSession(id.Value);
        async Task Until(Func<bool> condition)
        {
            var deadline = Environment.TickCount64 + 20000;
            while (!condition() && Environment.TickCount64 < deadline) await Task.Delay(30);
            Assert.True(condition(), "Real ConPTY did not report the expected state");
        }
        await Until(() => host.Sessions.Single(s => s.Id == id).CanChangeDirectory);
        Assert.True(((IProjectWorkbenchHost)fixture.Vm).ChangeSessionDirectory(id.Value, target));
        await Until(() => host.Sessions.Single(s => s.Id == id).WorkingDirectory == target);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Vm.ActiveSession!.Emulator.CommandCompleted += _ => completed.TrySetResult();
        host.SendInput(id.Value, "git add -- .; git commit -m 'native ConPTY'; git push --set-upstream origin main; Write-Host 'native-git-complete'", true);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await Until(() => host.Sessions.Single(s => s.Id == id).CanChangeDirectory);
        Assert.Equal("native ConPTY", (await repo.Client.Git(bare, default, "log", "-1", "--format=%s")).RequireSuccess().Output.Trim());
        Assert.True(fixture.Vm.ActiveSession!.ExcludeFromWorkspace);
    }
    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int id);
    [DllImport("kernel32.dll")] private static extern bool SetStdHandle(int id, IntPtr handle);
}
