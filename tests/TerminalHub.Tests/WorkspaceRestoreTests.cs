using Avalonia.Headless.XUnit;
using TerminalHub.App.ViewModels;
using TerminalHub.Core.Monitoring;
using TerminalHub.Core.Settings;
using TerminalHub.Pty;
using Xunit;

namespace TerminalHub.Tests;

/// <summary>Workspace save/restore: order, names, split layout and active
/// session survive a settings round-trip as fresh mock processes.</summary>
public class WorkspaceRestoreTests
{
    private sealed class FakeMonitor : ISystemMonitor
    {
        public SystemSample Current { get; } = new();
        public IReadOnlyList<ProcessInfo> Processes { get; } = [];
#pragma warning disable CS0067 // required by interface; unused here
        public event Action<ISystemMonitor>? Sampled;
#pragma warning restore CS0067
        public void Start(TimeSpan interval) { }
        public void Stop() { }
        public void Dispose() { }
    }

    /// <summary>Polls until <paramref name="condition"/> holds; timing out fails
    /// here instead of deferring to whatever assertion — if any — comes next.</summary>
    private static async Task Until(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(8));
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(25);
        }
        Assert.True(condition(), "Timed out waiting for the workspace state.");
    }

    private static string TempDir()
        => Path.Combine(Path.GetTempPath(), "th-workspace-" + Guid.NewGuid().ToString("N"));

    [AvaloniaFact]
    public async Task Workspace_SavesSessionOrderNamesCwd_RestoresOnRestart()
    {
        PtySessionFactory.UseMock = true;
        var dir = TempDir();
        var path = Path.Combine(dir, "settings.json");
        try
        {
            var vm = new MainWindowViewModel(new FakeMonitor(), new SettingsStore(path));
            await vm.SpawnStartupSessionsAsync();          // default profile: 3 sessions
            await Until(() => vm.SessionCards.Count == 3);
            vm.RenameSession((vm.SessionCards[0], "Alpha"));
            vm.SessionCards[0].Model.Emulator.ColorScheme = TerminalHub.Core.Terminal.TerminalColorScheme.Dark;
            vm.MoveSessionCard(vm.SessionCards[2], vm.SessionCards[0]); // drag last → front
            vm.Dispose();                                   // snapshot runs inside save

            var saved = new SettingsStore(path).Load();
            Assert.Equal(3, saved.Workspace.Sessions.Count);
            Assert.Equal("Terminal 03", saved.Workspace.Sessions[0].Name); // moved to front
            Assert.Equal("Alpha", saved.Workspace.Sessions[1].Name);
            Assert.Equal(TerminalHub.Core.Terminal.TerminalColorScheme.Dark, saved.Workspace.Sessions[1].ColorScheme);
            Assert.Equal(0, saved.Workspace.ActiveIndex);   // Terminal 03 stayed active → index 0
            Assert.False(saved.Workspace.IsSplit);

            var vm2 = new MainWindowViewModel(new FakeMonitor(), new SettingsStore(path));
            await vm2.SpawnStartupSessionsAsync();          // workspace restore, not StartupSessions
            await Until(() => vm2.SessionCards.Count == 3);
            Assert.Equal(new[] { "Terminal 03", "Alpha", "Terminal 02" },
                vm2.SessionCards.Select(c => c.Name).ToArray());
            Assert.Equal(TerminalHub.Core.Terminal.TerminalColorScheme.Dark,
                vm2.SessionCards[1].Model.Emulator.ColorScheme);
            vm2.Dispose();
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Workspace_SplitLayoutAndActiveSession_Restored()
    {
        PtySessionFactory.UseMock = true;
        var dir = TempDir();
        var path = Path.Combine(dir, "settings.json");
        try
        {
            var vm = new MainWindowViewModel(new FakeMonitor(), new SettingsStore(path));
            await vm.SpawnStartupSessionsAsync();
            await Until(() => vm.SessionCards.Count == 3);
            vm.ToggleSplitCommand.Execute(null);            // split: left=active, right=next
            await Until(() => vm.IsSplit);
            vm.FocusPane(1);                                 // focus right pane
            var left = vm.LeftPane!.Name;
            var right = vm.RightPane!.Name;
            vm.Dispose();

            var vm2 = new MainWindowViewModel(new FakeMonitor(), new SettingsStore(path));
            await vm2.SpawnStartupSessionsAsync();
            await Until(() => vm2.SessionCards.Count == 3 && vm2.IsSplit);
            Assert.Equal(left, vm2.LeftPane!.Name);
            Assert.Equal(right, vm2.RightPane!.Name);
            Assert.Equal(1, vm2.FocusedPane);
            Assert.Same(vm2.RightPane, vm2.ActiveSession);
            vm2.Dispose();
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Workspace_EmptyState_FallsBackToStartupSessions()
    {
        PtySessionFactory.UseMock = true;
        var dir = TempDir();
        var path = Path.Combine(dir, "settings.json");
        try
        {
            // Seed a settings file with two explicit startup sessions and no workspace.
            var seed = new AppSettings
            {
                StartupSessions =
                [
                    new StartupSession { Name = "Seed A" },
                    new StartupSession { Name = "Seed B" },
                ],
            };
            new SettingsStore(path).Save(seed);

            var vm = new MainWindowViewModel(new FakeMonitor(), new SettingsStore(path));
            await vm.SpawnStartupSessionsAsync();
            await Until(() => vm.SessionCards.Count == 2);
            Assert.Equal(new[] { "Seed A", "Seed B" }, vm.SessionCards.Select(c => c.Name).ToArray());
            vm.Dispose();
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
