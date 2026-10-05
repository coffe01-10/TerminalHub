using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using TerminalHub.App.Plugins;
using TerminalHub.Core.Pty;
using TerminalHub.Extensibility;
using TerminalHub.Official.TaskRunner;
using Xunit;

namespace TerminalHub.Tests;

public class TaskRunnerPluginTests
{
    private const string PackageJson = """
        {
          "scripts": {
            "build": "tsc -p .",
            "test app": "node test.js\n--watch",
            "count": 1
          }
        }
        """;

    private const string Makefile = """
        .PHONY: all
        all: build
        	echo skipped
        build:
        	echo build
        pattern%:
        	echo no
        VAR = value
        # comment
        """;

    private const string Justfile = """
        set shell := ["bash", "-cu"]
        default:
            echo hi
        test-app:
            echo test
        alias b := build
        """;

    private const string TasksJson = """
        {
          "tasks": [
            { "label": "compile", "type": "shell", "command": "dotnet\nbuild", "args": ["${workspaceFolder}/src"], "options": { "cwd": "src" } },
            { "label": "proc", "type": "process", "command": "dotnet" },
            { "label": "ref", "type": "shell", "command": { "value": "echo" } },
            { "label": "rooted", "type": "shell", "command": "echo", "args": ["hi"], "options": { "cwd": "/tmp/work" } }
          ]
        }
        """;

    [Fact]
    public void Parse_PackageJson_RunsNpm_AndSkipsNonStrings()
    {
        var tasks = TaskSources.ParsePackageJson(PackageJson, @"C:\proj");
        Assert.Equal(2, tasks.Count);
        Assert.Equal(TaskSourceKind.PackageJson, tasks[0].Source);
        Assert.Equal("npm run build", TaskSources.CommandFor(tasks[0]));
        Assert.Equal(@"C:\proj", tasks[0].WorkingDirectory);
        Assert.Equal("npm run \"test app\" --watch", TaskSources.FoldNewlines(TaskSources.CommandFor(tasks[1])));
        Assert.DoesNotContain("node test.js", TaskSources.CommandFor(tasks[1]));
    }

    [Fact]
    public void Parse_Makefile_TakesLeadingTargets_AndSkipsPhonyAndPatterns()
    {
        var tasks = TaskSources.ParseMakefile(Makefile, @"C:\proj");
        Assert.Equal(["all", "build"], tasks.Select(t => t.Name).ToArray());
        Assert.Equal("make all", TaskSources.CommandFor(tasks[0]));
        Assert.Equal("make build", TaskSources.CommandFor(tasks[1]));
        Assert.All(tasks, t => Assert.Equal(TaskSourceKind.Makefile, t.Source));
    }

    [Fact]
    public void Parse_Justfile_TakesLeadingRecipes()
    {
        var tasks = TaskSources.ParseJustfile(Justfile, @"C:\proj");
        Assert.Equal(["default", "test-app"], tasks.Select(t => t.Name).ToArray());
        Assert.Equal("just default", TaskSources.CommandFor(tasks[0]));
        Assert.Equal("just test-app", TaskSources.CommandFor(tasks[1]));
    }

    [Fact]
    public void Parse_VsCode_KeepsShellStrings_ExpandsFolder_AndJoinsRelativeCwd()
    {
        var tasks = TaskSources.ParseVsCodeTasks(TasksJson, @"C:\proj");
        Assert.Equal(2, tasks.Count);
        Assert.Equal("compile", tasks[0].Name);
        Assert.Equal(Path.GetFullPath(Path.Combine(@"C:\proj", "src")), tasks[0].WorkingDirectory);
        Assert.Equal(@"dotnet build C:\proj/src", TaskSources.FoldNewlines(tasks[0].Command));
        Assert.DoesNotContain("\n", TaskSources.FoldNewlines(tasks[0].Command));
        Assert.Equal("/tmp/work", tasks[1].WorkingDirectory);
        Assert.Equal("echo hi", tasks[1].Command);
    }

    [Fact]
    public void OneShotSession_NoOuterQuotesOnWindows_CmdSlashSCannotUnbalanceInnerQuotes()
    {
        // cmd /s strips the first and last quote of the whole string; wrapping a command that
        // itself contains quotes would leave `npm run "test app --watch` (missing closer).
        var (cmd, cmdArgs) = TaskSources.OneShotSession("npm run \"test app\"\n--watch", windows: true);
        Assert.Equal("cmd.exe", cmd);
        Assert.Equal("/d /s /c npm run \"test app\" --watch", cmdArgs);
        Assert.Equal(2, cmdArgs.Count(c => c == '"'));
        var (sh, shArgs) = TaskSources.OneShotSession("echo it's\nhere", windows: false);
        Assert.Equal("sh", sh);
        Assert.Equal("-c 'echo it'\"'\"'s here'", shArgs);
    }

    [Fact]
    public void FoldNewlines_CollapsesBlankLines()
    {
        Assert.Equal("a b c", TaskSources.FoldNewlines("a\r\n\nb\rc"));
    }

    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static T Named<T>(Control root, string name) where T : Control => root.GetLogicalDescendants().OfType<T>().First(c => c.Name == name);
    private static void Click(Control root, string name) => Named<Button>(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static PluginEntry Import(PluginManager manager, string name)
    {
        manager.Import(Path.GetDirectoryName(Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "OfficialPlugins", name), "plugin.json", SearchOption.AllDirectories).Single())!);
        var plugin = manager.Plugins.Last(); Assert.True(plugin.Enabled, plugin.Error); return plugin;
    }
    private static Control Page(PluginManager manager, PluginEntry plugin) => manager.Modules.Single(m => m.Owner == plugin.Manifest.Id).GetView();

    [AvaloniaFact]
    public async Task Paste_SendsFoldedCommandWithoutSubmit()
    {
        using var f = new StageLayoutTests.StageFixture(); await f.ReadyAsync();
        var manager = Field<PluginManager>(f.Window, "_plugins");
        var root = Path.Combine(Path.GetTempPath(), "terminalhub-tasks-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "package.json"), PackageJson);
            var plugin = Import(manager, "TaskRunner");
            var page = Page(manager, plugin);
            Named<TextBox>(page, "TaskDirectory").Text = root;
            Click(page, "TaskRefresh");
            var pty = (MockPtySession)f.Vm.ActiveCard!.Model.Pty;
            pty.RawInput.Clear();
            Click(page, "TaskRun1");
            var sent = pty.RawInput.ToString();
            Assert.Contains("npm run \"test app\" --watch", sent);
            Assert.DoesNotContain("\r", sent);
            Assert.DoesNotContain("node test.js", sent);
        }
        finally { foreach (var p in manager.Plugins.ToArray()) manager.Remove(p); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task NewSession_UsesOneShotShell_AndDoesNotSendInput()
    {
        using var f = new StageLayoutTests.StageFixture(); await f.ReadyAsync();
        var manager = Field<PluginManager>(f.Window, "_plugins");
        var root = Path.Combine(Path.GetTempPath(), "terminalhub-tasks-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "Makefile"), Makefile);
            var plugin = Import(manager, "TaskRunner");
            var page = Page(manager, plugin);
            Named<TextBox>(page, "TaskDirectory").Text = root;
            Named<CheckBox>(page, "TaskPaste").IsChecked = false;
            Click(page, "TaskRefresh");
            var before = f.Vm.SessionCards.Count;
            Click(page, "TaskRun0");
            for (var i = 0; i < 50 && f.Vm.SessionCards.Count <= before; i++) await Task.Delay(100);
            Assert.True(f.Vm.SessionCards.Count > before);
            var spawned = f.Vm.SessionCards.Last().Model;
            Assert.Equal(OperatingSystem.IsWindows() ? "cmd.exe" : "sh", spawned.Shell);
            Assert.Contains("make all", spawned.ShellArguments);
            Assert.Equal(Path.GetFullPath(root), Path.GetFullPath(spawned.WorkingDirectory));
            Assert.Equal("", ((MockPtySession)spawned.Pty).RawInput.ToString());
            var config = JsonDocument.Parse(f.Vm.PluginPreferences[plugin.Manifest.Id].Configuration);
            Assert.False(config.RootElement.GetProperty("Paste").GetBoolean());
        }
        finally { foreach (var p in manager.Plugins.ToArray()) manager.Remove(p); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task MissingDirectory_ShowsStatus_AndOneBadFileDoesNotHideOthers()
    {
        using var f = new StageLayoutTests.StageFixture(); await f.ReadyAsync();
        var manager = Field<PluginManager>(f.Window, "_plugins");
        var root = Path.Combine(Path.GetTempPath(), "terminalhub-tasks-" + Guid.NewGuid());
        try
        {
            var plugin = Import(manager, "TaskRunner");
            var page = Page(manager, plugin);
            Named<TextBox>(page, "TaskDirectory").Text = root;
            Click(page, "TaskRefresh");
            Assert.Contains("目录不存在", Named<TextBlock>(page, "TaskStatus").Text);
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "package.json"), "{ not json");
            await File.WriteAllTextAsync(Path.Combine(root, "justfile"), Justfile);
            Click(page, "TaskRefresh");
            Click(page, "TaskRun0");
            Assert.Contains("just default", ((MockPtySession)f.Vm.ActiveCard!.Model.Pty).RawInput.ToString());
        }
        finally { foreach (var p in manager.Plugins.ToArray()) manager.Remove(p); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task WorkspaceChanged_ReResolvesDirectory_InsteadOfKeepingTheOldProject()
    {
        using var f = new StageLayoutTests.StageFixture(); await f.ReadyAsync();
        var manager = Field<PluginManager>(f.Window, "_plugins");
        var first = Path.Combine(Path.GetTempPath(), "terminalhub-tasks-a-" + Guid.NewGuid());
        var second = Path.Combine(Path.GetTempPath(), "terminalhub-tasks-b-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(second);
            var plugin = Import(manager, "TaskRunner");
            var page = Page(manager, plugin);
            var box = Named<TextBox>(page, "TaskDirectory");
            // Pin a directory for the current workspace, then switch away and back.
            box.Text = first;
            Click(page, "TaskRefresh");
            await f.Vm.NewProjectWorkspaceCommand.ExecuteAsync(null);
            // The new workspace has no pin; follow resolves to the active terminal's cwd or
            // empties the field, but must not keep the previous project's directory.
            var landed = box.Text ?? "";
            Assert.True(landed.Length == 0 || Path.GetFullPath(landed) != Path.GetFullPath(first),
                "a workspace switch must not keep the previous project's directory");
        }
        finally { foreach (var p in manager.Plugins.ToArray()) manager.Remove(p); if (Directory.Exists(first)) Directory.Delete(first, true); if (Directory.Exists(second)) Directory.Delete(second, true); }
    }
}
