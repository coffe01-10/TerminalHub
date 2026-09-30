using System.Collections;
using System.Reflection;
using Avalonia.Headless.XUnit;
using TerminalHub.App.ViewModels;
using TerminalHub.Core.Logging;
using TerminalHub.Core.Monitoring;
using TerminalHub.Core.Sessions;
using TerminalHub.Core.Terminal;
using Xunit;

namespace TerminalHub.Tests;

public class CodeReviewRemainderTests
{
    [Theory]
    [InlineData("\u001b[8;1H\u001b[20A", 2)]
    [InlineData("\u001b[1;1H\u001b[20B", 5)]
    public void CursorEnteringScrollRegion_StopsAtTheOppositeMargin(string move, int expectedRow)
    {
        var buffer = new ScreenBuffer(10, 8);
        var parser = new VtParser(buffer);
        parser.Feed("\u001b[3;6r" + move);
        Assert.Equal(expectedRow, buffer.CursorY);
    }

    [Fact]
    public void UnderlineColorSubparameters_DoNotConsumeFollowingAttributes()
    {
        var buffer = new ScreenBuffer(10, 8);
        new VtParser(buffer).Feed("\u001b[58:2::255:0:0;5;1mX");
        var cell = buffer.CellAt(0, 0);
        Assert.Equal('X', cell.Char);
        Assert.True(cell.Attrs.HasFlag(CellAttrs.Blink));
        Assert.True(cell.Attrs.HasFlag(CellAttrs.Bold));
    }

    [Fact]
    public void SoftReset_PreservesCursorAndContent_ResetsOnlyActiveSavedCursor()
    {
        var buffer = new ScreenBuffer(10, 8);
        var parser = new VtParser(buffer);
        parser.Feed("\u001b[4;5H\u001b7\u001b[?47h\u001b[3;6r\u001b[?6h\u001b[2;3H\u001b7\u001b[!pX");
        Assert.True(buffer.OnAlternateScreen);
        Assert.Equal('X', buffer.CellAt(3, 2).Char);
        Assert.False(buffer.OriginMode);
        parser.Feed("\u001b8");
        Assert.Equal((0, 0), (buffer.CursorY, buffer.CursorX));
        parser.Feed("\u001b[?47l\u001b8");
        Assert.Equal((3, 4), (buffer.CursorY, buffer.CursorX));
    }

    [Fact]
    public void HardReset_ClearsHistoryAndBothSavedCursorStates()
    {
        var buffer = new ScreenBuffer(10, 3);
        var parser = new VtParser(buffer);
        parser.Feed("a\r\nb\r\nc\r\nd\r\n\u001b[2;5H\u001b7\u001b[?47h\u001b[3;7H\u001b7\u001bc");
        Assert.False(buffer.OnAlternateScreen);
        Assert.Equal(0, buffer.ScrollbackCount);
        parser.Feed("\u001b[2;2H\u001b8");
        Assert.Equal((0, 0), (buffer.CursorY, buffer.CursorX));
        parser.Feed("\u001b[?47h\u001b[2;2H\u001b8");
        Assert.Equal((0, 0), (buffer.CursorY, buffer.CursorX));
    }

    [Fact]
    public void HistoryFrameCache_IsBoundedAndKeepsLiveFrameReusable()
    {
        var buffer = new ScreenBuffer(10, 3);
        var parser = new VtParser(buffer);
        for (var i = 0; i < 100; i++) parser.Feed($"{i}\r\n");
        var live = buffer.CaptureFrame();
        for (var offset = 1; offset <= buffer.ScrollbackCount; offset++) buffer.CaptureFrame(offset);
        var cache = (IDictionary)typeof(ScreenBuffer).GetField("_frameCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(buffer)!;
        Assert.InRange(cache.Count, 1, 4);
        Assert.Same(live, buffer.CaptureFrame());
        parser.Feed("new");
        Assert.NotSame(live, buffer.CaptureFrame());
    }

    [Fact]
    public void CardPreview_IsReusedWhenIdle_AndRefreshedAfterOutputOrResize()
    {
        using var terminal = new TerminalEmulator(columns: 10, rows: 3);
        var card = new SessionCardViewModel(new TerminalSessionModel { Name = "preview", Emulator = terminal });
        terminal.Parser.Feed("hello");
        card.Refresh();
        var lines = card.PreviewLines;
        card.Refresh();
        Assert.Same(lines, card.PreviewLines);
        terminal.Parser.Feed(" world");
        card.Refresh();
        Assert.NotSame(lines, card.PreviewLines);
        Assert.Equal("hello world", card.PreviewText.Replace("\n", ""));
        lines = card.PreviewLines;
        terminal.Resize(20, 3);
        card.Refresh();
        Assert.NotSame(lines, card.PreviewLines);
    }

    [Fact]
    public void LogRetention_KeepsNewest32_ProtectsActiveWritersAndExports()
    {
        var dir = Path.Combine(Path.GetTempPath(), "th-log-retention-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var active = new SessionLogFile();
        var newest = new SessionLogFile();
        try
        {
            var activePath = active.Enable(dir);
            File.SetLastWriteTimeUtc(activePath, new DateTime(1999, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            for (var i = 0; i < 40; i++)
            {
                var time = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(i);
                var path = Path.Combine(dir, $"terminalhub-{time:yyyyMMdd-HHmmss-fff}.log");
                File.WriteAllText(path, "old log");
                File.SetLastWriteTimeUtc(path, time);
            }
            var export = Path.Combine(dir, "terminalhub-export.log");
            File.WriteAllText(export, "keep this export");
            var newestPath = newest.Enable(dir);
            Assert.True(File.Exists(activePath));
            Assert.True(File.Exists(newestPath));
            Assert.True(File.Exists(export));
            Assert.Equal(33, Directory.GetFiles(dir, "terminalhub-2*.log").Length + Directory.GetFiles(dir, "terminalhub-1*.log").Length);
            Assert.False(File.Exists(Path.Combine(dir, "terminalhub-20000101-000000-000.log")));
            Assert.True(File.Exists(Path.Combine(dir, "terminalhub-20000101-000039-000.log")));
            active.Write("active", "info", "still writing");
            using (var reader = new StreamReader(new FileStream(activePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                Assert.Contains("still writing", reader.ReadToEnd());
            active.Disable();
            Assert.Equal(32, Directory.GetFiles(dir, "terminalhub-2*.log").Length + Directory.GetFiles(dir, "terminalhub-1*.log").Length);
        }
        finally
        {
            active.Dispose();
            newest.Dispose();
            Directory.Delete(dir, true);
        }
    }

    [AvaloniaFact]
    public async Task FailedLogWrite_DisablesSinkAndReportsFailureWithoutThrowing()
    {
        var dashboard = new DashboardViewModel(new IdleMonitor());
        using var file = new SessionLogFile();
        using var logs = new LogsViewModel(dashboard, file, () => []);
        var writer = new FailingWriter();
        typeof(SessionLogFile).GetField("_writer", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(file, writer);
        Assert.Null(Record.Exception(() => file.Write("shell", "info", "output")));
        Assert.False(file.IsEnabled);
        Assert.True(writer.Disposed);
        await Task.Delay(30);
        Assert.Contains("已停止", logs.FileStatus);
        Assert.Contains("disk full", logs.FileStatus);
    }

    private sealed class FailingWriter : StreamWriter
    {
        public bool Disposed { get; private set; }
        public FailingWriter() : base(new MemoryStream()) { }
        public override void WriteLine(string? value) => throw new IOException("disk full");
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
            throw new IOException("disk full on close");
        }
    }

    private sealed class IdleMonitor : ISystemMonitor
    {
        public SystemSample Current { get; } = new();
        public IReadOnlyList<ProcessInfo> Processes { get; } = [];
#pragma warning disable CS0067
        public event Action<ISystemMonitor>? Sampled;
#pragma warning restore CS0067
        public void Start(TimeSpan interval) { }
        public void Stop() { }
        public void Dispose() { }
    }
}
