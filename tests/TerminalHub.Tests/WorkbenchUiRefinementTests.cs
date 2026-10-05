using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using TerminalHub.App.Controls;
using TerminalHub.App.Plugins;
using TerminalHub.App.ViewModels;
using TerminalHub.App.Views;
using Xunit;

namespace TerminalHub.Tests;

public class WorkbenchUiRefinementTests
{
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static void Call(object owner, string name) => owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, null);
    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("TERMINALHUB_UI_REFINEMENT_CAPTURES") is not { } directory) return;
        Directory.CreateDirectory(directory); window.CaptureRenderedFrame()!.Save(Path.Combine(directory, name + ".png"));
    }
    [AvaloniaTheory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task ToolsAndManager_FitEnglishAtMinimumSize_KeepFocusedOrderEditorAndPluginSettings(int theme)
    {
        using var f = new StageLayoutTests.StageFixture(width: 1100, height: 700); await f.ReadyAsync();
        f.Vm.ThemeIndex = theme; f.Vm.LanguageIndex = 2;
        var manager = Field<PluginManager>(f.Window, "_plugins");
        try
        {
            foreach (var folder in new[] { "CompactSidebar", "SessionPanel" })
                manager.Import(Path.GetDirectoryName(Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "AcceptancePlugins", folder), "plugin.json", SearchOption.AllDirectories).Single())!);
            Call(f.Window, "ToggleProjectTools"); await Task.Delay(100);
            var tools = Field<ProjectToolsWindow>(f.Window, "_projectToolsWindow");
            var view = Assert.IsType<ProjectToolsView>(tools.Content); var navigation = view.FindControl<ListBox>("ToolNavigation")!;
            Assert.Equal(6, navigation.ItemCount); Assert.Null(f.Window.FindControl<Border>("ProjectToolsRegion"));
            for (var i = 0; i < navigation.ItemCount; i++)
            {
                navigation.SelectedIndex = i; await Task.Delay(45);
                Assert.NotNull(view.FindControl<TabControl>("ToolsTabs")!.SelectedContent);
            }
            Capture(tools, $"tools-overview-{theme}");
            navigation.SelectedIndex = 0; await Task.Delay(60); Capture(tools, $"tools-rules-{theme}");
            tools.Close();
            Call(f.Window, "OpenPluginManager"); await Task.Delay(60);
            var window = Field<PluginManagerWindow>(f.Window, "_pluginManagerWindow"); window.Width = window.MinWidth; window.Height = window.MinHeight; await Task.Delay(80);
            Capture(window, $"plugins-{theme}");
            var tabs = window.FindControl<TabControl>("ManagerTabs")!; tabs.SelectedIndex = 1; await Task.Delay(60);
            var order = window.GetVisualDescendants().OfType<NumericUpDown>().First(n => n.IsEffectivelyVisible);
            order.Focus(); order.Value = 47;
            Assert.Contains(window.GetVisualDescendants().OfType<NumericUpDown>(), n => ReferenceEquals(n, order));
            Assert.Equal(47, order.Value); // saving does not recreate the focused editor
            foreach (var editor in window.GetVisualDescendants().OfType<NumericUpDown>().Where(n => n.IsEffectivelyVisible))
            {
                var point = editor.TranslatePoint(default, window)!.Value;
                Assert.True(point.X >= 0 && point.X + editor.Bounds.Width <= window.Bounds.Width, "Order editor must fit the narrow module card.");
            }
            Capture(window, $"modules-{theme}");
            tabs.SelectedIndex = 2; await Task.Delay(60);
            var option = window.GetVisualDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, "Show directories"));
            option.IsChecked = false; manager.SaveModules(); await Task.Delay(30);
            Assert.Contains(window.GetVisualDescendants().OfType<CheckBox>(), c => ReferenceEquals(c, option)); Assert.False(option.IsChecked);
            Capture(window, $"plugin-settings-{theme}");
            window.Close(); f.Window.Activate();
            await f.Vm.SetSplitLayoutAsync("Quad"); await f.Vm.SplitPaneAsync(3, true); await Task.Delay(100);
            foreach (var actions in f.Window.FindControl<ContentControl>("SplitHost")!.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("pane-action")))
            {
                var pane = actions.GetVisualAncestors().OfType<Border>().First(b => b.Name == "PaneFrame");
                var point = actions.TranslatePoint(default, pane)!.Value;
                Assert.True(point.X >= 0 && point.X + actions.Bounds.Width <= pane.Bounds.Width, "Pane actions must fit inside narrow panes.");
            }
            Capture(f.Window, $"panes-{theme}");
            f.Window.KeyPressQwerty(PhysicalKey.F6, RawInputModifiers.Control); await Task.Delay(60); Capture(f.Window, $"recent-{theme}"); f.Vm.FinishRecentSwitcher(false);
        }
        finally
        {
            foreach (var plugin in manager.Plugins.ToArray()) manager.Remove(plugin);
            f.Vm.LanguageIndex = 0;
            f.Window.Close();
            // Finish layout invalidations while the headless application's font services still exist.
            await Task.Delay(100);
        }
    }
}
