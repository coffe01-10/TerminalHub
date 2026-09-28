using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using TerminalHub.App.ViewModels;
using TerminalHub.App.Views;
using TerminalHub.Core.Pty;
using TerminalHub.Core.Sessions;
using TerminalHub.Pty;
using Xunit;

namespace TerminalHub.Tests;

public class DeployDockTests
{
    private static async Task FlushUi() =>
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

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
        var vm = new MainWindowViewModel();
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
            var shell = OperatingSystem.IsWindows() ? "pwsh" : "bash";
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
        }
    }

    [AvaloniaFact]
    public async Task PublishExit_NonZero_ReportsFailure()
    {
        PtySessionFactory.UseMock = true;
        var root = TempRepo(withScript: true, withArtifact: false);
        var vm = new MainWindowViewModel();
        try
        {
            vm.DeployFromDock(false, root);
            await WaitFor(() => vm.SessionCards.Any(c => c.Name == "Publish"));
            vm.SessionCards.Single(c => c.Name == "Publish").Model.Pty.Kill();
            await WaitFor(() => vm.Dashboard.OutputLog.Any(l =>
                l.Source == "deploy" && l.Level == "error"
                && l.Message.Contains("publish failed") && l.Message.Contains("打包失败")));
        }
        finally
        {
            vm.Dispose();
            Directory.Delete(root, true);
        }
    }

    [AvaloniaFact]
    public async Task ExistingArtifacts_OpenFolder_ForceRepublishes()
    {
        PtySessionFactory.UseMock = true;
        var root = TempRepo(withScript: true, withArtifact: true);
        var vm = new MainWindowViewModel();
        try
        {
            vm.DeployFromDock(false, root);
            await FlushUi();
            Assert.DoesNotContain(vm.SessionCards, c => c.Name == "Publish");
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
        var vm = new MainWindowViewModel();
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
        var window = new MainWindow { Width = 1200, Height = 800 };
        try
        {
            Directory.SetCurrentDirectory(root);
            window.Show();
            await FlushUi();

            var deploy = window.FindControl<Button>("DeployDockButton");
            Assert.NotNull(deploy);
            var center = CenterInWindow(window, deploy!);
            var vm = (MainWindowViewModel)window.DataContext!;
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
}
