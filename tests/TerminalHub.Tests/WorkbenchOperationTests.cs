using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TerminalHub.App.Controls;
using TerminalHub.App.ViewModels;
using TerminalHub.App.Views;
using TerminalHub.Core.Localization;
using TerminalHub.Core.Settings;
using Xunit;

namespace TerminalHub.Tests;

public class WorkbenchOperationTests
{
    [AvaloniaFact]
    public async Task TransferAndUndoRestoreBothWorkspacesWithoutReplacingProcesses()
    {
        using var f = new StageLayoutTests.StageFixture(); await f.ReadyAsync();
        var source = f.Vm.ActiveWorkspace;
        await f.Vm.SetSplitLayoutAsync("Quad");
        f.Vm.FocusPane(2); f.Vm.ColumnRatio = .37; f.Vm.RowRatio = .62;
        var original = f.Vm.SessionCards.ToArray(); var panes = Enumerable.Range(0, 4).Select(f.Vm.GetPane).ToArray();
        var moved = original[2]; var pty = moved.Model.Pty; var emulator = moved.Model.Emulator;
        emulator.Parser.Feed("\r\ntransfer-history-marker\r\n");
        await f.Vm.NewProjectWorkspaceCommand.ExecuteAsync(null); await Task.Delay(100);
        var target = f.Vm.ActiveWorkspace; var targetOriginal = f.Vm.SessionCards.ToArray();
        f.Vm.SwitchProjectWorkspace(source);
        f.Vm.MoveSessionToWorkspace(moved, target, 0);
        Assert.Same(target, f.Vm.ActiveWorkspace); Assert.Same(moved, target.Cards[0]);
        Assert.DoesNotContain(moved, source.Cards); Assert.Equal(4, source.Count);
        Assert.Equal(SplitLayout.Quad, source.Layout.SplitLayout); // remaining fifth terminal fills the vacated pane
        Assert.Same(pty, moved.Model.Pty); Assert.Same(emulator, moved.Model.Emulator); Assert.True(moved.Model.IsRunning);
        Assert.Contains("transfer-history-marker", emulator.Buffer.TailText(100));
        f.Vm.UndoLayout(); await Task.Delay(40);
        Assert.Same(source, f.Vm.ActiveWorkspace); Assert.Equal(original, source.Cards); Assert.Equal(targetOriginal, target.Cards);
        Assert.Equal(panes, Enumerable.Range(0, 4).Select(f.Vm.GetPane));
        Assert.Equal(2, f.Vm.FocusedPane); Assert.Equal(.37, f.Vm.ColumnRatio); Assert.Equal(.62, f.Vm.RowRatio);
        f.Vm.RedoLayout(); await Task.Delay(40);
        Assert.Same(target, f.Vm.ActiveWorkspace); Assert.Same(moved, target.Cards[0]); Assert.True(moved.Model.IsRunning);
        Assert.Same(pty, moved.Model.Pty);
        var savedSource = f.Vm.Settings.ProjectWorkspaces.Single(w => w.Id == source.Id);
        var savedTarget = f.Vm.Settings.ProjectWorkspaces.Single(w => w.Id == target.Id);
        Assert.DoesNotContain(savedSource.Layout.Sessions, s => s.Name == moved.Name);
        Assert.Equal(moved.Name, savedTarget.Layout.Sessions[0].Name);
    }

    [AvaloniaFact]
    public async Task TransferFromTwoPaneWorkspaceFallsBackToSingle_AndUndoRestoresSplit()
    {
        using var f = new StageLayoutTests.StageFixture(); await f.ReadyAsync();
        foreach (var card in f.Vm.SessionCards.Skip(2).ToArray()) f.Vm.CloseSessionCommand.Execute(card);
        await Task.Delay(100); await f.Vm.SetSplitLayoutAsync("Vertical");
        var source = f.Vm.ActiveWorkspace; var moved = f.Vm.SessionCards[0];
        await f.Vm.NewProjectWorkspaceCommand.ExecuteAsync(null); await Task.Delay(100);
        var target = f.Vm.ActiveWorkspace; f.Vm.SwitchProjectWorkspace(source);
        f.Vm.MoveSessionToWorkspace(moved, target);
        Assert.False(source.Layout.IsSplit); Assert.Single(source.Cards);
        f.Vm.UndoLayout(); Assert.True(f.Vm.IsSplit); Assert.Equal(SplitLayout.Vertical, f.Vm.SplitLayout);
        Assert.Same(moved.Model, f.Vm.LeftPane);
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task NativeDragCanHoverThenCancel_OrDropBeforeAnotherCard(bool autoHide)
    {
        using var f = new StageLayoutTests.StageFixture(); await f.ReadyAsync();
        var source = f.Vm.ActiveWorkspace; var original = source.Cards.ToArray();
        f.Vm.ShelfAutoHide = autoHide;
        await f.Vm.NewProjectWorkspaceCommand.ExecuteAsync(null); await Task.Delay(150);
        f.Vm.ShelfAutoHide = autoHide;
        var target = f.Vm.ActiveWorkspace; var targetFirst = target.Cards[0];
        f.Vm.SwitchProjectWorkspace(source); await Task.Delay(400);
        if (autoHide) { f.Window.MouseMove(new Point(2, 300)); await Task.Delay(350); }
        f.Window.FindControl<ListBox>("SessionShelf")!.ScrollIntoView(original[1]); await Task.Delay(100);
        var start = CardPoint(f.Window, original[1]); var tab = TabPoint(f.Window, target);
        f.Window.MouseDown(start, MouseButton.Left); f.Window.MouseMove(tab, RawInputModifiers.LeftMouseButton);
        Assert.Contains(f.Window.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("session-drop"));
        await Task.Delay(650);
        Assert.Same(target, f.Vm.ActiveWorkspace); Assert.Equal(original, source.Cards); // hover does not migrate
        f.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        f.Window.MouseUp(tab, MouseButton.Left); await Task.Delay(100);
        Assert.Same(source, f.Vm.ActiveWorkspace); Assert.Equal(original, source.Cards); Assert.False(f.Vm.CanUndoLayout);
        if (autoHide) { f.Window.MouseMove(new Point(2, 300)); await Task.Delay(350); }
        f.Window.FindControl<ListBox>("SessionShelf")!.ScrollIntoView(original[1]); await Task.Delay(100);
        start = CardPoint(f.Window, original[1]); tab = TabPoint(f.Window, target);
        f.Window.MouseDown(start, MouseButton.Left); f.Window.MouseMove(tab, RawInputModifiers.LeftMouseButton);
        await Task.Delay(650);
        var end = CardPoint(f.Window, targetFirst);
        f.Window.MouseMove(end, RawInputModifiers.LeftMouseButton); f.Window.MouseUp(end, MouseButton.Left); await Task.Delay(100);
        Assert.Same(target, f.Vm.ActiveWorkspace); Assert.Equal(new[] { original[1], targetFirst }, target.Cards);
        f.Vm.UndoLayout(); Assert.Equal(original, source.Cards); Assert.Equal(new[] { targetFirst }, target.Cards);
        Assert.False(f.Vm.CanUndoLayout); // the entire drag is one operation
    }

    [AvaloniaFact]
    public async Task LayoutGestureIsOneStep_AndNewOperationDropsRedo()
    {
        using var f = new StageLayoutTests.StageFixture(); await f.ReadyAsync();
        await f.Vm.SetSplitLayoutAsync("Horizontal");
        f.Vm.BeginLayoutGesture("调整分屏比例"); f.Vm.ColumnRatio = .4; f.Vm.ColumnRatio = .3;
        f.Vm.CompleteLayoutGesture();
        f.Vm.UndoLayout(); Assert.Equal(.5, f.Vm.ColumnRatio); Assert.True(f.Vm.IsSplit);
        f.Vm.RedoLayout(); Assert.Equal(.3, f.Vm.ColumnRatio);
        f.Vm.UndoLayout(); f.Vm.MoveSessionCard(f.Vm.SessionCards[0], f.Vm.SessionCards[1]);
        Assert.False(f.Vm.CanRedoLayout);
        f.Vm.CloseSessionCommand.Execute(f.Vm.SessionCards[0]); await Task.Delay(100);
        Assert.False(f.Vm.CanUndoLayout); Assert.False(f.Vm.CanRedoLayout); // cannot restore a closed PTY
    }

    [AvaloniaFact]
    public async Task SplitterDragAndPaneSwapCanBeUndoneWithoutLosingInput()
    {
        using var f = new StageLayoutTests.StageFixture(); await f.ReadyAsync();
        await f.Vm.SetSplitLayoutAsync("Horizontal"); await Task.Delay(120);
        var left = f.Vm.LeftPane; var right = f.Vm.RightPane;
        var divider = f.Window.GetVisualDescendants().OfType<GridSplitter>().Single(d => d.IsEffectivelyVisible);
        var start = divider.TranslatePoint(new Point(2, divider.Bounds.Height / 2), f.Window)!.Value;
        f.Window.MouseDown(start, MouseButton.Left);
        f.Window.MouseMove(start + new Vector(35, 0), RawInputModifiers.LeftMouseButton); await Task.Delay(25);
        f.Window.MouseMove(start + new Vector(75, 0), RawInputModifiers.LeftMouseButton); await Task.Delay(25);
        f.Window.MouseUp(start + new Vector(75, 0), MouseButton.Left); await Task.Delay(40);
        Assert.True(f.Vm.PaneTree!.Ratio > .5);
        f.Vm.UndoLayoutCommand.Execute(null); Assert.Equal(.5, f.Vm.PaneTree!.Ratio);
        Assert.True(f.Vm.IsSplit); // one undo must restore the whole ratio drag
        f.Vm.FocusPane(1); await Task.Delay(40);
        f.Vm.ActivateCard(f.Vm.SessionCards.Single(c => c.Model == left));
        Assert.Same(left, f.Vm.RightPane); Assert.Same(right, f.Vm.LeftPane);
        f.Window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control | RawInputModifiers.Alt);
        Assert.Same(left, f.Vm.LeftPane); Assert.Same(right, f.Vm.RightPane);
        f.Window.KeyTextInput("after-undo"); await Task.Delay(25);
        Assert.Contains("after-undo", f.Vm.ActiveSession!.Emulator.Buffer.TailText(30));
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task ShelfClickOnAlreadySelectedCard_AssignsToFocusedPane(bool activationPending)
    {
        using var f = new StageLayoutTests.StageFixture(); await f.ReadyAsync();
        await f.Vm.SetSplitLayoutAsync("Horizontal"); await Task.Delay(120);
        var left = f.Vm.LeftPane; var right = f.Vm.RightPane;
        var card = f.Vm.SessionCards.Single(c => c.Model == left);
        f.Vm.ActivateCard(card); await Task.Delay(50); // selected card, left pane
        var point = CardPoint(f.Window, card);
        EventHandler<PointerReleasedEventArgs> focusBeforeClick = (_, _) =>
        {
            f.Vm.FocusPane(1); // queue SyncActive before the shelf's bubbling click handler
            Assert.Same(card, f.Vm.ActiveCard);
        };
        if (activationPending)
            // Headless mouse helpers pump the dispatcher before each event, so
            // arrange the pending activation within the release event itself.
            f.Window.AddHandler(InputElement.PointerReleasedEvent, focusBeforeClick, RoutingStrategies.Tunnel);
        else
            f.Vm.FocusedPane = 1;
        Assert.Same(card, f.Vm.ActiveCard);
        f.Window.MouseDown(point, MouseButton.Left);
        f.Window.MouseUp(point, MouseButton.Left);
        f.Window.RemoveHandler(InputElement.PointerReleasedEvent, focusBeforeClick);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        Assert.Same(left, f.Vm.RightPane); Assert.Same(right, f.Vm.LeftPane);
        Assert.Same(left, f.Vm.ActiveSession);

        // A repeated click must not add an undo step or discard the pane swap.
        point = CardPoint(f.Window, card);
        f.Window.MouseDown(point, MouseButton.Left);
        f.Window.MouseUp(point, MouseButton.Left);
        f.Vm.UndoLayout();
        Assert.Same(left, f.Vm.LeftPane); Assert.Same(right, f.Vm.RightPane);
        Assert.True(f.Vm.IsSplit);
        f.Vm.RedoLayout();
        Assert.Same(left, f.Vm.RightPane); Assert.Same(right, f.Vm.LeftPane);
        Assert.True(left!.IsRunning); Assert.True(right!.IsRunning);
    }

    [AvaloniaFact]
    public async Task ConfiguredRecentShortcutReversesAndWorksFromPopout()
    {
        using var f = new StageLayoutTests.StageFixture(); await f.ReadyAsync();
        var recent = f.Vm.SessionShortcuts.Single(s => s.Binding.Action == SessionShortcutAction.Recent);
        recent.Gesture = "Ctrl+Alt+F8";
        var conflicting = f.Vm.SessionShortcuts.Single(s => s.Binding.Action == SessionShortcutAction.UndoLayout);
        conflicting.Gesture = "Ctrl+Alt+Shift+F8";
        Assert.NotEmpty(conflicting.Error); Assert.NotEmpty(recent.Error);
        conflicting.Gesture = "Ctrl+Alt+Z";
        Assert.Empty(recent.Error);
        var a = f.Vm.SessionCards[0]; var b = f.Vm.SessionCards[1]; var c = f.Vm.SessionCards[2];
        f.Vm.ActivateSearchSession(a.Model); f.Vm.ActivateSearchSession(b.Model); f.Vm.ActivateSearchSession(c.Model);
        await Task.Delay(80);
        f.Window.KeyPressQwerty(PhysicalKey.F8, RawInputModifiers.Control | RawInputModifiers.Alt);
        Assert.Same(b.Model, f.Vm.SelectedRecent!.Session);
        f.Window.KeyPressQwerty(PhysicalKey.F8, RawInputModifiers.Control | RawInputModifiers.Alt);
        Assert.Same(a.Model, f.Vm.SelectedRecent!.Session);
        f.Window.KeyPressQwerty(PhysicalKey.F8, RawInputModifiers.Control | RawInputModifiers.Alt | RawInputModifiers.Shift);
        Assert.Same(b.Model, f.Vm.SelectedRecent!.Session);
        f.Window.KeyReleaseQwerty(PhysicalKey.AltLeft, RawInputModifiers.Control); await Task.Delay(50);
        Assert.Same(b.Model, f.Vm.ActiveSession);
        f.Vm.OpenInNewWindowCommand.Execute(b); await Task.Delay(100);
        var popout = f.Vm.Popouts[0];
        popout.KeyPressQwerty(PhysicalKey.F8, RawInputModifiers.Control | RawInputModifiers.Alt);
        Assert.True(f.Vm.RecentSwitcherOpen);
        f.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.Control | RawInputModifiers.Alt);
        Assert.False(f.Vm.RecentSwitcherOpen); Assert.True(b.Model.Detached);
        popout.KeyTextInput("popout-returned-focus"); await Task.Delay(25);
        Assert.Contains("popout-returned-focus", b.Model.Emulator.Buffer.TailText(30));
        popout.KeyPressQwerty(PhysicalKey.F8, RawInputModifiers.Control | RawInputModifiers.Alt);
        f.Vm.SelectedRecent = f.Vm.RecentCandidates.Single(item => item.Session == a.Model);
        f.Window.KeyReleaseQwerty(PhysicalKey.ControlLeft, RawInputModifiers.None); await Task.Delay(60);
        Assert.Same(a.Model, f.Vm.ActiveSession); Assert.True(b.Model.IsRunning);
    }

    [AvaloniaTheory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task EnglishNavigationFitsNarrowWindowsAcrossThemes(int theme)
    {
        using var f = new StageLayoutTests.StageFixture(width: 1100, height: 680); await f.ReadyAsync();
        f.Vm.ThemeIndex = theme; f.Vm.LanguageIndex = 2; f.Vm.SettingsOpen = true; await Task.Delay(450);
        var tabs = f.Window.FindControl<TabControl>("SettingsTabs")!;
        var panel = f.Window.FindControl<Border>("SettingsPanel")!;
        foreach (var item in tabs.Items.OfType<TabItem>())
        {
            var origin = item.TranslatePoint(default, panel)!.Value;
            Assert.True(origin.X >= 0 && origin.X + item.Bounds.Width <= panel.Bounds.Width, $"Hidden settings tab: {item.Header}");
        }
        var tools = new ProjectToolsWindow(f.Window, f.Vm) { Width = 900, Height = 640 };
        tools.Show(f.Window); await Task.Delay(100);
        var capture = Environment.GetEnvironmentVariable("TERMINALHUB_WORKBENCH_CAPTURES");
        try
        {
            var navigation = ((ProjectToolsView)tools.Content!).FindControl<ListBox>("ToolNavigation")!;
            foreach (var text in navigation.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible))
            {
                var item = text.FindAncestorOfType<ListBoxItem>()!;
                Assert.True(text.TranslatePoint(default, item)!.Value.X + text.Bounds.Width <= item.Bounds.Width - item.Padding.Right + 1,
                    $"Tool navigation overflows: {text.Text}");
            }
            if (capture is not null)
            {
                Directory.CreateDirectory(capture);
                f.Window.CaptureRenderedFrame()!.Save(Path.Combine(capture, $"settings-en-{theme}.png"));
                for (var i = 0; i < 5; i++)
                {
                    ((ProjectToolsView)tools.Content!).FindControl<ListBox>("ToolNavigation")!.SelectedIndex = i; await Task.Delay(50);
                    tools.CaptureRenderedFrame()!.Save(Path.Combine(capture, $"tools-en-{theme}-{i}.png"));
                }
                f.Vm.SettingsOpen = false; await Task.Delay(450);
                f.Window.KeyPressQwerty(PhysicalKey.F6, RawInputModifiers.Control); await Task.Delay(80);
                f.Window.CaptureRenderedFrame()!.Save(Path.Combine(capture, $"recent-en-{theme}.png"));
                f.Vm.FinishRecentSwitcher(false);
            }
        }
        finally { tools.Close(); f.Vm.LanguageIndex = 0; }
    }

    [AvaloniaFact]
    public async Task RecentPreviewDoesNotSwitchOrType_ReleaseConfirmsAndEscapeCancels()
    {
        using var f = new StageLayoutTests.StageFixture(); await f.ReadyAsync();
        var source = f.Vm.ActiveWorkspace; var first = f.Vm.SessionCards[0];
        f.Vm.ActivateSearchSession(first.Model); await Task.Delay(100);
        await f.Vm.NewProjectWorkspaceCommand.ExecuteAsync(null); await Task.Delay(100);
        var target = f.Vm.ActiveWorkspace; var second = f.Vm.ActiveSession!;
        var before = second.Emulator.Buffer.TailText(100);
        f.Window.KeyPressQwerty(PhysicalKey.F6, RawInputModifiers.Control);
        Assert.True(f.Vm.RecentSwitcherOpen); Assert.Same(first.Model, f.Vm.SelectedRecent!.Session);
        Assert.Same(target, f.Vm.ActiveWorkspace); Assert.Same(second, f.Vm.ActiveSession);
        f.Window.KeyTextInput("must-not-reach-shell");
        f.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.Control);
        Assert.False(f.Vm.RecentSwitcherOpen); Assert.Same(second, f.Vm.ActiveSession);
        Assert.Equal(before, second.Emulator.Buffer.TailText(100));
        f.Window.KeyPressQwerty(PhysicalKey.F6, RawInputModifiers.Control);
        f.Window.KeyReleaseQwerty(PhysicalKey.ControlLeft, RawInputModifiers.None); await Task.Delay(100);
        Assert.Same(source, f.Vm.ActiveWorkspace); Assert.Same(first.Model, f.Vm.ActiveSession);
        f.Window.KeyPressQwerty(PhysicalKey.F6, RawInputModifiers.Control);
        Assert.Same(second, f.Vm.SelectedRecent!.Session);
        f.Window.KeyReleaseQwerty(PhysicalKey.ControlLeft, RawInputModifiers.None); await Task.Delay(100);
        Assert.Same(second, f.Vm.ActiveSession);
        f.Window.KeyTextInput("restored-focus"); await Task.Delay(30);
        Assert.Contains("restored-focus", second.Emulator.Buffer.TailText(100));
    }

    [AvaloniaFact]
    public async Task RecentCandidatesDropClosedSessions_AndCanFocusSeparateWindows()
    {
        using var f = new StageLayoutTests.StageFixture(); await f.ReadyAsync();
        var closed = f.Vm.SessionCards[0]; var detached = f.Vm.SessionCards[1];
        f.Vm.OpenInNewWindowCommand.Execute(detached); await Task.Delay(100);
        f.Vm.HandleRecentKeyDown(new KeyEventArgs { Key = Key.F6, KeyModifiers = KeyModifiers.Control });
        Assert.Contains(f.Vm.RecentCandidates, c => c.Session == closed.Model);
        f.Vm.CloseSessionCommand.Execute(closed);
        // SessionRemoved posts the candidate-list cleanup to the dispatcher —
        // pump it rather than sleeping a fixed delay (flaky under load).
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        Assert.DoesNotContain(f.Vm.RecentCandidates, c => c.Session == closed.Model);
        f.Vm.SelectedRecent = f.Vm.RecentCandidates.Single(c => c.Session == detached.Model);
        f.Vm.FinishRecentSwitcher(true);
        Assert.True(detached.Model.Detached); Assert.Single(f.Vm.Popouts); Assert.True(detached.Model.IsRunning);
        f.Vm.Popouts[0].Close(); await Task.Delay(50);
    }

    [AvaloniaFact]
    public void LanguageFormatsPreserveArguments_AndModulesFallBackToTheirDefault()
    {
        var locale = Localizer.Current; var original = locale.Selection;
        try
        {
            locale.SetLanguage("en");
            Assert.Equal("Imported 2 items, skipped 1", locale.Translate("已导入 2 项，跳过 1 项"));
            Assert.Equal("Unable to open folder: C:\\项目\\设置", locale.Translate("无法打开目录: C:\\项目\\设置"));
            Assert.Equal("运行中", new LogEntry(DateTime.Now, "info", "运行中").DisplayMessage.ToString());
            Assert.Equal("Running", new LogEntry(DateTime.Now, "info", "运行中", IsAppMessage: true).DisplayMessage.ToString());
            using var terminal = new TerminalHub.Core.Terminal.TerminalEmulator();
            terminal.Parser.Feed("\u001b]133;E;运行中\u0007\u001b]133;C\u0007\r\n\u001b]133;D;0\u0007");
            var command = new CommandRecordViewModel(terminal.Commands.Records[0], "C:\\设置");
            Assert.Equal("运行中", command.DisplayTitle.ToString());
            Assert.Equal("C:\\设置 · Done · 0.0s", command.DisplayDetail.ToString());
            locale.RegisterResources("test.module", "zh-CN", new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["zh-CN"] = new Dictionary<string, string> { ["title"] = "模块标题", ["fallback"] = "默认文案" },
                ["en"] = new Dictionary<string, string> { ["title"] = "Module title" }
            });
            Assert.Equal("Module title", locale.GetModuleText("test.module", "title", "missing"));
            Assert.Equal("默认文案", locale.GetModuleText("test.module", "fallback", "missing"));
            locale.SetLanguage("system", CultureInfo.GetCultureInfo("zh-CN")); Assert.Equal("zh-CN", locale.Language);
            locale.SetLanguage("system", CultureInfo.GetCultureInfo("en-US")); Assert.Equal("en", locale.Language);
        }
        finally { locale.UnregisterResources("test.module"); locale.SetLanguage(original); }
    }

    [AvaloniaFact]
    public async Task LanguageSwitchUpdatesOpenMenusAndWindows_WhileUserTextAndProcessesStayIntact()
    {
        using var f = new StageLayoutTests.StageFixture(width: 1100, firstSessionName: "设置"); await f.ReadyAsync();
        var card = f.Vm.SessionCards[0]; var emulator = card.Model.Emulator;
        f.Vm.ActivateSearchSession(card.Model); await Task.Delay(80);
        emulator.Parser.Feed("\r\n原始输出：设置，命令，工作区\r\n");
        var output = emulator.Buffer.TailText(100);
        var tools = new ProjectToolsWindow(f.Window, f.Vm); tools.Show(f.Window);
        f.Vm.OpenInNewWindowCommand.Execute(f.Vm.SessionCards[1]); await Task.Delay(60);
        var popout = f.Vm.Popouts[0]; popout.Width = 420;
        var profile = new PublishProfileWindow(); profile.Show(f.Window);
        var menu = (MenuFlyout)f.Window.FindControl<Button>("SessionMenuButton")!.Flyout!;
        menu.ShowAt(f.Window.FindControl<Button>("SessionMenuButton")!); await Task.Delay(80);
        try
        {
            f.Vm.LanguageIndex = 2; await Task.Delay(80);
            Assert.Contains(menu.Items.OfType<MenuItem>(), m => Equals(m.Header, "Single pane"));
            Assert.Contains(tools.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "My rules");
            Assert.Contains(f.Window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Settings");
            var level = f.Window.FindControl<ComboBox>("OutputLevelCombo")!;
            var selected = Assert.Single(level.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "All");
            Assert.Equal("All", selected.Text);
            Assert.DoesNotContain("独立窗口", popout.Title);
            Assert.Equal(Localizer.Current.Get("M0772"), profile.Title);
            var returnButton = Assert.Single(popout.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, Localizer.Current.Get("M0784")));
            var returnPosition = returnButton.TranslatePoint(default, popout)!.Value;
            Assert.True(returnPosition.X >= 0 && returnPosition.X + returnButton.Bounds.Width <= popout.Bounds.Width);
            Assert.Equal("设置", card.Name); Assert.Equal(output, emulator.Buffer.TailText(100)); Assert.True(card.Model.IsRunning);
            Assert.Same(emulator, card.Model.Emulator);
            Assert.Equal("en", f.Vm.Settings.Language);
            f.Vm.LanguageIndex = 1; await Task.Delay(40);
            Assert.Contains(menu.Items.OfType<MenuItem>(), m => Equals(m.Header, "回到单窗格"));
            Assert.Equal("全部", selected.Text);
        }
        finally { menu.Hide(); tools.Close(); profile.Close(); popout.Close(); f.Vm.LanguageIndex = 0; }
    }

    private static Point TabPoint(Window window, LiveWorkspace workspace)
    {
        var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("workspace-name") && ReferenceEquals(b.Tag, workspace));
        return button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
    }
    private static Point CardPoint(Window window, SessionCardViewModel card)
    {
        var control = window.GetVisualDescendants().OfType<StageCard>().Single(c => ReferenceEquals(c.DataContext, card));
        return control.TranslatePoint(new Point(control.Bounds.Width / 2, 20), window)!.Value;
    }
}
