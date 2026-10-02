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

public class ShelfDrawerTests
{
    private static void Click(StageLayoutTests.StageFixture fixture, string name)
    {
        var button = fixture.Window.FindControl<Button>(name)!;
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), fixture.Window)!.Value;
        fixture.Window.MouseDown(point, MouseButton.Left);
        fixture.Window.MouseUp(point, MouseButton.Left);
    }

    private static async Task Ready(StageLayoutTests.StageFixture fixture)
    {
        var deadline = Environment.TickCount64 + 5000;
        while (fixture.Vm.SessionCards.Count != 5 && Environment.TickCount64 < deadline) await Task.Delay(25);
        Assert.Equal(5, fixture.Vm.SessionCards.Count);
        await Task.Delay(400);
        fixture.Window.UpdateLayout();
    }

    private static StageCard Card(StageLayoutTests.StageFixture fixture, SessionCardViewModel model) =>
        fixture.Window.FindControl<ListBox>("SessionShelf")!.GetVisualDescendants().OfType<StageCard>()
            .Single(c => ReferenceEquals(c.DataContext, model));

    private static void AssertCentered(StageLayoutTests.StageFixture fixture)
    {
        var shelf = fixture.Window.FindControl<ListBox>("SessionShelf")!;
        var card = Card(fixture, fixture.Vm.ActiveCard!);
        var center = card.TranslatePoint(new Point(0, card.Bounds.Height / 2), shelf)!.Value.Y;
        Assert.InRange(center, shelf.Bounds.Height / 2 - 2, shelf.Bounds.Height / 2 + 2);
    }

    [AvaloniaTheory]
    [InlineData(1100, 680)]
    [InlineData(1440, 900)]
    public async Task FirstMiddleLast_CenterAndFadeAtBothWindowSizes(int width, int height)
    {
        using var fixture = new StageLayoutTests.StageFixture(width, height);
        await Ready(fixture);
        var window = fixture.Window;
        fixture.Vm.ShelfAutoHide = true;
        await Task.Delay(300);
        window.MouseMove(new Point(4, 300));
        await Task.Delay(300);
        foreach (var index in new[] { 0, 2, 4 })
        {
            fixture.Vm.ActivateCard(fixture.Vm.SessionCards[index]);
            await Task.Delay(350);
            window.UpdateLayout();
            AssertCentered(fixture);
            var active = Card(fixture, fixture.Vm.ActiveCard!);
            var near = Card(fixture, fixture.Vm.SessionCards[index == 4 ? 3 : index + 1]);
            var far = Card(fixture, fixture.Vm.SessionCards[index == 0 ? 4 : 0]);
            Assert.Equal(1, active.Opacity);
            Assert.True(near.Opacity > far.Opacity, $"Near {near.Opacity}, far {far.Opacity}");
        }
    }

    [AvaloniaFact]
    public async Task EdgeReveal_WheelAndRapidReversal_PreserveTerminalSizeAndProcesses()
    {
        using var fixture = new StageLayoutTests.StageFixture();
        await Ready(fixture);
        var window = fixture.Window;
        var vm = fixture.Vm;
        vm.InspectorVisible = false;
        var models = vm.SessionCards.Select(c => c.Model).ToArray();
        var stage = window.FindControl<StageSurface>("StageWindow")!;
        var drawer = window.FindControl<ShelfDrawer>("ShelfHost")!;
        var pinnedWidth = stage.Bounds.Width;
        Click(fixture, "ShelfToggleButton");
        await Task.Delay(400);
        window.UpdateLayout();
        Assert.True(vm.ShelfAutoHide);
        Assert.False(drawer.IsVisible);
        Assert.False(drawer.IsHitTestVisible);
        Assert.True(stage.Bounds.Width > pinnedWidth + 200);
        var expandedWidth = stage.Bounds.Width;
        var columns = vm.ActiveSession!.Emulator.Buffer.Columns;
        var edge = new Point(4, 300);
        window.MouseMove(edge);
        await Task.Delay(300);
        window.UpdateLayout();
        Assert.True(drawer.IsVisible && drawer.IsHitTestVisible);
        Assert.Equal(1, drawer.Reveal);
        AssertCentered(fixture);
        Assert.Equal(expandedWidth, stage.Bounds.Width);
        Assert.Equal(columns, vm.ActiveSession!.Emulator.Buffer.Columns);

        var shelf = window.FindControl<ListBox>("SessionShelf")!;
        var wheelPoint = shelf.TranslatePoint(new Point(90, shelf.Bounds.Height / 2), window)!.Value;
        var before = vm.ActiveCard!;
        var visibleCards = vm.ShelfItems.OfType<SessionCardViewModel>().ToList();
        var previousIndex = visibleCards.IndexOf(before);
        window.MouseWheel(wheelPoint, new Vector(0, previousIndex == 4 ? 1 : -1));
        await Task.Delay(350);
        Assert.NotSame(before, vm.ActiveCard);
        AssertCentered(fixture);
        Assert.Equal(expandedWidth, stage.Bounds.Width);
        Assert.All(models, model => Assert.True(model.IsRunning));
        Assert.Equal(models, vm.SessionCards.Select(c => c.Model));

        window.MouseMove(new Point(900, 300));
        await Task.Delay(500); // closing animation has started
        window.MouseMove(edge);
        await Task.Delay(300);
        Assert.True(drawer.IsVisible && drawer.IsHitTestVisible);
        Assert.Equal(1, drawer.Reveal);
        window.MouseMove(new Point(900, 300));
        await Task.Delay(800);
        Assert.False(drawer.IsVisible);
        Assert.False(drawer.IsHitTestVisible);
        Assert.Equal(columns, vm.ActiveSession!.Emulator.Buffer.Columns);
        Click(fixture, "ShelfToggleButton");
        await Task.Delay(300);
        Assert.False(vm.ShelfAutoHide);
        Assert.True(drawer.IsVisible && drawer.IsHitTestVisible);
        Assert.Equal(new Thickness(14, 14, 10, 16), shelf.Padding);
        Assert.All(shelf.GetVisualDescendants().OfType<StageCard>().Where(c => !c.IsActive), c => Assert.Equal(.92, c.Opacity));
    }

    [AvaloniaFact]
    public async Task OpenSessionMenu_KeepsDrawerVisible_AndModePersists()
    {
        using var fixture = new StageLayoutTests.StageFixture();
        await Ready(fixture);
        var window = fixture.Window;
        fixture.Vm.ShelfAutoHide = true;
        await Task.Delay(300);
        window.MouseMove(new Point(4, 300));
        await Task.Delay(300);
        var drawer = window.FindControl<ShelfDrawer>("ShelfHost")!;
        var button = Card(fixture, fixture.Vm.ActiveCard!).GetVisualDescendants().OfType<Button>().Single();
        button.Flyout!.ShowAt(button);
        window.MouseMove(new Point(900, 300));
        await Task.Delay(800);
        Assert.True(button.Flyout.IsOpen);
        Assert.True(drawer.IsVisible && drawer.IsHitTestVisible);
        button.Flyout.Hide();
        await Task.Delay(800);
        Assert.False(drawer.IsVisible);
        fixture.Vm.PersistSettings();
        Assert.True(fixture.Vm.Settings.ShelfAutoHide);
    }

    [AvaloniaFact]
    public async Task ColdStart_HiddenShelfRevealsAndPinnedChoiceSurvivesRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "terminalhub-shelf-" + Guid.NewGuid());
        var store = new SettingsStore(Path.Combine(directory, "settings.json"));
        MainWindow? window = null;
        try
        {
            PtySessionFactory.UseMock = true;
            store.Save(new AppSettings
            {
                ShelfAutoHide = true,
                StartupSessions = [new StartupSession { Name = "cold-start", WorkingDirectory = Environment.CurrentDirectory }]
            });
            window = new MainWindow(store);
            window.Show();
            await Task.Delay(600);
            var vm = (MainWindowViewModel)window.DataContext!;
            var drawer = window.FindControl<ShelfDrawer>("ShelfHost")!;
            Assert.Single(vm.SessionCards);
            Assert.True(vm.ShelfAutoHide);
            Assert.False(drawer.IsVisible);
            var stageWidth = window.FindControl<StageSurface>("StageWindow")!.Bounds.Width;
            window.MouseMove(new Point(4, 300));
            await Task.Delay(350);
            window.UpdateLayout();
            Assert.True(drawer.IsVisible && drawer.IsHitTestVisible);
            var shelf = window.FindControl<ListBox>("SessionShelf")!;
            var card = Assert.Single(shelf.GetVisualDescendants().OfType<StageCard>());
            var center = card.TranslatePoint(new Point(0, card.Bounds.Height / 2), shelf)!.Value.Y;
            Assert.InRange(center, shelf.Bounds.Height / 2 - 2, shelf.Bounds.Height / 2 + 2);
            Assert.Equal(stageWidth, window.FindControl<StageSurface>("StageWindow")!.Bounds.Width);
            var pin = window.FindControl<Button>("ShelfPinButton")!;
            var point = pin.TranslatePoint(new Point(pin.Bounds.Width / 2, pin.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.False(vm.ShelfAutoHide);
            window.Close();
            Assert.False(store.Load().ShelfAutoHide);
            window = new MainWindow(store);
            window.Show();
            await Task.Delay(500);
            Assert.False(((MainWindowViewModel)window.DataContext!).ShelfAutoHide);
            Assert.True(window.FindControl<ShelfDrawer>("ShelfHost")!.IsVisible);
        }
        finally
        {
            window?.Close();
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaFact]
    public async Task Wheel_AfterActiveGroupCollapses_SkipsHeaderAndKeepsGroupCollapsed()
    {
        using var fixture = new StageLayoutTests.StageFixture();
        await Ready(fixture);
        var vm = fixture.Vm;
        vm.ShelfAutoHide = true;
        await Task.Delay(300);
        fixture.Window.MouseMove(new Point(4, 300));
        await Task.Delay(300);
        var grouped = vm.SessionCards[2];
        vm.CreateGroup("wheel-group", grouped);
        var group = Assert.Single(vm.SessionGroups);
        vm.ActivateCard(grouped);
        await Task.Delay(350);
        vm.ToggleGroup(vm.ShelfItems.OfType<SessionGroupHeader>().Single(header => header.Group == group));
        await Task.Delay(100);
        Assert.DoesNotContain(grouped, vm.ShelfItems);
        var candidates = vm.ShelfItems.OfType<SessionCardViewModel>().ToArray();
        var next = candidates.First(card => vm.SessionCards.IndexOf(card) > vm.SessionCards.IndexOf(grouped));
        var shelf = fixture.Window.FindControl<ListBox>("SessionShelf")!;
        var wheelPoint = shelf.TranslatePoint(new Point(90, shelf.Bounds.Height / 2), fixture.Window)!.Value;
        fixture.Window.MouseWheel(wheelPoint, new Vector(0, -1));
        await Task.Delay(350);
        Assert.Same(next, vm.ActiveCard);
        Assert.True(group.Collapsed);
        AssertCentered(fixture);
    }

    [AvaloniaFact]
    public async Task FixedShelf_KeepsOriginalOpacityWheelScrollingAndVisibleSelectionPosition()
    {
        using var fixture = new StageLayoutTests.StageFixture();
        await Ready(fixture);
        var window = fixture.Window;
        var vm = fixture.Vm;
        var shelf = window.FindControl<ListBox>("SessionShelf")!;
        var scroll = shelf.GetVisualDescendants().OfType<ScrollViewer>().Single();
        vm.ActivateCard(vm.SessionCards[0]);
        await Task.Delay(350);
        window.UpdateLayout();
        var first = Card(fixture, vm.SessionCards[0]);
        Assert.False(vm.ShelfAutoHide);
        Assert.Equal(new Thickness(14, 14, 10, 16), shelf.Padding);
        Assert.True(shelf.AutoScrollToSelectedItem);
        Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, ScrollViewer.GetVerticalScrollBarVisibility(shelf));
        Assert.Equal(14, first.TranslatePoint(default, shelf)!.Value.Y, 1);
        Assert.All(shelf.GetVisualDescendants().OfType<StageCard>().Where(c => !c.IsActive), c => Assert.Equal(.92, c.Opacity));

        var offset = scroll.Offset.Y;
        vm.ActivateCard(vm.SessionCards[1]); // Already fully visible: don't move it to the center.
        await Task.Delay(350);
        Assert.Equal(offset, scroll.Offset.Y);
        var active = vm.ActiveCard;
        var point = shelf.TranslatePoint(new Point(90, 100), window)!.Value;
        window.MouseWheel(point, new Vector(0, -1));
        await Task.Delay(100);
        Assert.Same(active, vm.ActiveCard);
        Assert.True(scroll.Offset.Y > offset);
    }

    [AvaloniaTheory]
    [InlineData("DarkGlass")]
    [InlineData("Black")]
    [InlineData("White")]
    [InlineData("Paper")]
    public async Task FloatingWheel_UsesThemeLayers(string theme)
    {
        var original = ThemeManager.Current;
        try
        {
            using var fixture = new StageLayoutTests.StageFixture(1100, 700);
            await Ready(fixture);
            ThemeManager.Apply(theme);
            fixture.Vm.ShelfAutoHide = true;
            fixture.Vm.ActivateCard(fixture.Vm.SessionCards[2]);
            foreach (var card in fixture.Vm.SessionCards)
                card.Model.Emulator.Parser.Feed("\u001b[36mPS D:\\Projects>\u001b[0m dotnet build\r\n\u001b[32mBuild succeeded.\u001b[0m\r\n  0 Error(s)\r\n");
            await Task.Delay(300);
            fixture.Window.MouseMove(new Point(4, 300));
            await Task.Delay(400);
            fixture.Window.UpdateLayout();
            var drawer = fixture.Window.FindControl<ShelfDrawer>("ShelfHost")!;
            Assert.Equal(ThemeManager.Brush("Floating"), drawer.Background);
            AssertCentered(fixture);
            var output = Environment.GetEnvironmentVariable("TERMINALHUB_SHELF_CAPTURES");
            if (output is not null)
            {
                Directory.CreateDirectory(output);
                fixture.Window.CaptureRenderedFrame()!.Save(Path.Combine(output, $"shelf-{theme}.png"));
            }
        }
        finally { ThemeManager.Apply(original); }
    }
}
