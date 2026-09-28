using Avalonia.Headless.XUnit;
using TerminalHub.App.Views;
using TerminalHub.Core.Pty;
using TerminalHub.Core.Sessions;
using TerminalHub.Pty;
using Xunit;

namespace TerminalHub.Tests;

/// <summary>「↗ 在新窗口打开」: detach → popout window → close → reattach.</summary>
public class PopoutWindowTests
{
    // ---------- Core: SessionManager.Detach / Reattach ----------

    [Fact]
    public async Task Detach_KeepsProcessAlive_ReattachRestores()
    {
        var mgr = new SessionManager();
        var removed = new List<TerminalSessionModel>();
        var added = new List<TerminalSessionModel>();
        mgr.SessionRemoved += s => removed.Add(s);
        mgr.SessionAdded += s => added.Add(s);

        var a = await mgr.CreateAsync(() => new MockPtySession(),
            new PtyOptions { Shell = "mock" }, "A");
        var b = await mgr.CreateAsync(() => new MockPtySession(),
            new PtyOptions { Shell = "mock" }, "B");
        Assert.Same(b, mgr.Active);
        added.Clear();

        var detached = mgr.Detach(b);
        Assert.Same(b, detached);
        Assert.DoesNotContain(mgr.Sessions, s => ReferenceEquals(s, b));
        Assert.Contains(b, removed);            // observers notified
        Assert.True(b.IsRunning);               // …but the PTY was NOT killed
        Assert.Same(a, mgr.Active);             // active fell back

        mgr.Reattach(b);
        Assert.Contains(b, mgr.Sessions);
        Assert.Contains(b, added);
        Assert.Same(b, mgr.Active);             // reattach re-selects it

        foreach (var s in mgr.Sessions.ToArray()) mgr.Close(s);
    }

    [Fact]
    public async Task Detach_UnknownSession_ReturnsNull_ReattachIsIdempotent()
    {
        var mgr = new SessionManager();
        var a = await mgr.CreateAsync(() => new MockPtySession(),
            new PtyOptions { Shell = "mock" }, "A");
        var stray = new TerminalSessionModel
        {
            Name = "stray",
            Emulator = new TerminalHub.Core.Terminal.TerminalEmulator(new MockPtySession()),
        };

        Assert.Null(mgr.Detach(stray));
        mgr.Reattach(a); // already in the list — no-op
        Assert.Single(mgr.Sessions);

        mgr.Close(a);
        stray.Dispose();
    }

    // ---------- UI: popout window lifecycle ----------

    private static async Task<TerminalHub.App.ViewModels.MainWindowViewModel> ShowMain()
    {
        PtySessionFactory.UseMock = true;
        var window = new MainWindow { Width = 1200, Height = 800 };
        window.Show();
        await Task.Delay(400); // startup sessions spawn
        return (TerminalHub.App.ViewModels.MainWindowViewModel)window.DataContext!;
    }

    [AvaloniaFact]
    public async Task OpenInNewWindow_DetachesSession_ShowsPopout()
    {
        var window = new MainWindow { Width = 1200, Height = 800 };
        PtySessionFactory.UseMock = true;
        window.Show();
        await Task.Delay(400);
        var vm = (TerminalHub.App.ViewModels.MainWindowViewModel)window.DataContext!;
        var cardsBefore = vm.SessionCards.Count;
        var target = vm.ActiveSession!;
        Assert.True(vm.OpenInNewWindowCommand.CanExecute(null));

        vm.OpenInNewWindowCommand.Execute(null);
        await Task.Delay(200);

        Assert.Equal(cardsBefore - 1, vm.SessionCards.Count);
        Assert.DoesNotContain(vm.SessionCards, c => ReferenceEquals(c.Model, target));
        Assert.Single(vm.DetachedSessions);
        Assert.Same(target, vm.DetachedSessions[0]);
        Assert.True(target.IsRunning);                    // process untouched
        Assert.NotSame(target, vm.ActiveSession);         // main switched to another

        var pop = Assert.Single(vm.Popouts);
        Assert.Same(target, pop.Session);
        Assert.Same(target.Emulator, pop.Terminal.Emulator); // same live emulator bound
        Assert.Contains(vm.Dashboard.OutputLog, l => l.Message.Contains("已弹出"));
        window.Close();
    }

    [AvaloniaFact]
    public async Task PopoutClose_Reattaches_OtherSessionsUntouched()
    {
        var window = new MainWindow { Width = 1200, Height = 800 };
        PtySessionFactory.UseMock = true;
        window.Show();
        await Task.Delay(400);
        var vm = (TerminalHub.App.ViewModels.MainWindowViewModel)window.DataContext!;
        var cardsBefore = vm.SessionCards.Count;
        var target = vm.ActiveSession!;
        var others = vm.SessionCards.Where(c => !ReferenceEquals(c.Model, target))
            .Select(c => c.Model).ToList();

        vm.OpenInNewWindowCommand.Execute(null);
        await Task.Delay(150);
        var pop = Assert.Single(vm.Popouts);

        pop.Close(); // 关子窗 → 会话收回主窗，其余会话不受影响
        await Task.Delay(200);

        Assert.Empty(vm.Popouts);
        Assert.Empty(vm.DetachedSessions);
        Assert.Equal(cardsBefore, vm.SessionCards.Count);
        Assert.Contains(vm.SessionCards, c => ReferenceEquals(c.Model, target));
        Assert.True(target.IsRunning);
        Assert.Same(target, vm.ActiveSession);
        Assert.All(others, s => Assert.True(s.IsRunning));
        Assert.Contains(vm.Dashboard.OutputLog, l => l.Message.Contains("已收回"));
        window.Close();
    }

    [AvaloniaFact]
    public async Task PopoutDisabled_WhenNoSessions_MainCloseKillsPopped()
    {
        var window = new MainWindow { Width = 1200, Height = 800 };
        PtySessionFactory.UseMock = true;
        window.Show();
        await Task.Delay(400);
        var vm = (TerminalHub.App.ViewModels.MainWindowViewModel)window.DataContext!;

        var popped = vm.ActiveSession!;
        vm.OpenInNewWindowCommand.Execute(null);
        await Task.Delay(150);
        Assert.Single(vm.Popouts);

        // Close every remaining main-window session → button disarms.
        foreach (var card in vm.SessionCards.ToArray())
            vm.CloseSessionCommand.Execute(card);
        await Task.Delay(150);
        Assert.Null(vm.ActiveSession);
        Assert.False(vm.OpenInNewWindowCommand.CanExecute(null));

        // Closing the main window with a popout still open must kill its PTY too.
        window.Close();
        Assert.Empty(vm.Popouts);
        Assert.Empty(vm.DetachedSessions);
        Assert.False(popped.IsRunning);
    }
}
