using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using TerminalHub.App.Plugins;
using TerminalHub.App.Views;
using TerminalHub.Core.Settings;
using TerminalHub.Extensibility;
using TerminalHub.Pty;
using Xunit;

namespace TerminalHub.Tests;

public class PluginMarketplaceTests
{
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static void Call(object owner, string name) => owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, null);
    private static T Named<T>(Control root, string name) where T : Control => root.GetLogicalDescendants().OfType<T>().First(c => c.Name == name);
    private static void Click(Control page, string name) => Named<Button>(page, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static PluginManager Manager(MainWindow window) => Field<PluginManager>(window, "_plugins");
    private static ContentControl Workbench(MainWindow window) => window.FindControl<ContentControl>("InspectorWorkbenchContent")!;
    private static ModuleRegistration Tool(PluginManager manager, string owner) => manager.Modules.Single(m => m.Owner == owner && m.Definition.Surface == ExtensionSurface.WorkspaceTools);
    private static Control Options(PluginManager manager, string owner) => manager.Modules.Single(m => m.Owner == owner && m.Definition.Surface == ExtensionSurface.Settings).GetView();
    private static JsonElement Configuration(StageLayoutTests.StageFixture f, string owner) => JsonDocument.Parse(f.Vm.PluginPreferences[owner].Configuration).RootElement;
    private static async Task Until(Func<bool> condition, string message)
    {
        var deadline = Environment.TickCount64 + 12000;
        while (!condition() && Environment.TickCount64 < deadline) await Task.Delay(25);
        Assert.True(condition(), message);
    }
    private static void CleanPlugins(PluginManager manager) { foreach (var plugin in manager.Plugins.ToArray()) manager.Remove(plugin); }

    [AvaloniaFact]
    public async Task Shortcuts_UseExistingActionDockAndInspector_SwitchBackToBuiltinWithoutResizingTerminal()
    {
        using var f = new StageLayoutTests.StageFixture(width: 1100, height: 800); await f.ReadyAsync(); var manager = Manager(f.Window);
        try
        {
            manager.InstallOfficial("official.workspace-notes"); manager.InstallOfficial("official.screen-clips");
            var notes = Tool(manager, "official.workspace-notes"); var clips = Tool(manager, "official.screen-clips");
            f.Window.MoveWorkbenchModule(notes, ToolPlacement.Bottom); f.Window.MoveWorkbenchModule(clips, ToolPlacement.Right);
            await Task.Delay(80);
            var dock = f.Window.FindControl<TerminalHub.App.Controls.DropletDock>("ActionDock")!;
            var tabs = f.Window.FindControl<ListBox>("InspectorTabs")!;
            var launch = Named<Button>(f.Window, "Launch-" + notes.Id);
            Assert.Contains(dock, launch.GetVisualAncestors());
            Assert.Contains(tabs.Items.Cast<ListBoxItem>(), t => Equals(t.Tag, clips.Id));
            Assert.DoesNotContain(tabs.Items.Cast<ListBoxItem>(), t => Equals(t.Tag, notes.Id));
            var terminal = f.Window.FindControl<TerminalHub.App.Controls.TerminalView>("MainTerminal")!;
            var originalSize = terminal.Bounds.Size;
            launch.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(80);
            Assert.Same(notes.GetView(), Workbench(f.Window).Content); Assert.True(Workbench(f.Window).IsEffectivelyVisible);
            Assert.Equal(originalSize, terminal.Bounds.Size);
            Named<TextBox>(notes.GetView(), "NotesEditor").Text = "保留在原右侧面板中的笔记";
            tabs.SelectedItem = tabs.Items.Cast<ListBoxItem>().Single(t => Equals(t.Tag, clips.Id)); await Task.Delay(50);
            Assert.Same(clips.GetView(), Workbench(f.Window).Content);
            Assert.False(manager.Settings(notes).WasOpen); Assert.True(manager.Settings(clips).WasOpen);
            f.Vm.DockSelectCommand.Execute("1"); await Task.Delay(50);
            Assert.Equal(0, f.Vm.SelectedRightTab); Assert.False(Workbench(f.Window).IsVisible); Assert.False(manager.Settings(clips).WasOpen);
            f.Vm.DockSelectCommand.Execute("2"); Assert.Equal(3, f.Vm.SelectedRightTab);
            f.Vm.DockSelectCommand.Execute("3"); Assert.Equal(2, f.Vm.SelectedRightTab);
            Click(f.Window, "Launch-" + notes.Id); await Task.Delay(50);
            Assert.Equal("保留在原右侧面板中的笔记", Named<TextBox>(notes.GetView(), "NotesEditor").Text);
            Click(f.Window, "WorkbenchInspectorPopout"); await Task.Delay(50);
            Assert.Equal(ToolPlacement.Window, manager.Settings(notes).Placement); Assert.True(f.Window.IsWorkbenchModuleOpen(notes));
            Assert.False(Workbench(f.Window).IsVisible); Assert.Equal(originalSize, terminal.Bounds.Size);
            Assert.Empty(manager.LastError);
        }
        finally { CleanPlugins(manager); }
    }

    [AvaloniaFact]
    public async Task ManyDockShortcuts_ScrollInsideOriginalCapsule_AndHiddenTabCanOpenFromMarket()
    {
        using var f = new StageLayoutTests.StageFixture(width: 960, height: 700); await f.ReadyAsync(); var manager = Manager(f.Window);
        var registrations = new List<IDisposable>();
        try
        {
            for (var i = 0; i < 10; i++)
            {
                registrations.Add(manager.RegisterBuiltin(new("dock-extra" + i, "Extra dock tool " + i), () => new TextBlock { Text = "Example tool" }));
                f.Window.MoveWorkbenchModule(manager.Modules.Single(m => m.Id == "builtin:dock-extra" + i), ToolPlacement.Bottom);
            }
            await Task.Delay(80);
            var dock = f.Window.FindControl<TerminalHub.App.Controls.DropletDock>("ActionDock")!;
            Assert.True(dock.Bounds.Width <= f.Window.Bounds.Width - 48 + 1);
            var scroll = Assert.IsType<ScrollViewer>(dock.Child); Assert.True(scroll.Extent.Width > scroll.Viewport.Width);
            var last = manager.Modules.Single(m => m.Id == "builtin:dock-extra9");
            Click(f.Window, "Launch-" + last.Id); Assert.Same(last.GetView(), Workbench(f.Window).Content);
            f.Window.MoveWorkbenchModule(last, ToolPlacement.Right); manager.Settings(last).ShowLauncher = false; manager.SaveModules();
            Click(f.Window, "WorkbenchInspectorClose");
            Assert.DoesNotContain(f.Window.FindControl<ListBox>("InspectorTabs")!.Items.Cast<ListBoxItem>(), t => Equals(t.Tag, last.Id));
            f.Window.OpenWorkbenchModule(last);
            Assert.Same(last.GetView(), Workbench(f.Window).Content);
            Assert.Contains(f.Window.FindControl<ListBox>("InspectorTabs")!.Items.Cast<ListBoxItem>(), t => Equals(t.Tag, last.Id));
            Assert.Empty(manager.LastError);
        }
        finally { foreach (var registration in registrations) registration.Dispose(); }
    }

    [AvaloniaFact]
    public async Task Market_ListsUninstalledBundles_InstallsOnClick_FiltersAndUninstalls()
    {
        using var f = new StageLayoutTests.StageFixture(); await f.ReadyAsync(); var manager = Manager(f.Window);
        try
        {
            Assert.Empty(manager.Plugins);
            Assert.All(OfficialPluginCatalog.All, p => Assert.True(File.Exists(Path.Combine(p.PackageDirectory(manager.OfficialPackageRoot), "plugin.json"))));
            Call(f.Window, "OpenPluginManager"); var market = Field<PluginManagerWindow>(f.Window, "_pluginManagerWindow");
            var cards = market.FindControl<StackPanel>("PluginCards")!; var filter = market.FindControl<ComboBox>("MarketFilter")!;
            Assert.Equal(12, cards.Children.Count); filter.SelectedIndex = 2; Assert.Equal(7, cards.Children.Count);
            var sessionIds = f.Vm.SessionCards.Select(c => c.Model.Id).ToArray();
            Click(market, "Install-official.project-navigator");
            Assert.True(manager.Plugins.Single().Enabled); Assert.Equal(6, cards.Children.Count);
            filter.SelectedIndex = 1; Assert.Equal(6, cards.Children.Count);
            var module = Tool(manager, "official.project-navigator");
            Assert.Equal(ToolPlacement.Window, manager.Settings(module).Placement);
            Named<ComboBox>(market.FindControl<StackPanel>("PluginCards")!, "MarketPlacement-" + module.Id).SelectedIndex = (int)ToolPlacement.Bottom;
            Assert.Contains(f.Window.GetLogicalDescendants().OfType<Button>(), b => b.Name == "Launch-" + module.Id);
            Named<ComboBox>(market.FindControl<StackPanel>("PluginCards")!, "MarketPlacement-" + module.Id).SelectedIndex = (int)ToolPlacement.Right;
            Assert.DoesNotContain(f.Window.GetLogicalDescendants().OfType<Button>(), b => b.Name == "Launch-" + module.Id);
            Assert.Contains(f.Window.FindControl<ListBox>("InspectorTabs")!.Items.Cast<ListBoxItem>(), t => Equals(t.Tag, module.Id));
            Click(market, "Open-official.project-navigator");
            Assert.True(Workbench(f.Window).IsVisible); Assert.True(f.Vm.InspectorVisible);
            manager.Disable(manager.Plugins.Single()); Assert.Equal(6, cards.Children.Count);
            Assert.False(Workbench(f.Window).IsVisible);
            manager.Remove(manager.Plugins.Single()); filter.SelectedIndex = 2; Assert.Equal(7, cards.Children.Count);
            f.Vm.LanguageIndex = 2; market.FindControl<TextBox>("MarketSearch")!.Text = "git";
            await Until(() => cards.Children.Count == 1 && cards.Children[0].Name == "Market-official.git-workbench", "Market search did not find the uninstalled Git extension");
            Assert.Equal(sessionIds, f.Vm.SessionCards.Select(c => c.Model.Id)); Assert.All(f.Vm.SessionCards, c => Assert.True(c.Model.IsRunning));
            Assert.Empty(manager.LastError);
        }
        finally { CleanPlugins(manager); f.Vm.LanguageIndex = 0; }
    }

    [AvaloniaFact]
    public async Task MovingCachedPage_BetweenWindowBottomAndRight_PreservesNotesAndSessions()
    {
        using var f = new StageLayoutTests.StageFixture(); await f.ReadyAsync(); var manager = Manager(f.Window);
        try
        {
            manager.InstallOfficial("official.workspace-notes"); var module = Tool(manager, "official.workspace-notes");
            f.Window.OpenWorkbenchModule(module); var page = module.GetView();
            Named<TextBox>(page, "NotesEditor").Text = "移动面板前的笔记";
            foreach (var placement in new[] { ToolPlacement.Bottom, ToolPlacement.Right, ToolPlacement.Window, ToolPlacement.Right })
            {
                f.Window.MoveWorkbenchModule(module, placement); await Task.Delay(60);
                Assert.Same(page, module.GetView()); Assert.Equal("移动面板前的笔记", Named<TextBox>(page, "NotesEditor").Text);
                Assert.True(f.Window.IsWorkbenchModuleOpen(module));
                Assert.NotNull(page.GetVisualRoot()); Assert.All(f.Vm.SessionCards, c => Assert.True(c.Model.IsRunning));
            }
            Call(f.Window, "OpenPluginManager"); var market = Field<PluginManagerWindow>(f.Window, "_pluginManagerWindow");
            var marketCard = market.FindControl<StackPanel>("PluginCards")!.Children.Single(c => c.Name == "Market-" + module.Owner);
            marketCard.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, "设置")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var preferences = market.FindControl<StackPanel>("SettingCards")!;
            Named<ComboBox>(preferences, "Placement-" + module.Id).SelectedIndex = (int)ToolPlacement.Window;
            await Task.Delay(50);
            Assert.Equal(ToolPlacement.Window, manager.Settings(module).Placement); Assert.True(f.Window.IsWorkbenchModuleOpen(module));
            Assert.False(preferences.GetLogicalDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, "显示快捷入口")).IsEnabled);
            Named<ComboBox>(preferences, "Placement-" + module.Id).SelectedIndex = (int)ToolPlacement.Right;
            await Task.Delay(50); Assert.Same(page, Workbench(f.Window).Content);
            Assert.True(preferences.GetLogicalDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, "显示快捷入口")).IsEnabled);
            Click(page, "SaveNotes");
            manager.Settings(module).ShowLauncher = false; manager.SaveModules();
            Assert.DoesNotContain(f.Window.GetVisualDescendants().OfType<Button>(), b => b.Name == "Launch-" + module.Id);
            Click(f.Window, "WorkbenchInspectorClose");
            Assert.False(Workbench(f.Window).IsVisible); Assert.False(manager.Settings(module).WasOpen);
            f.Window.OpenWorkbenchModule(module);
            manager.Settings(module).Workspaces[f.Vm.ActiveWorkspace.Id] = false; manager.SaveModules(); Assert.False(Workbench(f.Window).IsVisible);
            Assert.Equal("移动面板前的笔记", Configuration(f, module.Owner).GetProperty("Notes").GetProperty(f.Vm.ActiveWorkspace.Id).GetString()); Assert.Empty(manager.LastError);
        }
        finally { CleanPlugins(manager); }
    }

    [AvaloniaFact]
    public async Task Settings_SaveBeforeDetaching_UpdateHiddenFoldersHistoryAndDiff_AndSurviveDisable()
    {
        using var repo = new GitPluginRepository(); await repo.Initialize();
        await repo.Commit("file.txt", "one", "one"); await repo.Commit("file.txt", "two", "two");
        var hidden = Path.Combine(repo.Work, ".hidden"); Directory.CreateDirectory(hidden);
        using var f = new StageLayoutTests.StageFixture(); await f.ReadyAsync(); var manager = Manager(f.Window);
        try
        {
            f.Vm.ActiveSession!.WorkingDirectory = repo.Work;
            manager.InstallOfficial("official.project-navigator"); manager.InstallOfficial("official.git-workbench");
            var navigator = Tool(manager, "official.project-navigator").GetView(); var settings = Options(manager, "official.project-navigator");
            await Until(() => Named<TextBlock>(navigator, "ProjectStatus").Text == repo.Work, "Navigator did not load the current folder");
            Assert.DoesNotContain(hidden, Named<ListBox>(navigator, "ProjectFolders").Items.Cast<string>());
            Named<CheckBox>(settings, "NavigatorHiddenSetting").IsChecked = true;
            await Until(() => Named<ListBox>(navigator, "ProjectFolders").Items.Cast<string>().Contains(hidden), "Hidden folder option was not applied");
            Named<TextBox>(settings, "NavigatorDefaultDirectory").Text = repo.Root;
            Assert.Equal(repo.Root, Configuration(f, "official.project-navigator").GetProperty("DefaultDirectory").GetString());
            Named<NumericUpDown>(settings, "NavigatorRecentLimit").Value = 2;
            for (var i = 0; i < 3; i++)
            {
                var child = Path.Combine(repo.Work, "folder" + i); Directory.CreateDirectory(child);
                Named<TextBox>(navigator, "ProjectPath").Text = child; Click(navigator, "ProjectGo");
                await Until(() => Named<TextBlock>(navigator, "ProjectStatus").Text == child, "Folder navigation did not complete");
            }
            Assert.Equal(2, Configuration(f, "official.project-navigator").GetProperty("Recent").GetArrayLength());
            var gitSettings = Options(manager, "official.git-workbench");
            Named<NumericUpDown>(gitSettings, "GitHistoryLimit").Value = 1;
            Named<TextBox>(gitSettings, "GitDefaultBase").Text = "develop";
            Named<CheckBox>(gitSettings, "GitWrapSetting").IsChecked = true;
            var git = Tool(manager, "official.git-workbench").GetView();
            await Until(() => Named<Button>(git, "GitRefresh").IsEnabled, "Git repository did not load");
            Assert.Equal(1, Named<ListBox>(git, "GitHistory").ItemCount); Assert.Equal("develop", Named<TextBox>(git, "GitPrBase").Text);
            Assert.Equal(TextWrapping.Wrap, Named<TextBox>(git, "GitDiff").TextWrapping);
            foreach (var plugin in manager.Plugins.ToArray()) { manager.Disable(plugin); manager.Enable(plugin); Assert.True(plugin.Enabled, plugin.Error); }
            Assert.Equal(2, Named<NumericUpDown>(Options(manager, "official.project-navigator"), "NavigatorRecentLimit").Value);
            Assert.Equal("develop", Named<TextBox>(Options(manager, "official.git-workbench"), "GitDefaultBase").Text);
            Assert.Empty(manager.LastError);
        }
        finally { CleanPlugins(manager); }
    }

    [AvaloniaFact]
    public async Task Startup_RestoresOnlyRequestedTools_AppExitKeepsOpenState_UserCloseClearsIt()
    {
        var directory = Path.Combine(Path.GetTempPath(), "terminalhub-market-startup-" + Guid.NewGuid());
        var store = new SettingsStore(Path.Combine(directory, "settings.json")); Window? window = null;
        PtySessionFactory.UseMock = true;
        store.Save(new AppSettings { Modules = new()
        {
            ["builtin:tools-0"] = new() { Placement = ToolPlacement.Bottom, Startup = ToolStartup.RestoreLast, WasOpen = true },
            ["builtin:tools-1"] = new() { Placement = ToolPlacement.Bottom, Startup = ToolStartup.RestoreLast, WasOpen = false },
            ["builtin:tools-2"] = new() { Placement = ToolPlacement.Right, Startup = ToolStartup.Manual, WasOpen = true }
        } });
        try
        {
            var first = new MainWindow(store); window = first; first.Show(); await Task.Delay(100);
            Assert.True(Workbench(first).IsVisible); Assert.True(first.FindControl<ListBox>("InspectorTabs")!.SelectedIndex >= 6);
            Assert.Equal("builtin:tools-0", ((ListBoxItem)first.FindControl<ListBox>("InspectorTabs")!.SelectedItem!).Tag);
            // Visiting a second tool must not replace the last selected tool on the next launch.
            var manager = Manager(first);
            first.OpenWorkbenchModule(manager.Modules.Single(m => m.Id == "builtin:tools-1"));
            first.OpenWorkbenchModule(manager.Modules.Single(m => m.Id == "builtin:tools-0"));
            first.Close(); Assert.True(store.Load().Modules["builtin:tools-0"].WasOpen);
            var second = new MainWindow(store); window = second; second.Show(); await Task.Delay(100);
            Assert.True(Workbench(second).IsVisible);
            Assert.Equal("builtin:tools-0", ((ListBoxItem)second.FindControl<ListBox>("InspectorTabs")!.SelectedItem!).Tag);
            Click(second, "WorkbenchInspectorClose"); Assert.False(Workbench(second).IsVisible); Assert.False(store.Load().Modules["builtin:tools-0"].WasOpen);
            second.Close();
            var saved = store.Load(); saved.Modules["builtin:tools-0"].Startup = ToolStartup.OnLaunch; store.Save(saved);
            var third = new MainWindow(store); window = third; third.Show(); await Task.Delay(100);
            Assert.True(Workbench(third).IsVisible);
            Assert.Equal("builtin:tools-0", ((ListBoxItem)third.FindControl<ListBox>("InspectorTabs")!.SelectedItem!).Tag);
        }
        finally { window?.Close(); await Task.Delay(60); Directory.Delete(directory, true); }
    }

    [AvaloniaTheory]
    [InlineData(0)] [InlineData(3)]
    public async Task MarketAndDock_FitEnglishMinimumWindow_AndExposeReachableActions(int theme)
    {
        using var repo = new GitPluginRepository(); await repo.Initialize(); await repo.Commit("README.md", "Example project", "Initial example");
        await File.WriteAllTextAsync(Path.Combine(repo.Work, "README.md"), "Example project\nUpdated for review");
        using var f = new StageLayoutTests.StageFixture(width: 1440, height: 1000); await f.ReadyAsync(); var manager = Manager(f.Window);
        f.Vm.ThemeIndex = theme; f.Vm.LanguageIndex = 2;
        try
        {
            Call(f.Window, "OpenPluginManager"); var market = Field<PluginManagerWindow>(f.Window, "_pluginManagerWindow");
            market.Width = market.MinWidth; market.Height = market.MinHeight; await Task.Delay(80);
            Assert.Equal("Plugin marketplace", market.Title); Capture(market, "market-" + theme);
            foreach (var button in market.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b.Bounds.Width > 0))
            {
                var x = button.TranslatePoint(default, market)!.Value.X;
                Assert.True(x >= 0 && x + button.Bounds.Width <= market.Bounds.Width + 1, "Market action overflows: " + button.Name);
            }
            f.Vm.ActiveSession!.WorkingDirectory = repo.Work;
            manager.InstallOfficial("official.project-navigator"); manager.InstallOfficial("official.git-workbench");
            market.FindControl<TextBox>("MarketSearch")!.Text = "git";
            await Until(() => market.FindControl<StackPanel>("PluginCards")!.Children.Count == 1, "Installed Git card did not appear");
            await Task.Delay(60); Capture(market, "market-installed-" + theme);
            var placement = Named<ComboBox>(market.FindControl<StackPanel>("PluginCards")!, "MarketPlacement-" + Tool(manager, "official.git-workbench").Id);
            var placementX = placement.TranslatePoint(default, market)!.Value.X;
            Assert.True(placementX >= 0 && placementX + placement.Bounds.Width <= market.Bounds.Width + 1, "The quick placement selector is clipped in the minimum market window");
            foreach (var owner in new[] { "official.project-navigator", "official.git-workbench" })
            {
                var module = Tool(manager, owner); f.Window.MoveWorkbenchModule(module, ToolPlacement.Right); f.Window.OpenWorkbenchModule(module); await Task.Delay(80);
                if (owner == "official.git-workbench")
                {
                    await Until(() => Named<Button>(module.GetView(), "GitRefresh").IsEnabled && Named<ListBox>(module.GetView(), "GitFiles").ItemCount > 0, "Git sidebar did not load the example repository");
                    Named<ListBox>(module.GetView(), "GitFiles").SelectedIndex = 0;
                    await Until(() => Named<Button>(module.GetView(), "GitRefresh").IsEnabled, "Git sidebar diff did not load");
                }
                var page = module.GetView(); Assert.InRange(page.Bounds.Width, 250, 500);
                var inspectorTabs = f.Window.FindControl<ListBox>("InspectorTabs")!;
                foreach (var tab in inspectorTabs.Items.Cast<ListBoxItem>())
                {
                    var x = tab.TranslatePoint(default, inspectorTabs)!.Value.X;
                    Assert.True(x >= 0 && x + tab.Bounds.Width <= inspectorTabs.Bounds.Width + 1,
                        "An inspector tab was scrolled or clipped out of the original sidebar: " + tab.Content);
                }
                Capture(f.Window, owner + "-right-" + theme);
                foreach (var button in page.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b.Bounds.Width > 0))
                {
                    var x = button.TranslatePoint(default, page)!.Value.X;
                    Assert.True(x >= 0 && x + button.Bounds.Width <= page.Bounds.Width + 1, "Sidebar action overflows: " + button.Name);
                }
                f.Window.MoveWorkbenchModule(module, ToolPlacement.Bottom); await Task.Delay(60); Capture(f.Window, owner + "-bottom-" + theme);
                Assert.True(f.Window.FindControl<TerminalHub.App.Controls.TerminalView>("MainTerminal")!.Bounds.Height >= 100,
                    "Opening bottom tools must leave the terminal readable when the output panel is also open.");
            }
            Assert.Empty(manager.LastError);
        }
        finally { CleanPlugins(manager); f.Vm.LanguageIndex = 0; }
    }
    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("TERMINALHUB_MARKET_CAPTURES") is not { } directory) return;
        Directory.CreateDirectory(directory); window.CaptureRenderedFrame()!.Save(Path.Combine(directory, name + ".png"));
    }
}
