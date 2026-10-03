using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TerminalHub.App.Plugins;
using TerminalHub.Extensibility;
using Xunit;

namespace TerminalHub.Tests;

public class PluginReviewFixTests
{
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static PluginEntry Import(PluginManager manager, string name)
    {
        manager.Import(Path.GetDirectoryName(Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "OfficialPlugins", name), "plugin.json", SearchOption.AllDirectories).Single())!);
        var plugin = manager.Plugins.Last(); Assert.True(plugin.Enabled, plugin.Error); return plugin;
    }

    [AvaloniaFact]
    public async Task Import_OverwriteDropsStaleFiles_KeepsUserSettings()
    {
        using var f = new StageLayoutTests.StageFixture(); await Task.Delay(650);
        // Sources stay outside the plugin root so Discover cannot pick them up.
        var root = Path.Combine(Path.GetTempPath(), "terminalhub-plugin-overwrite-" + Guid.NewGuid());
        var build = Path.Combine(Path.GetTempPath(), "terminalhub-plugin-overwrite-src-" + Guid.NewGuid());
        using var manager = new PluginManager(f.Vm, root, f.Window.Styles);
        try
        {
            string Source(string version, string? dependency)
            {
                var source = Path.Combine(build, version);
                Directory.CreateDirectory(source);
                File.Copy(typeof(WorkbenchProbePlugin).Assembly.Location, Path.Combine(source, "probe.dll"));
                if (dependency is not null) File.WriteAllText(Path.Combine(source, dependency), "old build");
                File.WriteAllText(Path.Combine(source, "plugin.json"), JsonSerializer.Serialize(
                    new PluginManifest { Id = "overwrite.probe", Name = "Overwrite probe", Entry = "probe.dll", EntryType = typeof(WorkbenchProbePlugin).FullName! }));
                return source;
            }
            manager.Import(Source("v1", "StaleDependency.dll"));
            var installed = Path.Combine(root, "overwrite.probe");
            Assert.True(File.Exists(Path.Combine(installed, "StaleDependency.dll")));
            File.WriteAllText(Path.Combine(installed, "settings.json"), "{\"user\":true}");
            manager.Import(Source("v2", null));
            Assert.False(File.Exists(Path.Combine(installed, "StaleDependency.dll")), "overwrite install must drop files the new build no longer ships");
            Assert.True(File.Exists(Path.Combine(installed, "settings.json")), "user settings inside the plugin directory must survive an overwrite");
            Assert.True(File.Exists(Path.Combine(installed, "probe.dll")));
            var plugin = manager.Plugins.Single(p => p.Manifest.Id == "overwrite.probe");
            Assert.True(plugin.Enabled, plugin.Error); Assert.Empty(manager.LastError);
        }
        finally
        {
            manager.Dispose();
            foreach (var directory in new[] { root, build }) if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [AvaloniaFact]
    public async Task ClosingWorkspace_RemovesItsNote_OtherNotesAndSessionsSurvive()
    {
        using var f = new StageLayoutTests.StageFixture(); await Task.Delay(650);
        var manager = Field<PluginManager>(f.Window, "_plugins");
        try
        {
            var plugin = Import(manager, "WorkspaceNotes");
            var editor = manager.Modules.Single(m => m.Owner == plugin.Manifest.Id).GetView()
                .GetLogicalDescendants().OfType<TextBox>().Single(t => t.Name == "NotesEditor");
            var keep = f.Vm.ActiveWorkspace;
            editor.Text = "留在配置里的工作区笔记";
            await f.Vm.NewProjectWorkspaceCommand.ExecuteAsync(null); await Task.Delay(60);
            editor.Text = "即将删除的工作区笔记";
            var doomed = f.Vm.ActiveWorkspace;
            f.Vm.CloseProjectWorkspaceCommand.Execute(doomed); await Task.Delay(80);
            var notes = JsonDocument.Parse(f.Vm.PluginPreferences[plugin.Manifest.Id].Configuration).RootElement.GetProperty("Notes");
            Assert.False(notes.TryGetProperty(doomed.Id, out _), "a deleted workspace must not keep its note in settings");
            Assert.Equal("留在配置里的工作区笔记", notes.GetProperty(keep.Id).GetString());
            Assert.Equal("留在配置里的工作区笔记", editor.Text);
            Assert.All(f.Vm.SessionCards, c => Assert.True(c.Model.IsRunning));
            Assert.Empty(manager.LastError);
        }
        finally { foreach (var p in manager.Plugins.ToArray()) manager.Remove(p); }
    }
}
