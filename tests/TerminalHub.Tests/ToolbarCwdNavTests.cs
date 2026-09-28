using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TerminalHub.App.ViewModels;
using TerminalHub.App.Views;
using TerminalHub.Core.Monitoring;
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
            Assert.Contains(buttons, b => b.Content as string == "←" && ReferenceEquals(b.Command, vm.CwdBackCommand));
            Assert.Contains(buttons, b => b.Content as string == "→" && ReferenceEquals(b.Command, vm.CwdForwardCommand));
            Assert.Contains(buttons, b => b.Content as string == "⟳" && ReferenceEquals(b.Command, vm.RefreshCwdCommand));
            Assert.Contains(buttons, b => b.Content as string == "⟳" && ReferenceEquals(b.Command, vm.Files.RefreshCommand));
            win.Close();
        }
        finally
        {
            vm.Dispose();
        }
    }

    private static string CwdNorm(string path) =>
        TerminalHub.Core.Sessions.CwdHistory.Normalize(path);
}
