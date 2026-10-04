using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TerminalHub.App.ViewModels;
using TerminalHub.App.Views;
using TerminalHub.Core.Monitoring;
using TerminalHub.Core.Settings;
using TerminalHub.Pty;
using Xunit;

namespace TerminalHub.Tests;

/// <summary>Platform Output defaults, persisted choices and real rendered app events.</summary>
public class OutputVisibilityTests
{
    [AvaloniaFact]
    public void Default_OutputVisibility_RespectsPlatform()
    {
        Assert.Equal(!OperatingSystem.IsWindows(), new AppSettings().OutputVisible);

        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new IdleMonitor(), new SettingsStore(Path.Combine(dir, "settings.json")));
        Assert.Equal(!OperatingSystem.IsWindows(), vm.OutputVisible);
    }

    [AvaloniaFact]
    public void ToggleOff_PersistsAcrossReload()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "settings.json");
        try
        {
            var vm = new MainWindowViewModel(new IdleMonitor(), new SettingsStore(path));
            vm.OutputVisible = true;
            vm.ToggleOutputCommand.Execute(null); // user hides it
            Assert.False(vm.OutputVisible);
            vm.PersistSettings();
            Assert.False(new SettingsStore(path).Load().OutputVisible);

            var vm2 = new MainWindowViewModel(new IdleMonitor(), new SettingsStore(path));
            Assert.False(vm2.OutputVisible); // choice survives restart
            vm2.ToggleOutputCommand.Execute(null);
            vm2.PersistSettings();
            Assert.True(new SettingsStore(path).Load().OutputVisible);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [AvaloniaFact]
    public async Task OutputPanel_ExplicitlyOpened_RendersRealEvent()
    {
        PtySessionFactory.UseMock = true;
        var window = new MainWindow { Width = 1440, Height = 900 };
        window.Show();
        await Task.Delay(600);
        var vm = (MainWindowViewModel)window.DataContext!;

        var panel = window.FindControl<Border>("OutputPanel")!;
        Assert.Equal(!OperatingSystem.IsWindows(), panel.IsVisible);
        vm.OutputVisible = true;
        Assert.True(panel.IsVisible);

        // Trigger a real app event through the product's app-event pipeline.
        // (Split toggling stopped logging entries when it became the layout switch.)
        vm.Dashboard.AppendAppOutput("info", "split opened", "split");
        await Task.Delay(400);
        var entry = Assert.Single(vm.Dashboard.OutputLog,
            e => e.Source == "split" && e.Level == "info");
        Assert.False(string.IsNullOrWhiteSpace(entry.Message));

        // The row actually renders: ListBox realized a container with
        // timestamp + level + message bindings for the entry.
        var list = window.FindControl<ListBox>("OutputList")!;
        Assert.Contains(list.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text == entry.Message);
        window.Close();
    }

    [AvaloniaFact]
    public async Task LevelFilter_DefaultAll_FiltersWithoutLosingEntries()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new IdleMonitor(), new SettingsStore(Path.Combine(dir, "settings.json")));
        Assert.Equal(0, vm.Dashboard.OutputLevelFilter); // 默认「全部」

        vm.Dashboard.AppendOutput("info", "split opened", "split");
        vm.Dashboard.AppendOutput("warn", "profile missing", "deploy");
        vm.Dashboard.AppendOutput("error", "spawn failed", "pty");
        await Task.Delay(300); // AppendOutput flushes through the dispatcher
        Assert.Equal(3, vm.Dashboard.OutputLog.Count);
        Assert.Equal(3, vm.Dashboard.VisibleOutput.Count); // 全部 = 不过滤

        vm.Dashboard.OutputLevelFilter = 3; // error only
        Assert.Equal(3, vm.Dashboard.OutputLog.Count);      // 切换不丢条目
        var only = Assert.Single(vm.Dashboard.VisibleOutput);
        Assert.Equal("spawn failed", only.Message);

        vm.Dashboard.OutputLevelFilter = 2;
        Assert.Equal("profile missing", Assert.Single(vm.Dashboard.VisibleOutput).Message);

        vm.Dashboard.OutputLevelFilter = 0; // back to 全部
        Assert.Equal(3, vm.Dashboard.VisibleOutput.Count);
    }

    [AvaloniaFact]
    public void LevelFilter_PersistsAcrossReload()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "settings.json");
        try
        {
            var vm = new MainWindowViewModel(new IdleMonitor(), new SettingsStore(path));
            vm.Dashboard.OutputLevelFilter = 2;
            vm.PersistSettings();
            Assert.Equal(2, new SettingsStore(path).Load().OutputLevelFilter);
            var vm2 = new MainWindowViewModel(new IdleMonitor(), new SettingsStore(path));
            Assert.Equal(2, vm2.Dashboard.OutputLevelFilter);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [AvaloniaFact]
    public async Task OutputPanel_LevelCombo_VisibleAndFilters()
    {
        PtySessionFactory.UseMock = true;
        var window = new MainWindow { Width = 1440, Height = 900 };
        window.Show();
        await Task.Delay(600);
        var vm = (MainWindowViewModel)window.DataContext!;

        vm.OutputVisible = true;
        await Task.Delay(100);
        var combo = window.FindControl<ComboBox>("OutputLevelCombo")!;
        Assert.True(combo.IsEffectivelyVisible);
        Assert.Equal(0, combo.SelectedIndex); // 全部

        vm.Dashboard.AppendAppOutput("info", "split opened", "split");  // real event → info row
        await Task.Delay(400);
        vm.Dashboard.AppendOutput("warn", "deploy profile missing", "deploy");
        await Task.Delay(300);
        Assert.True(vm.Dashboard.VisibleOutput.Count >= 2);

        combo.SelectedIndex = 2; // warn
        await Task.Delay(200);
        Assert.All(vm.Dashboard.VisibleOutput, e => Assert.Equal("warn", e.Level));

        var list = window.FindControl<ListBox>("OutputList")!;
        var levelTags = list.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Text is "[info]" or "[warn]" or "[error]").ToList();
        Assert.NotEmpty(levelTags); // rows realized, not a vacuous pass
        Assert.All(levelTags, t => Assert.Equal("[warn]", t.Text));
        combo.SelectedIndex = 0;
        await Task.Delay(150);
        Assert.Equal(vm.Dashboard.OutputLog.Count, vm.Dashboard.VisibleOutput.Count);
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
