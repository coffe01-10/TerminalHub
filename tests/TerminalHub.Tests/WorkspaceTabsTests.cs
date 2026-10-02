using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using TerminalHub.App.ViewModels;
using Xunit;

namespace TerminalHub.Tests;

public class WorkspaceTabsTests
{
    [AvaloniaFact]
    public async Task DragTabsReordersAndPersists_WithoutSwitchingOrRestartingSessions()
    {
        using var f = new StageLayoutTests.StageFixture();
        await Task.Delay(650);
        var first = f.Vm.ActiveWorkspace; first.Name = "短";
        await f.Vm.NewProjectWorkspaceCommand.ExecuteAsync(null); await Task.Delay(100);
        var second = f.Vm.ActiveWorkspace; second.Name = "较长的工作区名称";
        await f.Vm.NewProjectWorkspaceCommand.ExecuteAsync(null); await Task.Delay(150);
        var third = f.Vm.ActiveWorkspace;
        var models = f.Vm.AllSessionCards.Select(c => c.Model).ToArray();
        var active = f.Vm.ActiveSession;
        var start = TabPoint(f.Window, first);
        var end = TabPoint(f.Window, third) + new Vector(25, 0);
        f.Window.MouseDown(start, MouseButton.Left);
        f.Window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        Assert.Single(f.Window.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("dragging"));
        // A drag previews positions and commits only on release.
        Assert.Equal(new[] { first, second, third }, f.Vm.ProjectWorkspaces);
        f.Window.MouseUp(end, MouseButton.Left);
        await Task.Delay(100);
        Assert.Equal(new[] { second, third, first }, f.Vm.ProjectWorkspaces);
        Assert.Equal(f.Vm.ProjectWorkspaces.Select(w => w.Id), f.Vm.Settings.ProjectWorkspaces.Select(w => w.Id));
        Assert.Equal(third.Id, f.Vm.Settings.ActiveProjectWorkspaceId);
        Assert.Same(third, f.Vm.ActiveWorkspace); Assert.Same(active, f.Vm.ActiveSession);
        Assert.Equal(models.OrderBy(s => s.Id), f.Vm.AllSessionCards.Select(c => c.Model).OrderBy(s => s.Id));
        Assert.All(models, s => Assert.True(s.IsRunning));
        var left = TabPoint(f.Window, first);
        var right = TabPoint(f.Window, second) - new Vector(15, 0);
        f.Window.MouseDown(left, MouseButton.Left); f.Window.MouseMove(right, RawInputModifiers.LeftMouseButton); f.Window.MouseUp(right, MouseButton.Left);
        await Task.Delay(100);
        Assert.Equal(new[] { first, second, third }, f.Vm.ProjectWorkspaces);
    }

    [AvaloniaFact]
    public async Task EscapeOrReleaseOutsideCancelsDrag_AndCloseButtonStillCloses()
    {
        using var f = new StageLayoutTests.StageFixture(); await Task.Delay(650);
        var first = f.Vm.ActiveWorkspace;
        await f.Vm.NewProjectWorkspaceCommand.ExecuteAsync(null); await Task.Delay(150);
        var second = f.Vm.ActiveWorkspace;
        var start = TabPoint(f.Window, first); var end = TabPoint(f.Window, second) + new Vector(20, 0);
        f.Window.MouseDown(start, MouseButton.Left); f.Window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        f.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        f.Window.MouseUp(end, MouseButton.Left);
        Assert.Equal(new[] { first, second }, f.Vm.ProjectWorkspaces); Assert.Same(second, f.Vm.ActiveWorkspace);
        f.Window.MouseDown(start, MouseButton.Left);
        var outside = end + new Vector(0, 100);
        f.Window.MouseMove(outside, RawInputModifiers.LeftMouseButton); f.Window.MouseUp(outside, MouseButton.Left);
        Assert.Equal(new[] { first, second }, f.Vm.ProjectWorkspaces);
        await Task.Delay(100); // let the drag preview disappear before the next hit test
        var close = f.Window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("workspace-close") && ReferenceEquals(b.Tag, first));
        var closePoint = close.TranslatePoint(new Point(close.Bounds.Width / 2, close.Bounds.Height / 2), f.Window)!.Value;
        f.Window.MouseDown(closePoint, MouseButton.Left); f.Window.MouseUp(closePoint, MouseButton.Left);
        await Task.Delay(100);
        Assert.Equal(new[] { second }, f.Vm.ProjectWorkspaces);
    }

    [AvaloniaFact]
    public async Task RenameMenuStillTargetsTheChosenWorkspace()
    {
        using var f = new StageLayoutTests.StageFixture(); await Task.Delay(650);
        var first = f.Vm.ActiveWorkspace;
        await f.Vm.NewProjectWorkspaceCommand.ExecuteAsync(null); await Task.Delay(150);
        var button = f.Window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("workspace-name") && ReferenceEquals(b.Tag, first));
        button.ContextMenu!.Open(button); await Task.Delay(30);
        var item = Assert.Single(button.ContextMenu.Items.OfType<MenuItem>());
        Assert.Same(first, item.Tag);
        try
        {
            item.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            await Task.Delay(50);
            var dialog = Assert.Single(f.Window.OwnedWindows);
            dialog.GetVisualDescendants().OfType<TextBox>().Single().Text = "已重命名";
            dialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "保存"))
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("已重命名", first.Name); Assert.Empty(f.Window.OwnedWindows);
        }
        finally { button.ContextMenu.Close(); foreach (var owned in f.Window.OwnedWindows.ToArray()) owned.Close(); }
    }

    [AvaloniaFact]
    public async Task DragAtRightEdgeScrollsOverflowTabs_AndMovesToEnd()
    {
        using var f = new StageLayoutTests.StageFixture(width: 1100); await Task.Delay(650);
        var first = f.Vm.ActiveWorkspace; first.Name = "最左边的长名称工作区";
        for (var i = 0; i < 6; i++)
        {
            await f.Vm.NewProjectWorkspaceCommand.ExecuteAsync(null); await Task.Delay(80);
            f.Vm.ActiveWorkspace.Name = $"较长的项目工作区名称 {i}";
        }
        await Task.Delay(100);
        var scroll = f.Window.FindControl<ScrollViewer>("WorkspaceTabScroll")!;
        scroll.Offset = default; await Task.Delay(50);
        Assert.True(scroll.Extent.Width > scroll.Viewport.Width);
        var start = TabPoint(f.Window, first);
        var edge = scroll.TranslatePoint(new Point(scroll.Bounds.Width - 5, scroll.Bounds.Height / 2), f.Window)!.Value;
        f.Window.MouseDown(start, MouseButton.Left); f.Window.MouseMove(edge, RawInputModifiers.LeftMouseButton);
        // Keep the pointer at the edge while the native dispatcher scrolls.
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (scroll.Offset.X < scroll.Extent.Width - scroll.Viewport.Width - 1 && DateTime.UtcNow < deadline) await Task.Delay(30);
        Assert.True(scroll.Offset.X > 0);
        f.Window.MouseUp(edge, MouseButton.Left); await Task.Delay(100);
        Assert.Same(first, f.Vm.ProjectWorkspaces.Last());
    }

    private static Point TabPoint(Window window, LiveWorkspace workspace)
    {
        var button = window.GetVisualDescendants().OfType<Button>()
            .Single(b => b.Classes.Contains("workspace-name") && ReferenceEquals(b.Tag, workspace));
        return button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
    }

    [AvaloniaFact]
    public async Task RapidTabClicksKeepSwitching_WithoutOpeningRenameDialog()
    {
        using var f = new StageLayoutTests.StageFixture();
        await Task.Delay(650);
        var first = f.Vm.ActiveWorkspace;
        await f.Vm.NewProjectWorkspaceCommand.ExecuteAsync(null);
        await Task.Delay(150);
        var second = f.Vm.ActiveWorkspace;
        try
        {
            // Repeated clicks on one tab can be recognized as double taps,
            // then immediately continue switching to the other workspace.
            for (var i = 0; i < 6; i++)
            {
                Click(first); Click(first); Click(second); Click(second);
            }
            await Task.Delay(100);
            Assert.Empty(f.Window.OwnedWindows);
            Assert.Same(second, f.Vm.ActiveWorkspace);
            Click(first);
            await Task.Delay(100);
            Assert.Same(first, f.Vm.ActiveWorkspace);
            Assert.Same(first.Active!.Emulator, f.Window.FindControl<TerminalHub.App.Controls.TerminalView>("MainTerminal")!.Emulator);
            // Clicking the already active tab must return keyboard input to the terminal.
            Click(first); f.Window.KeyTextInput("workspace-click-focus");
            await Task.Delay(50);
            Assert.Contains("workspace-click-focus", f.Vm.ActiveSession!.Emulator.Buffer.TailText(20));
        }
        finally { foreach (var owned in f.Window.OwnedWindows.ToArray()) owned.Close(); }

        void Click(LiveWorkspace workspace)
        {
            var button = f.Window.GetVisualDescendants().OfType<Button>()
                .Single(b => b.Classes.Contains("workspace-name") && ReferenceEquals(b.Tag, workspace));
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), f.Window)!.Value;
            f.Window.MouseMove(point);
            f.Window.MouseDown(point, MouseButton.Left);
            f.Window.MouseUp(point, MouseButton.Left);
        }
    }
}
