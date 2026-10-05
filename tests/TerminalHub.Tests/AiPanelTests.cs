using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using TerminalHub.App.ViewModels;
using TerminalHub.Core.Ai;
using TerminalHub.Core.Pty;
using TerminalHub.Core.Sessions;
using TerminalHub.Core.Settings;
using TerminalHub.Core.Terminal;
using TerminalHub.Pty;
using Xunit;

namespace TerminalHub.Tests;

public class AiPanelTests
{
    [AvaloniaFact]
    public async Task TaskButtons_UpdateAfterAsyncSpawnAndExit()
    {
        using var session = await MockSession();
        var spawn = new TaskCompletionSource<TerminalSessionModel?>();
        using var panel = new AiPanelViewModel(clis: [Claude], spawn: (_, _) => spawn.Task, autoScan: false);
        panel.TaskDraft = "summarize";
        var starting = panel.StartTaskCommand.ExecuteAsync(null);
        var task = Assert.Single(panel.Tasks);
        var show = new Button { DataContext = task };
        var remove = new Button { DataContext = task };
        using var showBinding = show.Bind(Control.IsVisibleProperty, new Binding("Active"));
        using var removeBinding = remove.Bind(Control.IsVisibleProperty, new Binding("!Active"));
        Assert.False(show.IsVisible);
        Assert.True(remove.IsVisible);

        spawn.SetResult(session);
        await starting;
        Assert.True(show.IsVisible);
        Assert.False(remove.IsVisible);
        panel.RemoveTaskCommand.Execute(task);
        Assert.Single(panel.Tasks);

        session.Emulator.SendText("exit\r");
        panel.Scan();
        Assert.False(show.IsVisible);
        Assert.True(remove.IsVisible);
        panel.RemoveTaskCommand.Execute(task);
        Assert.Empty(panel.Tasks);
    }

    private static readonly AiCli Codex = new(
        AiCliCatalog.Known.First(k => k.Id == "codex"), @"C:\tools\codex.cmd");
    private static readonly AiCli Claude = new(
        AiCliCatalog.Known.First(k => k.Id == "claude"), @"C:\tools\claude.exe");

    private static async Task<TerminalSessionModel> MockSession()
    {
        var emu = new TerminalEmulator(new MockPtySession(), 100, 28);
        await emu.StartAsync(new PtyOptions { Shell = "cmd.exe" });
        return new TerminalSessionModel { Name = "ai-task", Emulator = emu };
    }

    [Fact]
    public void Detect_UsesLocatorHook_AndReturnsSpecsInCatalogOrder()
    {
        var found = AiCliCatalog.Detect(id => id is "codex" or "grok" ? $"/usr/local/bin/{id}" : null);
        Assert.Equal(new[] { "codex", "grok" }, found.Select(c => c.Id).ToArray());
        Assert.Equal("/usr/local/bin/codex", found[0].Path);
        Assert.True(found[0].DetectsAwaitingInput);   // only Codex has a verified detector
        Assert.False(found[1].DetectsAwaitingInput);
    }

    [Fact]
    public void SpawnCommand_CmdShim_WrapsThroughCmdExe()
    {
        var (shell, args) = AiCliCatalog.SpawnCommand(Codex, "fix the \"flaky\" test");
        Assert.Equal("cmd.exe", shell);
        Assert.Contains("/d /s /c", args);
        Assert.Contains(@"C:\tools\codex.cmd", args);
        Assert.Contains("fix the 'flaky' test", args);   // quotes sanitized — cmd quoting can't break
    }

    [Fact]
    public void SpawnCommand_Exe_PassesPromptAsArgument()
    {
        var (shell, args) = AiCliCatalog.SpawnCommand(Claude, "翻译 README");
        Assert.Equal(@"C:\tools\claude.exe", shell);
        Assert.Equal("\"翻译 README\"", args);
    }

    [AvaloniaFact]
    public async Task StartTask_SpawnsCliSession_TaggedAi()
    {
        PtySessionFactory.UseMock = true;
        var settingsPath = Path.Combine(Path.GetTempPath(), $"th-ai-{Guid.NewGuid():N}.json");
        var vm = new MainWindowViewModel(settingsStore: new SettingsStore(settingsPath));
        try
        {
            vm.AiPanel.Clis.Add(Codex);
            vm.AiPanel.SelectedCli = Codex;
            vm.AiPanel.TaskDraft = "review the flaky test";
            await vm.AiPanel.StartTaskCommand.ExecuteAsync(null);

            var task = Assert.Single(vm.AiPanel.Tasks);
            Assert.NotNull(task.Session);
            for (var i = 0; i < 50 && !vm.SessionCards.Any(c => c.Model == task.Session); i++)
                await Task.Delay(100);
            var card = Assert.Single(vm.SessionCards, c => c.Model == task.Session);
            Assert.Equal(TerminalHub.Core.Sessions.SessionTag.Ai, card.Model.Tag);
            Assert.Equal("cmd.exe", card.Model.Shell);          // .cmd shim routed through cmd.exe
            Assert.Contains("codex.cmd", card.Model.ShellArguments);
            Assert.Contains("review the flaky test", card.Model.ShellArguments);
            Assert.Equal("运行中", task.Status);
        }
        finally { vm.Dispose(); if (File.Exists(settingsPath)) File.Delete(settingsPath); }
    }

    [AvaloniaFact]
    public async Task Scan_CodexComposerFrame_MarksAwaitingInput_AndNotifies()
    {
        TerminalSessionModel? session = null;
        var notes = new List<string>();
        var panel = new AiPanelViewModel(
            clis: [Codex],
            spawn: async (_, _) => session = await MockSession(),
            notify: (s, m) => notes.Add(m),
            isVisible: _ => false,
            autoScan: false);
        try
        {
            panel.TaskDraft = "do the thing";
            await panel.StartTaskCommand.ExecuteAsync(null);
            var task = Assert.Single(panel.Tasks);
            Assert.NotNull(session);

            session!.Emulator.Parser.Feed(
                "\x1b[2J\x1b[H\x1b[2;3H>_ OpenAI Codex (v0.158.0)\x1b[10;1H› Ask Codex to do anything\x1b[10;3H");
            panel.Scan();
            Assert.True(task.AwaitingInput);
            Assert.Equal("等待输入", task.Status);
            Assert.Contains(notes, n => n.Contains("等待输入"));

            session.Emulator.Parser.Feed("\x1b[5;1Hworking…");   // composer left → back to running
            panel.Scan();
            Assert.False(task.AwaitingInput);
            Assert.Equal("运行中", task.Status);
        }
        finally { session?.Dispose(); panel.Dispose(); }
    }

    [AvaloniaFact]
    public async Task Scan_ExitedCli_MarksExitedWithCode()
    {
        TerminalSessionModel? session = null;
        var notes = new List<string>();
        var panel = new AiPanelViewModel(
            clis: [Claude], spawn: async (_, _) => session = await MockSession(),
            notify: (s, m) => notes.Add(m), isVisible: _ => false, autoScan: false);
        try
        {
            panel.TaskDraft = "summarize";
            await panel.StartTaskCommand.ExecuteAsync(null);
            var task = Assert.Single(panel.Tasks);

            session!.Emulator.SendText("exit\r");
            panel.Scan();
            Assert.Equal("已退出（0）", task.Status);
            Assert.False(task.Active);
            Assert.Contains(notes, n => n.Contains("已退出"));

            // RemoveTask only applies to inactive tasks — this one is now removable.
            panel.RemoveTaskCommand.Execute(task);
            Assert.Empty(panel.Tasks);
        }
        finally { session?.Dispose(); panel.Dispose(); }
    }

    [AvaloniaFact]
    public async Task Scan_NoDetectorCli_StaysRunningUntilExit()
    {
        TerminalSessionModel? session = null;
        var panel = new AiPanelViewModel(
            clis: [Claude], spawn: async (_, _) => session = await MockSession(),
            isVisible: _ => true, autoScan: false);
        try
        {
            panel.TaskDraft = "x";
            await panel.StartTaskCommand.ExecuteAsync(null);
            var task = Assert.Single(panel.Tasks);
            session!.Emulator.Parser.Feed("\x1b[2J\x1b[H\x1b[10;1H› \x1b[10;3H"); // composer-ish frame, no detector
            panel.Scan();
            Assert.False(task.AwaitingInput);   // no claim we can't verify
            Assert.Equal("运行中", task.Status);
        }
        finally { session?.Dispose(); panel.Dispose(); }
    }

    [Fact]
    public async Task StartTask_EmptyDraft_OnlyNotices()
    {
        var panel = new AiPanelViewModel(clis: [Codex], autoScan: false);
        try
        {
            await panel.StartTaskCommand.ExecuteAsync(null);
            Assert.Empty(panel.Tasks);
            Assert.NotEmpty(panel.Notice);
        }
        finally { panel.Dispose(); }
    }
}
