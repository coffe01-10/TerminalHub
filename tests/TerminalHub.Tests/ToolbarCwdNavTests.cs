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

public class ToolbarCwdNavTests
{
    private sealed class FakeMonitor : ISystemMonitor
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

    private static async Task Until(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(8));
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(25);
        }
    }

    private static async Task<MainWindowViewModel> SpawnVmAsync()
    {
        PtySessionFactory.UseMock = true;
        var vm = new MainWindowViewModel(new FakeMonitor());
        await vm.SpawnStartupSessionsAsync();
        await Until(() => vm.ActiveSession is not null);
        Assert.NotNull(vm.ActiveSession);
        return vm;
    }

    [AvaloniaFact]
    public async Task CwdBackForward_UsesHistory_AndSyncsFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "th-cwd-" + Guid.NewGuid().ToString("N"));
        var a = Path.Combine(root, "alpha");
        var b = Path.Combine(root, "beta");
        Directory.CreateDirectory(a);
        Directory.CreateDirectory(b);
        MainWindowViewModel? vm = null;
        try
        {
            vm = await SpawnVmAsync();
            var emu = vm.ActiveSession!.Emulator;
            var fullA = Path.GetFullPath(a);
            var fullB = Path.GetFullPath(b);

            emu.Parser.Feed($"\u001b]7;file://{fullA}\u0007");
            await Until(() => string.Equals(
                CwdNorm(vm.ActiveSession.WorkingDirectory), CwdNorm(fullA), StringComparison.Ordinal));
            emu.Parser.Feed($"\u001b]7;file://{fullB}\u0007");
            await Until(() => string.Equals(
                CwdNorm(vm.ActiveSession.WorkingDirectory), CwdNorm(fullB), StringComparison.Ordinal));
            // CanCwdBack/CanCwdForward update on the UI thread (posted) — wait for it.
            await Until(() => vm.CanCwdBack);

            Assert.True(vm.CanCwdBack);
            Assert.False(vm.CanCwdForward);

            vm.CwdBackCommand.Execute(null);
            Assert.Equal(CwdNorm(fullA), CwdNorm(vm.ActiveSession.WorkingDirectory));
            Assert.Equal(CwdNorm(fullA), CwdNorm(vm.Files.CurrentPath));
            Assert.True(vm.CanCwdForward);

            vm.CwdForwardCommand.Execute(null);
            Assert.Equal(CwdNorm(fullB), CwdNorm(vm.ActiveSession.WorkingDirectory));
            Assert.Equal(CwdNorm(fullB), CwdNorm(vm.Files.CurrentPath));
            Assert.False(vm.CanCwdForward);
            Assert.True(vm.CanCwdBack);
        }
        finally
        {
            vm?.Dispose();
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [AvaloniaFact]
    public async Task RefreshCwd_SyncsToolbarAndFiles_FromSessionWorkingDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "th-cwd-r-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        MainWindowViewModel? vm = null;
        try
        {
            vm = await SpawnVmAsync();
            // Mock PTY has no ProcessId → Refresh falls back to session WorkingDirectory.
            vm.ActiveSession!.WorkingDirectory = root;
            vm.RefreshCwdCommand.Execute(null);

            Assert.Equal(CwdNorm(root), CwdNorm(vm.Files.CurrentPath));
            Assert.Contains(Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar)), vm.Breadcrumb);
        }
        finally
        {
            vm?.Dispose();
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [AvaloniaFact]
    public async Task ToolbarButtons_BindCwdCommands_FilesRefreshUntouched()
    {
        var vm = await SpawnVmAsync();
        try
        {
            var win = new MainWindow { DataContext = vm };
            win.Show();
            await Task.Delay(50);

            var buttons = win.GetVisualDescendants().OfType<Button>().ToList();
            // Toolbar buttons render PathIcon content — identify them by tooltip text.
            Assert.Contains(buttons, b => ToolTip.GetTip(b) as string == "后退目录历史" && ReferenceEquals(b.Command, vm.CwdBackCommand));
            Assert.Contains(buttons, b => ToolTip.GetTip(b) as string == "前进目录历史" && ReferenceEquals(b.Command, vm.CwdForwardCommand));
            Assert.Contains(buttons, b => ToolTip.GetTip(b) as string == "刷新当前目录" && ReferenceEquals(b.Command, vm.RefreshCwdCommand));
            Assert.Contains(buttons, b => ToolTip.GetTip(b) as string == "刷新" && ReferenceEquals(b.Command, vm.Files.RefreshCommand));
            win.Close();
        }
        finally
        {
            vm.Dispose();
        }
    }

    /// <summary>Regression: an ssh session's OSC 7 cwd is a REMOTE path — it must
    /// stay verbatim (no local GetFullPath mangling), must not navigate the local
    /// Files panel, and must not be persisted as a local spawn dir.</summary>
    [AvaloniaFact]
    public async Task Ssh_RemoteCwd_StaysVerbatim_FilesAndSnapshotUnaffected()
    {
        PtySessionFactory.UseMock = true;
        var dir = Path.Combine(Path.GetTempPath(), "th-ssh-cwd-" + Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(dir, "settings.json");
        Directory.CreateDirectory(dir);
        MainWindowViewModel? vm = null;
        try
        {
            // The session is a MockPty — the Connect gate only needs the ssh
            // binary to LOOK installed; CI/dev boxes may not have openssh-client.
            vm = new MainWindowViewModel(new FakeMonitor(), new SettingsStore(settingsPath),
                sshAvailable: () => true);
            await vm.SpawnStartupSessionsAsync();
            await Until(() => vm.SessionCards.Count > 0);

            // The real SSH panel path → spawns a session tagged SessionTag.Ssh.
            vm.Ssh.EditHost = "box.example";
            vm.Ssh.AddOrUpdateCommand.Execute(null);
            vm.Ssh.ConnectCommand.Execute(vm.Ssh.Hosts[0]);
            await Until(() => vm.SessionCards.Any(c => c.Model.Tag == TerminalHub.Core.Sessions.SessionTag.Ssh));
            var ssh = vm.SessionCards.First(c => c.Model.Tag == TerminalHub.Core.Sessions.SessionTag.Ssh).Model;
            await Until(() => ReferenceEquals(vm.ActiveSession, ssh));

            var localDir = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
            vm.Files.NavigateTo(localDir);

            ssh.Emulator.Parser.Feed("\u001b]7;file:///srv/app\u0007");
            await Until(() => ssh.WorkingDirectory == "/srv/app");
            Assert.Equal("/srv/app", ssh.WorkingDirectory);    // verbatim — not "C:\srv\app"
            Assert.Equal(Path.GetFullPath(localDir), vm.Files.CurrentPath); // Files stays local

            // Remote cwd history is verbatim too — back sends a remote-valid cd.
            ssh.Emulator.Parser.Feed("\u001b]7;file:///var/log\u0007");
            await Until(() => ssh.WorkingDirectory == "/var/log" && vm.CanCwdBack);
            vm.CwdBackCommand.Execute(null);
            Assert.Equal("/srv/app", ssh.WorkingDirectory);

            vm.PersistSettings();
            var saved = new SettingsStore(settingsPath).Load();
            var sshEntry = saved.Workspace.Sessions.Single(s => s.Shell == "ssh");
            Assert.Equal("", sshEntry.WorkingDirectory);       // remote path never becomes a local spawn dir
        }
        finally
        {
            vm?.Dispose();
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    private static string CwdNorm(string path) =>
        TerminalHub.Core.Sessions.CwdHistory.Normalize(path);
}
