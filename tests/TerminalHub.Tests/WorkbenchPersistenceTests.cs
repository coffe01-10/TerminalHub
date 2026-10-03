using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Headless.XUnit;
using TerminalHub.App.ViewModels;
using TerminalHub.Core.Localization;
using TerminalHub.Core.Monitoring;
using TerminalHub.Core.Settings;
using TerminalHub.Pty;
using Xunit;

namespace TerminalHub.Tests;

[Collection("ProcessWide")]
public class WorkbenchPersistenceTests
{
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

    [AvaloniaFact]
    public async Task RestartRestoresMovedOrderLayoutPropertiesAndLanguage_WithFreshProcesses()
    {
        var directory = Path.Combine(Path.GetTempPath(), "terminalhub-transfer-" + Guid.NewGuid());
        var store = new SettingsStore(Path.Combine(directory, "settings.json"));
        var previousLanguage = Localizer.Current.Selection;
        var previousMock = PtySessionFactory.UseMock;
        MainWindowViewModel? first = null, restored = null;
        try
        {
            PtySessionFactory.UseMock = true;
            store.Save(new AppSettings { SessionGroups = [new() { Id = "kept-group", Name = "项目分组" }] });
            first = new(new IdleMonitor(), store);
            await first.SpawnStartupSessionsAsync(); await Task.Delay(100);
            await first.SetSplitLayoutAsync("Vertical"); first.RowRatio = .38;
            var source = first.ActiveWorkspace;
            var moved = first.SessionCards[0]; first.RenameSession((moved, "设置"));
            moved.Model.Pinned = true; moved.Model.GroupId = "kept-group";
            moved.Model.Emulator.ColorScheme = TerminalHub.Core.Terminal.TerminalColorScheme.Dark;
            var oldId = moved.Model.Id; var oldPty = moved.Model.Pty;
            await first.NewProjectWorkspaceCommand.ExecuteAsync(null); await Task.Delay(80);
            var target = first.ActiveWorkspace;
            first.ShelfAutoHide = true; first.ShelfWidth = 196;
            first.MoveSessionToWorkspace(moved, target, 0);
            await first.SetSplitLayoutAsync("Horizontal"); first.ColumnRatio = .34; first.FocusPane(1); await Task.Delay(40);
            first.LanguageIndex = 2;
            var targetNames = target.Cards.Select(c => c.Name).ToArray();
            var sourceNames = source.Cards.Select(c => c.Name).ToArray();
            var activeName = first.ActiveSession!.Name;
            first.Dispose(); first = null;
            restored = new(new IdleMonitor(), store);
            await restored.SpawnStartupSessionsAsync(); await Task.Delay(100);
            Assert.Equal("en", Localizer.Current.Language); Assert.Equal(2, restored.LanguageIndex);
            Assert.Equal(target.Id, restored.ActiveWorkspace.Id); Assert.True(restored.ShelfAutoHide); Assert.Equal(196, restored.ShelfWidth);
            Assert.Equal(targetNames, restored.SessionCards.Select(c => c.Name));
            Assert.Equal(sourceNames, restored.ProjectWorkspaces.Single(w => w.Id == source.Id).Cards.Select(c => c.Name));
            Assert.True(restored.IsSplit); Assert.Equal(.34, restored.ColumnRatio); Assert.Equal(1, restored.FocusedPane);
            Assert.Equal(activeName, restored.ActiveSession!.Name);
            var movedAgain = restored.SessionCards.Single(c => c.Name == "设置");
            Assert.NotEqual(oldId, movedAgain.Model.Id); Assert.NotSame(oldPty, movedAgain.Model.Pty);
            Assert.True(movedAgain.Model.Pinned); Assert.Equal("kept-group", movedAgain.Model.GroupId);
            Assert.Equal(TerminalHub.Core.Terminal.TerminalColorScheme.Dark, movedAgain.Model.Emulator.ColorScheme);
            Assert.True(movedAgain.Model.IsRunning); Assert.False(restored.CanUndoLayout);
            restored.SwitchProjectWorkspace(restored.ProjectWorkspaces.Single(w => w.Id == source.Id));
            Assert.True(restored.IsSplit); Assert.Equal(SplitLayout.Vertical, restored.SplitLayout); Assert.Equal(.38, restored.RowRatio);
        }
        finally
        {
            first?.Dispose(); restored?.Dispose();
            PtySessionFactory.UseMock = previousMock; Localizer.Current.SetLanguage(previousLanguage);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [AvaloniaFact]
    public async Task RealConPtyKeepsExecutingAndAcceptsInputAfterMigrationUndoAndRedo()
    {
        if (!OperatingSystem.IsWindows()) return;
        var directory = Path.Combine(Path.GetTempPath(), "terminalhub-transfer-pty-" + Guid.NewGuid());
        var previousMock = PtySessionFactory.UseMock;
        var previousLanguage = Localizer.Current.Selection;
        MainWindowViewModel? vm = null;
        var script = "[Console]::WriteLine('migration-before'); " +
            "$line = [Console]::ReadLine(); [Console]::WriteLine('migration-after:' + $line); Start-Sleep -Seconds 30";
        var arguments = "-NoLogo -NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        try
        {
            PtySessionFactory.UseMock = false;
            var store = new SettingsStore(Path.Combine(directory, "settings.json"));
            store.Save(new AppSettings
            {
                Shell = ShellKind.Custom, CustomShellPath = "powershell.exe", ActiveProjectWorkspaceId = "a",
                ProjectWorkspaces =
                [
                    new() { Id = "a", Name = "Source", Layout = new() { Sessions = [new() { Name = "Running task", Shell = "powershell.exe", Arguments = arguments, WorkingDirectory = Environment.CurrentDirectory }] } },
                    new() { Id = "b", Name = "Destination" }
                ]
            });
            vm = new(new IdleMonitor(), store);
            var ids = new[] { -10, -11, -12 }; var handles = ids.Select(GetStdHandle).ToArray();
            try
            {
                foreach (var id in ids) SetStdHandle(id, IntPtr.Zero);
                await vm.SpawnStartupSessionsAsync();
            }
            finally { for (var i = 0; i < ids.Length; i++) SetStdHandle(ids[i], handles[i]); }
            await Task.Delay(100);
            var source = vm.ProjectWorkspaces.Single(w => w.Id == "a"); var target = vm.ProjectWorkspaces.Single(w => w.Id == "b");
            var card = Assert.Single(source.Cards); var pty = Assert.IsType<ConPtySession>(card.Model.Pty);
            await WaitForText(card.Model.Emulator, "migration-before");
            vm.MoveSessionToWorkspace(card, target); vm.UndoLayout(); vm.RedoLayout();
            vm.LanguageIndex = 2;
            Assert.Same(pty, card.Model.Pty); Assert.True(pty.IsRunning); Assert.Same(target, vm.ActiveWorkspace);
            card.Model.Emulator.SendText("still-running\r");
            await WaitForText(card.Model.Emulator, "migration-after:still-running");
            Assert.True(pty.IsRunning);
        }
        finally
        {
            vm?.Dispose(); PtySessionFactory.UseMock = previousMock; Localizer.Current.SetLanguage(previousLanguage);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static async Task WaitForText(TerminalHub.Core.Terminal.TerminalEmulator terminal, string text)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!terminal.Buffer.TailText(100).Contains(text) && DateTime.UtcNow < deadline) await Task.Delay(25);
        Assert.Contains(text, terminal.Buffer.TailText(100));
    }

    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int id);
    [DllImport("kernel32.dll")] private static extern bool SetStdHandle(int id, IntPtr handle);
}
