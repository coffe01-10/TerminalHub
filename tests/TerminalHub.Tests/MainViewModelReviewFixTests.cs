using Avalonia.Headless.XUnit;
using TerminalHub.App.ViewModels;
using TerminalHub.Core.Monitoring;
using TerminalHub.Core.Sessions;
using TerminalHub.Core.Terminal;
using TerminalHub.Core.Settings;
using TerminalHub.Pty;
using Xunit;

namespace TerminalHub.Tests;

/// <summary>Regressions for the 2026-10 main-ViewModel review findings: an ssh
/// session's REMOTE cwd must never leak into local spawn dirs (restart/split
/// fill entries), the notification visibility check must follow the live pane
/// tree (Quad bottom panes, maximized pane), SystemMonitor.Dispose must wait
/// out an in-flight sampling callback, and raw Debug-line capture stays gated
/// until the Debug tab has been opened.</summary>
public class MainViewModelReviewFixTests
{
    private sealed class FakeMonitor : ISystemMonitor
    {
        public SystemSample Current { get; } = new();
        public IReadOnlyList<ProcessInfo> Processes { get; } = [];
        public event Action<ISystemMonitor>? Sampled;
        public int SubscriberCount => Sampled?.GetInvocationList().Length ?? 0;
        public void Start(TimeSpan interval) { }
        public void Stop() { }
        public void Dispose() { }
        public void Tick() => Sampled?.Invoke(this);
    }

    private static async Task<bool> Until(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(8));
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(25);
        }
        return condition();
    }

    private static string TempSettingsPath(out string dir)
    {
        dir = Path.Combine(Path.GetTempPath(), "th-vmfix-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "settings.json");
    }

    /// <summary>What CreateSessionAsync falls back to when the caller passes an
    /// empty cwd — the value a respawn/split entry must land on for ssh.</summary>
    private static string DefaultSpawnDir() => OperatingSystem.IsWindows()
        ? "C:\\"
        : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static async Task<MainWindowViewModel> SpawnSingleSessionVmAsync(SettingsStore store)
    {
        PtySessionFactory.UseMock = true;
        var vm = new MainWindowViewModel(new FakeMonitor(), store, sshAvailable: () => true);
        vm.Settings.StartupSessions.Clear();
        await vm.SpawnStartupSessionsAsync();   // empty startup → one local session
        Assert.True(await Until(() => vm.SessionCards.Count == 1));
        return vm;
    }

    /// <summary>Spawn an ssh session, make it active and report a REMOTE cwd
    /// via OSC 7 — the state the spawn entries must not treat as a local dir.</summary>
    private static async Task<TerminalSessionModel> ConnectSshWithRemoteCwdAsync(MainWindowViewModel vm)
    {
        vm.Ssh.EditHost = "box.example";
        vm.Ssh.AddOrUpdateCommand.Execute(null);
        vm.Ssh.ConnectCommand.Execute(vm.Ssh.Hosts[0]);
        Assert.True(await Until(() => vm.SessionCards.Any(c => c.Model.Tag == SessionTag.Ssh)));
        var ssh = vm.SessionCards.First(c => c.Model.Tag == SessionTag.Ssh).Model;
        Assert.True(await Until(() => ReferenceEquals(vm.ActiveSession, ssh)));
        ssh.Emulator.Parser.Feed("\u001b]7;file:///srv/app\u0007");
        Assert.True(await Until(() => ssh.WorkingDirectory == "/srv/app"));
        return ssh;
    }

    // ---- M1: ssh remote cwd never becomes a local spawn dir ----

    [AvaloniaFact]
    public async Task RestartSession_SshRemoteCwd_RespawnsWithLocalDefaultDir()
    {
        var settingsPath = TempSettingsPath(out var dir);
        MainWindowViewModel? vm = null;
        try
        {
            vm = await SpawnSingleSessionVmAsync(new SettingsStore(settingsPath));
            var ssh = await ConnectSshWithRemoteCwdAsync(vm);
            var oldId = ssh.Id;
            ssh.Emulator.SendText("exit\r");   // mock ssh client exits cleanly
            Assert.True(await Until(() => ssh.Pty.ExitCode == 0));

            await vm.RestartSession(vm.SessionCards.First(c => ReferenceEquals(c.Model, ssh)));

            Assert.True(await Until(() => vm.SessionCards.Any(c =>
                c.Model.Tag == SessionTag.Ssh && c.Model.Id != oldId)));
            var respawn = vm.SessionCards
                .First(c => c.Model.Tag == SessionTag.Ssh && c.Model.Id != oldId).Model;
            Assert.NotEqual("/srv/app", respawn.WorkingDirectory);
            Assert.Equal(DefaultSpawnDir(), respawn.WorkingDirectory);
        }
        finally
        {
            vm?.Dispose();
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [AvaloniaFact]
    public async Task SetSplitLayout_ActiveSshRemoteCwd_FillsNewPaneWithLocalDefaultDir()
    {
        var settingsPath = TempSettingsPath(out var dir);
        MainWindowViewModel? vm = null;
        try
        {
            vm = await SpawnSingleSessionVmAsync(new SettingsStore(settingsPath));
            var ssh = await ConnectSshWithRemoteCwdAsync(vm);
            Assert.True(await Until(() => vm.ActiveWorkingDirectory == "/srv/app"));
            // Drop the spare local session so the split layout must CREATE pane 2.
            vm.CloseSessionCommand.Execute(vm.SessionCards.First(c => !ReferenceEquals(c.Model, ssh)));
            Assert.True(await Until(() => vm.SessionCards.All(c => ReferenceEquals(c.Model, ssh))));

            await vm.SetSplitLayoutAsync("Horizontal");

            Assert.True(await Until(() => vm.IsSplit && vm.SessionCards.Count == 2));
            var created = vm.SessionCards.First(c => !ReferenceEquals(c.Model, ssh)).Model;
            Assert.NotEqual("/srv/app", created.WorkingDirectory);
            Assert.Equal(DefaultSpawnDir(), created.WorkingDirectory);
        }
        finally
        {
            vm?.Dispose();
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [AvaloniaFact]
    public async Task SplitPane_SshTarget_CreatesNewSessionWithLocalDefaultDir()
    {
        var settingsPath = TempSettingsPath(out var dir);
        MainWindowViewModel? vm = null;
        try
        {
            vm = await SpawnSingleSessionVmAsync(new SettingsStore(settingsPath));
            var ssh = await ConnectSshWithRemoteCwdAsync(vm);
            vm.CloseSessionCommand.Execute(vm.SessionCards.First(c => !ReferenceEquals(c.Model, ssh)));
            Assert.True(await Until(() => vm.SessionCards.All(c => ReferenceEquals(c.Model, ssh))));

            await vm.SplitPaneAsync(0, vertical: false);   // target = active ssh session

            Assert.True(await Until(() => vm.SessionCards.Count == 2));
            var created = vm.SessionCards.First(c => !ReferenceEquals(c.Model, ssh)).Model;
            Assert.NotEqual("/srv/app", created.WorkingDirectory);
            Assert.Equal(DefaultSpawnDir(), created.WorkingDirectory);
        }
        finally
        {
            vm?.Dispose();
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    // ---- M7: banner visibility follows the live pane tree ----

    /// <summary>Drive the shell-integration marks so Emulator.CommandCompleted
    /// fires exactly the way a real OSC 133 shell would report it.</summary>
    private static void CompleteCommand(TerminalEmulator emu)
    {
        emu.Parser.Feed("\u001b]133;C\u0007");
        emu.Parser.Feed("\u001b]133;D;0\u0007");
    }

    [AvaloniaFact]
    public async Task CommandCompletion_VisibilityFollowsLivePaneTree()
    {
        var settingsPath = TempSettingsPath(out var dir);
        MainWindowViewModel? vm = null;
        try
        {
            PtySessionFactory.UseMock = true;
            vm = new MainWindowViewModel(new FakeMonitor(), new SettingsStore(settingsPath));
            vm.Settings.NotifyCommandCompletion = true;
            await vm.SpawnStartupSessionsAsync();
            Assert.True(await Until(() => vm.SessionCards.Count == 3));
            await vm.NewSessionCommand.ExecuteAsync(null);
            await vm.NewSessionCommand.ExecuteAsync(null);
            Assert.True(await Until(() => vm.SessionCards.Count == 5));

            await vm.SetSplitLayoutAsync("Quad");
            Assert.True(await Until(() => vm.PaneCount == 4
                && vm.BottomLeftPane is not null && vm.BottomRightPane is not null));
            var panes = Enumerable.Range(0, 4).Select(vm.GetPane).ToArray();
            var hidden = vm.SessionCards.First(c => !panes.Contains(c.Model)).Model;

            // A background (non-visible) session still banners.
            CompleteCommand(hidden.Emulator);
            Assert.True(await Until(() => vm.NotificationVisible), "hidden session should banner");
            vm.DismissNotificationCommand.Execute(null);

            // Quad's bottom-left pane IS on screen — the old LeftPane||RightPane
            // check missed it and bugged a banner for a visible terminal.
            CompleteCommand(vm.BottomLeftPane!.Emulator);
            await Task.Delay(400);
            Assert.False(vm.NotificationVisible, "visible quad bottom pane must not banner");

            // Maximizing pane 0 hides the bottom-left pane again → banner.
            vm.FocusPane(0);
            vm.TogglePaneMaximizedCommand.Execute(null);
            CompleteCommand(vm.BottomLeftPane!.Emulator);
            Assert.True(await Until(() => vm.NotificationVisible), "pane hidden by maximize should banner");
        }
        finally
        {
            vm?.Dispose();
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    // ---- L19: monitor disposal waits out callbacks; VM unsubscribes ----

    [Fact]
    public void SystemMonitor_Dispose_WaitsForInFlightSampleCallback()
    {
        var monitor = new SystemMonitor();
        var inCallback = new AutoResetEvent(false);
        var release = new AutoResetEvent(false);
        monitor.Sampled += _ =>
        {
            inCallback.Set();
            release.WaitOne(TimeSpan.FromSeconds(10));
        };
        monitor.Start(TimeSpan.FromMilliseconds(5));
        try
        {
            Assert.True(inCallback.WaitOne(TimeSpan.FromSeconds(10)), "first sample never ran");
            var dispose = Task.Run(() => monitor.Dispose());
            Thread.Sleep(300);
            Assert.False(dispose.IsCompleted,
                "Dispose must block while a sampling callback is still running");
            release.Set();
            Assert.True(dispose.Wait(TimeSpan.FromSeconds(10)),
                "Dispose never returned after the callback finished");
        }
        finally
        {
            release.Set();
            monitor.Dispose();
        }
    }

    [AvaloniaFact]
    public void Dispose_UnsubscribesMonitorSampling()
    {
        PtySessionFactory.UseMock = true;
        var settingsPath = TempSettingsPath(out var dir);
        var monitor = new FakeMonitor();
        // Its own throwaway store — Dispose persists settings; never the user's.
        var vm = new MainWindowViewModel(monitor, new SettingsStore(settingsPath));
        try
        {
            var before = monitor.SubscriberCount;   // shell VM + DashboardViewModel
            Assert.True(before >= 2);
            vm.Dispose();
            Assert.Equal(before - 1, monitor.SubscriberCount);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    // ---- L21: raw Debug capture gated until the Debug tab is opened ----

    [AvaloniaFact]
    public async Task RawDebugCapture_GatedUntilDebugTabOpened()
    {
        var settingsPath = TempSettingsPath(out var dir);
        MainWindowViewModel? vm = null;
        try
        {
            PtySessionFactory.UseMock = true;
            vm = new MainWindowViewModel(new FakeMonitor(), new SettingsStore(settingsPath));
            await vm.SpawnStartupSessionsAsync();
            Assert.True(await Until(() => vm.ActiveSession is not null));
            Assert.False(vm.Dashboard.DebugCaptureEnabled);

            // PTY output flows to the Output log but NOT to the Debug tab.
            vm.ActiveSession!.Emulator.SendText("debug-gate-probe-1\r");
            Assert.True(await Until(() => vm.Dashboard.OutputLog.Count > 0));
            await Task.Delay(300);
            Assert.Empty(vm.Dashboard.DebugLog);

            // Opening the Debug tab flips the sticky gate; capture resumes live.
            vm.Dashboard.SelectedBottomTab = 1;
            Assert.True(vm.Dashboard.DebugCaptureEnabled);
            vm.ActiveSession!.Emulator.SendText("debug-gate-probe-2\r");
            Assert.True(await Until(() => vm.Dashboard.DebugLog.Count > 0));
        }
        finally
        {
            vm?.Dispose();
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }
}
