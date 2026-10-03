using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using TerminalHub.App.Plugins;
using TerminalHub.App.Views;
using TerminalHub.Core.Terminal;
using TerminalHub.Official.ScreenClips;
using Xunit;

namespace TerminalHub.Tests;

public class OfficialPluginTests
{
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static void Call(object owner, string name) => owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, null);
    private static T Named<T>(Control root, string name) where T : Control => root.GetLogicalDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Click(Control root, string name) => Named<Button>(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static string Text(Control root) => string.Join("\n", root.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text));
    private static PluginEntry Import(PluginManager manager, string name)
    {
        manager.Import(Path.GetDirectoryName(Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "OfficialPlugins", name), "plugin.json", SearchOption.AllDirectories).Single())!);
        var plugin = manager.Plugins.Last(); Assert.True(plugin.Enabled, plugin.Error); return plugin;
    }
    private static Control Page(PluginManager manager, PluginEntry plugin) => manager.Modules.Single(m => m.Owner == plugin.Manifest.Id).GetView();

    [Fact]
    public void ScreenText_PreservesWideClustersIndentationAndPhysicalRows_WithoutPaddingDuplicates()
    {
        using var terminal = new TerminalEmulator(columns: 24, rows: 5);
        terminal.Parser.Feed("  中e\u0301👩‍💻🇨🇳\r\n\r\nend  ");
        var text = ScreenClipsPlugin.ExtractText(terminal.Buffer.CaptureFrame());
        Assert.Equal("  中e\u0301👩‍💻🇨🇳" + Environment.NewLine + Environment.NewLine + "end", text);
    }

    [Fact]
    public void ScreenText_DoesNotRevealConcealedScreenCharacters()
    {
        using var terminal = new TerminalEmulator(columns: 24, rows: 2);
        terminal.Parser.Feed("a\x1b[8mhidden中\x1b[28mz");
        Assert.Equal("a        z", ScreenClipsPlugin.ExtractText(terminal.Buffer.CaptureFrame()));
    }

    [AvaloniaFact]
    public async Task Notes_SwitchAndDisableBeforeAutosave_KeepWorkspaceTextAndRestoreConfiguration()
    {
        using var f = new StageLayoutTests.StageFixture(); await Task.Delay(650);
        var manager = Field<PluginManager>(f.Window, "_plugins");
        try
        {
            var plugin = Import(manager, "WorkspaceNotes"); var page = Page(manager, plugin);
            var editor = Named<TextBox>(page, "NotesEditor"); var first = f.Vm.ActiveWorkspace;
            editor.Text = "第一项目：保留中文、原始 command/path";
            await f.Vm.NewProjectWorkspaceCommand.ExecuteAsync(null); await Task.Delay(60);
            Assert.Equal("", editor.Text); editor.Text = "第二项目：另一个笔记";
            f.Vm.SwitchProjectWorkspace(first); await Task.Delay(60);
            Assert.Equal("第一项目：保留中文、原始 command/path", editor.Text);
            f.Vm.LanguageIndex = 2; Assert.Equal("第一项目：保留中文、原始 command/path", editor.Text);
            Assert.Equal("Save notes", Named<Button>(page, "SaveNotes").Content);
            editor.Text = "停用前最后输入"; manager.Disable(plugin);
            var config = JsonDocument.Parse(f.Vm.PluginPreferences[plugin.Manifest.Id].Configuration);
            Assert.Equal("停用前最后输入", config.RootElement.GetProperty("Notes").GetProperty(first.Id).GetString());
            Assert.Contains(config.RootElement.GetProperty("Notes").EnumerateObject(), p => p.Value.GetString() == "第二项目：另一个笔记");
            manager.Enable(plugin); Assert.True(plugin.Enabled, plugin.Error);
            Assert.Equal("停用前最后输入", Named<TextBox>(Page(manager, plugin), "NotesEditor").Text);
            Assert.All(f.Vm.SessionCards, c => Assert.True(c.Model.IsRunning)); Assert.Empty(manager.LastError);
        }
        finally { foreach (var plugin in manager.Plugins.ToArray()) manager.Remove(plugin); f.Vm.LanguageIndex = 0; }
    }

    [AvaloniaFact]
    public async Task Clips_ActualDllCapture_SelectNewestLimitTo20AndCopy_ClearOnDisable()
    {
        using var f = new StageLayoutTests.StageFixture(); await Task.Delay(650);
        var manager = Field<PluginManager>(f.Window, "_plugins");
        try
        {
            var plugin = Import(manager, "ScreenClips");
            Call(f.Window, "ToggleProjectTools"); var tools = Field<ProjectToolsWindow>(f.Window, "_projectToolsWindow");
            var view = Assert.IsType<ProjectToolsView>(tools.Content); var module = manager.Modules.Single(m => m.Owner == plugin.Manifest.Id);
            var navigation = view.FindControl<ListBox>("ToolNavigation")!;
            navigation.SelectedItem = navigation.Items.Cast<ListBoxItem>().Single(i => Equals(i.Tag, module.Id)); await Task.Delay(80);
            var page = Page(manager, plugin); var terminal = f.Vm.ActiveSession!.Emulator;
            for (var i = 0; i < 22; i++)
            {
                terminal.Parser.Feed($"\x1b[2J\x1b[H摘录{i}👩‍💻");
                await manager.Commands.Single(c => c.Owner == plugin.Manifest.Id).Command.Execute();
            }
            Assert.Equal(20, Named<ListBox>(page, "ClipList").ItemCount);
            Assert.NotNull(Named<ListBox>(page, "ClipList").SelectedItem);
            Assert.Equal("摘录21👩‍💻", Named<TextBox>(page, "ClipPreview").Text);
            Named<ListBox>(page, "ClipList").SelectedIndex = 19;
            Assert.Equal("摘录2👩‍💻", Named<TextBox>(page, "ClipPreview").Text);
            Click(page, "CopyClip"); await Task.Delay(30);
            Assert.Equal("摘录2👩‍💻", await tools.Clipboard!.GetTextAsync());
            Click(page, "ClearClips"); Assert.Equal(0, Named<ListBox>(page, "ClipList").ItemCount);
            Assert.False(Named<Button>(page, "CopyClip").IsEnabled);
            await manager.Commands.Single(c => c.Owner == plugin.Manifest.Id).Command.Execute();
            manager.Disable(plugin); manager.Enable(plugin); Assert.True(plugin.Enabled, plugin.Error);
            Assert.Equal(0, Named<ListBox>(Page(manager, plugin), "ClipList").ItemCount);
            Assert.Empty(manager.LastError); Assert.True(f.Vm.ActiveSession.IsRunning);
        }
        finally { foreach (var plugin in manager.Plugins.ToArray()) manager.Remove(plugin); }
    }

    [AvaloniaFact]
    public async Task Watch_UsesActualShellMarkers_DistinguishesFailureUnknownAndNoMarker_AndFiltersWorkspaces()
    {
        using var f = new StageLayoutTests.StageFixture(); await Task.Delay(650);
        var manager = Field<PluginManager>(f.Window, "_plugins");
        try
        {
            var plugin = Import(manager, "CommandWatch"); var page = Page(manager, plugin);
            var session = f.Vm.ActiveSession!;
            session.Emulator.Parser.Feed("error: arbitrary output"); Assert.Contains("尚未收到命令标记", Text(page)); Assert.DoesNotContain("失败", Text(page));
            session.Emulator.Parser.Feed("\x1b]133;C\x07"); Assert.Contains("执行中", Text(page));
            session.Emulator.Parser.Feed("\x1b]133;D;7\x07"); Assert.Contains("失败", Text(page)); Assert.Contains("退出码 7", Text(page));
            session.Emulator.Parser.Feed("\x1b]133;C\x07\x1b]133;D\x07"); Assert.Contains("退出码未知", Text(page));
            session.Emulator.Parser.Feed("\x1b]133;C\x07\x1b]133;D;0\x07"); Assert.Contains("退出码 0", Text(page));
            var firstName = session.Name; await f.Vm.NewProjectWorkspaceCommand.ExecuteAsync(null); await Task.Delay(60);
            Assert.DoesNotContain(firstName, Text(page)); Named<CheckBox>(page, "WorkspaceFilter").IsChecked = false;
            Assert.Contains(firstName, Text(page)); Assert.Contains("false", f.Vm.PluginPreferences[plugin.Manifest.Id].Configuration);
            f.Vm.LanguageIndex = 2; Assert.Contains("Exit code 0", Text(page));
            manager.Disable(plugin); manager.Enable(plugin); Assert.True(plugin.Enabled, plugin.Error);
            var restored = Page(manager, plugin); Assert.False(Named<CheckBox>(restored, "WorkspaceFilter").IsChecked);
            Assert.Contains("No command marker yet", Text(restored)); Assert.DoesNotContain("Exit code 0", Text(restored)); Assert.True(session.IsRunning);
            Assert.Empty(manager.LastError);
        }
        finally { foreach (var plugin in manager.Plugins.ToArray()) manager.Remove(plugin); f.Vm.LanguageIndex = 0; }
    }

    [AvaloniaTheory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task OfficialPages_FitMinimumToolsWindowAndEnglish_AndUseDynamicThemes(int theme)
    {
        using var f = new StageLayoutTests.StageFixture(); await Task.Delay(650);
        f.Vm.ThemeIndex = theme; f.Vm.LanguageIndex = 2;
        var manager = Field<PluginManager>(f.Window, "_plugins");
        try
        {
            foreach (var name in new[] { "WorkspaceNotes", "ScreenClips", "CommandWatch" }) Import(manager, name);
            Call(f.Window, "ToggleProjectTools"); var tools = Field<ProjectToolsWindow>(f.Window, "_projectToolsWindow");
            tools.Width = tools.MinWidth; tools.Height = tools.MinHeight;
            var view = Assert.IsType<ProjectToolsView>(tools.Content); var nav = view.FindControl<ListBox>("ToolNavigation")!;
            foreach (var module in manager.Modules.Where(m => m.Owner.StartsWith("official.")).ToArray())
            {
                nav.SelectedItem = nav.Items.Cast<ListBoxItem>().Single(i => Equals(i.Tag, module.Id)); await Task.Delay(80);
                var page = module.GetView(); Assert.True(page.Bounds.Width > 250);
                Assert.True(nav.Bounds.Width >= 180); // 112px split English names into three-character fragments.
                Assert.False(view.FindControl<Grid>("ToolsSaveBar")!.IsVisible); // Built-in save action does not save plugin contents.
                foreach (var button in page.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible))
                {
                    var point = button.TranslatePoint(default, tools)!.Value;
                    Assert.True(point.X >= 0 && point.X + button.Bounds.Width <= tools.Bounds.Width, "Plugin action must fit at minimum width.");
                }
                if (Environment.GetEnvironmentVariable("TERMINALHUB_OFFICIAL_PLUGIN_CAPTURES") is { } captures)
                { Directory.CreateDirectory(captures); tools.CaptureRenderedFrame()!.Save(Path.Combine(captures, $"{module.Definition.Id}-{theme}.png")); }
            }
            Call(f.Window, "OpenPluginManager"); var window = Field<PluginManagerWindow>(f.Window, "_pluginManagerWindow");
            window.Width = window.MinWidth; window.Height = window.MinHeight; await Task.Delay(80);
            Assert.Equal("Developer guide ↗", window.FindControl<Button>("PluginDevelopmentLink")!.Content);
            Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "docs/plugins/index.html")));
            window.Close(); Assert.Empty(manager.LastError);
        }
        finally { foreach (var plugin in manager.Plugins.ToArray()) manager.Remove(plugin); f.Vm.LanguageIndex = 0; f.Window.Close(); await Task.Delay(100); }
    }
}
