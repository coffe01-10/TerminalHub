using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TerminalHub.App.Controls;
using TerminalHub.App.Plugins;
using TerminalHub.App.ViewModels;
using TerminalHub.App.Views;
using TerminalHub.Core.Settings;
using TerminalHub.Pty;

namespace TerminalHub.DesktopMeasurements;

public sealed partial class MeasurementApplication
{
    private async Task AcceptWorkbenchAsync()
    {
        var desktop = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var directory = Path.Combine(Path.GetTempPath(), "terminalhub-native-acceptance-" + Guid.NewGuid().ToString("N"));
        var oldSettings = Environment.GetEnvironmentVariable("TERMINALHUB_SETTINGS_DIR");
        MainWindow? window = null; string? failure = null;
        var passed = new List<string>(); double scaling = 0; int screens = 0;
        try
        {
            Environment.SetEnvironmentVariable("TERMINALHUB_SETTINGS_DIR", directory);
            PtySessionFactory.UseMock = false;
            var store = new SettingsStore(Path.Combine(directory, "settings.json"));
            var script = "[Console]::WriteLine('native-ready'); while (($line = [Console]::ReadLine()) -ne $null) { [Console]::WriteLine('native-echo:' + $line) }";
            var args = "-NoLogo -NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
            store.Save(new AppSettings { InspectorVisible = false, OutputVisible = false, Shell = ShellKind.Custom, CustomShellPath = "powershell.exe",
                Workspace = new() { Sessions = Enumerable.Range(1, 6).Select(i => new WorkspaceSession { Name = $"Acceptance {i}", Shell = "powershell.exe", Arguments = args, WorkingDirectory = Environment.CurrentDirectory }).ToList() } });
            window = new MainWindow(store) { Width = 1440, Height = 900, Title = "Terminal Hub · Workbench acceptance" };
            desktop.MainWindow = window; window.Show();
            var vm = (MainWindowViewModel)window.DataContext!;
            await Until(() => vm.SessionCards.Count == 6 && vm.SessionCards.All(c => c.Model.Emulator.Buffer.TailText(30).Contains("native-ready")));
            await Task.Delay(350);
            Check(vm.SessionCards.All(c => c.Model.Pty is ConPtySession && c.Model.IsRunning), "Six real ConPTY sessions");
            var originals = vm.SessionCards.Select(c => (c.Model, c.Model.Pty)).ToArray();
            await vm.SetSplitLayoutAsync("Quad"); await vm.SplitPaneAsync(3, true); await vm.SplitPaneAsync(4, false); await Task.Delay(400);
            Check(vm.PaneCount == 6 && Views().Length == 6 && Views().All(v => v.Bounds.Width > 0 && v.Bounds.Height > 0), $"Six nested panes render in the real desktop window: panes={vm.PaneCount}, views={Views().Length}, bounds={string.Join(';', Views().Select(v => v.Bounds.ToString()))}");
            vm.FocusPane(5); var active = vm.ActiveSession!;
            active.Emulator.SendText("nested-input\r"); await Until(() => active.Emulator.Buffer.TailText(40).Contains("native-echo:nested-input"));
            Check(originals.Where(s => s.Model != active).All(s => !s.Model.Emulator.Buffer.TailText(40).Contains("native-echo:nested-input")), "Input reaches only the focused real PTY");
            vm.TogglePaneMaximizedCommand.Execute(null); await Task.Delay(150); Check(Views().Length == 1, "Maximize renders one pane");
            vm.TogglePaneMaximizedCommand.Execute(null); await Task.Delay(150); Check(Views().Length == 6, "Restore renders all panes");
            vm.RemoveFocusedPane(); vm.UndoLayout(); await Task.Delay(150);
            Check(vm.PaneCount == 6 && originals.All(s => s.Model.IsRunning && ReferenceEquals(s.Pty, s.Model.Pty)), "Remove view and undo preserve processes");
            var manager = (PluginManager)typeof(MainWindow).GetField("_plugins", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
            var baseStyles = window.Styles.Count;
            var repo = Environment.CurrentDirectory;
            foreach (var name in new[] { "Minimal", "CompactSidebar", "SessionPanel" }) manager.Import(Path.Combine(repo, "examples", "plugins", name, "bin", "Release", "net8.0"));
            Check(manager.Plugins.Count == 3 && manager.Plugins.All(p => p.Enabled && p.Error.Length == 0), "Three real plugin DLLs enable together");
            Check(!window.FindControl<ListBox>("SessionShelf")!.IsVisible && window.Styles.Count == baseStyles + 2, "Sidebar replacement and plugin styles apply");
            typeof(MainWindow).GetMethod("ToggleProjectTools", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
            await Task.Delay(200);
            var list = (ListBox)typeof(MainWindow).GetField("_toolModulesList", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
            for (var i = 0; i < list.ItemCount; i++) { list.SelectedIndex = i; await Task.Delay(60); }
            Check(list.ItemCount == 7, "Five built-in tools and two plugin tools render in the embedded region");
            window.CollapseProjectTools();
            vm.LanguageIndex = 2; await Task.Delay(100);
            var snapshot = Path.ChangeExtension(Program.Report, ".png");
            using (var bitmap = new RenderTargetBitmap(new((int)window.Bounds.Width, (int)window.Bounds.Height))) { bitmap.Render(window); bitmap.Save(snapshot); }
            manager.Disable(manager.Plugins.Single(p => p.Manifest.Id == "example.compact-sidebar"));
            Check(window.FindControl<ListBox>("SessionShelf")!.IsVisible && window.Styles.Count == baseStyles && manager.Plugins.Single(p => p.Manifest.Id == "example.session-panel").Enabled, "Disabling the sidebar restores the host while the overview remains enabled");
            manager.Disable(manager.Plugins.Single(p => p.Manifest.Id == "example.session-panel"));
            Check(manager.Commands.All(c => c.Owner != "example.session-panel") && originals.All(s => s.Model.IsRunning && ReferenceEquals(s.Pty, s.Model.Pty)), "Disabling overview removes its shortcut without closing sessions");
            screens = window.Screens.All.Count; scaling = window.RenderScaling;
            foreach (var plugin in manager.Plugins.ToArray()) manager.Remove(plugin);
            TerminalView[] Views() => window.FindControl<ContentControl>("SplitHost")!.GetVisualDescendants().OfType<TerminalView>().ToArray();
            void Check(bool condition, string scenario) { if (!condition) throw new InvalidOperationException(scenario); passed.Add(scenario); }
        }
        catch (Exception ex) { failure = ex.ToString(); }
        finally
        {
            window?.Close(); Environment.SetEnvironmentVariable("TERMINALHUB_SETTINGS_DIR", oldSettings);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            File.WriteAllText(Program.Report, JsonSerializer.Serialize(new { CapturedUtc = DateTime.UtcNow, Method = "Real Windows Avalonia window with six real ConPTY processes; in-process UI commands and actual plugin DLLs. Does not exercise the system IME candidate window or OS mouse injection.", Screens = screens, Scaling = scaling, Passed = passed, Failure = failure }, new JsonSerializerOptions { WriteIndented = true }));
            desktop.Shutdown(failure is null ? 0 : 1);
        }
        static async Task Until(Func<bool> ready)
        {
            var deadline = Environment.TickCount64 + 20000;
            while (!ready() && Environment.TickCount64 < deadline) await Task.Delay(50);
            if (!ready()) throw new TimeoutException("Real PTY output did not arrive.");
        }
    }
}
