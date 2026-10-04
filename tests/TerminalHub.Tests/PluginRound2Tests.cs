using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TerminalHub.App.Plugins;
using TerminalHub.Core.Pty;
using TerminalHub.Extensibility;
using Xunit;

namespace TerminalHub.Tests;

public class PluginRound2Tests
{
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static T Named<T>(Control root, string name) where T : Control => root.GetLogicalDescendants().OfType<T>().First(c => c.Name == name);
    private static void Click(Control root, string name)
        => Named<Button>(root, name).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    private static PluginEntry Import(PluginManager manager, string name)
    {
        manager.Import(Path.GetDirectoryName(Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "OfficialPlugins", name), "plugin.json", SearchOption.AllDirectories).Single())!);
        var plugin = manager.Plugins.Last(); Assert.True(plugin.Enabled, plugin.Error); return plugin;
    }
    private static Control Page(PluginManager manager, PluginEntry plugin) => manager.Modules.Single(m => m.Owner == plugin.Manifest.Id).GetView();

    // ---------- declarative script plugins ----------

    [Fact]
    public void Expand_ReplacesAllPlaceholders()
    {
        var run = ManifestPluginHarness.Expand("echo {cwd}|{session}|{workspace}|{dir}", "C:\\w", "sess", "ws", "C:\\p");
        Assert.Equal("echo C:\\w|sess|ws|C:\\p", run);
    }

    private static class ManifestPluginHarness
    {
        // ManifestPlugin lives in the App assembly internals; reach it via the
        // loaded type instead of InternalsVisibleTo.
        public static string Expand(string run, string cwd, string session, string workspace, string dir)
        {
            var type = typeof(PluginManager).Assembly.GetType("TerminalHub.App.Plugins.ManifestPlugin")!;
            return (string)type.GetMethod("Expand", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [run, cwd, session, workspace, dir])!;
        }
    }

    [AvaloniaFact]
    public async Task ScriptPlugin_CommandsRegisterAndSpawnSession()
    {
        using var f = new StageLayoutTests.StageFixture(); await f.ReadyAsync();
        var root = Path.Combine(Path.GetTempPath(), "terminalhub-script-" + Guid.NewGuid());
        using var manager = new PluginManager(f.Vm, root, f.Window.Styles);
        try
        {
            var dir = Path.Combine(root, "scripted"); Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "plugin.json"), JsonSerializer.Serialize(new PluginManifest
            {
                Id = "x.script", Name = "Script probe",
                Commands = [new() { Id = "go", Title = "Go", Run = "git status {cwd}" }]
            }));
            manager.Discover();
            var plugin = manager.Plugins.Single(p => p.Manifest.Id == "x.script");
            manager.Enable(plugin);
            Assert.True(plugin.Enabled, plugin.Error);
            Assert.True(plugin.IsScript);
            var (_, command) = Assert.Single(manager.Commands, c => c.Owner == "x.script");

            var before = f.Vm.SessionCards.Count;
            await command.Execute();
            for (var i = 0; i < 50 && f.Vm.SessionCards.Count <= before; i++) await Task.Delay(100);
            Assert.True(f.Vm.SessionCards.Count > before, "command should spawn a session");
            var spawned = f.Vm.SessionCards.Last().Model;
            Assert.Equal("cmd.exe", spawned.Shell);
            Assert.Contains("git status", spawned.ShellArguments);
            Assert.Contains(spawned.WorkingDirectory, spawned.ShellArguments); // {cwd} expanded
        }
        finally { manager.Dispose(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task ScriptPlugin_ManifestWithoutEntryOrCommands_IsRejected()
    {
        using var f = new StageLayoutTests.StageFixture(); await f.ReadyAsync();
        var root = Path.Combine(Path.GetTempPath(), "terminalhub-script-bad-" + Guid.NewGuid());
        using var manager = new PluginManager(f.Vm, root, f.Window.Styles);
        try
        {
            var dir = Path.Combine(root, "empty"); Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "plugin.json"),
                JsonSerializer.Serialize(new PluginManifest { Id = "x.empty", Name = "Empty" }));
            manager.Discover();
            var plugin = manager.Plugins.Single(p => p.Directory == dir);
            Assert.NotEqual("", plugin.Error);
        }
        finally { manager.Dispose(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    // ---------- hot reload ----------

    [AvaloniaFact]
    public async Task Reload_ReEnablesDllPlugin_AndScriptPluginIsSkipped()
    {
        using var f = new StageLayoutTests.StageFixture(); await f.ReadyAsync();
        var manager = Field<PluginManager>(f.Window, "_plugins");
        try
        {
            var plugin = Import(manager, "WorkspaceNotes");
            Assert.True(plugin.Enabled);
            manager.Reload(plugin);
            Assert.True(plugin.Enabled, plugin.Error);
            Assert.True(f.Vm.PluginPreferences[plugin.Manifest.Id].Enabled);

            var script = new PluginEntry(new PluginManifest { Id = "x.script", Commands = [new() { Id = "a", Run = "echo" }] }, plugin.Directory);
            manager.Reload(script); // no-op for scripts — must not throw
        }
        finally { foreach (var p in manager.Plugins.ToArray()) manager.Remove(p); }
    }

    // ---------- official plugins ----------

    [AvaloniaFact]
    public async Task Broadcast_SendsToEveryRunningSessionInWorkspace()
    {
        using var f = new StageLayoutTests.StageFixture(); await f.ReadyAsync();
        var manager = Field<PluginManager>(f.Window, "_plugins");
        try
        {
            var plugin = Import(manager, "TerminalBroadcast");
            var page = Page(manager, plugin);
            Named<TextBox>(page, "BroadcastInput").Text = "broadcast-me";
            Click(page, "BroadcastSend");
            foreach (var card in f.Vm.SessionCards)
                Assert.Contains("broadcast-me", ((MockPtySession)card.Model.Pty).RawInput.ToString());
        }
        finally { foreach (var p in manager.Plugins.ToArray()) manager.Remove(p); }
    }

    [AvaloniaFact]
    public async Task Snippets_SaveApplyRemove_PersistsConfiguration()
    {
        using var f = new StageLayoutTests.StageFixture(); await f.ReadyAsync();
        var manager = Field<PluginManager>(f.Window, "_plugins");
        try
        {
            var plugin = Import(manager, "Snippets");
            var page = Page(manager, plugin);
            Named<TextBox>(page, "SnippetName").Text = "go build";
            Named<TextBox>(page, "SnippetCommand").Text = "go build ./...";
            Click(page, "SnippetAdd");
            var active = f.Vm.ActiveCard!.Model;
            Click(page, "SnippetApply");
            Assert.Contains("go build ./...", ((MockPtySession)active.Pty).RawInput.ToString());
            var config = JsonDocument.Parse(f.Vm.PluginPreferences[plugin.Manifest.Id].Configuration);
            Assert.Equal("go build ./...", config.RootElement.GetProperty("Items")[0].GetProperty("Command").GetString());
            Click(page, "SnippetRemove");
            config = JsonDocument.Parse(f.Vm.PluginPreferences[plugin.Manifest.Id].Configuration);
            Assert.Equal(0, config.RootElement.GetProperty("Items").GetArrayLength());
        }
        finally { foreach (var p in manager.Plugins.ToArray()) manager.Remove(p); }
    }
}
