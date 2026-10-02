using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using TerminalHub.App.Controls;
using TerminalHub.App.ViewModels;
using TerminalHub.App.Views;
using TerminalHub.Core.Pty;
using TerminalHub.Core.Settings;
using TerminalHub.Core.Terminal;
using TerminalHub.Pty;
using Xunit;

namespace TerminalHub.Tests;

public class ProjectFeaturesTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkspaceSwitchBatchesShelfAndReusesThumbnails_RapidSwitchKeepsFinalSelection(bool autoHide)
    {
        using var f = new StageLayoutTests.StageFixture();
        await Task.Delay(650);
        var vm = f.Vm;
        vm.ShelfAutoHide = autoHide;
        if (autoHide)
        {
            await Task.Delay(350);
            f.Window.MouseMove(new Point(4, 300));
            await Task.Delay(300);
        }
        var first = vm.ActiveWorkspace;
        var cards = vm.SessionCards.ToArray();
        var thumbnails = f.Window.GetVisualDescendants().OfType<StageCard>()
            .ToDictionary(c => (SessionCardViewModel)c.DataContext!);
        await vm.NewProjectWorkspaceCommand.ExecuteAsync(null);
        await Task.Delay(200);
        var second = vm.ActiveWorkspace;
        vm.ShelfAutoHide = autoHide;
        var sessionChanges = 0; var shelfChanges = 0;
        System.Collections.Specialized.NotifyCollectionChangedEventHandler onSessions = (_, _) => sessionChanges++;
        System.Collections.Specialized.NotifyCollectionChangedEventHandler onShelf = (_, _) => shelfChanges++;
        vm.SessionCards.CollectionChanged += onSessions;
        vm.ShelfItems.CollectionChanged += onShelf;
        vm.SwitchProjectWorkspace(first);
        Assert.Equal(1, sessionChanges); Assert.Equal(1, shelfChanges);
        await Task.Delay(100);
        if (autoHide)
        {
            // Hidden drawers defer creating their visual items until revealed.
            f.Window.MouseMove(new Point(900, 300));
            f.Window.MouseMove(new Point(4, 300));
            await Task.Delay(300);
        }
        Assert.Equal(cards, vm.SessionCards);
        Assert.Same(vm.ActiveCard, f.Window.FindControl<ListBox>("SessionShelf")!.SelectedItem);
        var restoredThumbnails = f.Window.GetVisualDescendants().OfType<StageCard>().ToArray();
        Assert.Equal(cards.Length, restoredThumbnails.Length);
        foreach (var thumbnail in restoredThumbnails)
        {
            var card = (SessionCardViewModel)thumbnail.DataContext!;
            Assert.Same(thumbnails[card], thumbnail);
            Assert.Equal(autoHide, thumbnail.WheelMode);
            Assert.Same(card.Model.Emulator, thumbnail.GetVisualDescendants().OfType<StagePreview>().Single().Emulator);
        }
        // Workspace tabs should present their final surface immediately, rather
        // than starting a thumbnail flight on each complete workspace rebuild.
        Assert.Equal(1, f.Window.FindControl<StageSurface>("StageWindow")!.Reveal);
        for (var i = 0; i < 6; i++)
        {
            vm.SwitchProjectWorkspace(second);
            vm.SwitchProjectWorkspace(first);
        }
        await Task.Delay(150);
        Assert.Same(first, vm.ActiveWorkspace);
        Assert.Same(first.Active, vm.ActiveSession);
        Assert.Same(vm.ActiveSession!.Emulator, f.Window.FindControl<TerminalView>("MainTerminal")!.Emulator);
        Assert.Same(vm.ActiveCard, f.Window.FindControl<ListBox>("SessionShelf")!.SelectedItem);
        Assert.All(vm.AllSessionCards, c => Assert.True(c.Model.IsRunning));
        vm.SessionCards.CollectionChanged -= onSessions;
        vm.ShelfItems.CollectionChanged -= onShelf;
    }

    [AvaloniaFact]
    public async Task WorkspaceSwitchPreservesPtyAndSplit_SearchReturnsToBackgroundWorkspace()
    {
        using var f = new StageLayoutTests.StageFixture(); await Task.Delay(650);
        var vm = f.Vm; var first = vm.ActiveWorkspace; var sessions = vm.SessionCards.Select(c => c.Model).ToArray();
        vm.ToggleSplitCommand.Execute(null); Assert.True(vm.IsSplit); var left = vm.LeftPane; var right = vm.RightPane;
        System.Collections.Specialized.NotifyCollectionChangedEventHandler persistDuringRebuild = (_, _) => vm.PersistSettings();
        vm.SessionCards.CollectionChanged += persistDuringRebuild;
        await vm.NewProjectWorkspaceCommand.ExecuteAsync(null); await Task.Delay(200);
        var second = vm.ActiveWorkspace; Assert.NotSame(first, second); Assert.Single(vm.SessionCards);
        await vm.SetSplitLayoutAsync("Vertical"); await Task.Delay(100);
        Assert.True(vm.IsSplit); Assert.DoesNotContain(vm.LeftPane, sessions); Assert.DoesNotContain(vm.RightPane, sessions);
        Assert.All(sessions, s => Assert.True(s.IsRunning));
        vm.SwitchProjectWorkspace(first);
        Assert.Equal(5, vm.SessionCards.Count); Assert.True(vm.IsSplit, $"Saved split={first.Layout.IsSplit}, indices={first.Layout.LeftIndex}/{first.Layout.RightIndex}, mode={first.Layout.SplitLayout}"); Assert.Same(left, vm.LeftPane); Assert.Same(right, vm.RightPane);
        sessions[0].Emulator.Parser.Feed("\r\nbackground-needle\r\n");
        vm.SwitchProjectWorkspace(second);
        var b = sessions[0].Emulator.Buffer; var hit = Assert.Single(b.SearchLines("background-needle"));
        var result = new SessionSearchResult(sessions[0], b, hit, b.CreateAnchor(hit.Line, 0), "background-needle");
        Assert.True(await f.Window.LocateSearchResultAsync(result)); Assert.Same(first, vm.ActiveWorkspace); Assert.Same(sessions[0], vm.ActiveSession);
        vm.CloseProjectWorkspaceCommand.Execute(second); await Task.Delay(120); Assert.All(sessions, s => Assert.True(s.IsRunning));
        vm.SaveProjectToolsCommand.Execute(null);
        Assert.Single(vm.Settings.ProjectWorkspaces); Assert.Equal(5, vm.Settings.ProjectWorkspaces[0].Layout.Sessions.Count);
        vm.SessionCards.CollectionChanged -= persistDuringRebuild;
    }

    [AvaloniaFact]
    public async Task BroadcastUsesSelectedTargetsAndEachPasteProtocol_EscapeStopsIt()
    {
        using var f = new StageLayoutTests.StageFixture(); await Task.Delay(650); var vm = f.Vm;
        var targets = vm.BroadcastTargets.ToArray(); targets[0].Selected = targets[1].Selected = true;
        var a = (MockPtySession)targets[0].Card.Model.Pty; var b = (MockPtySession)targets[1].Card.Model.Pty;
        var ignored = (MockPtySession)targets[2].Card.Model.Pty;
        targets[0].Card.Model.Emulator.Parser.Feed("\x1b[?2004h");
        vm.StartBroadcastCommand.Execute(null); Assert.True(vm.BroadcastEnabled);
        vm.SendTerminalInput(targets[2].Card.Model.Emulator, new([], "中文\nnext"));
        Assert.Contains("\x1b[200~中文\nnext\x1b[201~", a.RawInput.ToString());
        Assert.Contains("中文\rnext", b.RawInput.ToString()); Assert.Empty(ignored.RawInput.ToString());
        f.Window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Assert.False(vm.BroadcastEnabled);
        var before = a.RawInput.Length; vm.SendTerminalInput(targets[2].Card.Model.Emulator, TerminalInput.Text("only-source"));
        Assert.Equal(before, a.RawInput.Length); Assert.Contains("only-source", ignored.RawInput.ToString());
    }

    [AvaloniaFact]
    public async Task RulesMatchUnicodeAndLocateHistory_TaskWaitsForActualCompletion()
    {
        using var f = new StageLayoutTests.StageFixture(); await Task.Delay(650); var vm = f.Vm;
        vm.NewOutputRuleCommand.Execute(null); vm.SelectedOutputRule!.Pattern = "错误\\s+\\d+"; vm.SelectedOutputRule.IsRegex = true;
        var session = vm.ActiveSession!; session.Emulator.Parser.Feed("\r\n中文 错误 42\r\n");
        vm.ScanOutputRules(); var result = Assert.Single(vm.OutputRuleResults);
        Assert.Equal("错误 42", result.Result.Query); Assert.True(await f.Window.LocateSearchResultAsync(result.Result));
        vm.SelectedOutputRule.Pattern = "["; Assert.NotEmpty(vm.SelectedOutputRule.Error); vm.ScanOutputRules(); Assert.Empty(vm.OutputRuleResults);
        vm.NewProjectTaskCommand.Execute(null); var task = vm.SelectedProjectTask!; task.Command = "echo hello"; task.SessionName = session.Name;
        vm.RunProjectTask(task); Assert.True(task.Running);
        Assert.Contains("echo hello\r", ((MockPtySession)session.Pty).RawInput.ToString());
        session.Emulator.Parser.Feed("\x1b]133;C\x07"); vm.StopProjectTask(task); Assert.True(task.Running);
        Assert.EndsWith("\x03", ((MockPtySession)session.Pty).RawInput.ToString());
        session.Emulator.Parser.Feed("\x1b]133;D;7\x07"); await Task.Delay(80);
        Assert.False(task.Running); Assert.Contains("7", task.Status);
    }

    [AvaloniaTheory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task ToolsAreVisibleAcrossThemes(int theme)
    {
        using var f = new StageLayoutTests.StageFixture(); await Task.Delay(650); f.Vm.ThemeIndex = theme;
        var tools = new ProjectToolsWindow(f.Window, f.Vm); tools.Show(f.Window); await Task.Delay(80);
        var tabs = tools.GetVisualDescendants().OfType<TabControl>().First(); Assert.Equal(5, tabs.Items.Count);
        var navigation = tools.FindControl<ListBox>("ToolNavigation")!;
        Assert.Equal(0, navigation.SelectedIndex);
        Assert.True(tools.FindControl<StackPanel>("RuleEmptyState")!.IsVisible);
        Assert.False(tools.FindControl<ScrollViewer>("RuleEditor")!.IsVisible);
        if (Environment.GetEnvironmentVariable("TERMINALHUB_STAGE_CAPTURES") is { } emptyDirectory)
        { Directory.CreateDirectory(emptyDirectory); tools.CaptureRenderedFrame()!.Save(Path.Combine(emptyDirectory, $"project-empty-{theme}.png")); }
        var createRule = Assert.Single(tools.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "＋ 新建第一条规则"));
        var createPoint = createRule.TranslatePoint(new Avalonia.Point(createRule.Bounds.Width / 2, createRule.Bounds.Height / 2), tools)!.Value;
        tools.MouseDown(createPoint, MouseButton.Left); tools.MouseUp(createPoint, MouseButton.Left);
        await Task.Delay(40); Assert.NotNull(f.Vm.SelectedOutputRule);
        f.Vm.SelectedOutputRule!.Name = "构建错误"; f.Vm.SelectedOutputRule.Pattern = "error";
        Assert.False(tools.FindControl<StackPanel>("RuleEmptyState")!.IsVisible);
        Assert.True(tools.FindControl<ScrollViewer>("RuleEditor")!.IsVisible);
        f.Vm.NewProjectTaskCommand.Execute(null); f.Vm.SelectedProjectTask!.Name = "构建项目"; f.Vm.SelectedProjectTask.Command = "dotnet build";
        for (var i = 0; i < 5; i++)
        {
            navigation.SelectedIndex = i; tools.UpdateLayout(); await Task.Delay(40);
            Assert.Equal(i, tabs.SelectedIndex); Assert.True(tabs.Bounds.Width > 500);
            if (Environment.GetEnvironmentVariable("TERMINALHUB_STAGE_CAPTURES") is { } directory)
            { Directory.CreateDirectory(directory); tools.CaptureRenderedFrame()!.Save(Path.Combine(directory, $"project-tools-{theme}-{i}.png")); }
        }
        if (Environment.GetEnvironmentVariable("TERMINALHUB_STAGE_CAPTURES") is { } output) f.Window.CaptureRenderedFrame()!.Save(Path.Combine(output, $"project-main-{theme}.png"));
        tools.Width = tools.MinWidth; tools.Height = tools.MinHeight; tools.UpdateLayout();
        for (var i = 0; i < 5; i++)
        {
            navigation.SelectedIndex = i; tools.UpdateLayout(); await Task.Delay(40);
            var page = Assert.IsAssignableFrom<Control>(tabs.SelectedContent);
            Assert.True(page.Bounds.Width <= tabs.Bounds.Width + 1, $"Page {i} exceeds the tool window at its minimum width.");
            if (Environment.GetEnvironmentVariable("TERMINALHUB_STAGE_CAPTURES") is { } minimumOutput)
                tools.CaptureRenderedFrame()!.Save(Path.Combine(minimumOutput, $"project-minimum-{theme}-{i}.png"));
        }
        tools.Close();
    }

    [Fact]
    public async Task RecordingPreservesUnicodeColorsResizeAndBackwardSeek()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".threc");
        using var emulator = new TerminalEmulator(columns: 20, rows: 5);
        emulator.Parser.Feed("\x1b[31m红色中文\x1b[0m");
        try
        {
            await using (var recorder = new TerminalRecorder(emulator, path, "fixture"))
            {
                await Task.Delay(10); emulator.Parser.Feed("\r\nA👨‍👩‍👧‍👦B"); emulator.Resize(30, 7);
                emulator.Parser.Feed("\r\nlast");
            }
            using var playback = await TerminalPlayback.LoadAsync(path);
            Assert.Contains("红色中文", ScreenBuffer.FlattenRow(playback.Emulator.Buffer.GetLine(0)).Text);
            playback.Seek(playback.DurationMs); Assert.Equal(30, playback.Emulator.Buffer.Columns);
            Assert.Contains("last", string.Join("\n", Enumerable.Range(0, playback.Emulator.Buffer.TotalLines).Select(i => ScreenBuffer.FlattenRow(playback.Emulator.Buffer.GetLine(i)).Text)));
            playback.Seek(0); Assert.Equal(20, playback.Emulator.Buffer.Columns);
            Assert.Empty(playback.Emulator.Buffer.SearchLines("last")); Assert.Equal(TerminalColor.Indexed(1), playback.Emulator.Buffer.GetLine(0)[0].Fg);
            playback.Seek(playback.DurationMs); Assert.Single(playback.Emulator.Buffer.SearchLines("last"));
        }
        finally { File.Delete(path); }
    }

    [AvaloniaFact]
    public async Task SavedTabsRestoreEachLayoutAndDefinitions_WithFreshProcesses()
    {
        var directory = Path.Combine(Path.GetTempPath(), "terminalhub-tabs-" + Guid.NewGuid());
        PtySessionFactory.UseMock = true; var store = new SettingsStore(Path.Combine(directory, "settings.json"));
        store.Save(new AppSettings { StartupSessions = [new StartupSession { Name = "Shell", WorkingDirectory = Environment.CurrentDirectory }] });
        MainWindow? first = null, second = null;
        try
        {
            first = new MainWindow(store); first.Show(); await Task.Delay(600); var vm = (MainWindowViewModel)first.DataContext!;
            vm.ActiveWorkspace.Name = "前端"; await vm.SetSplitLayoutAsync("Vertical"); await Task.Delay(100);
            await vm.NewProjectWorkspaceCommand.ExecuteAsync(null); await Task.Delay(100); vm.ActiveWorkspace.Name = "后端";
            vm.NewOutputRuleCommand.Execute(null); vm.SelectedOutputRule!.Pattern = "warning";
            vm.NewProjectTaskCommand.Execute(null); vm.SelectedProjectTask!.Command = "dotnet build";
            vm.MoveProjectWorkspace(vm.ProjectWorkspaces[0], 1);
            var ids = vm.AllSessionCards.Select(c => c.Model.Id).ToArray(); first.Close(); first = null;
            second = new MainWindow(store); second.Show(); await Task.Delay(800); var restored = (MainWindowViewModel)second.DataContext!;
            Assert.Equal(2, restored.ProjectWorkspaces.Count); Assert.Equal("后端", restored.ActiveWorkspace.Name); Assert.Single(restored.SessionCards);
            Assert.Equal(new[] { "后端", "前端" }, restored.ProjectWorkspaces.Select(w => w.Name));
            Assert.Equal("dotnet build", Assert.Single(restored.ProjectTasks).Command); Assert.Equal("warning", Assert.Single(restored.OutputRules).Pattern);
            restored.SwitchProjectWorkspace(restored.ProjectWorkspaces.First(w => w.Name == "前端"));
            Assert.True(restored.IsSplit); Assert.Equal(SplitLayout.Vertical, restored.SplitLayout); Assert.Equal(2, restored.SessionCards.Count);
            Assert.All(restored.AllSessionCards, c => Assert.DoesNotContain(c.Model.Id, ids));
        }
        finally { first?.Close(); second?.Close(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task RecordingStartsDuringUtf8AndPreservesPendingWrapAndCurrentStyle()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".threc"); using var terminal = new TerminalEmulator(columns: 6, rows: 4);
        terminal.Parser.Feed("\x1b[32mabcdef"); var chinese = Encoding.UTF8.GetBytes("中文"); terminal.Parser.Feed(chinese.AsSpan(0, 2));
        try
        {
            await using (var recorder = new TerminalRecorder(terminal, path, "boundary")) { terminal.Parser.Feed(chinese.AsSpan(2)); }
            using var playback = await TerminalPlayback.LoadAsync(path); playback.Seek(playback.DurationMs);
            var source = terminal.Buffer.CaptureFrame(); var actual = playback.Emulator.Buffer.CaptureFrame();
            Assert.Equal(source.Cells.Select(c => c.Text), actual.Cells.Select(c => c.Text));
            Assert.Equal(source.Cells.Select(c => c.Fg), actual.Cells.Select(c => c.Fg)); Assert.Equal(source.CursorX, actual.CursorX); Assert.Equal(source.CursorY, actual.CursorY);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task RecordingStartedInTuiRetainsPrimaryScreenAfterExit()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".threc"); using var terminal = new TerminalEmulator(columns: 20, rows: 5);
        terminal.Parser.Feed("primary screen\x1b[?1049h\x1b[2J\x1b[Happlication screen");
        try
        {
            await using (var recorder = new TerminalRecorder(terminal, path, "tui")) { terminal.Parser.Feed("\x1b[?1049l\r\nback to shell"); }
            using var playback = await TerminalPlayback.LoadAsync(path); Assert.True(playback.Emulator.Buffer.OnAlternateScreen);
            playback.Seek(playback.DurationMs); Assert.False(playback.Emulator.Buffer.OnAlternateScreen);
            var source = terminal.Buffer.CaptureFrame(); var actual = playback.Emulator.Buffer.CaptureFrame();
            Assert.Equal(source.Cells.Select(c => c.Text), actual.Cells.Select(c => c.Text));
        }
        finally { File.Delete(path); }
    }

    [AvaloniaFact]
    public async Task PlaybackControlsPauseChangeSpeedAndSeek_WithoutResizingRecordedGrid()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".threc"); using var source = new TerminalEmulator(columns: 20, rows: 5);
        PlaybackWindow? window = null;
        try
        {
            source.Parser.Feed("start");
            await using (var recorder = new TerminalRecorder(source, path, "controls"))
            { await Task.Delay(200); source.Parser.Feed("\r\nfinish"); await Task.Delay(200); }
            var playback = await TerminalPlayback.LoadAsync(path); window = new PlaybackWindow(playback); window.Show(); await Task.Delay(80);
            var view = Assert.Single(window.GetVisualDescendants().OfType<TerminalView>()); Assert.True(view.IsPreview); Assert.Equal(20, playback.Emulator.Buffer.Columns);
            var button = Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "播放"));
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); await Task.Delay(50);
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Assert.Equal("播放", button.Content); var paused = playback.PositionMs;
            await Task.Delay(60); Assert.Equal(paused, playback.PositionMs);
            Assert.Single(window.GetVisualDescendants().OfType<ComboBox>()).SelectedIndex = 3;
            var timeline = Assert.Single(window.GetVisualDescendants().OfType<Slider>()); timeline.Value = playback.DurationMs;
            Assert.NotEmpty(playback.Emulator.Buffer.SearchLines("finish")); timeline.Value = 0;
            Assert.Empty(playback.Emulator.Buffer.SearchLines("finish"));
            window.Width = 720; window.Height = 420; window.UpdateLayout(); Assert.Equal(20, playback.Emulator.Buffer.Columns); Assert.Equal(5, playback.Emulator.Buffer.Rows);
        }
        finally { window?.Close(); File.Delete(path); }
    }

    [AvaloniaFact]
    public async Task ClosingMainWindowFlushesRecordingWithoutWaitingOnUiContinuation()
    {
        using var fixture = new StageLayoutTests.StageFixture(); await Task.Delay(600);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".threc");
        try
        {
            fixture.Vm.StartRecording(path); fixture.Vm.ActiveSession!.Emulator.Parser.Feed("\r\nrecord-before-close\r\n");
            await Task.Delay(30); fixture.Window.Close();
            using var playback = await TerminalPlayback.LoadAsync(path); playback.Seek(playback.DurationMs);
            Assert.NotEmpty(playback.Emulator.Buffer.SearchLines("record-before-close"));
        }
        finally { File.Delete(path); }
    }

    [AvaloniaFact]
    public async Task SessionArrivingAfterSwitchIsIncludedInBackgroundWorkspaceSave()
    {
        using var fixture = new StageLayoutTests.StageFixture(); await Task.Delay(600); var vm = fixture.Vm;
        var first = vm.ActiveWorkspace;
        await vm.NewProjectWorkspaceCommand.ExecuteAsync(null); var second = vm.ActiveWorkspace;
        vm.SwitchProjectWorkspace(first); vm.PersistSettings();
        Assert.Single(Assert.Single(vm.Settings.ProjectWorkspaces, w => w.Id == second.Id).Layout.Sessions);
        await Task.Delay(120); vm.PersistSettings();
        Assert.Single(second.Cards);
        var saved = Assert.Single(vm.Settings.ProjectWorkspaces, w => w.Id == second.Id);
        Assert.Single(saved.Layout.Sessions); Assert.Equal(0, saved.Layout.ActiveIndex);
    }
}
