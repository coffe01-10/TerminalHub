using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TerminalHub.App.Controls;
using TerminalHub.App.Plugins;
using TerminalHub.App.ViewModels;
using TerminalHub.App.Views;
using TerminalHub.Core.Settings;
using TerminalHub.Extensibility;
using Xunit;

namespace TerminalHub.Tests;

public class WorkbenchExtensionAcceptanceTests
{
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static void Call(object owner, string name) => owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, null);
    private static TerminalView[] Views(MainWindow window) => window.FindControl<ContentControl>("SplitHost")!.GetVisualDescendants().OfType<TerminalView>().ToArray();
    [AvaloniaFact]
    public async Task SixPaneTree_RatioMaximizeRemoveUndoAndWorkspaceRoundtrip_KeepProcessesAndViews()
    {
        using var f = new StageLayoutTests.StageFixture(); await Task.Delay(650);
        await f.Vm.SetSplitLayoutAsync("Quad");
        await f.Vm.SplitPaneAsync(3, true); await f.Vm.SplitPaneAsync(4, false); await Task.Delay(120);
        Assert.Equal(6, f.Vm.PaneCount); Assert.Equal(6, Views(f.Window).Length);
        var sessions = f.Vm.PaneTree!.Leaves.Select(l => l.Session!).ToArray();
        var ptys = sessions.Select(s => s.Pty).ToArray(); var nativeViews = Views(f.Window).ToDictionary(v => v.Emulator!);
        f.Vm.FocusPane(5); await Task.Delay(50); f.Window.KeyTextInput("sixth-pane-input"); await Task.Delay(40);
        Assert.Contains("sixth-pane-input", sessions[5].Emulator.Buffer.TailText(50));
        Assert.DoesNotContain("sixth-pane-input", sessions[0].Emulator.Buffer.TailText(50));
        var branchId = f.Vm.PaneTree.Second!.Second!.Id;
        f.Vm.BeginLayoutGesture("调整分屏比例"); f.Vm.SetPaneRatio(f.Vm.PaneTree.Second.Second, .36); f.Vm.CompleteLayoutGesture();
        f.Vm.UndoLayout(); Assert.Equal(.5, f.Vm.PaneTree!.Second!.Second!.Ratio);
        f.Vm.RedoLayout(); Assert.Equal(.36, f.Vm.PaneTree!.Second!.Second!.Ratio);
        f.Vm.TogglePaneMaximizedCommand.Execute(null); await Task.Delay(50);
        Assert.Single(Views(f.Window)); Assert.Same(sessions[5].Emulator, Views(f.Window)[0].Emulator);
        f.Vm.TogglePaneMaximizedCommand.Execute(null); await Task.Delay(50);
        Assert.Equal(6, Views(f.Window).Length);
        f.Vm.RemoveFocusedPane(); Assert.Equal(5, f.Vm.PaneCount); Assert.True(sessions[5].IsRunning);
        f.Vm.UndoLayout(); Assert.Equal(6, f.Vm.PaneCount);
        var source = f.Vm.ActiveWorkspace;
        await f.Vm.NewProjectWorkspaceCommand.ExecuteAsync(null); await Task.Delay(100);
        f.Vm.SwitchProjectWorkspace(source); await Task.Delay(100);
        Assert.Equal(branchId, f.Vm.PaneTree!.Second!.Second!.Id); Assert.Equal(.36, f.Vm.PaneTree.Second.Second.Ratio);
        Assert.Equal(sessions, f.Vm.PaneTree.Leaves.Select(l => l.Session));
        Assert.All(Views(f.Window), v => Assert.Same(nativeViews[v.Emulator!], v));
        for (var i = 0; i < sessions.Length; i++) { Assert.Same(ptys[i], sessions[i].Pty); Assert.True(sessions[i].IsRunning); }
        var saved = f.Vm.Settings.ProjectWorkspaces.Single(w => w.Id == source.Id).Layout;
        var roundtrip = JsonSerializer.Deserialize<WorkspaceState>(JsonSerializer.Serialize(saved))!;
        var loaded = PaneNode.Load(roundtrip.PaneTree, i => source.Cards.ElementAtOrDefault(i)?.Model)!;
        Assert.Equal(sessions, loaded.Leaves.Select(l => l.Session)); Assert.Equal(.36, loaded.Second!.Second!.Ratio);
        var last = source.Cards.Single(c => c.Model == sessions[5]); f.Vm.RenameSession((last, "第六窗格")); await Task.Delay(40);
        Assert.Contains(f.Window.FindControl<ContentControl>("SplitHost")!.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "第六窗格");
    }

    [AvaloniaFact]
    public async Task NativePaneDrop_EdgeSplitsCenterSwaps_AndOneUndoRestoresGesture()
    {
        using var f = new StageLayoutTests.StageFixture(); await Task.Delay(650);
        await f.Vm.SetSplitLayoutAsync("Horizontal"); await Task.Delay(100);
        var original = f.Vm.PaneTree!.Leaves.Select(l => l.Session).ToArray(); var moving = f.Vm.SessionCards[3];
        var thumbnail = f.Window.GetVisualDescendants().OfType<StageCard>().Single(c => c.DataContext == moving);
        var start = thumbnail.TranslatePoint(new Point(thumbnail.Bounds.Width / 2, 20), f.Window)!.Value;
        var target = Views(f.Window).Single(v => v.Emulator == original[1]!.Emulator);
        var end = target.TranslatePoint(new Point(target.Bounds.Width - 10, target.Bounds.Height / 2), f.Window)!.Value;
        f.Window.MouseDown(start, MouseButton.Left); f.Window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        Assert.NotNull(Field<Border?>(f.Window, "_paneDropPreview"));
        f.Window.MouseUp(end, MouseButton.Left); await Task.Delay(150);
        Assert.Equal(3, f.Vm.PaneCount); Assert.Same(moving.Model, f.Vm.GetPane(2));
        f.Vm.UndoLayout(); Assert.Equal(original, f.Vm.PaneTree!.Leaves.Select(l => l.Session)); Assert.True(moving.Model.IsRunning);
        await f.Vm.DropSessionOnPaneAsync(f.Vm.SessionCards.Single(c => c.Model == original[0]), 1, "Center");
        Assert.Same(original[0], f.Vm.GetPane(1)); Assert.Same(original[1], f.Vm.GetPane(0));
        f.Vm.UndoLayout(); Assert.Equal(original, f.Vm.PaneTree!.Leaves.Select(l => l.Session));
    }

    [AvaloniaFact]
    public async Task EmbeddedTools_AllFivePagesKeepEditsAcrossSelectionCollapseAndWorkspaceSwitch()
    {
        using var f = new StageLayoutTests.StageFixture(); await Task.Delay(650);
        Call(f.Window, "ToggleProjectTools"); await Task.Delay(80);
        var manager = Field<PluginManager>(f.Window, "_plugins");
        var pages = manager.Modules.Where(m => m.Owner == "builtin").Select(m => m.GetView()).ToArray(); Assert.Equal(5, pages.Length);
        var list = Field<ListBox>(f.Window, "_toolModulesList"); var content = Field<ContentControl>(f.Window, "_toolModulePage");
        f.Vm.NewOutputRuleCommand.Execute(null); f.Vm.SelectedOutputRule!.Name = "保留规则"; f.Vm.SelectedOutputRule.Pattern = "error";
        f.Vm.NewProjectTaskCommand.Execute(null); f.Vm.SelectedProjectTask!.Command = "echo acceptance";
        for (var i = 0; i < 5; i++)
        {
            list.SelectedIndex = i; await Task.Delay(40);
            Assert.Same(pages[i], content.Content); Assert.True(pages[i].IsEffectivelyVisible); Assert.True(pages[i].Bounds.Width > 400);
            foreach (var card in pages[i].GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("card")))
                Assert.Equal(new CornerRadius(12), card.CornerRadius);
        }
        f.Window.CollapseProjectTools(); Assert.False(f.Window.FindControl<Border>("ProjectToolsRegion")!.IsVisible);
        Call(f.Window, "ToggleProjectTools"); Assert.Same(pages[4], content.Content);
        var source = f.Vm.ActiveWorkspace; await f.Vm.NewProjectWorkspaceCommand.ExecuteAsync(null); await Task.Delay(80);
        f.Vm.SwitchProjectWorkspace(source); await Task.Delay(80);
        Assert.Contains(f.Vm.OutputRules, r => r.Name == "保留规则" && r.Pattern == "error");
        Assert.Contains(f.Vm.ProjectTasks, t => t.Command == "echo acceptance");
        Assert.Equal(pages, manager.Modules.Where(m => m.Owner == "builtin").Select(m => m.GetView()));
    }

    [AvaloniaFact]
    public async Task PublishedExamples_EnableTogether_ConfigureTranslateSwitchAndDisableIndividually()
    {
        using var f = new StageLayoutTests.StageFixture(); await Task.Delay(650);
        var manager = Field<PluginManager>(f.Window, "_plugins"); var styles = f.Window.Styles.Count;
        var existing = f.Vm.SessionCards.Select(c => (c.Model, c.Model.Pty)).ToArray();
        try
        {
            foreach (var folder in new[] { "Minimal", "CompactSidebar", "SessionPanel" })
                manager.Import(Path.GetDirectoryName(Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "AcceptancePlugins", folder), "plugin.json", SearchOption.AllDirectories).Single())!);
            Assert.Equal(3, manager.Plugins.Count); Assert.All(manager.Plugins, p => { Assert.True(p.Enabled, p.Error); Assert.Empty(p.Error); });
            Assert.Equal(styles + 2, f.Window.Styles.Count); Assert.False(f.Window.FindControl<ListBox>("SessionShelf")!.IsVisible);
            var compact = manager.Modules.Single(m => m.Owner == "example.compact-sidebar"); var sidebar = Assert.IsType<ListBox>(compact.GetView());
            await Task.Delay(100);
            Assert.Equal(5, sidebar.ItemCount); sidebar.SelectedIndex = -1; sidebar.SelectedIndex = 1;
            var selectedName = sidebar.Items.Cast<object>().ElementAt(1).ToString();
            await Task.Delay(40);
            Assert.True(f.Vm.ActiveSession!.Name == selectedName, $"Sidebar selected {selectedName}, active {f.Vm.ActiveSession.Name}, error {manager.LastError}");
            Call(f.Window, "ToggleProjectTools");
            var overview = manager.Modules.Single(m => m.Owner == "example.session-panel" && m.Definition.Id == "sessions");
            var page = overview.GetView(); var config = manager.Modules.Single(m => m.Owner == "example.session-panel" && m.Definition.Surface == ExtensionSurface.Settings);
            var checkbox = Assert.IsType<CheckBox>(config.GetView()); checkbox.IsChecked = false;
            Assert.Contains("false", f.Vm.PluginPreferences["example.session-panel"].Configuration);
            f.Vm.LanguageIndex = 2; await Task.Delay(40); Assert.Equal("Session overview", overview.ToString()); Assert.Equal("Show directories", checkbox.Content);
            f.Window.KeyPressQwerty(PhysicalKey.F8, RawInputModifiers.Control | RawInputModifiers.Shift); await Task.Delay(100);
            Assert.Equal(6, f.Vm.SessionCards.Count); Assert.Equal(6, sidebar.ItemCount);
            var summary = page.GetVisualDescendants().OfType<TextBlock>().Single(); Assert.Contains(f.Vm.SessionCards[^1].Name, summary.Text);
            var source = f.Vm.ActiveWorkspace; await f.Vm.NewProjectWorkspaceCommand.ExecuteAsync(null); await Task.Delay(100);
            manager.Settings(overview).Workspaces[f.Vm.ActiveWorkspace.Id] = false; manager.SaveModules();
            Assert.DoesNotContain(overview, manager.Visible(ExtensionSurface.WorkspaceTools)); Assert.Single(sidebar.Items);
            f.Vm.SwitchProjectWorkspace(source); Assert.Contains(overview, manager.Visible(ExtensionSurface.WorkspaceTools)); Assert.Equal(6, sidebar.ItemCount);
            var panelPlugin = manager.Plugins.Single(p => p.Manifest.Id == "example.session-panel"); manager.Disable(panelPlugin);
            Assert.True(manager.Plugins.Single(p => p.Manifest.Id == "example.compact-sidebar").Enabled); Assert.False(f.Window.FindControl<ListBox>("SessionShelf")!.IsVisible);
            var count = f.Vm.SessionCards.Count; Assert.False(manager.HandleShortcut(new KeyEventArgs { Key = Key.F8, KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift })); Assert.Equal(count, f.Vm.SessionCards.Count);
            manager.Enable(panelPlugin); Assert.True(panelPlugin.Enabled, panelPlugin.Error);
            Assert.False(Assert.IsType<CheckBox>(manager.Modules.Single(m => m.Owner == panelPlugin.Manifest.Id && m.Definition.Surface == ExtensionSurface.Settings).GetView()).IsChecked);
            Assert.Single(manager.Commands, c => c.Owner == panelPlugin.Manifest.Id);
            manager.Disable(manager.Plugins.Single(p => p.Manifest.Id == "example.compact-sidebar"));
            Assert.True(f.Window.FindControl<ListBox>("SessionShelf")!.IsVisible); Assert.Equal(styles, f.Window.Styles.Count); Assert.True(panelPlugin.Enabled);
            Assert.Empty(manager.LastError);
            foreach (var (model, pty) in existing) { Assert.Same(pty, model.Pty); Assert.True(model.IsRunning); }
        }
        finally { foreach (var plugin in manager.Plugins.ToArray()) manager.Remove(plugin); f.Vm.LanguageIndex = 0; }
    }

    [AvaloniaFact]
    public async Task PluginLifecycle_CancelsTimersSubscriptionsStylesResourcesAndFailedActivation()
    {
        using var f = new StageLayoutTests.StageFixture(); await Task.Delay(650);
        var directory = Path.Combine(Path.GetTempPath(), "terminalhub-plugin-probe-" + Guid.NewGuid());
        using var manager = new PluginManager(f.Vm, directory, f.Window.Styles);
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "probe"));
            File.Copy(typeof(WorkbenchProbePlugin).Assembly.Location, Path.Combine(directory, "probe", "probe.dll"));
            File.WriteAllText(Path.Combine(directory, "probe", "plugin.json"), JsonSerializer.Serialize(new PluginManifest { Id = "acceptance.probe", Name = "Lifecycle probe", Entry = "probe.dll", EntryType = typeof(WorkbenchProbePlugin).FullName! }));
            manager.Discover(); var plugin = Assert.Single(manager.Plugins); var styles = f.Window.Styles.Count; var resources = Application.Current!.Resources.MergedDictionaries.Count;
            manager.Enable(plugin); Assert.True(plugin.Enabled, plugin.Error);
            var view = Assert.IsType<TextBlock>(Assert.Single(manager.Modules).GetView()); await Task.Delay(120);
            Assert.True(int.Parse(view.Text!) > 0); Assert.Equal(styles + 1, f.Window.Styles.Count); Assert.Equal(resources + 1, Application.Current.Resources.MergedDictionaries.Count);
            manager.Disable(plugin); var stopped = view.Text; await Task.Delay(120); Assert.Equal(stopped, view.Text);
            Assert.Empty(manager.Modules); Assert.Empty(manager.Commands); Assert.Equal(styles, f.Window.Styles.Count); Assert.Equal(resources, Application.Current.Resources.MergedDictionaries.Count);
            f.Vm.LanguageIndex = 2; Assert.Equal(stopped, view.Text);
            manager.Enable(plugin); Assert.True(plugin.Enabled, plugin.Error); Assert.Single(manager.Modules);
            await Assert.Single(manager.Commands).Command.Execute();
            Assert.False(plugin.Enabled); Assert.Contains("acceptance-command-failure", manager.LastError); Assert.Empty(manager.Modules); Assert.Empty(manager.Commands);
            Assert.Equal(styles, f.Window.Styles.Count); Assert.Equal(resources, Application.Current.Resources.MergedDictionaries.Count);
            manager.Enable(plugin); Assert.True(plugin.Enabled, plugin.Error); Assert.Empty(manager.LastError);
            manager.Disable(plugin); f.Vm.PluginPreferences[plugin.Manifest.Id].Configuration = "{\"fail\":true}";
            manager.Enable(plugin); Assert.False(plugin.Enabled); Assert.Contains("acceptance-initialize-failure", manager.LastError);
            Assert.Empty(manager.Modules); Assert.Equal(styles, f.Window.Styles.Count); Assert.Equal(resources, Application.Current.Resources.MergedDictionaries.Count);
        }
        finally { manager.Dispose(); f.Vm.LanguageIndex = 0; if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}

// Loaded from this test assembly in a collectible plugin context, exercising the actual DLL loader.
public sealed class WorkbenchProbePlugin : IWorkbenchPlugin
{
    public void Initialize(IPluginContext context)
    {
        var view = new TextBlock { Text = "0" }; var ticks = 0;
        context.RegisterView(new("probe", "Probe"), () => view);
        context.Schedule(TimeSpan.FromMilliseconds(25), () => view.Text = (++ticks).ToString());
        context.Subscribe(_ => view.Text = (++ticks).ToString());
        context.RegisterStyles(new Style(x => x.OfType<TextBlock>().Class("acceptance-probe")));
        context.RegisterResources(new ResourceDictionary { ["acceptance-probe"] = "registered" });
        context.RegisterCommand(new("fail", "Fail deliberately", () => throw new InvalidOperationException("acceptance-command-failure")));
        if (context.ReadConfiguration<Dictionary<string, bool>>()?.GetValueOrDefault("fail") == true) throw new InvalidOperationException("acceptance-initialize-failure");
    }
    public void Deactivate() { }
}
