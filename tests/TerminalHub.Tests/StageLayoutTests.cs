using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using TerminalHub.App.Controls;
using TerminalHub.App.ViewModels;
using TerminalHub.App.Views;
using TerminalHub.Core.Pty;
using TerminalHub.Core.Settings;
using TerminalHub.Pty;
using Xunit;

namespace TerminalHub.Tests;

public class StageLayoutTests
{
    private static async Task Until(Func<bool> condition, string? message = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(25);
        Assert.True(condition(), message ?? "Timed out waiting for the UI to settle.");
    }

    internal sealed class StageFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "terminalhub-stage-" + Guid.NewGuid());
        public MainWindow Window { get; }
        public MainWindowViewModel Vm => (MainWindowViewModel)Window.DataContext!;

        public StageFixture(int width = 1440, int height = 900, string? firstSessionName = null)
        {
            PtySessionFactory.UseMock = true;
            var store = new SettingsStore(Path.Combine(_directory, "settings.json"));
            store.Save(new AppSettings
            {
                InspectorVisible = true, OutputVisible = true, DockVisibilityMode = 1,
                StartupSessions = Enumerable.Range(1, 5).Select(i => new StartupSession
                {
                    Name = firstSessionName is not null && i == 1 ? firstSessionName : $"Terminal {i:00}",
                    Tag = i == 2 ? "测试环境" : "开发环境",
                    WorkingDirectory = Environment.CurrentDirectory
                }).ToList()
            });
            Window = new MainWindow(store) { Width = width, Height = height };
            Window.Show();
        }

        /// <summary>Waits until the five startup sessions have spawned — replaces
        /// fixed boot sleeps that flake when session startup outruns them.</summary>
        public async Task ReadyAsync()
        {
            var deadline = Environment.TickCount64 + 5000;
            while (Vm.SessionCards.Count < 5 && Environment.TickCount64 < deadline)
                await Task.Delay(20);
            Assert.True(Vm.SessionCards.Count >= 5, "startup sessions never spawned");
        }

        public void Dispose()
        {
            Window.Close();
            Directory.Delete(_directory, true);
        }
    }

    [AvaloniaFact]
    public async Task ActiveThumb_UsesPlatformShadow_InactiveDoesNot()
    {
        using var fixture = new StageFixture();
        await Task.Delay(600);
        var cards = fixture.Window.GetVisualDescendants().OfType<StageCard>()
            .Where(c => c.IsEffectivelyVisible).ToList();
        Assert.NotEmpty(cards);
        var active = Assert.Single(cards, c => c.IsActive);
        var accent = (SolidColorBrush)ThemeManager.Brush("Accent");

        var activeBorder = active.GetVisualChildren().OfType<Border>().First();
        Assert.Equal(OperatingSystem.IsWindows() ? 1 : 2, activeBorder.BorderThickness.Left);
        Assert.Equal(accent.Color, ((ISolidColorBrush)activeBorder.BorderBrush!).Color);
        // Neon halo: a visible glow shadow tinted with the theme accent.
        bool Glows(BoxShadows shadows)
        {
            for (var i = 0; i < shadows.Count; i++)
            {
                var s = shadows[i];
                if (s.Blur >= 12 && s.Color.A > 0x30
                    && Math.Abs(s.Color.R - accent.Color.R) < 0x40
                    && Math.Abs(s.Color.G - accent.Color.G) < 0x40
                    && Math.Abs(s.Color.B - accent.Color.B) < 0x40)
                    return true;
            }
            return false;
        }
        if (OperatingSystem.IsWindows())
            Assert.Equal((BoxShadows)Application.Current!.Resources["ActiveCardShadow"]!, activeBorder.BoxShadow);
        else Assert.True(Glows(activeBorder.BoxShadow));

        var inactive = cards.First(c => !c.IsActive);
        var inactiveBorder = inactive.GetVisualChildren().OfType<Border>().First();
        Assert.False(Glows(inactiveBorder.BoxShadow)); // idle card keeps its plain shadow
        Assert.NotEqual(accent.Color, ((ISolidColorBrush?)inactiveBorder.BorderBrush)?.Color);
    }

    [AvaloniaFact]
    public async Task LongSessionName_ChromeButtonsStayReachable()
    {
        var longName = "VeryLongSessionName-" + new string('长', 40) + "-tail";
        using var fixture = new StageFixture(width: 1100, height: 700, firstSessionName: longName);
        await Task.Delay(700);
        var window = fixture.Window;
        fixture.Vm.ActiveCard = fixture.Vm.SessionCards[0];
        await Task.Delay(300);

        var stage = window.FindControl<StageSurface>("StageWindow")!;
        var menu = window.FindControl<Button>("SessionMenuButton")!;
        var close = window.GetVisualDescendants().OfType<Button>()
            .First(b => ToolTip.GetTip(b) as string == "关闭当前终端 · Ctrl+Shift+W");
        Assert.True(menu.IsEffectivelyVisible);
        Assert.True(close.IsEffectivelyVisible);

        // The star-sized name column must shrink so the trailing Auto columns
        // (menu, close) stay inside the stage card's right edge.
        var stageRight = stage.TranslatePoint(new Point(stage.Bounds.Width, 0), window)!.Value.X;
        Assert.True(close.TranslatePoint(new Point(close.Bounds.Width, 0), window)!.Value.X <= stageRight + 1);
        Assert.True(menu.TranslatePoint(new Point(menu.Bounds.Width, 0), window)!.Value.X <= stageRight + 1);
    }

    [AvaloniaFact]
    public async Task ShelfStack_SegmentTopsNoOverlap_OthersTuck()
    {
        using var fixture = new StageFixture();
        await Task.Delay(600);
        var vm = fixture.Vm;
        var cards = vm.SessionCards;
        Assert.NotEmpty(cards);
        Assert.Equal(0, cards[0].ShelfTopMargin.Top); // topmost card must not bleed into the title row
        Assert.All(cards.Skip(1), c => Assert.Equal(-SessionCardViewModel.ShelfOverlap, c.ShelfTopMargin.Top));

        // A card right below a pin/group header tops its own segment — tucking
        // it would paint over the header's bottom half.
        var pinned = cards[0];
        var grouped = cards[2];
        vm.SetPinned(pinned, true);
        vm.CreateGroup("组", grouped);
        Assert.Equal(0, pinned.ShelfTopMargin.Top); // below the pin header
        Assert.Equal(0, grouped.ShelfTopMargin.Top); // below the group header
        Assert.All(cards.Where(c => c != pinned && c != grouped),
            c => Assert.Equal(-SessionCardViewModel.ShelfOverlap, c.ShelfTopMargin.Top));
    }

    [AvaloniaFact]
    public async Task NewSessionDock_IsLinuxPrimaryAccentPill_AndWindowsKeepsOriginalEntries()
    {
        using var fixture = new StageFixture();
        await Task.Delay(400);
        var vm = fixture.Vm;
        var window = fixture.Window;
        var btn = window.FindControl<Button>("NewDockButton")!;
        if (OperatingSystem.IsWindows())
        {
            Assert.False(btn.IsVisible);
            Assert.False(window.FindControl<Button>("SettingsDockButton")!.IsVisible);
            var dock = window.FindControl<DropletDock>("ActionDock")!;
            Assert.Equal(new[] { "1", "2", "3", "4" }, dock.GetVisualDescendants()
                .OfType<Button>().Where(b => b.Classes.Contains("dock") && b.IsVisible)
                .Select(b => b.CommandParameter?.ToString()));
            Assert.NotNull(window.FindControl<Button>("ChromeNew")!.Command);
            return;
        }
        Assert.True(btn.IsVisible);
        Assert.Contains("dock-primary", btn.Classes);
        var accent = Assert.IsAssignableFrom<ISolidColorBrush>(window.FindResource("UiAccent")).Color;
        var onAccent = Assert.IsAssignableFrom<ISolidColorBrush>(window.FindResource("UiOnAccent")).Color;
        Assert.Equal(accent, Assert.IsAssignableFrom<ISolidColorBrush>(btn.Background).Color);
        var icon = btn.GetVisualDescendants().OfType<PathIcon>().First();
        Assert.Equal(onAccent, Assert.IsAssignableFrom<ISolidColorBrush>(icon.Foreground).Color);

        // dock-active persists after the click — the icon must stay OnAccent
        // instead of sinking into the accent fill.
        vm.DockSelectCommand.Execute("0");
        await Task.Delay(300);
        Assert.Equal(accent, Assert.IsAssignableFrom<ISolidColorBrush>(btn.Background).Color);
        Assert.Equal(onAccent, Assert.IsAssignableFrom<ISolidColorBrush>(icon.Foreground).Color);
    }

    [AvaloniaFact]
    public async Task CardMenu_PerCardActions_WithoutActivating()
    {
        using var fixture = new StageFixture();
        await Task.Delay(600);
        var vm = fixture.Vm;
        var window = fixture.Window;
        var cards = window.GetVisualDescendants().OfType<StageCard>()
            .Where(c => c.IsEffectivelyVisible).ToList();
        Assert.True(cards.Count >= 2);
        var target = cards.First(c => !ReferenceEquals(c.DataContext, vm.ActiveCard));
        var targetVm = (SessionCardViewModel)target.DataContext!;
        var button = target.GetVisualDescendants().OfType<Button>()
            .First(b => ToolTip.GetTip(b) as string == "会话操作");

        var before = vm.ActiveCard;
        button.Flyout!.ShowAt(button);
        await Task.Delay(200);
        var flyout = Assert.IsType<MenuFlyout>(button.Flyout);
        Assert.Same(before, vm.ActiveCard); // opening the menu must not activate the card
        var items = flyout.Items.OfType<MenuItem>().ToList();
        Assert.Contains(items, i => i.Header as string == "设为当前终端");
        Assert.Contains(items, i => i.Header as string == "置顶");
        Assert.Contains(items, i => i.Header as string == "移入分组");
        Assert.Contains(items, i => i.Header as string == "关闭会话");
        flyout.Hide();

        items.First(i => i.Header as string == "置顶")
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await Task.Delay(300);
        Assert.True(targetVm.Model.Pinned); // menu action applied to the pointed card
        Assert.Same(before, vm.ActiveCard); // and still did not switch the stage
    }

    [AvaloniaFact]
    public async Task TagPill_YieldsToUnreadOrExitStatus()
    {
        using var fixture = new StageFixture();
        await Task.Delay(600);
        var card = fixture.Vm.SessionCards[0];
        Assert.True(card.HasTag);
        Assert.Equal(!OperatingSystem.IsWindows(), card.ShowTagPill); // running + no unread: pill replaces plain 运行中
        card.HasUnreadOutput = true;
        Assert.False(card.ShowTagPill); // 有新输出 must not hide behind the pill
    }

    [AvaloniaTheory]
    [InlineData("DarkGlass", "#65ACED")]
    [InlineData("Black", "#A9C8F5")]
    [InlineData("White", "#245AB5")]
    [InlineData("Paper", "#8C5132")]
    public void ActiveCardShadow_MatchesPlatformAppearance(string theme, string accentHex)
    {
        var previous = ThemeManager.Current;
        try
        {
            ThemeManager.Apply(theme);
            var shadows = (BoxShadows)Application.Current!.Resources["ActiveCardShadow"]!;
            var accent = Color.Parse(accentHex);
            var glows = false;
            for (var i = 0; i < shadows.Count; i++)
            {
                var s = shadows[i];
                if (s.Blur >= 12 && s.Color.A > 0x30
                    && Math.Abs(s.Color.R - accent.R) < 0x40
                    && Math.Abs(s.Color.G - accent.G) < 0x40
                    && Math.Abs(s.Color.B - accent.B) < 0x40)
                    glows = true;
            }
            if (OperatingSystem.IsWindows())
            {
                var expected = BoxShadows.Parse(theme switch
                {
                    "Paper" => "0 2 3 0 #28816D50", "White" => "0 3 8 0 #20314766",
                    "Black" => "0 1 4 0 #60000000", _ => "0 3 12 0 #40258ED6"
                });
                Assert.Equal(expected, shadows);
            }
            else Assert.True(glows, $"{theme}: ActiveCardShadow lacks an accent-tinted glow");
        }
        finally { ThemeManager.Apply(previous); }
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
        fixture.Vm.InspectorVisible = false; // default-on; this test asserts the toggle reveals it
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
        // Under load the posted ActivateFrom can take longer than a fixed delay
        // to kick the spring — wait for the flight to actually start.
        await Until(() => !(surface.RenderTransform?.Value.IsIdentity ?? true),
            "Activation spring never started.");
        fixture.Vm.ActiveCard = fixture.Vm.SessionCards[1];
        fixture.Window.Close();
        await Until(() => surface.Reveal == 1d, "Spring did not settle after close.");
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
        vm.InspectorVisible = false; // default-on now; flip to write an explicit value
        vm.InspectorVisible = true;
        vm.OutputVisible = true;
        vm.DockVisibilityMode = 2;
        Assert.True(vm.Settings.InspectorVisible);
        Assert.True(vm.Settings.OutputVisible);
        Assert.Equal(2, vm.Settings.DockVisibilityMode);
    }

    [AvaloniaFact]
    public async Task RestartSession_RespawnsExited_InSameShelfSlot()
    {
        using var fixture = new StageFixture();
        var ready = DateTime.UtcNow.AddSeconds(5);
        while (fixture.Vm.SessionCards.Count < 5 && DateTime.UtcNow < ready)
            await Task.Delay(25);
        var card = fixture.Vm.SessionCards[1];
        var originals = fixture.Vm.SessionCards.ToHashSet();
        ((MockPtySession)card.Model.Pty).Kill();
        Assert.NotNull(card.Model.Pty.ExitCode);
        await fixture.Vm.RestartSession(card);
        // Card append + slot restore both arrive via dispatcher posts — wait
        // for a genuinely new card (not an old card shifted into slot 1).
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!(fixture.Vm.SessionCards.Count == 5
                 && !originals.Contains(fixture.Vm.SessionCards[1]))
               && DateTime.UtcNow < deadline)
            await Task.Delay(25);
        var spawned = fixture.Vm.SessionCards[1];
        Assert.True(spawned.Model.Pty.IsRunning);
        Assert.NotSame(card, spawned);
        Assert.Same(spawned, fixture.Vm.ActiveCard);
    }

    [AvaloniaFact]
    public async Task RestartSession_KeepsPinnedCardInPinBlock()
    {
        using var fixture = new StageFixture();
        var ready = DateTime.UtcNow.AddSeconds(5);
        while (fixture.Vm.SessionCards.Count < 5 && DateTime.UtcNow < ready)
            await Task.Delay(25);
        var card = fixture.Vm.SessionCards[3];
        fixture.Vm.SetPinned(card, true);   // moves to the pin block at index 0
        Assert.Equal(0, fixture.Vm.SessionCards.IndexOf(card));
        ((MockPtySession)card.Model.Pty).Kill();
        var originals = fixture.Vm.SessionCards.ToHashSet();
        await fixture.Vm.RestartSession(card);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!(fixture.Vm.SessionCards.Count > 0
                 && !originals.Contains(fixture.Vm.SessionCards[0]))
               && DateTime.UtcNow < deadline)
            await Task.Delay(25);
        var spawned = fixture.Vm.SessionCards.Single(c => !originals.Contains(c));
        Assert.True(spawned.Model.Pinned);
        Assert.Equal(0, fixture.Vm.SessionCards.IndexOf(spawned));
        Assert.Same(spawned, fixture.Vm.ActiveCard);
    }

    [AvaloniaFact]
    public async Task RestartSession_KeepsGroupedCardInsideItsGroup()
    {
        using var fixture = new StageFixture();
        var ready = DateTime.UtcNow.AddSeconds(5);
        while (fixture.Vm.SessionCards.Count < 5 && DateTime.UtcNow < ready)
            await Task.Delay(25);
        var card = fixture.Vm.SessionCards[1];
        fixture.Vm.CreateGroup("组A", card);
        fixture.Vm.MoveCardToGroup(fixture.Vm.SessionCards[3],
            card.Model.GroupId);   // [G:A1] [card] [G:A2] — restart the middle one
        var groupId = card.Model.GroupId;
        var slot = fixture.Vm.SessionCards.IndexOf(card);
        ((MockPtySession)card.Model.Pty).Kill();
        var originals = fixture.Vm.SessionCards.ToHashSet();
        await fixture.Vm.RestartSession(card);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        // Append lands before the slot-restore post — wait for the new card
        // to actually arrive at its old group position, not merely exist.
        while (!(fixture.Vm.SessionCards.Count == 5
                 && !originals.Contains(fixture.Vm.SessionCards[slot])
                 && fixture.Vm.SessionCards[slot].Model.GroupId == groupId)
               && DateTime.UtcNow < deadline)
            await Task.Delay(25);
        var spawned = fixture.Vm.SessionCards.Single(c => !originals.Contains(c));
        Assert.Equal(groupId, spawned.Model.GroupId);
        // Stays inside the group span — at its old slot between the siblings.
        Assert.Equal(slot, fixture.Vm.SessionCards.IndexOf(spawned));
    }

    [AvaloniaFact]
    public async Task Bell_RespectsPlatformNotificationBehavior()
    {
        using var fixture = new StageFixture();
        var ready = DateTime.UtcNow.AddSeconds(5);
        while (fixture.Vm.SessionCards.Count < 5 && DateTime.UtcNow < ready)
            await Task.Delay(25);
        var bg = fixture.Vm.SessionCards.First(c => !c.IsActive);
        bg.Model.Emulator.Parser.Feed("\a"u8);
        if (OperatingSystem.IsWindows())
        {
            await Task.Delay(120);
            Assert.False(fixture.Vm.NotificationVisible);
            Assert.False(bg.HasUnreadOutput);
            var view = fixture.Window.FindControl<TerminalView>("MainTerminal")!;
            fixture.Vm.ActiveSession!.Emulator.Parser.Feed("\a"u8);
            Assert.Equal(0L, typeof(TerminalView).GetField("_bellFlashUntil",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view));
            return;
        }
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!fixture.Vm.NotificationVisible && DateTime.UtcNow < deadline)
            await Task.Delay(25);
        Assert.True(fixture.Vm.NotificationVisible);
        Assert.Contains(bg.Model.Name, fixture.Vm.NotificationText);
        Assert.True(bg.HasUnreadOutput);
        // A bell on the active session stays silent — it's already on screen.
        fixture.Vm.NotificationVisible = false;
        fixture.Vm.ActiveSession!.Emulator.Parser.Feed("\a"u8);
        await Task.Delay(120);
        Assert.False(fixture.Vm.NotificationVisible);
    }

    [AvaloniaFact]
    public async Task WideSavedRails_WindowShrink_PreservesStageChrome_AndSavedWidths()
    {
        using var fixture = new StageFixture();
        await Task.Delay(600);
        fixture.Vm.ShelfWidth = 648;
        fixture.Vm.InspectorWidth = 648;
        fixture.Window.Width = 1100;
        await Task.Delay(400);
        var stage = fixture.Window.FindControl<StageSurface>("StageWindow")!;
        Assert.True(stage.Bounds.Width >= 459, $"Stage width: {stage.Bounds.Width}");
        var menu = fixture.Window.FindControl<Button>("SessionMenuButton")!;
        var stageRight = stage.TranslatePoint(new Point(stage.Bounds.Width, 0), fixture.Window)!.Value.X;
        Assert.True(menu.TranslatePoint(new Point(menu.Bounds.Width, 0), fixture.Window)!.Value.X <= stageRight + 1);
        Assert.Equal(648, fixture.Vm.ShelfWidth);
        Assert.Equal(648, fixture.Vm.InspectorWidth);
    }

    [AvaloniaFact]
    public async Task EmptySshForm_EscapeReturnsTerminalFocus_WithoutHidingAddForm()
    {
        using var fixture = new StageFixture();
        fixture.Vm.SelectedRightTab = 3;
        await Task.Delay(600);
        var host = fixture.Window.GetVisualDescendants().OfType<TextBox>()
            .Single(t => t.Watermark as string == "主机 host / IP");
        host.Focus();
        fixture.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.True(host.IsEffectivelyVisible);
        Assert.True(fixture.Vm.Ssh.Editing);
        Assert.True(OperatingSystem.IsWindows() ? host.IsFocused
            : fixture.Window.FindControl<TerminalView>("MainTerminal")!.IsFocused);
    }

    [AvaloniaTheory]
    [InlineData("ShelfResizeHandle")]
    [InlineData("InspectorResizeHandle")]
    public async Task RailDrag_KeepsRoomForStage_AlongsideOtherRail(string name)
    {
        using var fixture = new StageFixture(width: 1100);
        await Task.Delay(600);
        var handle = fixture.Window.FindControl<Control>(name)!;
        var start = handle.TranslatePoint(new Point(handle.Bounds.Width / 2, 100), fixture.Window)!.Value;
        var end = start + new Vector(name == "ShelfResizeHandle" ? 600 : -600, 0);
        fixture.Window.MouseDown(start, MouseButton.Left);
        fixture.Window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        fixture.Window.MouseUp(end, MouseButton.Left);
        await Task.Delay(100);
        var stage = fixture.Window.FindControl<StageSurface>("StageWindow")!;
        Assert.True(stage.Bounds.Width >= 459, $"Stage width: {stage.Bounds.Width}");
    }

    [AvaloniaFact]
    public async Task OutputResize_AfterHeightClamp_StartsFromDisplayedHeight()
    {
        using var fixture = new StageFixture(height: 900);
        await Task.Delay(600);
        fixture.Vm.OutputHeight = 800;
        fixture.Window.Height = 700;
        await Task.Delay(200);
        var panel = fixture.Window.FindControl<Border>("OutputPanel")!;
        var height = panel.Height;
        Assert.True(height < fixture.Vm.OutputHeight);
        var handle = fixture.Window.FindControl<Control>("OutputResizeHandle")!;
        var start = handle.TranslatePoint(new Point(handle.Bounds.Width / 2, handle.Bounds.Height / 2), fixture.Window)!.Value;
        fixture.Window.MouseDown(start, MouseButton.Left);
        fixture.Window.MouseMove(start + new Vector(0, 20), RawInputModifiers.LeftMouseButton);
        fixture.Window.MouseUp(start + new Vector(0, 20), MouseButton.Left);
        Assert.Equal(height - 20, fixture.Vm.OutputHeight, 1);
    }
}
