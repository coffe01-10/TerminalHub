using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.Headless;
using TerminalHub.App.Views;
using TerminalHub.App.ViewModels;
using TerminalHub.Core.Settings;
using TerminalHub.Pty;
using Xunit;

namespace TerminalHub.Tests;

public class ShellStartupTests
{
    /// <summary>The fake "installed" shell must be a candidate ShellDiscovery
    /// would offer on this OS — cmd.exe on Windows, bash on Linux.</summary>
    private static readonly string FakeShell = OperatingSystem.IsWindows() ? "cmd.exe" : "bash";
    private static readonly ShellKind FakeShellKind = OperatingSystem.IsWindows() ? ShellKind.Cmd : ShellKind.Bash;

    [AvaloniaFact]
    public async Task WorkspaceUsingInstalledShell_DoesNotRequireUnusedDefault()
    {
        var directory = Path.Combine(Path.GetTempPath(), "th-shell-ready-" + Guid.NewGuid().ToString("N"));
        var previousMock = PtySessionFactory.UseMock;
        PtySessionFactory.UseMock = true;
        try
        {
            var store = new SettingsStore(Path.Combine(directory, "settings.json"));
            store.Save(new AppSettings { Workspace = new WorkspaceState
            {
                Sessions = [new WorkspaceSession { Name = "cmd work", Shell = FakeShell }]
            } });
            using var vm = new MainWindowViewModel(settingsStore: store, shellAvailable: command => command == FakeShell);
            await vm.SpawnStartupSessionsAsync();
            await Task.Delay(30);
            Assert.False(vm.ShellSetupOpen);
            Assert.Equal("cmd work", Assert.Single(vm.SessionCards).Name);
        }
        finally
        {
            PtySessionFactory.UseMock = previousMock;
            Directory.Delete(directory, true);
        }
    }
    [AvaloniaFact]
    public async Task ShellPicker_IsVisible_AndClosingKeepsUnrestoredWorkspace()
    {
        var directory = Path.Combine(Path.GetTempPath(), "th-shell-ui-" + Guid.NewGuid().ToString("N"));
        var previousMock = PtySessionFactory.UseMock;
        PtySessionFactory.UseMock = true;
        try
        {
            var store = new SettingsStore(Path.Combine(directory, "settings.json"));
            store.Save(new AppSettings { Workspace = new WorkspaceState
            {
                Sessions = [new WorkspaceSession { Name = "saved work", Shell = "pwsh" }]
            } });
            var window = new MainWindow(store, shellAvailable: command => command == FakeShell) { Width = 1100, Height = 680 };
            try
            {
                window.Show();
                await Task.Delay(50);
                Assert.True(window.FindControl<Border>("ShellSetupPanel")!.IsVisible);
                Assert.Single(window.FindControl<ComboBox>("StartupShellPicker")!.Items);
                var capture = Environment.GetEnvironmentVariable("TERMINALHUB_SHELL_CAPTURE");
                if (capture is not null) window.CaptureRenderedFrame()!.Save(capture);
            }
            finally { window.Close(); }
            Assert.Equal("saved work", Assert.Single(store.Load().Workspace.Sessions).Name);
        }
        finally
        {
            PtySessionFactory.UseMock = previousMock;
            Directory.Delete(directory, true);
        }
    }
    [AvaloniaFact]
    public async Task MissingDefaultShell_OffersAvailableChoice_ThenRestoresWorkspace()
    {
        var directory = Path.Combine(Path.GetTempPath(), "th-shell-" + Guid.NewGuid().ToString("N"));
        var previousMock = PtySessionFactory.UseMock;
        PtySessionFactory.UseMock = true;
        try
        {
            var store = new SettingsStore(Path.Combine(directory, "settings.json"));
            store.Save(new AppSettings { Workspace = new WorkspaceState
            {
                Sessions = [new WorkspaceSession { Name = "my work", Shell = "pwsh", WorkingDirectory = Environment.CurrentDirectory }],
                ActiveIndex = 0
            } });
            using var vm = new MainWindowViewModel(settingsStore: store, shellAvailable: command => command == FakeShell);
            await vm.SpawnStartupSessionsAsync();
            Assert.True(vm.ShellSetupOpen);
            Assert.Empty(vm.SessionCards);
            Assert.Equal(FakeShell, Assert.Single(vm.AvailableStartupShells).Command);
            await vm.ContinueShellSetupCommand.ExecuteAsync(null);
            await Task.Delay(30); // SessionAdded adds cards on the UI dispatcher.
            Assert.False(vm.ShellSetupOpen);
            var session = Assert.Single(vm.SessionCards).Model;
            Assert.Equal("my work", session.Name);
            Assert.Equal(FakeShell, session.Shell);
            Assert.Equal(FakeShellKind, store.Load().Shell);
        }
        finally
        {
            PtySessionFactory.UseMock = previousMock;
            Directory.Delete(directory, true);
        }
    }
}
