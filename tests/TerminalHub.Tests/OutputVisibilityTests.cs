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

/// <summary>Dashboard mockup: the Output bottom bar is expanded by default and
/// carries only real app/session events (timestamp + level) — no demo rows.
/// Toggle still hides it and persists.</summary>
public class OutputVisibilityTests
{
    [AvaloniaFact]
    public void Default_OutputVisible()
    {
        Assert.True(new AppSettings().OutputVisible);

        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new IdleMonitor(), new SettingsStore(Path.Combine(dir, "settings.json")));
        Assert.True(vm.OutputVisible); // fresh install → output bar expanded
    }

    [AvaloniaFact]
    public void ToggleOff_PersistsAcrossReload()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "settings.json");
        try
        {
            var vm = new MainWindowViewModel(new IdleMonitor(), new SettingsStore(path));
            Assert.True(vm.OutputVisible);
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
    public async Task OutputPanel_VisibleByDefault_RendersRealEvent()
    {
        PtySessionFactory.UseMock = true;
        var window = new MainWindow { Width = 1440, Height = 900 };
        window.Show();
        await Task.Delay(600);
        var vm = (MainWindowViewModel)window.DataContext!;

        var panel = window.FindControl<Border>("OutputPanel")!;
        Assert.True(panel.IsVisible); // default-on without clicking 输出

        // Trigger a real app event: split creates one logged entry per pane.
        vm.ToggleSplitCommand.Execute(null);
        await Task.Delay(400);
        var entry = Assert.Single(vm.Dashboard.OutputLog,
            e => e.Source == "split" && e.Level == "info");
        Assert.False(string.IsNullOrWhiteSpace(entry.Message));

        // The row actually renders: ListBox realized a container with
        // timestamp + level + message bindings for the entry.
        var list = panel.GetVisualDescendants().OfType<ListBox>()
            .First(l => Equals(l.ItemsSource, vm.Dashboard.OutputLog) ||
                        l.ItemsSource == vm.Dashboard.OutputLog);
        Assert.Contains(list.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text == entry.Message);
        vm.ToggleSplitCommand.Execute(null);
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
