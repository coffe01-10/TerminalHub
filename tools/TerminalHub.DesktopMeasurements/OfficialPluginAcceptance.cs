using System.Reflection;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using TerminalHub.App.Plugins;
using TerminalHub.App.ViewModels;
using TerminalHub.App.Views;
using TerminalHub.Core.Settings;
using TerminalHub.Extensibility;
using TerminalHub.Pty;

namespace TerminalHub.DesktopMeasurements;

public sealed partial class MeasurementApplication
{
    private async Task AcceptOfficialPluginsAsync()
    {
        var desktop = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var directory = Path.Combine(Path.GetTempPath(), "terminalhub-official-acceptance-" + Guid.NewGuid().ToString("N"));
        var oldSettings = Environment.GetEnvironmentVariable("TERMINALHUB_SETTINGS_DIR");
        MainWindow? window = null; string? failure = null; var passed = new List<string>();
        try
        {
            Environment.SetEnvironmentVariable("TERMINALHUB_SETTINGS_DIR", directory); PtySessionFactory.UseMock = false;
            var script = "[Console]::WriteLine('official-ready'); while (($line = [Console]::ReadLine()) -ne $null) { [Console]::Write([char]27 + ']133;C' + [char]7); [Console]::WriteLine('official-output:' + $line); $code = if ($line -eq 'fail') { 7 } else { 0 }; [Console]::Write([char]27 + ']133;D;' + $code + [char]7) }";
            var arguments = "-NoLogo -NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var store = new SettingsStore(Path.Combine(directory, "settings.json"));
            store.Save(new AppSettings { InspectorVisible = false, OutputVisible = false, Shell = ShellKind.Custom, CustomShellPath = "powershell.exe",
                Workspace = new() { Sessions = [new() { Name = "官方插件验收", Shell = "powershell.exe", Arguments = arguments, WorkingDirectory = Environment.CurrentDirectory }] } });
            window = new MainWindow(store) { Width = 1100, Height = 740, Title = "Terminal Hub · Official plugin acceptance" };
            desktop.MainWindow = window; window.Show(); var vm = (MainWindowViewModel)window.DataContext!;
            await Until(() => vm.ActiveSession?.Emulator.Buffer.TailText(20).Contains("official-ready") == true);
            var session = vm.ActiveSession!; Check(session.Pty is ConPtySession && session.IsRunning, "Real Windows ConPTY session is ready");
            vm.LanguageIndex = 1; var manager = Field<PluginManager>(window, "_plugins");
            // Import dir defaults to the repo build output; TERMINALHUB_OFFICIAL_PLUGIN_DIR
            // points the acceptance run at a specific packaged drop.
            var pluginDir = Environment.GetEnvironmentVariable("TERMINALHUB_OFFICIAL_PLUGIN_DIR")
                ?? Path.Combine(Environment.CurrentDirectory, "artifacts", "official-plugins");
            foreach (var name in new[] { "WorkspaceNotes", "ScreenClips", "CommandWatch" }) manager.Import(Path.Combine(pluginDir, name));
            Check(manager.Plugins.Count == 3 && manager.Plugins.All(p => p.Enabled), "Three packaged official DLLs load together");
            typeof(MainWindow).GetMethod("ToggleProjectTools", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
            var tools = Field<ProjectToolsWindow>(window, "_projectToolsWindow"); var view = (ProjectToolsView)tools.Content!;
            var nav = view.FindControl<ListBox>("ToolNavigation")!;
            Check(nav.ItemCount == 8, "Three official modules join the five built-in tools");
            var notes = Select("official.workspace-notes");
            Named<TextBox>(notes, "NotesEditor").Text = "原生窗口笔记：中英文保留";
            await Task.Delay(1200);
            Check(vm.PluginPreferences["official.workspace-notes"].Configuration.Contains("Notes"), "Notes autosave in the native tools window");
            var clips = Select("official.screen-clips");
            ((IWorkbenchHost)vm).SendInput(session.Id, "中文回显", submit: true);
            await Until(() => session.Emulator.Buffer.TailText(40).Contains("official-output:中文回显"));
            await manager.Commands.Single(c => c.Owner == "official.screen-clips").Command.Execute();
            Check(Named<TextBox>(clips, "ClipPreview").Text?.Contains("official-output:中文回显") == true, "Clip captures actual Unicode ConPTY output");
            var watch = Select("official.command-watch");
            ((IWorkbenchHost)vm).SendInput(session.Id, "fail", submit: true);
            await Until(() => Labels(watch).Contains("退出码 7"));
            Check(Labels(watch).Contains("失败"), "Actual shell marker exit code 7 reaches the command watch");
            vm.LanguageIndex = 2;
            Check(Labels(watch).Contains("Exit code 7"), "English translation preserves observed exit code");
            window.CollapseProjectTools();
            typeof(MainWindow).GetMethod("ToggleProjectTools", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
            Check(ReferenceEquals(view, Field<ProjectToolsWindow>(window, "_projectToolsWindow").Content), "Closing and reopening tools retains plugin page state");
            var notesPlugin = manager.Plugins.Single(p => p.Manifest.Id == "official.workspace-notes"); manager.Disable(notesPlugin); manager.Enable(notesPlugin);
            Check(Named<TextBox>(Select("official.workspace-notes"), "NotesEditor").Text == "原生窗口笔记：中英文保留", "Notes restore after actual disable and reload");
            foreach (var plugin in manager.Plugins.ToArray()) manager.Disable(plugin);
            Check(session.IsRunning && manager.Commands.All(c => !c.Owner.StartsWith("official.")), "Disabling every plugin clears commands while preserving the real PTY");
            Check(string.IsNullOrEmpty(manager.LastError), "No plugin load or lifecycle errors");
            foreach (var plugin in manager.Plugins.ToArray()) manager.Remove(plugin);
            Control Select(string owner)
            {
                var module = manager.Modules.Single(m => m.Owner == owner);
                nav.SelectedItem = nav.Items.Cast<ListBoxItem>().Single(item => Equals(item.Tag, module.Id)); return module.GetView();
            }
            void Check(bool condition, string scenario) { if (!condition) throw new InvalidOperationException(scenario); passed.Add(scenario); }
        }
        catch (Exception ex) { failure = ex.ToString(); }
        finally
        {
            window?.Close(); Environment.SetEnvironmentVariable("TERMINALHUB_SETTINGS_DIR", oldSettings);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            File.WriteAllText(Program.Report, JsonSerializer.Serialize(new { CapturedUtc = DateTime.UtcNow,
                Method = "Actual Windows Avalonia window, packaged official DLLs and real ConPTY output/OSC markers; in-process UI actions. OS file picker, system clipboard, Linux and IME candidate window are not exercised.", Passed = passed, Failure = failure }, new JsonSerializerOptions { WriteIndented = true }));
            desktop.Shutdown(failure is null ? 0 : 1);
        }
        static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;
        static T Named<T>(Control root, string name) where T : Control => root.GetLogicalDescendants().OfType<T>().Single(c => c.Name == name);
        static string Labels(Control root) => string.Join("\n", root.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text));
        static async Task Until(Func<bool> ready)
        {
            var deadline = Environment.TickCount64 + 20000;
            while (!ready() && Environment.TickCount64 < deadline) await Task.Delay(50);
            if (!ready()) throw new TimeoutException("Expected native plugin/PTY state did not arrive.");
        }
    }
}
