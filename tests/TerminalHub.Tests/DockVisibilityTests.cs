using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TerminalHub.App.Controls;
using TerminalHub.App.ViewModels;
using TerminalHub.App.Views;
using TerminalHub.Core.Monitoring;
using TerminalHub.Core.Settings;
using TerminalHub.Pty;
using Xunit;

namespace TerminalHub.Tests;

/// <summary>Dashboard mockup: the bottom glass dock is always visible with six
/// keys (新建/监控/SSH/日志/部署/设置) — not a collapsed「工具」pill. Default
/// mode is always-on; auto-hide/hidden stay reachable from settings.</summary>
public class DockVisibilityTests
{
    [AvaloniaFact]
    public void Default_IsAlwaysVisible()
    {
        Assert.Equal(1, new AppSettings().DockVisibilityMode);

        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new IdleMonitor(), new SettingsStore(Path.Combine(dir, "settings.json")));
        Assert.Equal(1, vm.DockVisibilityMode); // fresh install → dock always on
    }

    [AvaloniaFact]
    public void ModeChange_PersistsAcrossReload()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "settings.json");
        try
        {
            var vm = new MainWindowViewModel(new IdleMonitor(), new SettingsStore(path));
            foreach (var mode in new[] { 0, 2, 1 })
            {
                vm.DockVisibilityMode = mode;
                vm.PersistSettings();
                var reloaded = new SettingsStore(path).Load();
                Assert.Equal(mode, reloaded.DockVisibilityMode);
            }
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [AvaloniaFact]
    public async Task Dock_AlwaysVisible_SixKeys_LabelsMatchMockup()
    {
        PtySessionFactory.UseMock = true;
        var window = new MainWindow { Width = 1440, Height = 900 };
        window.Show();
        await Task.Delay(500);
        var vm = (MainWindowViewModel)window.DataContext!;
        Assert.Equal(1, vm.DockVisibilityMode);

        var dock = window.FindControl<DropletDock>("ActionDock")!;
        Assert.True(dock.IsVisible);
        Assert.Equal(1, dock.Reveal); // mode 1 → revealed, not collapsed
        Assert.False(window.FindControl<Border>("DockHint")!.IsVisible); // no 工具 pill

        var buttons = dock.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Classes.Contains("dock")).ToList();
        var labels = buttons
            .Select(b => b.GetVisualDescendants().OfType<TextBlock>().First().Text).ToList();
        Assert.Equal(new[] { "新建会话", "监控", "SSH", "日志", "部署", "设置" }, labels);

        // Every key is bound to the dock command and accepts its parameter.
        Assert.All(buttons, b => Assert.NotNull(b.Command));
        Assert.False(vm.SettingsOpen);
        buttons[^1].Command!.Execute("5"); // 设置
        Assert.True(vm.SettingsOpen);
        Assert.Equal(5, vm.DockHighlight); // active neon lands on 设置
        buttons[^1].Command!.Execute("5");
        Assert.False(vm.SettingsOpen);
        window.Close();
    }

    private sealed class IdleMonitor : ISystemMonitor
    {
        public SystemSample Current { get; } = new();
        public IReadOnlyList<ProcessInfo> Processes { get; } = [];
#pragma warning disable CS0067
        public event Action<ISystemMonitor>? Sampled;
#pragma warning restore CS0067
        public void Start(TimeSpan interval) { }
        public void Stop() { }
        public void Dispose() { }
    }
}
