using System.Net;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using TerminalHub.App.Controls;
using TerminalHub.App.ViewModels;
using TerminalHub.App.Views;
using TerminalHub.Core.Pty;
using TerminalHub.Core.Settings;
using Xunit;

namespace TerminalHub.Tests;

public class NextIterationTests
{
    private static async Task Until(Func<bool> condition)
    {
        var deadline = Environment.TickCount64 + 5000;
        while (!condition() && Environment.TickCount64 < deadline) await Task.Delay(25);
        Assert.True(condition());
    }

    [AvaloniaFact]
    public async Task FourPanes_MaximizeRestoreAndTemplate_PreserveProcessesAndRatios()
    {
        using var fixture = new StageLayoutTests.StageFixture();
        var vm = fixture.Vm;
        await Until(() => vm.SessionCards.Count == 5);
        var processes = vm.SessionCards.Select(c => c.Model).ToArray();
        await vm.SetSplitLayoutAsync("Quad");
        vm.ColumnRatio = .6; vm.RowRatio = .4;
        vm.FocusPane(3);
        await Task.Delay(150);
        fixture.Window.UpdateLayout();
        // Panes render through the pane tree (MainWindow.PaneTree.cs): each leaf is a
        // PaneFrame border hosting its TerminalView; the static SplitGrid is legacy.
        var boxes = PaneFrames(fixture.Window, vm, 4);
        Assert.Equal(4, Enumerable.Range(0, 4).Select(vm.GetPane).Distinct().Count());
        Assert.All(boxes, b => Assert.True(b.IsEffectivelyVisible && b.Bounds.Width > 80 && b.Bounds.Height > 50));
        Assert.True(boxes[0].Bounds.Width > boxes[1].Bounds.Width);
        Assert.True(boxes[0].Bounds.Height < boxes[2].Bounds.Height);
        var pane = vm.BottomRightPane!;
        boxes[3].GetVisualDescendants().OfType<TerminalView>().Single().Focus();
        Assert.Equal(3, vm.FocusedPane);
        var originalColumns = pane.Emulator.Buffer.Columns;
        vm.TogglePaneMaximizedCommand.Execute(null);
        await Task.Delay(150);
        fixture.Window.UpdateLayout();
        // Maximizing re-renders the tree with just the focused leaf.
        Assert.Single(fixture.Window.GetVisualDescendants()
            .OfType<Border>().Where(b => b.Name == "PaneFrame" && b.IsEffectivelyVisible).ToList());
        Assert.True(pane.Emulator.Buffer.Columns > originalColumns);
        vm.TogglePaneMaximizedCommand.Execute(null);
        await Task.Delay(150);
        Assert.Equal(originalColumns, pane.Emulator.Buffer.Columns);
        vm.TemplateName = "四窗格";
        vm.SaveCurrentAsTemplate();
        var template = vm.SelectedTemplate!;
        Assert.True(WorkspaceTemplateTransfer.TryParse(WorkspaceTemplateTransfer.ToJson(template), out var imported, out _));
        await vm.OpenTemplateAsync(imported);
        // RestoreWorkspaceAsync activates the focused pane synchronously, but
        // ActiveSession converges via a posted SyncActive — wait for both the
        // spawned cards and that activation before asserting.
        await Until(() => vm.SessionCards.Count == 10
            && ReferenceEquals(vm.ActiveSession, vm.BottomRightPane));
        Assert.Equal(SplitLayout.Quad, vm.SplitLayout);
        Assert.Equal(3, vm.FocusedPane);
        Assert.Equal(.6, vm.ColumnRatio);
        Assert.Equal(.4, vm.RowRatio);
        Assert.Same(vm.BottomRightPane, vm.ActiveSession);
        Assert.All(processes, p => Assert.True(p.IsRunning));
        Assert.DoesNotContain(vm.BottomRightPane, processes);
        var focused = vm.BottomRightPane;
        await vm.SetSplitLayoutAsync("Vertical");
        await Until(() => ReferenceEquals(focused, vm.ActiveSession));
        fixture.Window.UpdateLayout();
        var stacked = PaneFrames(fixture.Window, vm, 2);
        Assert.Equal(stacked[0].Bounds.Width, stacked[1].Bounds.Width, 1);
        Assert.True(stacked[1].Bounds.Y > stacked[0].Bounds.Y);
    }

    // Live pane frames ordered by pane index: each PaneFrame hosts exactly one
    // pane's TerminalView, matched by emulator instead of control name.
    private static Border[] PaneFrames(MainWindow window, MainWindowViewModel vm, int count) =>
        Enumerable.Range(0, count).Select(i => window.GetVisualDescendants().OfType<Border>()
            .Single(b => b.Name == "PaneFrame" && b.GetVisualDescendants().OfType<TerminalView>()
                .Any(t => ReferenceEquals(t.Emulator, vm.GetPane(i)!.Emulator))))
        .ToArray();

    [AvaloniaFact]
    public async Task CrossSearch_ReflowsOnActivation_AndRefusesTrimmedOrClosedOutput()
    {
        using var fixture = new StageLayoutTests.StageFixture();
        var vm = fixture.Vm;
        await Until(() => vm.SessionCards.Count == 5);
        var first = vm.SessionCards[0].Model;
        var second = vm.SessionCards[1].Model;
        foreach (var session in new[] { first, second })
            session.Emulator.Parser.Feed("\r\nunique-error 中文失败\r\n");
        vm.ActiveCard = vm.SessionCards[4];
        await Task.Delay(50);
        vm.Dashboard.SearchQuery = "unique-error";
        Assert.Empty(vm.Dashboard.SearchHits);
        vm.Dashboard.SearchScope = 1;
        Assert.Equal(2, vm.Dashboard.SearchHits.Count);
        var result = vm.Dashboard.SearchHits.First(h => h.Session == first);
        var secondResult = vm.Dashboard.SearchHits.First(h => h.Session == second);
        await vm.SetSplitLayoutAsync("Quad");
        Assert.True(await fixture.Window.LocateSearchResultAsync(result));
        Assert.True(ReferenceEquals(first, vm.ActiveSession), $"Expected {first.Name}, active {vm.ActiveSession?.Name}, focused {vm.FocusedPane}, panes {string.Join(", ", Enumerable.Range(0, vm.PaneCount).Select(i => vm.GetPane(i)?.Name))}");
        Assert.Equal("unique-error", fixture.Window.FindControl<TerminalView>(vm.FocusedPane switch
        { 0 => "LeftTerminal", 1 => "RightTerminal", 2 => "BottomLeftTerminal", _ => "BottomRightTerminal" })!.SearchQuery);
        var buf = first.Emulator.Buffer;
        Assert.Contains(result.Anchor, buf.Anchors);
        lock (buf.SyncRoot) buf.ClearScrollback();
        // Force old output into history, then drop it: an old numeric row must never land on new content.
        first.Emulator.Parser.Feed(string.Concat(Enumerable.Repeat("replacement\r\n", buf.Rows + 10)));
        lock (buf.SyncRoot) buf.ClearScrollback();
        Assert.False(await fixture.Window.LocateSearchResultAsync(result));
        Assert.Contains("裁剪", vm.Dashboard.SearchStatus);
        vm.CloseSessionCommand.Execute(vm.SessionCards.First(c => c.Model == second));
        await Until(() => !vm.SessionCards.Any(c => c.Model == second));
        Assert.False(await fixture.Window.LocateSearchResultAsync(secondResult));
        Assert.Contains("关闭", vm.Dashboard.SearchStatus);
        vm.Dashboard.SearchQuery = "";
        Assert.DoesNotContain(result.Anchor, buf.Anchors);
    }

    [AvaloniaFact]
    public async Task AllSearch_PopoutResultReattachesAndRevealsWithoutRestarting()
    {
        using var fixture = new StageLayoutTests.StageFixture();
        var vm = fixture.Vm;
        await Until(() => vm.SessionCards.Count == 5);
        var session = vm.ActiveSession!;
        session.Emulator.Parser.Feed("\r\nunique-detached 中文\r\n");
        vm.OpenInNewWindowCommand.Execute(null);
        await Until(() => vm.DetachedSessions.Contains(session) && vm.SessionCards.Count == 4);
        vm.Dashboard.SearchScope = 1;
        vm.Dashboard.SearchQuery = "unique-detached";
        var result = Assert.Single(vm.Dashboard.SearchHits);
        Assert.Same(session, result.Session);
        Assert.True(await fixture.Window.LocateSearchResultAsync(result));
        Assert.Empty(vm.DetachedSessions);
        Assert.Empty(vm.Popouts);
        Assert.Same(session, vm.ActiveSession);
        Assert.True(session.IsRunning);
    }

    [AvaloniaFact]
    public async Task CurrentSearch_RepaintedScreenResultDoesNotLocateReplacementText()
    {
        using var fixture = new StageLayoutTests.StageFixture();
        var vm = fixture.Vm;
        await Until(() => vm.SessionCards.Count == 5);
        var terminal = vm.ActiveSession!.Emulator;
        terminal.Parser.Feed("\x1b[2J\x1b[Hrepaint-error");
        vm.Dashboard.SearchQuery = "repaint-error";
        var result = Assert.Single(vm.Dashboard.SearchHits);
        Assert.True(result.CanLocate());
        terminal.Parser.Feed("\x1b[Hsuccess text ");
        Assert.False(await fixture.Window.LocateSearchResultAsync(result));
        Assert.Contains("覆盖", vm.Dashboard.SearchStatus);
    }

    [AvaloniaFact]
    public async Task FourPanes_ClosingFocusedSession_DoesNotDuplicateOrKillOtherPanes()
    {
        using var fixture = new StageLayoutTests.StageFixture();
        var vm = fixture.Vm;
        await Until(() => vm.SessionCards.Count == 5);
        await vm.SetSplitLayoutAsync("Quad");
        vm.FocusPane(3);
        var removed = vm.BottomRightPane!;
        vm.CloseSessionCommand.Execute(vm.SessionCards.First(c => c.Model == removed));
        await Until(() => vm.SessionCards.Count == 4 && vm.BottomRightPane != removed);
        Assert.Equal(4, Enumerable.Range(0, 4).Select(vm.GetPane).Distinct().Count());
        Assert.All(Enumerable.Range(0, 4).Select(vm.GetPane), p => Assert.True(p!.IsRunning));
        var next = vm.BottomLeftPane!;
        vm.CloseSessionCommand.Execute(vm.SessionCards.First(c => c.Model == next));
        await Until(() => vm.SessionCards.Count == 3 && vm.SplitLayout != SplitLayout.Quad);
        Assert.True(vm.IsSplit);
        Assert.NotSame(vm.LeftPane, vm.RightPane);
        Assert.All(vm.SessionCards, c => Assert.True(c.Model.IsRunning));
    }

    [AvaloniaFact]
    public async Task ButtonRapidClicks_ExecuteImmediately_AndKeepHitBounds()
    {
        var calls = 0;
        var button = new Button { Content = "操作", Width = 120, Height = 40, Command = new RelayCommand(() => calls++) };
        var window = new Window { Width = 220, Height = 140, Content = button };
        window.Show();
        try
        {
            window.UpdateLayout();
            var bounds = button.Bounds;
            var point = button.TranslatePoint(new Point(bounds.Width / 2, bounds.Height / 2), window)!.Value;
            window.MouseMove(point);
            window.MouseDown(point, MouseButton.Left);
            await Task.Delay(80);
            var presenter = button.GetVisualDescendants().OfType<ContentPresenter>().First();
            var scale = Assert.IsType<ScaleTransform>(presenter.RenderTransform);
            Assert.True(scale.ScaleY < 1);
            window.MouseUp(point, MouseButton.Left);
            Assert.Equal(1, calls);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.Equal(2, calls);
            window.MouseMove(new Point(1, 1));
            await Task.Delay(800);
            Assert.Equal(1, scale.ScaleX, 3);
            Assert.Equal(1, scale.ScaleY, 3);
            Assert.Equal(bounds, button.Bounds);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task Themes_LayersAndInterruptedPanelExpansion_RemainUsable(int theme)
    {
        using var fixture = new StageLayoutTests.StageFixture(1100, 700);
        var vm = fixture.Vm;
        await Until(() => vm.SessionCards.Count == 5);
        vm.ThemeIndex = theme;
        Assert.NotEqual(((ISolidColorBrush)ThemeManager.Brush("Canvas")).Color, ((ISolidColorBrush)ThemeManager.Brush("Panel")).Color);
        Assert.NotEqual(((ISolidColorBrush)ThemeManager.Brush("Panel")).Color, ((ISolidColorBrush)ThemeManager.Brush("Floating")).Color);
        vm.SettingsOpen = true;
        await Task.Delay(80);
        var panel = fixture.Window.FindControl<Border>("SettingsPanel")!;
        var scale = Assert.IsType<ScaleTransform>(panel.RenderTransform);
        vm.SettingsOpen = false;
        Assert.False(panel.IsHitTestVisible);
        await Task.Delay(40);
        var interrupted = scale.ScaleY;
        vm.SettingsOpen = true;
        Assert.Equal(interrupted, scale.ScaleY);
        Assert.True(panel.IsHitTestVisible);
        await Until(() => panel.Opacity == 1);
        Assert.Equal(1, panel.Opacity, 3);
        Assert.Equal(1, scale.ScaleY, 3);
        vm.SettingsOpen = false;
        await vm.SetSplitLayoutAsync("Quad");
        foreach (var i in Enumerable.Range(0, 4))
            vm.GetPane(i)!.Emulator.Parser.Feed($"\r\n服务 {i} · 中文构建\r\n\x1b[32mready\x1b[0m\r\n");
        await Task.Delay(900);
        var captures = Environment.GetEnvironmentVariable("TERMINALHUB_ITERATION_CAPTURES");
        if (captures is not null)
        {
            Directory.CreateDirectory(captures);
            fixture.Window.CaptureRenderedFrame()!.Save(Path.Combine(captures, $"quad-{theme}.png"));
        }
    }

    private sealed class ReleaseHandler : HttpMessageHandler
    {
        public bool FailDownload { get; set; }
        public byte[] Payload { get; } = Encoding.UTF8.GetBytes(new string('文', 50000));
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("latest"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""
                {"tag_name":"v0.4.0","body":"本轮改进","assets":[
                {"name":"TerminalHub-Setup.exe","size":150000,"browser_download_url":"https://example.com/setup"},
                {"name":"TerminalHub-preview.zip","size":150000,"browser_download_url":"https://example.com/portable"},
                {"name":"TerminalHub-linux-x64.tar.gz","size":150000,"browser_download_url":"https://example.com/linux"}]}
                """) });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = FailDownload ? new StreamContent(new InterruptedStream(Payload)) : new ByteArrayContent(Payload) });
        }
    }
    private sealed class InterruptedStream(byte[] payload) : MemoryStream(payload)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => Position > 0 ? ValueTask.FromException<int>(new IOException("连接中断")) : base.ReadAsync(buffer[..Math.Min(512, buffer.Length)], cancellationToken);
    }
    private sealed class ProgressCapture : IProgress<DownloadProgress>
    {
        public List<DownloadProgress> Values { get; } = [];
        public void Report(DownloadProgress value) => Values.Add(value);
    }
    [Fact]
    public async Task PlatformUpdate_StreamsProgress_AndCleansInterruptedDownloadWithoutReplacingExistingFile()
    {
        using var handler = new ReleaseHandler();
        using var http = new HttpClient(handler);
        var updates = new ReleaseUpdates(http);
        var release = await updates.LatestAsync();
        Assert.Equal(2, release.ForPlatform(UpdatePlatform.Windows).Count());
        var asset = Assert.Single(release.ForPlatform(UpdatePlatform.Linux));
        Assert.Equal("本轮改进", release.Notes);
        var directory = Path.Combine(Path.GetTempPath(), "terminalhub-download-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, asset.Name);
        try
        {
            var progress = new ProgressCapture();
            await updates.DownloadAsync(asset, destination, progress);
            Assert.Equal(handler.Payload, File.ReadAllBytes(destination));
            Assert.Equal(100, progress.Values.Last().Percent);
            handler.FailDownload = true;
            await Assert.ThrowsAsync<IOException>(() => updates.DownloadAsync(asset, destination));
            Assert.Equal(handler.Payload, File.ReadAllBytes(destination));
            Assert.Single(Directory.GetFiles(directory));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => updates.DownloadAsync(asset, destination, cancellationToken: cancellation.Token));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }
}
