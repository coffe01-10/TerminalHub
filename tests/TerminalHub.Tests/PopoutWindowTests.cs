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

    [Fact]
    public async Task AutoNamedSession_SkipsNamesTakenByExplicitlyNamedSessions()
    {
        var mgr = new SessionManager();
        // Startup sessions carry explicit names and never bump the auto counter —
        // the next auto-named session must still pick an unused number.
        for (var i = 1; i <= 3; i++)
            await mgr.CreateAsync(() => new MockPtySession(),
                new PtyOptions { Shell = "mock" }, $"Terminal {i:D2}");

        var auto = await mgr.CreateAsync(() => new MockPtySession(),
            new PtyOptions { Shell = "mock" });
        Assert.Equal("Terminal 04", auto.Name);

        // …and a rename that claims a future number is skipped too.
        mgr.Rename(auto, "Terminal 05");
        var next = await mgr.CreateAsync(() => new MockPtySession(),
            new PtyOptions { Shell = "mock" });
        Assert.Equal("Terminal 06", next.Name);
        Assert.Equal(5, mgr.Sessions.Select(s => s.Name).Distinct().Count());

        foreach (var s in mgr.Sessions.ToArray()) mgr.Close(s);
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

    [AvaloniaFact]
    public async Task Popout_PreservesCwdHistory_AndKeepsTrackingWhileDetached()
    {
        var root = Path.Combine(Path.GetTempPath(), "th-pop-cwd-" + Guid.NewGuid().ToString("N"));
        var a = Path.Combine(root, "alpha");
        var b = Path.Combine(root, "beta");
        var c = Path.Combine(root, "gamma");
        Directory.CreateDirectory(a);
        Directory.CreateDirectory(b);
        Directory.CreateDirectory(c);
        var window = new MainWindow { Width = 1200, Height = 800 };
        PtySessionFactory.UseMock = true;
        window.Show();
        try
        {
            await Task.Delay(400);
            var vm = (TerminalHub.App.ViewModels.MainWindowViewModel)window.DataContext!;
            var target = vm.ActiveSession!;
            var emu = target.Emulator;
            var fullA = Path.GetFullPath(a);
            var fullB = Path.GetFullPath(b);
            var fullC = Path.GetFullPath(c);

            // Two cwd reports → back/forward history has somewhere to go.
            emu.Parser.Feed($"\u001b]7;file://{fullA}\u0007");
            await Until(() => Norm(target.WorkingDirectory) == Norm(fullA));
            emu.Parser.Feed($"\u001b]7;file://{fullB}\u0007");
            await Until(() => Norm(target.WorkingDirectory) == Norm(fullB));
            await Until(() => vm.CanCwdBack);

            vm.OpenInNewWindowCommand.Execute(null);
            await Task.Delay(200);
            var pop = Assert.Single(vm.Popouts);

            // Detached but still wired: a cwd report from inside the popout
            // keeps landing in the session and its (preserved) history.
            emu.Parser.Feed($"\u001b]7;file://{fullC}\u0007");
            await Until(() => Norm(target.WorkingDirectory) == Norm(fullC));

            pop.Close();
            await Task.Delay(200);
            Assert.Same(target, vm.ActiveSession);

            // History survived the round trip — ← goes to B, → back to C.
            await Until(() => vm.CanCwdBack);
            vm.CwdBackCommand.Execute(null);
            Assert.Equal(Norm(fullB), Norm(vm.ActiveSession!.WorkingDirectory));
            await Until(() => vm.CanCwdForward);
            vm.CwdForwardCommand.Execute(null);
            Assert.Equal(Norm(fullC), Norm(vm.ActiveSession!.WorkingDirectory));
        }
        finally
        {
            window.Close();
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(25);
        }
        Assert.True(condition(), "condition not met within timeout");
    }

    private static string Norm(string path) =>
        TerminalHub.Core.Sessions.CwdHistory.Normalize(path);
}
