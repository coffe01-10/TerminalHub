using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using TerminalHub.App.Controls;
using TerminalHub.App.ViewModels;
using TerminalHub.App.Views;
using TerminalHub.Core.Settings;
using TerminalHub.Pty;

namespace TerminalHub.DesktopMeasurements;

internal static class Program
{
    internal static string Report = "";
    [STAThread]
    public static int Main(string[] args)
    {
        Report = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/performance/desktop.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Report)!);
        var ids = new[] { -10, -11, -12 };
        var handles = OperatingSystem.IsWindows() ? ids.Select(GetStdHandle).ToArray() : [];
        try
        {
            if (OperatingSystem.IsWindows()) foreach (var id in ids) SetStdHandle(id, IntPtr.Zero);
            return AppBuilder.Configure<MeasurementApplication>().UsePlatformDetect().WithInterFont()
                .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        finally
        {
            if (OperatingSystem.IsWindows()) for (var i = 0; i < ids.Length; i++) SetStdHandle(ids[i], handles[i]);
        }
    }
    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int id);
    [DllImport("kernel32.dll")] private static extern bool SetStdHandle(int id, IntPtr handle);
}

public sealed partial class MeasurementApplication : TerminalHub.App.App
{
    public override void Initialize()
    {
        base.Initialize();
        ThemeManager.Apply("DarkGlass");
    }
    public override void OnFrameworkInitializationCompleted()
    {
        Dispatcher.UIThread.Post(async () =>
        {
            if (Environment.GetCommandLineArgs().Contains("--workbench-acceptance")) await AcceptWorkbenchAsync();
            else if (Environment.GetCommandLineArgs().Contains("--workspace-switch")) await MeasureWorkspaceSwitchAsync();
            else await MeasureAsync();
        });
    }
    private sealed record Sample(int Sessions, string Load, double WallMs, double AppCpuMs,
        double AppCpuPercentOfMachine, double AppCpuPercentOfOneCore, double AllocatedMiB,
        double WorkingSetMiB, long OutputBytes, int RunningPtys, bool WindowVisible, bool WindowActive,
        int Screens, double Scaling);
    private async Task MeasureAsync()
    {
        var desktop = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var samples = new List<Sample>();
        string? failure = null;
        var directory = Path.Combine(Path.GetDirectoryName(Program.Report)!, "desktop-settings-" + Guid.NewGuid().ToString("N"));
        MainWindow? window = null;
        PtySessionFactory.UseMock = false;
        try
        {
            foreach (var count in new[] { 1, 5, 10 })
            foreach (var load in new[] { "idle", "one", "all" })
            {
                var store = new SettingsStore(Path.Combine(directory, "settings.json"));
                var settings = new AppSettings
                {
                    InspectorVisible = false, OutputVisible = false, DockVisibilityMode = 0,
                    StartupSessions = [],
                    Workspace = new WorkspaceState
                    {
                        ActiveIndex = 0,
                        Sessions = Enumerable.Range(0, count).Select(i => new WorkspaceSession
                        {
                            Name = $"worker {i}",
                            Shell = OperatingSystem.IsWindows() ? "powershell.exe" : "bash",
                            WorkingDirectory = Environment.CurrentDirectory,
                            Arguments = WorkloadArguments(load != "idle" && (load == "all" || i == 0), i)
                        }).ToList()
                    }
                };
                store.Save(settings);
                window = new MainWindow(store) { Width = 1440, Height = 900, Title = "Terminal Hub · PTY performance measurement" };
                desktop.MainWindow = window;
                window.Show();
                var vm = (MainWindowViewModel)window.DataContext!;
                var deadline = Environment.TickCount64 + 15000;
                while (vm.SessionCards.Count < count && Environment.TickCount64 < deadline) await Task.Delay(50);
                if (vm.SessionCards.Count != count) throw new InvalidOperationException($"Expected {count} PTYs, opened {vm.SessionCards.Count}.");
                long bytes = 0;
                foreach (var card in vm.SessionCards)
                    card.Model.Pty.OutputReceived += (_, data) => Interlocked.Add(ref bytes, data.Length);
                await Task.Delay(2500); // fonts, shell startup, first frame
                using var process = Process.GetCurrentProcess();
                var cpu = process.TotalProcessorTime;
                var allocated = GC.GetTotalAllocatedBytes(true);
                var beforeBytes = Interlocked.Read(ref bytes);
                var wall = Stopwatch.StartNew();
                await Task.Delay(6000); // paced by actual shell sleeps and the desktop compositor
                wall.Stop(); process.Refresh();
                var cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds;
                samples.Add(new(count, load, wall.Elapsed.TotalMilliseconds, cpuMs,
                    cpuMs / wall.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100,
                    cpuMs / wall.Elapsed.TotalMilliseconds * 100,
                    (GC.GetTotalAllocatedBytes(true) - allocated) / 1048576.0,
                    process.WorkingSet64 / 1048576.0, Interlocked.Read(ref bytes) - beforeBytes,
                    vm.SessionCards.Count(c => c.Model.IsRunning), window.IsVisible, window.IsActive,
                    window.Screens.All.Count, window.RenderScaling));
                Save();
                window.Close(); window = null;
                if (File.Exists(Path.Combine(directory, "settings.json"))) File.Delete(Path.Combine(directory, "settings.json"));
            }
        }
        catch (Exception ex) { failure = ex.ToString(); }
        finally
        {
            window?.Close();
            Save();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            desktop.Shutdown(failure is null ? 0 : 1);
        }
        void Save() => File.WriteAllText(Program.Report, JsonSerializer.Serialize(new
        {
            CapturedUtc = DateTime.UtcNow,
            Method = "Actual Avalonia desktop MainWindow + real PTY shells; 2.5s warmup, 6s sampling per case; 50ms sleep after each output line (nominal 20 lines/s per producing PTY). Application CPU excludes child-shell and desktop-compositor CPU. No input-method or multi-monitor interaction is automated.",
            Environment = new { Environment.OSVersion, Environment.ProcessorCount, RuntimeInformation.FrameworkDescription },
            Samples = samples, Failure = failure
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static string WorkloadArguments(bool output, int worker)
    {
        if (OperatingSystem.IsWindows())
        {
            var command = output
                ? $"$i=0; while ($true) {{ [Console]::WriteLine(([char]27)+'[32mworker {worker} '+$i+([char]27)+'[0m build output abcdefghijklmnopqrstuvwxyz 0123456789'); $i++; Start-Sleep -Milliseconds 50 }}"
                : "[Console]::WriteLine('idle-ready'); while ($true) { Start-Sleep -Seconds 1 }";
            return "-NoLogo -NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
        }
        var script = output ? $"i=0; while true; do printf '\\033[32mworker {worker} %s\\033[0m build output abcdefghijklmnopqrstuvwxyz 0123456789\\n' \"$i\"; i=$((i+1)); sleep .05; done" : "printf 'idle-ready\\n'; while true; do sleep 1; done";
        return "--noprofile --norc -c '" + script.Replace("'", "'\"'\"'") + "'";
    }
}
