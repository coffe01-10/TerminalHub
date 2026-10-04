using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TerminalHub.App.ViewModels;
using TerminalHub.App.Views;
using TerminalHub.Core.Deploy;
using TerminalHub.Core.Pty;
using TerminalHub.Core.Sessions;
using TerminalHub.Core.Settings;
using TerminalHub.Pty;
using Xunit;

namespace TerminalHub.Tests;

public class DeployDockTests
{
    private static async Task FlushUi() =>
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

    /// <summary>Polls (pumping the UI dispatcher) until <paramref name="pred"/>
    /// holds; timing out fails here instead of deferring to whatever assertion
    /// — if any — comes next.</summary>
    private static async Task WaitFor(Func<bool> pred, int ms = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < deadline)
        {
            await FlushUi();
            if (pred()) return;
            await Task.Delay(30);
        }
        await FlushUi();
        Assert.True(pred(), $"Timed out after {ms}ms waiting for the deploy dock state.");
    }

    private static string TempRepo(bool withScript, bool withArtifact)
    {
        var root = Path.Combine(Path.GetTempPath(), $"th-dock-{Guid.NewGuid():N}");
        if (withScript)
        {
            var scripts = Directory.CreateDirectory(Path.Combine(root, "scripts"));
            File.WriteAllText(Path.Combine(scripts.FullName, "publish-linux.sh"), "#!/bin/sh\necho PUB\n");
            File.WriteAllText(Path.Combine(scripts.FullName, "publish-windows.ps1"), "Write-Host PUB\n");
        }
        else
        {
            Directory.CreateDirectory(root);
        }
        if (withArtifact)
        {
            var dir = Directory.CreateDirectory(Path.Combine(root, "artifacts", "publish", "linux-x64"));
            File.WriteAllText(Path.Combine(dir.FullName, "TerminalHub"), "bin");
        }
        return root;
    }

    [AvaloniaFact]
    public async Task MissingArtifacts_StartsDeploySession_AndReportsExit()
    {
        PtySessionFactory.UseMock = true;
        var root = TempRepo(withScript: true, withArtifact: false);
        var settingsPath = Path.Combine(Path.GetTempPath(), $"th-dock-set-{Guid.NewGuid():N}.json");
        var vm = new MainWindowViewModel(settingsStore: new SettingsStore(settingsPath));
        try
        {
            vm.DeployFromDock(forceRepublish: false, root);
            await WaitFor(() => vm.SessionCards.Any(c => c.Name == "Publish"));

            var card = vm.SessionCards.Single(c => c.Name == "Publish");
            Assert.Equal(SessionTag.Deploy, card.Model.Tag);
            Assert.Equal(Path.GetFullPath(root), Path.GetFullPath(card.Model.WorkingDirectory));
            Assert.Contains(vm.Dashboard.OutputLog, l =>
                l.Source == "deploy" && l.Message.Contains("publish start") && l.Message.Contains("开始打包"));
            var script = OperatingSystem.IsWindows() ? "publish-windows.ps1" : "publish-linux.sh";
            // The app prefers pwsh and falls back to Windows PowerShell — expect
            // whichever this machine resolves.
            var shell = OperatingSystem.IsWindows()
                ? PublishPlanner.ResolveWindowsShell(name =>
                    PublishPlanner.NameOnPath(name, Environment.GetEnvironmentVariable("PATH"), windows: true))
                : "bash";
            Assert.Contains(vm.Dashboard.OutputLog, l =>
                l.Source == "deploy" && l.Message.Contains(script));
            Assert.Contains(shell, card.Model.Emulator.Buffer.TailText(40));
            Assert.DoesNotContain(vm.Dashboard.OutputLog, l => l.Message.Contains("force republish"));

            vm.DeployFromDock(forceRepublish: false, root);
            await FlushUi();
            Assert.Single(vm.SessionCards, c => c.Name == "Publish");
            Assert.Contains(vm.Dashboard.OutputLog, l =>
                l.Source == "deploy" && l.Message.Contains("publish already running"));

            card.Model.Emulator.SendText("exit\r");
            await WaitFor(() => vm.Dashboard.OutputLog.Any(l =>
                l.Source == "deploy" && l.Message.Contains("publish succeeded") && l.Message.Contains("打包成功")));
        }
        finally
        {
            vm.Dispose();
            Directory.Delete(root, true);
            if (File.Exists(settingsPath)) File.Delete(settingsPath);
        }
    }

    [AvaloniaFact]
    public async Task PublishExit_NonZero_ReportsFailure()
    {
        PtySessionFactory.UseMock = true;
        var root = TempRepo(withScript: true, withArtifact: false);
        var settingsPath = Path.Combine(Path.GetTempPath(), $"th-dock-set-{Guid.NewGuid():N}.json");
        var vm = new MainWindowViewModel(settingsStore: new SettingsStore(settingsPath));
        try
        {
            vm.DeployFromDock(false, root);
            await WaitFor(() => vm.SessionCards.Any(c => c.Name == "Publish"));
            vm.SessionCards.Single(c => c.Name == "Publish").Model.Pty.Kill();
            await WaitFor(() => vm.Dashboard.OutputLog.Any(l =>
                l.Source == "deploy" && l.Level == "error"
                && l.Message.Contains("publish failed") && l.Message.Contains("打包失败")));
            Assert.Equal(LastPublishResults.Fail, vm.Settings.LastPublishResult?.Outcome);
            Assert.Equal(-1, vm.Settings.LastPublishResult!.ExitCode);
            AssertDurationBadge(vm.LastPublishBadge, "失败 exit -1");
        }
        finally
        {
            vm.Dispose();
            Directory.Delete(root, true);
            if (File.Exists(settingsPath)) File.Delete(settingsPath);
        }
    }

    [AvaloniaFact]
    public async Task ExistingArtifacts_OpenFolder_ForceRepublishes()
    {
        PtySessionFactory.UseMock = true;
        var root = TempRepo(withScript: true, withArtifact: true);
        var opened = new List<string>();
        var vm = new MainWindowViewModel(openFolder: opened.Add);
        try
        {
            vm.DeployFromDock(false, root);
            await FlushUi();
            Assert.DoesNotContain(vm.SessionCards, c => c.Name == "Publish");
            Assert.Single(opened);
            Assert.Contains("linux-x64", opened[0]);
            Assert.Contains(vm.Dashboard.OutputLog, l =>
                l.Source == "deploy" && l.Message.Contains("产物目录") && l.Message.Contains("linux-x64"));
            Assert.Contains(vm.Dashboard.OutputLog, l =>
                l.Source == "deploy" && l.Message.Contains("TerminalHub"));
            Assert.Contains(vm.Dashboard.OutputLog, l =>
                l.Source == "deploy" && l.Message.Contains("Ctrl") && l.Message.Contains("重新打包"));

            vm.DeployFromDock(true, root);
            await WaitFor(() => vm.SessionCards.Any(c => c.Name == "Publish"));
            Assert.Contains(vm.Dashboard.OutputLog, l =>
                l.Source == "deploy" && l.Message.Contains("force republish") && l.Message.Contains("重新打包"));
            Assert.Equal(SessionTag.Deploy, vm.SessionCards.Single(c => c.Name == "Publish").Model.Tag);
        }
        finally
        {
            vm.Dispose();
            Directory.Delete(root, true);
        }
    }

    [AvaloniaFact]
    public async Task MissingScript_PrintsHints_DoesNotSpawn()
    {
        PtySessionFactory.UseMock = true;
        var root = TempRepo(withScript: false, withArtifact: false);
        var vm = new MainWindowViewModel();
        try
        {
            var before = vm.SessionCards.Count;
            vm.DeployFromDock(true, root);
            await FlushUi();
            Assert.Equal(before, vm.SessionCards.Count);
            Assert.Contains(vm.Dashboard.OutputLog, l =>
                l.Source == "deploy" && l.Message.Contains("publish script not found"));
            Assert.Contains(vm.Dashboard.OutputLog, l =>
                l.Source == "deploy" && (l.Message.Contains("publish-linux.sh") || l.Message.Contains("publish-windows.ps1")));
        }
        finally
        {
            vm.Dispose();
            Directory.Delete(root, true);
        }
    }

    [AvaloniaFact]
    public async Task ArmedCtrl_DockSelect_ForcesOnce_ThenOpensArtifacts()
    {
        PtySessionFactory.UseMock = true;
        var root = TempRepo(withScript: true, withArtifact: true);
        var vm = new MainWindowViewModel(openFolder: _ => { });
        var previous = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(root);
            vm.ArmDeployCtrl(true);
            // XAML CommandParameter arrives as a string; int must work too.
            vm.DockSelectCommand.Execute("4");
            await WaitFor(() => vm.SessionCards.Any(c => c.Name == "Publish"));
            Assert.Contains(vm.Dashboard.OutputLog, l =>
                l.Source == "deploy" && l.Message.Contains("force republish"));

            vm.Dashboard.ClearOutput();
            vm.DockSelectCommand.Execute(4);
            await FlushUi();
            Assert.Single(vm.SessionCards, c => c.Name == "Publish");
            Assert.DoesNotContain(vm.Dashboard.OutputLog, l => l.Message.Contains("force republish"));
            Assert.Contains(vm.Dashboard.OutputLog, l =>
                l.Source == "deploy" && l.Message.Contains("产物目录"));
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
            vm.Dispose();
            Directory.Delete(root, true);
        }
    }

    [AvaloniaFact]
    public async Task Pointer_PlainClickOpens_CtrlClickForcesRepublish()
    {
        PtySessionFactory.UseMock = true;
        var root = TempRepo(withScript: true, withArtifact: true);
        var previous = Directory.GetCurrentDirectory();
        var window = new MainWindow(openFolder: _ => { }) { Width = 1200, Height = 800 };
        try
        {
            Directory.SetCurrentDirectory(root);
            window.Show();
            await FlushUi();

            var deploy = window.FindControl<Button>("DeployDockButton");
            Assert.NotNull(deploy);
            var vm = (MainWindowViewModel)window.DataContext!;
            // The dock auto-hides: hover the bottom strip first so it slides up,
            // THEN measure the button center — measuring while hidden yields the
            // translateY(130px)-shifted position outside the window.
            window.MouseMove(new Point(window.Bounds.Width / 2, window.Bounds.Height - 40));
            await Task.Delay(500);
            var center = CenterInWindow(window, deploy!);
            Click(window, center, RawInputModifiers.None);
            await FlushUi();
            Assert.DoesNotContain(vm.SessionCards, c => c.Name == "Publish");
            Assert.Contains(vm.Dashboard.OutputLog, l =>
                l.Source == "deploy" && l.Message.Contains("产物目录"));
            Assert.Contains(vm.Dashboard.OutputLog, l =>
                l.Source == "deploy" && l.Message.Contains("Ctrl") && l.Message.Contains("重新打包"));

            vm.Dashboard.ClearOutput();
            Click(window, center, RawInputModifiers.Control);
            await WaitFor(() => vm.SessionCards.Any(c => c.Name == "Publish"));
            var card = vm.SessionCards.Single(c => c.Name == "Publish");
            Assert.Equal(SessionTag.Deploy, card.Model.Tag);
            Assert.Contains(vm.Dashboard.OutputLog, l =>
                l.Source == "deploy" && l.Message.Contains("force republish") && l.Message.Contains("重新打包"));
            var script = OperatingSystem.IsWindows() ? "publish-windows.ps1" : "./scripts/publish-linux.sh";
            Assert.Contains(vm.Dashboard.OutputLog, l =>
                l.Source == "deploy" && l.Message.Contains("publish start") && l.Message.Contains(script));
            Assert.Equal(Path.GetFullPath(root), Path.GetFullPath(card.Model.WorkingDirectory));
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
            window.Close();
            Directory.Delete(root, true);
        }
    }

    private static Point CenterInWindow(Window window, Control control)
    {
        Assert.True(control.Bounds.Width > 1 && control.Bounds.Height > 1);
        var center = control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window);
        Assert.NotNull(center);
        return center!.Value;
    }

    private static void Click(Window window, Point point, RawInputModifiers modifiers)
    {
        window.MouseMove(point, modifiers);
        window.MouseDown(point, MouseButton.Left, modifiers);
        window.MouseUp(point, MouseButton.Left, modifiers);
    }

    [AvaloniaFact]
    public async Task RepublishMenuItem_ForcesRepublish()
    {
        PtySessionFactory.UseMock = true;
        var root = TempRepo(withScript: true, withArtifact: true);
        var previous = Directory.GetCurrentDirectory();
        var window = new MainWindow { Width = 1200, Height = 800 };
        try
        {
            Directory.SetCurrentDirectory(root);
            window.Show();
            await FlushUi();
            var deploy = window.FindControl<Button>("DeployDockButton");
            Assert.NotNull(deploy);
            var item = Assert.IsAssignableFrom<MenuItem>(Assert.IsType<MenuFlyout>(deploy!.ContextFlyout).Items[0]);
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var vm = (MainWindowViewModel)window.DataContext!;
            await WaitFor(() => vm.Dashboard.OutputLog.Any(l =>
                l.Source == "deploy" && l.Message.Contains("force republish")));
            await WaitFor(() => vm.SessionCards.Any(c => c.Name == "Publish" && c.Model.Tag == SessionTag.Deploy));
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
            window.Close();
            Directory.Delete(root, true);
        }
    }

    [AvaloniaFact]
    public async Task PublishSuccess_RefreshesRecentArtifactsFromDisk()
    {
        PtySessionFactory.UseMock = true;
        var root = TempRepo(withScript: true, withArtifact: true);
        var settingsPath = Path.Combine(Path.GetTempPath(), $"th-dock-set-{Guid.NewGuid():N}.json");
        var vm = new MainWindowViewModel(settingsStore: new SettingsStore(settingsPath));
        try
        {
            vm.DeployFromDock(true, root);
            await WaitFor(() => vm.SessionCards.Any(c => c.Name == "Publish"));
            vm.SessionCards.Single(c => c.Name == "Publish").Model.Emulator.SendText("exit\r");
            await WaitFor(() => vm.Dashboard.OutputLog.Any(l =>
                l.Source == "deploy" && l.Message.Contains("recent artifacts") && l.Message.Contains("最近产物已刷新")));
            Assert.Contains(vm.Dashboard.OutputLog, l =>
                l.Source == "deploy" && l.Message.Contains("linux-x64") && l.Message.Contains(root));
            Assert.Contains(vm.RecentArtifacts, a => a.Rid == "linux-x64" && a.Path.Contains(root));
        }
        finally
        {
            vm.Dispose();
            Directory.Delete(root, true);
            if (File.Exists(settingsPath)) File.Delete(settingsPath);
        }
    }

    [AvaloniaFact]
    public async Task ActiveProfile_DrivesNextDeploy_ExplicitStartIgnoresIt()
    {
        PtySessionFactory.UseMock = true;
        var pinned = TempRepo(withScript: true, withArtifact: false);
        var other = TempRepo(withScript: true, withArtifact: false);
        var settingsPath = Path.Combine(Path.GetTempPath(), $"th-dock-set-{Guid.NewGuid():N}.json");
        var previous = Directory.GetCurrentDirectory();
        var vm = new MainWindowViewModel(settingsStore: new SettingsStore(settingsPath));
        try
        {
            Directory.SetCurrentDirectory(other);
            var otherRid = OperatingSystem.IsWindows() ? PublishProfiles.LinuxRid : PublishProfiles.WindowsRid;
            var otherScript = OperatingSystem.IsWindows() ? "publish-linux.sh" : "publish-windows.ps1";
            Assert.True(vm.SavePublishProfile("cross", pinned, otherRid, "nightly"));
            await FlushUi();
            Assert.Contains(vm.Dashboard.OutputLog, l =>
                l.Source == "deploy" && l.Message.Contains("profile saved") && l.Message.Contains("下次 Deploy"));

            vm.DeployFromDock(forceRepublish: true);
            await WaitFor(() => vm.SessionCards.Any(c => c.Name == "Publish"));
            var card = vm.SessionCards.Single(c => c.Name == "Publish");
            Assert.Equal(Path.GetFullPath(pinned), Path.GetFullPath(card.Model.WorkingDirectory));
            Assert.Contains(vm.Dashboard.OutputLog, l =>
                l.Source == "deploy" && l.Message.Contains("使用配置档") && l.Message.Contains("cross"));
            Assert.Contains(vm.Dashboard.OutputLog, l =>
                l.Source == "deploy" && l.Message.Contains("publish start") && l.Message.Contains(otherScript));

            var loaded = new SettingsStore(settingsPath).Load();
            Assert.Equal("cross", PublishProfiles.Active(loaded)!.Name);
            Assert.Equal(Path.GetFullPath(pinned), PublishProfiles.Active(loaded)!.RepoRoot);
            Assert.Equal(otherRid, PublishProfiles.Active(loaded)!.Rid);
            Assert.Equal("nightly", PublishProfiles.Active(loaded)!.Note);

            card.Model.Emulator.SendText("exit\r");
            await WaitFor(() => !card.Model.IsRunning);

            vm.Dashboard.ClearOutput();
            vm.DeployFromDock(forceRepublish: true, other);
            await WaitFor(() => vm.SessionCards.Count(c => c.Name == "Publish") == 2);
            var second = vm.SessionCards.Last(c => c.Name == "Publish");
            Assert.Equal(Path.GetFullPath(other), Path.GetFullPath(second.Model.WorkingDirectory));
            var hostScript = OperatingSystem.IsWindows() ? "publish-windows.ps1" : "publish-linux.sh";
            Assert.Contains(vm.Dashboard.OutputLog, l =>
                l.Source == "deploy" && l.Message.Contains("publish start") && l.Message.Contains(hostScript));
            Assert.DoesNotContain(vm.Dashboard.OutputLog, l => l.Message.Contains("使用配置档"));
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
            vm.Dispose();
            Directory.Delete(pinned, true);
            Directory.Delete(other, true);
            File.Delete(settingsPath);
        }
    }

    [AvaloniaFact]
    public async Task SwitchAndDeleteProfile_ChangeTheNextDeployRoot()
    {
        PtySessionFactory.UseMock = true;
        var rootA = TempRepo(withScript: true, withArtifact: false);
        var rootB = TempRepo(withScript: true, withArtifact: false);
        var settingsPath = Path.Combine(Path.GetTempPath(), $"th-dock-set-{Guid.NewGuid():N}.json");
        var previous = Directory.GetCurrentDirectory();
        var vm = new MainWindowViewModel(settingsStore: new SettingsStore(settingsPath));
        try
        {
            Directory.SetCurrentDirectory(rootB);
            Assert.True(vm.SavePublishProfile("alpha", rootA, "", ""));
            Assert.True(vm.SavePublishProfile("beta", rootB, "", "second"));
            Assert.Equal("beta", vm.ActivePublishProfileName);
            Assert.True(vm.ActivatePublishProfile("alpha"));
            Assert.Equal("alpha", vm.ActivePublishProfileName);
            await FlushUi();
            Assert.Contains(vm.Dashboard.OutputLog, l =>
                l.Message.Contains("profile active") && l.Message.Contains("下次 Deploy"));

            vm.DeployFromDock(forceRepublish: true);
            await WaitFor(() => vm.SessionCards.Any(c => c.Name == "Publish"));
            Assert.Equal(Path.GetFullPath(rootA),
                Path.GetFullPath(vm.SessionCards.Single(c => c.Name == "Publish").Model.WorkingDirectory));

            vm.SessionCards.Single(c => c.Name == "Publish").Model.Pty.Kill();
            await WaitFor(() => vm.Dashboard.OutputLog.Any(l => l.Message.Contains("publish failed")));

            Assert.True(vm.DeletePublishProfile("alpha"));
            Assert.Equal("", vm.ActivePublishProfileName);
            // beta is still saved but inactive — CWD must win over beta's pinned root.
            Directory.SetCurrentDirectory(rootA);
            vm.DeployFromDock(forceRepublish: true);
            await WaitFor(() => vm.SessionCards.Count(c => c.Name == "Publish") == 2);
            Assert.Equal(Path.GetFullPath(rootA),
                Path.GetFullPath(vm.SessionCards.Last(c => c.Name == "Publish").Model.WorkingDirectory));

            var loaded = new SettingsStore(settingsPath).Load();
            Assert.Equal("beta", Assert.Single(loaded.PublishProfiles).Name);
            Assert.Equal("", loaded.ActivePublishProfileId);
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
            vm.Dispose();
            Directory.Delete(rootA, true);
            Directory.Delete(rootB, true);
            File.Delete(settingsPath);
        }
    }

    [AvaloniaFact]
    public async Task DeployMenu_ListsProfilesAndRecentArtifacts_RepublishStaysFirst()
    {
        PtySessionFactory.UseMock = true;
        var root = TempRepo(withScript: true, withArtifact: true);
        var win = Directory.CreateDirectory(Path.Combine(root, "artifacts", "publish", "win-x64"));
        var setup = Path.Combine(win.FullName, "Setup.exe");
        File.WriteAllText(setup, "x");
        File.SetLastWriteTimeUtc(setup, DateTime.UtcNow.AddHours(2));
        var settingsPath = Path.Combine(Path.GetTempPath(), $"th-dock-set-{Guid.NewGuid():N}.json");
        var empty = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"th-empty-{Guid.NewGuid():N}"));
        var previous = Directory.GetCurrentDirectory();
        var window = new MainWindow(new SettingsStore(settingsPath), _ => { }) { Width = 1200, Height = 800 };
        try
        {
            Directory.SetCurrentDirectory(empty.FullName);
            window.Show();
            await FlushUi();
            var vm = (MainWindowViewModel)window.DataContext!;

            window.RefreshDeployContextMenu();
            var deploy = window.FindControl<Button>("DeployDockButton");
            Assert.NotNull(deploy);
            var flyout = Assert.IsType<MenuFlyout>(deploy!.ContextFlyout);
            var republish = Assert.IsAssignableFrom<MenuItem>(flyout.Items[0]!);
            Assert.Contains("重新打包", republish.Header?.ToString());

            var profiles = MenuByHeader(flyout, "配置档");
            var deletes = MenuByHeader(flyout, "删除配置档");
            var recent = MenuByHeader(flyout, "最近产物");
            Assert.Contains("无已存配置", OnlyChild(profiles).Header?.ToString());
            Assert.Contains("暂无产物", OnlyChild(recent).Header?.ToString());
            Assert.False(OnlyChild(recent).IsEnabled);

            Directory.SetCurrentDirectory(root);
            window.RefreshDeployContextMenu();
            var listed = recent.Items.OfType<MenuItem>().ToList();
            Assert.Equal(2, listed.Count);
            Assert.Contains("win-x64", listed[0].Tag?.ToString());
            Assert.Contains("linux-x64", listed[1].Tag?.ToString());

            Assert.True(vm.SavePublishProfile("alpha", "", "", ""));
            Assert.True(vm.SavePublishProfile("beta", "", PublishProfiles.LinuxRid, "note"));
            window.RefreshDeployContextMenu();
            var names = profiles.Items.OfType<MenuItem>().Select(i => i.Header?.ToString() ?? "").ToList();
            Assert.Contains(names, n => n.Contains("beta") && n.Contains("✓"));
            Assert.Contains(names, n => n == "alpha");
            Assert.Equal(2, deletes.Items.OfType<MenuItem>().Count());

            var recentItems = recent.Items.OfType<MenuItem>().ToList();
            Assert.Equal(2, recentItems.Count);
            Assert.Contains("win-x64", recentItems[0].Tag?.ToString());
            Assert.Contains("linux-x64", recentItems[1].Tag?.ToString());
            Assert.DoesNotContain(recentItems, i => (i.Header?.ToString() ?? "").Contains("示例"));

            profiles.Items.OfType<MenuItem>().Single(i => (i.Header?.ToString() ?? "") == "alpha")
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal("alpha", vm.ActivePublishProfileName);

            recentItems[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await FlushUi();
            Assert.Contains(vm.Dashboard.OutputLog, l =>
                l.Source == "deploy" && l.Message.Contains("win-x64"));

            republish.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitFor(() => vm.SessionCards.Any(c => c.Name == "Publish" && c.Model.Tag == SessionTag.Deploy));
            Assert.Contains(vm.Dashboard.OutputLog, l =>
                l.Source == "deploy" && l.Message.Contains("force republish"));
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
            window.Close();
            Directory.Delete(root, true);
            Directory.Delete(empty.FullName, true);
            File.Delete(settingsPath);
        }
    }

    [AvaloniaFact]
    public async Task PublishPtyLines_StreamIntoOutput_AsDeploy_OtherSessionsKeepTheirName()
    {
        PtySessionFactory.UseMock = true;
        var root = TempRepo(withScript: true, withArtifact: false);
        var vm = new MainWindowViewModel();
        try
        {
            await vm.NewSessionCommand.ExecuteAsync(null);
            await WaitFor(() => vm.SessionCards.Count == 1);
            var plain = vm.SessionCards.Single();
            vm.Dashboard.SelectedBottomTab = 2;
            plain.Model.Emulator.SendText("PLAIN_SESSION_TOKEN\r");
            await WaitFor(() => vm.Dashboard.OutputLog.Any(l => l.Message.Contains("PLAIN_SESSION_TOKEN")));
            Assert.All(
                vm.Dashboard.OutputLog.Where(l => l.Message.Contains("PLAIN_SESSION_TOKEN")),
                l => Assert.Equal(plain.Name, l.Source));
            Assert.Equal(2, vm.Dashboard.SelectedBottomTab);

            vm.DeployFromDock(forceRepublish: true, root);
            await WaitFor(() => vm.SessionCards.Any(c => c.Name == "Publish" && c.Model.IsRunning));
            Assert.True(vm.IsPublishRunning);
            Assert.True(vm.PublishBusy());
            Assert.Equal("打包中", vm.DeployDockCaption);

            var publish = vm.SessionCards.Single(c => c.Name == "Publish");
            vm.Dashboard.SelectedBottomTab = 2;
            publish.Model.Emulator.SendText("PUBLISH_STREAM_TOKEN\r");
            await WaitFor(() => vm.Dashboard.OutputLog.Any(l =>
                l.Message.Contains("PUBLISH_STREAM_TOKEN") && l.Source == "deploy"));
            Assert.All(
                vm.Dashboard.OutputLog.Where(l => l.Message.Contains("PUBLISH_STREAM_TOKEN")),
                l => Assert.Equal("deploy", l.Source));
            Assert.DoesNotContain(vm.Dashboard.OutputLog, l =>
                l.Message.Contains("PUBLISH_STREAM_TOKEN") && l.Source == "Publish");
            Assert.Equal(0, vm.Dashboard.SelectedBottomTab);
            Assert.Contains(vm.Dashboard.OutputLog, l =>
                l.Source == "deploy" && l.Message.Contains("publish start"));
        }
        finally
        {
            vm.Dispose();
            Directory.Delete(root, true);
        }
    }

    [AvaloniaFact]
    public async Task CancelPublish_StopsSession_ReportsCancel_ThenAnotherPublishCanStart()
    {
        PtySessionFactory.UseMock = true;
        var root = TempRepo(withScript: true, withArtifact: false);
        var settingsPath = Path.Combine(Path.GetTempPath(), $"th-dock-set-{Guid.NewGuid():N}.json");
        var vm = new MainWindowViewModel(settingsStore: new SettingsStore(settingsPath));
        try
        {
            vm.CancelPublishCommand.Execute(null);
            await FlushUi();
            Assert.Contains(vm.Dashboard.OutputLog, l =>
                l.Source == "deploy" && l.Message.Contains("publish is not running"));
            var idleWarnings = vm.Dashboard.OutputLog.Count(l => l.Message.Contains("publish is not running"));
            vm.CancelPublishCommand.Execute(null);
            await FlushUi();
            Assert.Equal(idleWarnings, vm.Dashboard.OutputLog.Count(l => l.Message.Contains("publish is not running")));

            vm.DeployFromDock(forceRepublish: true, root);
            await WaitFor(() => vm.PublishBusy());
            Assert.True(vm.IsPublishRunning);

            vm.CancelPublishCommand.Execute(null);
            await WaitFor(() => vm.Dashboard.OutputLog.Any(l =>
                l.Source == "deploy" && l.Message.Contains("publish cancelled") && l.Message.Contains("已取消打包")));
            Assert.False(vm.PublishBusy());
            Assert.False(vm.IsPublishRunning);
            Assert.Equal(OperatingSystem.IsWindows() ? "Deploy" : "部署", vm.DeployDockCaption);
            Assert.DoesNotContain(vm.Dashboard.OutputLog, l => l.Message.Contains("publish failed"));

            vm.CancelPublishCommand.Execute(null);
            await FlushUi();
            Assert.Equal(idleWarnings + 1, vm.Dashboard.OutputLog.Count(l => l.Message.Contains("publish is not running")));
            vm.CancelPublishCommand.Execute(null);
            await FlushUi();
            Assert.Equal(idleWarnings + 1, vm.Dashboard.OutputLog.Count(l => l.Message.Contains("publish is not running")));

            vm.DeployFromDock(forceRepublish: true, root);
            await WaitFor(() => vm.SessionCards.Count(c => c.Name == "Publish" && c.Model.IsRunning) == 1);
            Assert.Equal(2, vm.SessionCards.Count(c => c.Name == "Publish"));
            Assert.True(vm.PublishBusy());
            Assert.True(vm.IsPublishRunning);
        }
        finally
        {
            vm.Dispose();
            Directory.Delete(root, true);
            if (File.Exists(settingsPath)) File.Delete(settingsPath);
        }
    }

    [AvaloniaFact]
    public async Task DeployMenu_CancelEnabledOnlyWhilePublishRunning()
    {
        PtySessionFactory.UseMock = true;
        var root = TempRepo(withScript: true, withArtifact: false);
        var settingsPath = Path.Combine(Path.GetTempPath(), $"th-dock-set-{Guid.NewGuid():N}.json");
        var previous = Directory.GetCurrentDirectory();
        var window = new MainWindow(new SettingsStore(settingsPath)) { Width = 1200, Height = 800 };
        try
        {
            Directory.SetCurrentDirectory(root);
            window.Show();
            await FlushUi();
            var vm = (MainWindowViewModel)window.DataContext!;
            vm.DockVisibilityMode = 1;
            await WaitFor(() => window.FindControl<Button>("DeployDockButton")!
                .GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == vm.DeployDockCaption));
            var deploy = window.FindControl<Button>("DeployDockButton");
            Assert.NotNull(deploy);

            window.RefreshDeployContextMenu();
            var flyout = Assert.IsType<MenuFlyout>(deploy!.ContextFlyout);
            var republish = Assert.IsAssignableFrom<MenuItem>(flyout.Items[0]!);
            Assert.Contains("重新打包", republish.Header?.ToString());
            var cancel = MenuByHeader(flyout, "取消打包");
            Assert.Contains("Cancel", cancel.Header?.ToString());
            Assert.False(cancel.IsEnabled);
            Assert.False(vm.IsPublishRunning);
            Assert.Equal(OperatingSystem.IsWindows() ? "Deploy" : "部署", DockCaption(deploy));

            vm.DeployFromDock(forceRepublish: true, root);
            await WaitFor(() => vm.PublishBusy());
            window.RefreshDeployContextMenu();
            Assert.True(vm.IsPublishRunning);
            Assert.True(cancel.IsEnabled);
            await WaitFor(() => DockCaption(deploy) == "打包中");

            cancel.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitFor(() => !vm.PublishBusy() && vm.Dashboard.OutputLog.Any(l =>
                l.Source == "deploy" && l.Message.Contains("publish cancelled")));
            window.RefreshDeployContextMenu();
            Assert.False(vm.IsPublishRunning);
            Assert.False(cancel.IsEnabled);
            await WaitFor(() => DockCaption(deploy) == (OperatingSystem.IsWindows() ? "Deploy" : "部署"));

            republish.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitFor(() => vm.SessionCards.Any(c => c.Name == "Publish" && c.Model.IsRunning));
            window.RefreshDeployContextMenu();
            Assert.True(cancel.IsEnabled);
            Assert.Contains(vm.Dashboard.OutputLog, l =>
                l.Source == "deploy" && l.Message.Contains("force republish"));
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
            window.Close();
            Directory.Delete(root, true);
            if (File.Exists(settingsPath)) File.Delete(settingsPath);
        }
    }

    [AvaloniaFact]
    public async Task LastPublish_Success_PersistsBadge_OpensArtifact_DisablesWhenMissing()
    {
        PtySessionFactory.UseMock = true;
        var root = TempRepo(withScript: true, withArtifact: true);
        var settingsPath = Path.Combine(Path.GetTempPath(), $"th-dock-set-{Guid.NewGuid():N}.json");
        var previous = Directory.GetCurrentDirectory();
        var window = new MainWindow(new SettingsStore(settingsPath), _ => { }) { Width = 1200, Height = 800 };
        MainWindowViewModel? vm = null;
        MainWindowViewModel? reloaded = null;
        try
        {
            Directory.SetCurrentDirectory(root);
            window.Show();
            await FlushUi();
            vm = (MainWindowViewModel)window.DataContext!;
            Assert.Equal("", vm.LastPublishSummary);
            Assert.Equal("", vm.LastPublishBadge);
            Assert.False(vm.HasLastPublishBadge);
            Assert.False(vm.CanOpenLastSuccessfulArtifact);
            Assert.Contains("尚未打包", vm.DeployDockTip);
            Assert.Equal(OperatingSystem.IsWindows() ? "Deploy" : "部署", vm.DeployDockCaption);

            window.RefreshDeployContextMenu();
            var deploy = window.FindControl<Button>("DeployDockButton");
            Assert.NotNull(deploy);
            var flyout = Assert.IsType<MenuFlyout>(deploy!.ContextFlyout);
            var openLast = MenuByHeader(flyout, "打开上次成功产物");
            Assert.Contains("Open last success", openLast.Header?.ToString());
            Assert.False(openLast.IsEnabled);
            var copyLast = MenuByHeader(flyout, "复制上次成功产物路径");
            Assert.Contains("Copy last artifact path", copyLast.Header?.ToString());
            Assert.False(copyLast.IsEnabled);
            Assert.Equal(openLast.IsEnabled, copyLast.IsEnabled);
            var recent = MenuByHeader(flyout, "最近产物");
            Assert.Contains("linux-x64", recent.Items.OfType<MenuItem>().First().Tag?.ToString());

            vm.DeployFromDock(forceRepublish: true, root);
            await WaitFor(() => vm.IsPublishRunning);
            Assert.Equal("打包中", vm.DeployDockCaption);
            Assert.StartsWith("打包中 ·", vm.LastPublishBadge);
            Assert.True(vm.HasLastPublishBadge);
            Assert.Equal("", vm.LastPublishSummary);
            Assert.Contains("已耗时", vm.DeployDockTip);

            vm.SessionCards.Single(c => c.Name == "Publish").Model.Emulator.SendText("exit\r");
            await WaitFor(() => vm.Settings.LastPublishResult?.Outcome == LastPublishResults.Success);
            var expected = Path.GetFullPath(Path.Combine(root, "artifacts", "publish", "linux-x64"));
            var stored = vm.Settings.LastPublishResult!;
            Assert.Equal(0, stored.ExitCode);
            Assert.True(stored.DurationMs >= 0);
            Assert.NotEqual(default, stored.FinishedAt);
            Assert.Equal(expected, Path.GetFullPath(stored.ArtifactPath));
            Assert.Equal(Path.GetFullPath(root), Path.GetFullPath(stored.RepoRoot));
            AssertDurationBadge(vm.LastPublishSummary, "成功");
            Assert.Equal(vm.LastPublishSummary, vm.LastPublishBadge);
            Assert.True(vm.HasLastPublishBadge);
            Assert.True(vm.CanOpenLastSuccessfulArtifact);
            Assert.Contains("exit 0", vm.DeployDockTip);
            Assert.Equal(OperatingSystem.IsWindows() ? "Deploy" : "部署", vm.DeployDockCaption);
            await WaitFor(() => deploy.GetVisualDescendants().OfType<TextBlock>()
                .Any(t => t.IsVisible && t.Text == vm.LastPublishBadge));

            var loaded = new SettingsStore(settingsPath).Load();
            Assert.Equal(LastPublishResults.Success, loaded.LastPublishResult!.Outcome);
            Assert.Equal(stored.ArtifactPath, loaded.LastPublishResult.ArtifactPath);
            Assert.Equal(0, loaded.LastPublishResult.ExitCode);
            Assert.Equal(stored.DurationMs, loaded.LastPublishResult.DurationMs);
            Assert.Equal(stored.FinishedAt, loaded.LastPublishResult.FinishedAt);

            window.RefreshDeployContextMenu();
            Assert.True(openLast.IsEnabled);
            Assert.True(copyLast.IsEnabled);
            Assert.True(vm.CanCopyLastSuccessfulArtifact);
            Assert.Contains("linux-x64", recent.Items.OfType<MenuItem>().First().Tag?.ToString());

            openLast.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await FlushUi();
            Assert.Contains(vm.Dashboard.OutputLog, l =>
                l.Source == "deploy" && l.Message.Contains(stored.ArtifactPath));

            copyLast.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await FlushUi();
            Assert.Contains(vm.Dashboard.OutputLog, l =>
                l.Source == "deploy" && l.Level == "info"
                && l.Message.Contains("已复制产物路径")
                && l.Message.Contains(stored.ArtifactPath));

            vm.DeployFromDock(forceRepublish: true, root);
            await WaitFor(() => vm.SessionCards.Any(c => c.Name == "Publish" && c.Model.IsRunning));
            Assert.Equal("打包中", vm.DeployDockCaption);
            Assert.StartsWith("打包中 ·", vm.LastPublishBadge);
            Assert.True(vm.HasLastPublishBadge);
            Assert.StartsWith("成功", vm.LastPublishSummary);
            vm.SessionCards.Last(c => c.Name == "Publish").Model.Pty.Kill();
            await WaitFor(() => vm.Settings.LastPublishResult!.Outcome == LastPublishResults.Fail);
            Assert.Equal(-1, vm.Settings.LastPublishResult!.ExitCode);
            AssertDurationBadge(vm.LastPublishBadge, "失败 exit -1");
            Assert.Equal(expected, Path.GetFullPath(vm.Settings.LastPublishResult.ArtifactPath));
            Assert.True(vm.CanOpenLastSuccessfulArtifact);
            Assert.Contains("exit -1", vm.DeployDockTip);
            Assert.Contains(vm.Dashboard.OutputLog, l =>
                l.Source == "deploy" && l.Message.Contains("publish failed"));

            Directory.Delete(expected, true);
            Assert.False(vm.CanOpenLastSuccessfulArtifact);
            Assert.False(vm.CanCopyLastSuccessfulArtifact);
            Assert.Equal(expected, Path.GetFullPath(vm.Settings.LastPublishResult.ArtifactPath));
            window.RefreshDeployContextMenu();
            Assert.False(openLast.IsEnabled);
            Assert.False(copyLast.IsEnabled);
            var warns = vm.Dashboard.OutputLog.Count(l => l.Message.Contains("last successful artifact missing"));
            openLast.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            vm.OpenLastSuccessfulArtifact();
            await FlushUi();
            Assert.True(vm.Dashboard.OutputLog.Count(l =>
                l.Source == "deploy" && l.Level == "warn"
                && l.Message.Contains("last successful artifact missing")) > warns);
            var warns2 = vm.Dashboard.OutputLog.Count(l => l.Message.Contains("last successful artifact missing"));
            copyLast.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            vm.CopyLastSuccessfulArtifactPath();
            await FlushUi();
            Assert.True(vm.Dashboard.OutputLog.Count(l =>
                l.Source == "deploy" && l.Level == "warn"
                && l.Message.Contains("last successful artifact missing")) > warns2);

            reloaded = new MainWindowViewModel(settingsStore: new SettingsStore(settingsPath));
            Assert.Equal(LastPublishResults.Fail, reloaded.Settings.LastPublishResult!.Outcome);
            Assert.Equal(-1, reloaded.Settings.LastPublishResult.ExitCode);
            Assert.StartsWith("失败", reloaded.LastPublishBadge);
            Assert.False(reloaded.CanOpenLastSuccessfulArtifact);
            Assert.Contains("exit -1", reloaded.DeployDockTip);
            Assert.Equal(OperatingSystem.IsWindows() ? "Deploy" : "部署", reloaded.DeployDockCaption);
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
            window.Close();
            vm?.Dispose();
            reloaded?.Dispose();
            if (Directory.Exists(root)) Directory.Delete(root, true);
            if (File.Exists(settingsPath)) File.Delete(settingsPath);
        }
    }

    [AvaloniaFact]
    public async Task LastPublish_Cancel_RecordsCancelledOutcome_HidesBadgeWhileRunning()
    {
        PtySessionFactory.UseMock = true;
        var root = TempRepo(withScript: true, withArtifact: false);
        var settingsPath = Path.Combine(Path.GetTempPath(), $"th-dock-set-{Guid.NewGuid():N}.json");
        var vm = new MainWindowViewModel(settingsStore: new SettingsStore(settingsPath));
        MainWindowViewModel? reloaded = null;
        try
        {
            Assert.Equal("", vm.LastPublishSummary);
            Assert.Contains("尚未打包", vm.DeployDockTip);
            Assert.False(vm.CanOpenLastSuccessfulArtifact);

            vm.DeployFromDock(forceRepublish: true, root);
            await WaitFor(() => vm.IsPublishRunning);
            Assert.Equal("打包中", vm.DeployDockCaption);
            Assert.StartsWith("打包中 ·", vm.LastPublishBadge);
            Assert.True(vm.HasLastPublishBadge);
            Assert.DoesNotContain("尚未打包", vm.DeployDockTip);
            Assert.Contains("已耗时", vm.DeployDockTip);

            vm.CancelPublishCommand.Execute(null);
            await WaitFor(() => vm.Settings.LastPublishResult?.Outcome == LastPublishResults.Cancelled);
            Assert.False(vm.PublishBusy());
            Assert.False(vm.IsPublishRunning);
            Assert.Equal(OperatingSystem.IsWindows() ? "Deploy" : "部署", vm.DeployDockCaption);
            Assert.Equal(-1, vm.Settings.LastPublishResult!.ExitCode);
            Assert.True(vm.Settings.LastPublishResult.DurationMs >= 0);
            Assert.NotEqual(default, vm.Settings.LastPublishResult.FinishedAt);
            Assert.Equal("", vm.Settings.LastPublishResult.ArtifactPath);
            AssertDurationBadge(vm.LastPublishBadge, "已取消");
            Assert.Contains("exit -1", vm.DeployDockTip);
            Assert.Contains(vm.Dashboard.OutputLog, l =>
                l.Source == "deploy" && l.Message.Contains("publish cancelled"));
            Assert.DoesNotContain(vm.Dashboard.OutputLog, l => l.Message.Contains("publish failed"));
            Assert.False(vm.CanOpenLastSuccessfulArtifact);

            var loaded = new SettingsStore(settingsPath).Load();
            Assert.Equal(LastPublishResults.Cancelled, loaded.LastPublishResult!.Outcome);
            Assert.Equal(-1, loaded.LastPublishResult.ExitCode);
            Assert.Equal(vm.Settings.LastPublishResult.DurationMs, loaded.LastPublishResult.DurationMs);

            vm.DeployFromDock(forceRepublish: true, root);
            await WaitFor(() => vm.IsPublishRunning);
            Assert.Equal("打包中", vm.DeployDockCaption);
            Assert.StartsWith("打包中 ·", vm.LastPublishBadge);
            Assert.True(vm.HasLastPublishBadge);
            Assert.StartsWith("已取消", vm.LastPublishSummary);
            Assert.Contains("exit -1", vm.DeployDockTip);

            reloaded = new MainWindowViewModel(settingsStore: new SettingsStore(settingsPath));
            Assert.Equal(LastPublishResults.Cancelled, reloaded.Settings.LastPublishResult!.Outcome);
            Assert.StartsWith("已取消", reloaded.LastPublishBadge);
            Assert.Contains("exit -1", reloaded.DeployDockTip);
        }
        finally
        {
            vm.Dispose();
            reloaded?.Dispose();
            Directory.Delete(root, true);
            if (File.Exists(settingsPath)) File.Delete(settingsPath);
        }
    }

    [AvaloniaFact]
    public async Task ActiveProfileLabel_UpdatesOnSwitch_AndClearLastResult_ResetsBadge()
    {
        PtySessionFactory.UseMock = true;
        var root = TempRepo(withScript: true, withArtifact: true);
        var settingsPath = Path.Combine(Path.GetTempPath(), $"th-dock-set-{Guid.NewGuid():N}.json");
        var window = new MainWindow(new SettingsStore(settingsPath)) { Width = 1200, Height = 800 };
        MainWindowViewModel? vm = null;
        try
        {
            window.Show();
            await FlushUi();
            vm = (MainWindowViewModel)window.DataContext!;
            Assert.Equal("", vm.ActivePublishProfileLabel);
            Assert.False(vm.HasActivePublishProfileLabel);
            Assert.False(vm.CanClearLastPublishResult);

            Assert.True(vm.SavePublishProfile("默认", root, "linux-x64", ""));
            Assert.Equal("默认 · linux-x64", vm.ActivePublishProfileLabel);
            Assert.True(vm.HasActivePublishProfileLabel);
            Assert.Contains("配置档：默认 · linux-x64", vm.DeployDockTip);
            await WaitFor(() => window.FindControl<Button>("DeployDockButton")!
                .GetVisualDescendants().OfType<TextBlock>()
                .Any(t => t.IsVisible && t.Text == "默认 · linux-x64"));

            Assert.True(vm.SavePublishProfile("win box", root, "win-x64", ""));
            Assert.Equal("win box · win-x64", vm.ActivePublishProfileLabel);
            Assert.True(vm.ActivatePublishProfile("默认"));
            Assert.Equal("默认 · linux-x64", vm.ActivePublishProfileLabel);

            LastPublishResults.Record(
                vm.Settings, LastPublishResults.Success, 0,
                DateTimeOffset.UtcNow, 42_000, root,
                Path.Combine(root, "artifacts", "publish", "linux-x64"));
            vm.PersistSettings();
            Assert.True(vm.CanClearLastPublishResult);
            Assert.StartsWith("成功", vm.LastPublishBadge);
            Assert.True(vm.CanOpenLastSuccessfulArtifact);

            window.RefreshDeployContextMenu();
            var deploy = window.FindControl<Button>("DeployDockButton");
            Assert.NotNull(deploy);
            var flyout = Assert.IsType<MenuFlyout>(deploy!.ContextFlyout);
            var clear = MenuByHeader(flyout, "清除上次发布结果");
            Assert.Contains("Clear last result", clear.Header?.ToString());
            Assert.True(clear.IsEnabled);

            clear.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await FlushUi();
            Assert.Null(vm.Settings.LastPublishResult);
            Assert.Equal("", vm.LastPublishBadge);
            Assert.False(vm.HasLastPublishBadge);
            Assert.False(vm.CanClearLastPublishResult);
            Assert.False(vm.CanOpenLastSuccessfulArtifact);
            Assert.False(vm.CanCopyLastSuccessfulArtifact);
            Assert.Contains(vm.Dashboard.OutputLog, l =>
                l.Source == "deploy" && l.Level == "info"
                && l.Message.Contains("已清除上次发布结果"));

            window.RefreshDeployContextMenu();
            Assert.False(clear.IsEnabled);

            var loaded = new SettingsStore(settingsPath).Load();
            Assert.Null(loaded.LastPublishResult);
            Assert.Equal("默认", PublishProfiles.Active(loaded)!.Name);

            Assert.True(vm.DeletePublishProfile("默认"));
            Assert.Equal("", vm.ActivePublishProfileName);
            Assert.Equal("", vm.ActivePublishProfileLabel);
            Assert.False(vm.HasActivePublishProfileLabel);
        }
        finally
        {
            vm?.Dispose();
            window.Close();
            Directory.Delete(root, true);
            if (File.Exists(settingsPath)) File.Delete(settingsPath);
        }
    }

    [AvaloniaFact]
    public void PublishProfileWindow_ApplyDraft_RoundTripsFields()
    {
        var dialog = new PublishProfileWindow();
        dialog.ApplyDraft(new PublishProfileDraft("nightly", "/repo", PublishProfiles.WindowsRid, "note"));
        Assert.Equal("nightly", dialog.ProfileName);
        Assert.Equal("/repo", dialog.RepoRoot);
        Assert.Equal(PublishProfiles.WindowsRid, dialog.SelectedRid);
        Assert.Equal("note", dialog.NoteText);

        dialog.ApplyDraft(new PublishProfileDraft("host", "", "", ""));
        Assert.Equal("", dialog.SelectedRid);
        Assert.Equal("host", dialog.ProfileName);
    }

    /// <summary>Regression: the "Publish" session is a one-shot task — the workspace
    /// snapshot must skip it or every restart re-runs the publish script.</summary>
    [AvaloniaFact]
    public async Task PublishSession_ExcludedFromWorkspaceSnapshot()
    {
        PtySessionFactory.UseMock = true;
        var root = TempRepo(withScript: true, withArtifact: false);
        var settingsPath = Path.Combine(Path.GetTempPath(), $"th-dock-set-{Guid.NewGuid():N}.json");
        var vm = new MainWindowViewModel(settingsStore: new SettingsStore(settingsPath));
        try
        {
            await vm.SpawnStartupSessionsAsync();
            await WaitFor(() => vm.SessionCards.Count > 0);
            vm.DeployFromDock(false, root);
            await WaitFor(() => vm.SessionCards.Any(c => c.Name == "Publish"));
            vm.PersistSettings();

            var saved = new SettingsStore(settingsPath).Load();
            Assert.NotEmpty(saved.Workspace.Sessions);          // normal sessions still snapshot
            Assert.DoesNotContain(saved.Workspace.Sessions, s => s.Name == "Publish");
            Assert.DoesNotContain(saved.Workspace.Sessions,
                s => s.Arguments.Contains("publish", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            vm.Dispose();
            Directory.Delete(root, true);
            if (File.Exists(settingsPath)) File.Delete(settingsPath);
        }
    }

    private static MenuItem MenuByHeader(MenuFlyout flyout, string startsWith) =>
        flyout.Items.OfType<MenuItem>().Single(i =>
            i.Header is string header && header.StartsWith(startsWith, StringComparison.Ordinal));

    private static string? DockCaption(Button deploy) =>
        deploy.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text)
            .FirstOrDefault(t => t is "Deploy" or "部署" or "打包中");

    private static MenuItem OnlyChild(MenuItem menu) =>
        Assert.Single(menu.Items.OfType<MenuItem>());

    private static void AssertDurationBadge(string? text, string prefix) =>
        Assert.Matches("^" + System.Text.RegularExpressions.Regex.Escape(prefix) + @" · (<1s|\d+m\d+s|\d+m|\d+s)$", text);
}
