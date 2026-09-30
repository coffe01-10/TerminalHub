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
                Sessions = [new WorkspaceSession { Name = "cmd work", Shell = "cmd.exe" }]
            } });
            using var vm = new MainWindowViewModel(settingsStore: store, shellAvailable: command => command == "cmd.exe");
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
            var window = new MainWindow(store, shellAvailable: command => command == "cmd.exe") { Width = 1100, Height = 680 };
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
            using var vm = new MainWindowViewModel(settingsStore: store, shellAvailable: command => command == "cmd.exe");
            await vm.SpawnStartupSessionsAsync();
            Assert.True(vm.ShellSetupOpen);
            Assert.Empty(vm.SessionCards);
            Assert.Equal("cmd.exe", Assert.Single(vm.AvailableStartupShells).Command);
            await vm.ContinueShellSetupCommand.ExecuteAsync(null);
            await Task.Delay(30); // SessionAdded adds cards on the UI dispatcher.
            Assert.False(vm.ShellSetupOpen);
            var session = Assert.Single(vm.SessionCards).Model;
            Assert.Equal("my work", session.Name);
            Assert.Equal("cmd.exe", session.Shell);
            Assert.Equal(ShellKind.Cmd, store.Load().Shell);
        }
        finally
        {
            PtySessionFactory.UseMock = previousMock;
            Directory.Delete(directory, true);
        }
    }
}
