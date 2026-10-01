using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using TerminalHub.App.Controls;
using TerminalHub.App.ViewModels;
using TerminalHub.App.Views;
using TerminalHub.Core.Settings;
using TerminalHub.Pty;
using Xunit;

namespace TerminalHub.Tests;

public class StageLayoutTests
{
    internal sealed class StageFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "terminalhub-stage-" + Guid.NewGuid());
        public MainWindow Window { get; }
        public MainWindowViewModel Vm => (MainWindowViewModel)Window.DataContext!;

        public StageFixture(int width = 1440, int height = 900)
        {
            PtySessionFactory.UseMock = true;
            var store = new SettingsStore(Path.Combine(_directory, "settings.json"));
            store.Save(new AppSettings
            {
                StartupSessions = Enumerable.Range(1, 5).Select(i => new StartupSession
                {
                    Name = $"Terminal {i:00}", Tag = i == 2 ? "测试环境" : "开发环境",
                    WorkingDirectory = Environment.CurrentDirectory
                }).ToList()
            });
            Window = new MainWindow(store) { Width = width, Height = height };
            Window.Show();
        }

        public void Dispose()
        {
            Window.Close();
            Directory.Delete(_directory, true);
        }
    }

    [AvaloniaTheory]
    [InlineData(1440, 900)]
    [InlineData(1100, 680)]
    public async Task ShelfAndDock_StayUsableAtBothWindowSizes(int width, int height)
    {
        using var fixture = new StageFixture(width, height);
        var window = fixture.Window;
        await Task.Delay(700);
        fixture.Vm.ActiveCard = fixture.Vm.SessionCards[2];
        fixture.Vm.DockVisibilityMode = 0; // exercise the auto-hide path explicitly
        fixture.Vm.OutputVisible = false;    // this test asserts the toggle reveals the panel
        await Task.Delay(450);
        var stage = window.FindControl<StageSurface>("StageWindow")!;
        var dock = window.FindControl<DropletDock>("ActionDock")!;
        Assert.False(dock.IsHitTestVisible);
        Assert.False(fixture.Vm.InspectorVisible);
        Assert.False(window.FindControl<Border>("OutputPanel")!.IsVisible);
        fixture.Vm.DockVisibilityMode = 1;
        await Task.Delay(450);
        var stageBottom = stage.TranslatePoint(new Point(0, stage.Bounds.Height), window)!.Value.Y;
        var dockTop = dock.TranslatePoint(default, window)!.Value.Y;
        Assert.True(stageBottom < dockTop, "The dock must not obscure terminal output.");
        var dockCenter = dock.TranslatePoint(new Point(dock.Bounds.Width / 2, 0), window)!.Value.X;
        Assert.InRange(dockCenter, window.Bounds.Width / 2 - 1, window.Bounds.Width / 2 + 1);
        Assert.True(window.FindControl<TerminalView>("MainTerminal")!.Bounds.Height > 120);
        Assert.Equal(5, window.GetVisualDescendants().OfType<StageCard>().Count());
        var viewport = window.FindControl<Border>("TerminalViewport")!;
        var terminal = window.FindControl<TerminalView>("MainTerminal")!;
        Assert.True(terminal.TranslatePoint(default, viewport)!.Value.Y >= 8);

        var expandedWidth = stage.Bounds.Width;
        window.KeyPressQwerty(PhysicalKey.B, RawInputModifiers.Control | RawInputModifiers.Shift);
        window.KeyPressQwerty(PhysicalKey.J, RawInputModifiers.Control | RawInputModifiers.Shift);
        await Task.Delay(100);
        Assert.True(fixture.Vm.InspectorVisible);
        Assert.True(window.FindControl<Border>("OutputPanel")!.IsVisible);
        Assert.True(stage.Bounds.Width < expandedWidth - 250);
        window.KeyPressQwerty(PhysicalKey.B, RawInputModifiers.Control | RawInputModifiers.Shift);
        window.KeyPressQwerty(PhysicalKey.J, RawInputModifiers.Control | RawInputModifiers.Shift);
        fixture.Vm.DockVisibilityMode = 0;
        await Task.Delay(450);

        // Capture the real view, with representative output fed through its VT parser.
        foreach (var card in fixture.Vm.SessionCards)
        {
            card.Model.Emulator.Parser.Feed("\u001b[36mPS D:\\Projects\\TerminalHub>\u001b[0m dotnet build\r\n\r\n" +
                "  Restoring projects...\r\n  All projects are up-to-date.\r\n\r\n" +
                "\u001b[32mBuild succeeded.\u001b[0m\r\n  0 Warning(s)\r\n  0 Error(s)\r\n\r\n" +
                "Time Elapsed 00:00:02.41\r\n\u001b[36mPS D:\\Projects\\TerminalHub>\u001b[0m ");
            card.Refresh();
        }
        await Task.Delay(450);
        var output = Environment.GetEnvironmentVariable("TERMINALHUB_STAGE_CAPTURES");
        if (output is not null)
        {
            Directory.CreateDirectory(output);
            window.CaptureRenderedFrame()!.Save(Path.Combine(output, $"stage-{width}.png"));
            window.MouseMove(new Point(window.Bounds.Width / 2, window.Bounds.Height - 40));
            await Task.Delay(450);
            window.CaptureRenderedFrame()!.Save(Path.Combine(output, $"stage-dock-{width}.png"));
        }
    }

    [AvaloniaFact]
    public async Task ShelfDrag_BoundaryJitterDoesNotReorderUntilDrop_OrActivateBackgroundSession()
    {
        using var fixture = new StageFixture();
        await Task.Delay(700);
        var window = fixture.Window;
        var vm = fixture.Vm;
        var order = vm.SessionCards.ToArray();
        var active = vm.ActiveSession;
        var shelf = window.FindControl<ListBox>("SessionShelf")!;
        shelf.ScrollIntoView(order[0]);
        await Task.Delay(100);
        var cards = shelf.GetVisualDescendants().OfType<StageCard>().ToArray();
        var first = cards.Single(c => ReferenceEquals(c.DataContext, order[0]));
        var second = cards.Single(c => ReferenceEquals(c.DataContext, order[1]));
        var firstContainer = first.FindAncestorOfType<ListBoxItem>()!;
        var secondContainer = second.FindAncestorOfType<ListBoxItem>()!;
        var origin = firstContainer.TranslatePoint(new Point(90, window.ThumbnailHeight / 2), window)!.Value;
        var stride = secondContainer.Bounds.Y - firstContainer.Bounds.Y;
        window.MouseDown(origin, MouseButton.Left);
        window.MouseMove(origin + new Vector(0, stride / 2 + 14));
        for (var i = 0; i < 8; i++)
        {
            window.MouseMove(origin + new Vector(0, stride / 2 + (i % 2 == 0 ? 2 : -2)));
            await Task.Delay(25);
        }
        await Task.Delay(280);
        Assert.Equal(order, vm.SessionCards);
        Assert.Same(active, vm.ActiveSession);
        Assert.InRange(second.SlotOffset, -stride - 1, -stride + 1);
        window.MouseUp(origin + new Vector(0, stride / 2), MouseButton.Left);
        await Task.Delay(600);
        Assert.Same(order[1], vm.SessionCards[0]);
        Assert.Same(order[0], vm.SessionCards[1]);
        Assert.Same(active, vm.ActiveSession);
        Assert.All(shelf.GetVisualDescendants().OfType<StageCard>(), c => Assert.Equal(0, c.SlotOffset));
    }

    [AvaloniaFact]
    public async Task SplitFocus_LeavesSurfaceStill_AndTitlesFollowRenameAndAssignment()
    {
        using var fixture = new StageFixture();
        await Task.Delay(700);
        var window = fixture.Window;
        var vm = fixture.Vm;
        var surface = window.FindControl<StageSurface>("StageWindow")!;
        vm.ToggleSplitCommand.Execute(null);
        await Task.Delay(100);
        foreach (var side in new[] { "Right", "Left", "Right" })
        {
            var pane = window.FindControl<Border>(side + "PaneBox")!;
            var point = pane.TranslatePoint(new Point(60, 65), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            await Task.Delay(60);
            Assert.Same(side == "Left" ? vm.LeftPane : vm.RightPane, vm.ActiveSession);
            Assert.True(surface.RenderTransform!.Value.IsIdentity);
            Assert.Equal(1, surface.Reveal);
        }
        var right = vm.SessionCards.Single(c => ReferenceEquals(c.Model, vm.RightPane));
        vm.RenameSession((right, "右侧 · 构建日志"));
        Assert.Equal("右侧 · 构建日志", window.FindControl<TextBlock>("RightPaneTitle")!.Text);
        Assert.Equal(vm.LeftPane!.Name, window.FindControl<TextBlock>("LeftPaneTitle")!.Text);
        vm.ActiveCard = vm.SessionCards.First(c => c.Model != vm.LeftPane && c.Model != vm.RightPane);
        await Task.Delay(60);
        Assert.Equal(vm.RightPane!.Name, window.FindControl<TextBlock>("RightPaneTitle")!.Text);
        Assert.True(surface.RenderTransform!.Value.IsIdentity);
    }

    [AvaloniaFact]
    public async Task TiltedCard_ClickAndRapidSwitch_PreserveSessionAndInput()
    {
        using var fixture = new StageFixture();
        await Task.Delay(700);
        var window = fixture.Window;
        var vm = fixture.Vm;
        var originalSessions = vm.SessionCards.Select(c => c.Model).ToArray();
        window.FindControl<ListBox>("SessionShelf")!.ScrollIntoView(vm.SessionCards[0]);
        await Task.Delay(100);
        var thumbnail = window.GetVisualDescendants().OfType<StageCard>().First();
        var restingPose = thumbnail.Child!.RenderTransform!.Value;
        var point = thumbnail.TranslatePoint(new Point(80, 45), window)!.Value;
        window.MouseMove(point);
        await Task.Delay(40);
        var container = thumbnail.GetVisualAncestors().OfType<ListBoxItem>().First();
        var presenter = container.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().First();
        Assert.True(presenter.Background is null || presenter.Background is Avalonia.Media.ISolidColorBrush { Color.A: 0 });
        var output = Environment.GetEnvironmentVariable("TERMINALHUB_STAGE_CAPTURES");
        if (output is not null)
            window.CaptureRenderedFrame()!.Save(Path.Combine(output, "stage-hover.png"));
        await Task.Delay(400);
        Assert.True(thumbnail.IsPointerOver);
        Assert.NotEqual(restingPose, thumbnail.Child.RenderTransform!.Value);
        Assert.True(thumbnail.RenderTransform!.Value.IsIdentity);
        var hoverPose = thumbnail.Child.RenderTransform.Value;
        // This fixed slot edge used to enter/exit repeatedly as the card tilted.
        var edge = thumbnail.TranslatePoint(new Point(2, 45), window)!.Value;
        window.MouseMove(edge);
        await Task.Delay(400);
        Assert.True(thumbnail.IsPointerOver);
        Assert.Equal(hoverPose, thumbnail.Child.RenderTransform.Value);
        window.MouseMove(new Point(500, 100));
        await Task.Delay(400);
        Assert.False(thumbnail.IsPointerOver);
        Assert.Equal(restingPose, thumbnail.Child.RenderTransform.Value);
        window.MouseMove(point);
        await Task.Delay(150);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        await Task.Delay(400);
        Assert.Same(originalSessions[0], vm.ActiveSession);
        Assert.True(window.FindControl<TerminalView>("MainTerminal")!.IsFocused);
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Control);
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Control);
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Control);
        await Task.Delay(450);
        Assert.Same(originalSessions[3], vm.ActiveSession);
        Assert.Equal(originalSessions, vm.SessionCards.Select(c => c.Model));
        window.KeyTextInput("stage-input");
        await Task.Delay(100);
        Assert.Contains("stage-input", vm.ActiveSession!.Emulator.Buffer.TailText(10));
        Assert.DoesNotContain("stage-input", originalSessions[0].Emulator.Buffer.TailText(10));

        vm.ToggleSplitCommand.Execute(null);
        await Task.Delay(150);
        Assert.True(vm.IsSplit);
        Assert.NotSame(vm.LeftPane, vm.RightPane);
        vm.ToggleSplitCommand.Execute(null);
        vm.OpenInNewWindowCommand.Execute(null);
        await Task.Delay(150);
        var popout = Assert.Single(vm.Popouts);
        popout.Close();
        await Task.Delay(200);
        Assert.Equal(5, vm.SessionCards.Count);
        Assert.All(originalSessions, session => Assert.True(session.IsRunning));
    }

    [AvaloniaFact]
    public async Task Dock_GrowsFromHint_WithoutBottomBlankAreaTriggeringIt()
    {
        using var fixture = new StageFixture();
        await Task.Delay(700);
        var window = fixture.Window;
        fixture.Vm.DockVisibilityMode = 0; // the hint pill exists only in auto-hide
        var dock = window.FindControl<DropletDock>("ActionDock")!;
        var hint = window.FindControl<Border>("DockHint")!;
        // Reveal eases back to 0 after the mode flip — wait until it settles.
        for (var i = 0; i < 40 && dock.Reveal > 0; i++) await Task.Delay(100);
        Assert.Equal(0, dock.Reveal);
        Assert.Equal(82, dock.SurfaceBounds.Width);
        Assert.Equal(30, dock.SurfaceBounds.Height);
        window.MouseMove(new Point(window.Bounds.Width / 2 - 180, window.Bounds.Height - 40));
        await Task.Delay(100);
        Assert.Equal(0, dock.Reveal);
        var point = hint.TranslatePoint(new Point(41, 15), window)!.Value;
        window.MouseMove(point);
        await Task.Delay(80);
        Assert.InRange(dock.Reveal, .01, .99);
        Assert.InRange(dock.SurfaceBounds.Width, 83, dock.Bounds.Width - 1);
        Assert.Equal(dock.Bounds.Height, dock.SurfaceBounds.Bottom, 5);
        Assert.True(hint.Opacity < 1);
        await Task.Delay(450);
        Assert.Equal(dock.Bounds.Width, dock.SurfaceBounds.Width, 5);
        Assert.Equal(1, dock.Child!.Opacity);
        Assert.False(hint.IsHitTestVisible);
        Assert.True(dock.IsHitTestVisible);
    }

    [AvaloniaFact]
    public async Task AutoHideDock_RevealsAndHides_AndHiddenModeIgnoresPointer()
    {
        using var fixture = new StageFixture();
        var window = fixture.Window;
        await Task.Delay(700);
        fixture.Vm.DockVisibilityMode = 0; // auto-hide is the behavior under test
        await Task.Delay(300);
        var dock = window.FindControl<DropletDock>("ActionDock")!;
        var hint = window.FindControl<Border>("DockHint")!;
        var bottom = hint.TranslatePoint(new Point(hint.Bounds.Width / 2, hint.Bounds.Height / 2), window)!.Value;
        window.MouseMove(bottom);
        await Task.Delay(400);
        Assert.True(dock.IsHitTestVisible);
        var dockCenter = dock.TranslatePoint(new Point(dock.Bounds.Width / 2, dock.Bounds.Height / 2), window)!.Value;
        window.MouseMove(dockCenter);
        await Task.Delay(550);
        Assert.True(dock.IsHitTestVisible);
        window.MouseMove(new Point(500, 100));
        await Task.Delay(800);
        Assert.False(dock.IsHitTestVisible);
        fixture.Vm.DockVisibilityMode = 2;
        window.MouseMove(bottom);
        await Task.Delay(400);
        Assert.False(dock.IsHitTestVisible);
        fixture.Vm.DockVisibilityMode = 1;
        await Task.Delay(400);
        Assert.True(dock.IsHitTestVisible);
    }

    [AvaloniaFact]
    public async Task Activation_ExpandsFromThumbnailBounds_WithoutResizingPty()
    {
        using var fixture = new StageFixture();
        await Task.Delay(700);
        var surface = fixture.Window.FindControl<StageSurface>("StageWindow")!;
        var emulator = fixture.Vm.ActiveSession!.Emulator;
        var gridSize = (emulator.Buffer.Columns, emulator.Buffer.Rows);
        var source = new Rect(-260, 70, 220, 180);
        surface.FinishActivation(); // Start a fresh flight after any startup animation.
        surface.ActivateFrom(source);
        surface.Transitions = null;
        surface.Reveal = 0;
        var initial = surface.RenderTransform!.Value;
        Assert.Equal(new Point(source.X, source.Y), new Point().Transform(initial));
        Assert.Equal(source.Width, surface.Bounds.Width * initial.M11, 5);
        Assert.Equal(source.Height, surface.Bounds.Height * initial.M22, 5);
        surface.Reveal = .45;
        var middle = surface.RenderTransform.Value;
        if (Environment.GetEnvironmentVariable("TERMINALHUB_STAGE_CAPTURES") is { } output)
        {
            Directory.CreateDirectory(output);
            fixture.Window.CaptureRenderedFrame()!.Save(Path.Combine(output, "stage-expanding.png"));
        }
        surface.ActivateFrom(new Rect(-260, 400, 220, 180));
        surface.Transitions = null;
        surface.Reveal = 0;
        Assert.Equal(middle, surface.RenderTransform.Value);
        surface.Reveal = 1;
        Assert.True(surface.RenderTransform.Value.IsIdentity);
        Assert.Equal(gridSize, (emulator.Buffer.Columns, emulator.Buffer.Rows));
    }

    [AvaloniaFact]
    public async Task SwitchAndClose_DuringSpringAndQueuedSelection_DoesNotLeaveCallbacks()
    {
        using var fixture = new StageFixture();
        await Task.Delay(700);
        var surface = fixture.Window.FindControl<StageSurface>("StageWindow")!;
        fixture.Vm.ActiveCard = fixture.Vm.SessionCards[0];
        await Task.Delay(90);
        Assert.False(surface.RenderTransform?.Value.IsIdentity ?? true);
        fixture.Vm.ActiveCard = fixture.Vm.SessionCards[1];
        fixture.Window.Close();
        await Task.Delay(650);
        Assert.Equal(1d, surface.Reveal);
        Assert.True(surface.RenderTransform?.Value.IsIdentity ?? true);
    }

    [AvaloniaFact]
    public async Task SwitchingIntoDifferentGrid_ResizesBeforeDrawing_AndPreferencesPersist()
    {
        using var fixture = new StageFixture();
        await Task.Delay(700);
        var vm = fixture.Vm;
        var current = vm.ActiveSession!.Emulator.Buffer;
        var expectedColumns = current.Columns;
        var expectedRows = current.Rows;
        var next = vm.SessionCards[0];
        next.Model.Emulator.Resize(240, 80);
        vm.ActiveCard = next;
        await Task.Delay(650);
        Assert.Equal(expectedColumns, next.Model.Emulator.Buffer.Columns);
        Assert.Equal(expectedRows, next.Model.Emulator.Buffer.Rows);
        vm.InspectorVisible = true;
        vm.OutputVisible = true;
        vm.DockVisibilityMode = 2;
        Assert.True(vm.Settings.InspectorVisible);
        Assert.True(vm.Settings.OutputVisible);
        Assert.Equal(2, vm.Settings.DockVisibilityMode);
    }
}


