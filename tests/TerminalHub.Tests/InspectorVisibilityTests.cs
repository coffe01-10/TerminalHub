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

/// <summary>Dashboard mockup: the right inspector (Processes/Files/Logs/SSH)
/// is expanded on cold start at mockup width (~300-326). An explicitly
/// persisted InspectorVisible=false still keeps it closed.</summary>
public class InspectorVisibilityTests
{
    [AvaloniaFact]
    public void Default_InspectorVisible_OnColdStart()
    {
        Assert.Null(new AppSettings().InspectorVisible); // unset = default-on

        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var vm = new MainWindowViewModel(new IdleMonitor(), new SettingsStore(Path.Combine(dir, "settings.json")));
        Assert.True(vm.InspectorVisible); // fresh install → inspector expanded
        Assert.Equal(0, vm.SelectedRightTab); // Processes tab first, like the mockup
    }

    [AvaloniaFact]
    public void PersistedFalse_StaysClosed()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "settings.json");
        try
        {
            new SettingsStore(path).Save(new AppSettings { InspectorVisible = false });
            using var vm = new MainWindowViewModel(new IdleMonitor(), new SettingsStore(path));
            Assert.False(vm.InspectorVisible); // explicit user choice wins
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [AvaloniaFact]
    public void Toggle_PersistsAcrossReload()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "settings.json");
        try
        {
            using var vm = new MainWindowViewModel(new IdleMonitor(), new SettingsStore(path));
            vm.ToggleInspectorCommand.Execute(null); // user hides it
            Assert.False(vm.InspectorVisible);
            vm.PersistSettings();
            Assert.Equal(false, new SettingsStore(path).Load().InspectorVisible);

            using var vm2 = new MainWindowViewModel(new IdleMonitor(), new SettingsStore(path));
            Assert.False(vm2.InspectorVisible); // choice survives restart
            vm2.ToggleInspectorCommand.Execute(null);
            vm2.PersistSettings();
            Assert.Equal(true, new SettingsStore(path).Load().InspectorVisible);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [AvaloniaFact]
    public async Task Processes_FillsFromRealMonitor()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var monitor = new SystemMonitor();
        using var vm = new MainWindowViewModel(monitor, new SettingsStore(Path.Combine(dir, "settings.json")));
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(6);
        while (vm.Dashboard.Processes.Count == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(150);
        Assert.NotEmpty(vm.Dashboard.Processes); // real /proc or Win32 sampling
        Assert.All(vm.Dashboard.Processes, p =>
        {
            Assert.True(p.Pid > 0);
            Assert.False(string.IsNullOrWhiteSpace(p.Name));
        });
    }

    [AvaloniaFact]
    public async Task InspectorPanel_VisibleByDefault_RendersProcessRows()
    {
        PtySessionFactory.UseMock = true;
        var window = new MainWindow { Width = 1440, Height = 900 };
        try
        {
            window.Show();
            var vm = (MainWindowViewModel)window.DataContext!;
            Assert.True(vm.InspectorVisible);

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(6);
            while (vm.Dashboard.Processes.Count == 0 && DateTime.UtcNow < deadline)
                await Task.Delay(150);
            Assert.NotEmpty(vm.Dashboard.Processes);

            await Task.Delay(300); // let bindings realize rows
            var list = window.GetVisualDescendants().OfType<ListBox>()
                .First(l => ReferenceEquals(l.ItemsSource, vm.Dashboard.Processes));
            Assert.True(list.IsEffectivelyVisible);
            Assert.NotEmpty(list.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => int.TryParse(t.Text, out _))); // PID column realized
        }
        finally { window.Close(); PtySessionFactory.UseMock = false; }
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
