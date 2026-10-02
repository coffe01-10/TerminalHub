using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using TerminalHub.App.ViewModels;
using TerminalHub.App.Views;
using TerminalHub.Core.Settings;
using TerminalHub.Pty;

namespace TerminalHub.DesktopMeasurements;

public sealed partial class MeasurementApplication
{
    private async Task MeasureWorkspaceSwitchAsync()
    {
        var desktop = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var directory = Path.Combine(Path.GetDirectoryName(Program.Report)!, "switch-settings-" + Guid.NewGuid().ToString("N"));
        MainWindow? window = null;
        string? failure = null;
        var samples = new List<object>();
        PtySessionFactory.UseMock = false;
        try
        {
            var projects = Enumerable.Range(0, 2).Select(w => new ProjectWorkspaceState
            {
                Id = $"measurement-{w}", Name = $"Measurement {w}",
                Layout = new WorkspaceState
                {
                    ActiveIndex = 0,
                    Sessions = Enumerable.Range(0, 10).Select(i => new WorkspaceSession
                    {
                        Name = $"worker {w}-{i}", Shell = OperatingSystem.IsWindows() ? "powershell.exe" : "bash",
                        WorkingDirectory = Environment.CurrentDirectory, Arguments = WorkloadArguments(true, w * 10 + i)
                    }).ToList()
                }
            }).ToList();
            var store = new SettingsStore(Path.Combine(directory, "settings.json"));
            store.Save(new AppSettings
            {
                InspectorVisible = false, OutputVisible = false, DockVisibilityMode = 0, StartupSessions = [],
                ProjectWorkspaces = projects, ActiveProjectWorkspaceId = projects[0].Id
            });
            window = new MainWindow(store) { Width = 1440, Height = 900, Title = "Terminal Hub · Workspace switch measurement" };
            desktop.MainWindow = window;
            window.Show();
            var vm = (MainWindowViewModel)window.DataContext!;
            var deadline = Environment.TickCount64 + 20000;
            while (vm.AllSessionCards.Count() < 20 && Environment.TickCount64 < deadline) await Task.Delay(50);
            if (vm.AllSessionCards.Count() != 20) throw new InvalidOperationException("Expected 20 real PTYs in two workspaces.");
            await Task.Delay(2500);
            foreach (var autoHide in new[] { false, true })
            {
                foreach (var workspace in vm.ProjectWorkspaces) workspace.ShelfAutoHide = autoHide;
                vm.ShelfAutoHide = autoHide;
                // Warm both workspaces before collecting font/layout costs.
                foreach (var workspace in vm.ProjectWorkspaces) { vm.SwitchProjectWorkspace(workspace); await Task.Delay(500); }
                var commands = new List<double>();
                var layouts = new List<double>();
                var gaps = new List<double>();
                var sessionChanges = 0; var shelfChanges = 0;
                System.Collections.Specialized.NotifyCollectionChangedEventHandler onSessions = (_, _) => sessionChanges++;
                System.Collections.Specialized.NotifyCollectionChangedEventHandler onShelf = (_, _) => shelfChanges++;
                vm.SessionCards.CollectionChanged += onSessions;
                vm.ShelfItems.CollectionChanged += onShelf;
                var heartbeat = Stopwatch.StartNew();
                var last = heartbeat.Elapsed.TotalMilliseconds;
                var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
                timer.Tick += (_, _) => { var now = heartbeat.Elapsed.TotalMilliseconds; gaps.Add(now - last); last = now; };
                timer.Start();
                var allocated = GC.GetTotalAllocatedBytes(true);
                using var process = Process.GetCurrentProcess();
                var cpu = process.TotalProcessorTime;
                var wall = Stopwatch.StartNew();
                for (var i = 0; i < 40; i++)
                {
                    var target = vm.ProjectWorkspaces.First(w => w != vm.ActiveWorkspace);
                    var elapsed = Stopwatch.StartNew();
                    vm.SwitchProjectWorkspace(target);
                    commands.Add(elapsed.Elapsed.TotalMilliseconds);
                    await Dispatcher.UIThread.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.Loaded);
                    layouts.Add(elapsed.Elapsed.TotalMilliseconds);
                    await Task.Delay(120);
                }
                wall.Stop(); timer.Stop();
                vm.SessionCards.CollectionChanged -= onSessions;
                vm.ShelfItems.CollectionChanged -= onShelf;
                samples.Add(new
                {
                    Shelf = autoHide ? "hidden-drawer" : "fixed", Switches = commands.Count, SessionsPerWorkspace = 10,
                    CommandMs = Distribution(commands), CommandAndLayoutMs = Distribution(layouts), UiHeartbeatGapMs = Distribution(gaps),
                    HeartbeatGapsOver33Ms = gaps.Count(g => g > 33), HeartbeatSamples = gaps.Count,
                    SessionCollectionChanges = sessionChanges, ShelfCollectionChanges = shelfChanges,
                    AllocatedMiB = (GC.GetTotalAllocatedBytes(true) - allocated) / 1048576.0,
                    AppCpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds, WallMs = wall.Elapsed.TotalMilliseconds,
                    RunningPtys = vm.AllSessionCards.Count(c => c.Model.IsRunning), window.RenderScaling, window.IsVisible
                });
            }
        }
        catch (Exception ex) { failure = ex.ToString(); }
        finally
        {
            window?.Close();
            File.WriteAllText(Program.Report, JsonSerializer.Serialize(new
            {
                CapturedUtc = DateTime.UtcNow,
                Method = "Real Avalonia desktop, two workspaces of 10 real PTYs each, all output every 50ms. Warmup 2.5s plus both workspaces 500ms. 40 switches with 120ms pause per shelf mode. Command includes synchronous bindings; command+layout includes Loaded dispatcher work and window layout. Render-priority 16ms UI heartbeat measures UI responsiveness, not GPU frame rate. CPU excludes child shells.",
                Environment = new { Environment.OSVersion, Environment.ProcessorCount, RuntimeInformation.FrameworkDescription },
                Samples = samples, Failure = failure
            }, new JsonSerializerOptions { WriteIndented = true }));
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            desktop.Shutdown(failure is null ? 0 : 1);
        }
        static object Distribution(List<double> values)
        {
            var sorted = values.Order().ToArray();
            return new { Mean = values.Average(), P95 = sorted[(int)Math.Ceiling(sorted.Length * .95) - 1], Max = sorted[^1] };
        }
    }
}
