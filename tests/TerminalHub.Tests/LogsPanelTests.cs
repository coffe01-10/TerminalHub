using Avalonia.Headless.XUnit;
using TerminalHub.App.ViewModels;
using TerminalHub.Core.Logging;
using TerminalHub.Core.Monitoring;
using Xunit;

namespace TerminalHub.Tests;

public class LogsPanelTests
{
    private sealed class FakeMonitor : ISystemMonitor
    {
        public SystemSample Current { get; } = new();
        public IReadOnlyList<ProcessInfo> Processes { get; } = [];
#pragma warning disable CS0067 // event required by interface; unused in tests
        public event Action<ISystemMonitor>? Sampled;
#pragma warning restore CS0067
        public void Start(TimeSpan interval) { }
        public void Stop() { }
        public void Dispose() { }
    }

    private static (DashboardViewModel dash, LogsViewModel logs, SessionLogFile file) MakeLogs()
    {
        var dash = new DashboardViewModel(new FakeMonitor());
        var file = new SessionLogFile();
        var logs = new LogsViewModel(dash, file, () => ["Terminal 01", "Terminal 02"]);
        logs.RefreshSessions();
        return (dash, logs, file);
    }

    [AvaloniaFact]
    public async Task StreamsAppear_InEntries()
    {
        var (dash, logs, _) = MakeLogs();
        dash.AppendOutput("info", "hello world", "Terminal 01");
        await Task.Delay(80);
        Assert.Single(logs.Entries);
        Assert.Equal("hello world", logs.Entries[0].Message);
        Assert.Equal("Terminal 01", logs.Entries[0].Source);
    }

    [AvaloniaFact]
    public async Task FilterText_NarrowsEntries()
    {
        var (dash, logs, _) = MakeLogs();
        dash.AppendOutput("info", "GET /api/posts 200", "Terminal 01");
        dash.AppendOutput("info", "compiled ok", "Terminal 01");
        await Task.Delay(80);
        logs.FilterText = "posts";
        await Task.Delay(20);
        Assert.Single(logs.Entries);
        Assert.Contains("posts", logs.Entries[0].Message);
    }

    [AvaloniaFact]
    public async Task LevelAndSession_Filters()
    {
        var (dash, logs, _) = MakeLogs();
        dash.AppendOutput("info", "fine", "Terminal 01");
        dash.AppendOutput("warn", "careful", "Terminal 02");
        await Task.Delay(80);

        logs.LevelFilterIndex = 2; // warn
        await Task.Delay(20);
        Assert.Single(logs.Entries);
        Assert.Equal("careful", logs.Entries[0].Message);

        logs.LevelFilterIndex = 0;
        logs.SessionFilterIndex = 2; // Terminal 02 (index 0 = all)
        await Task.Delay(20);
        Assert.Single(logs.Entries);
        Assert.Equal("Terminal 02", logs.Entries[0].Source);
    }

    [Fact]
    public void SessionLogFile_WritesAndToggles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "th-logs-" + Guid.NewGuid().ToString("N"));
        try
        {
            var file = new SessionLogFile();
            Assert.False(file.IsEnabled);
            var path = file.Enable(dir);
            Assert.True(file.IsEnabled);
            file.Write("Terminal 01", "info", "disk-write-check");
            file.Disable();

            var content = File.ReadAllText(path);
            Assert.Contains("disk-write-check", content);
            Assert.Contains("(Terminal 01)", content);
            Assert.False(file.IsEnabled);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task FileLogging_Toggle_PersistsCallback_AndWritesLines()
    {
        var dash = new DashboardViewModel(new FakeMonitor());
        var dir = Path.Combine(Path.GetTempPath(), "th-logs-" + Guid.NewGuid().ToString("N"));
        var persisted = false;
        var file = new SessionLogFile();
        var logs = new LogsViewModel(dash, file, () => [],
            persistFileLogging: v => persisted = v, logDir: dir);
        try
        {
            logs.FileLogging = true;
            Assert.True(persisted);
            Assert.Contains(".log", logs.FileStatus);

            dash.AppendOutput("info", "via-vm-line", "Terminal 01");
            // The file write happens in MainWindowViewModel's pipeline, not the VM —
            // here we assert the sink is enabled and the file exists.
            Assert.True(file.IsEnabled);
            Assert.True(File.Exists(file.CurrentPath));
            var path = file.CurrentPath!;
            file.Write("Terminal 01", "info", "manual-check");
            file.Disable();
            Assert.Contains("manual-check", File.ReadAllText(path));
        }
        finally
        {
            logs.FileLogging = false;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        await Task.CompletedTask;
    }

    [Fact]
    public void Decoder_RawLineReceived_KeepsAnsi()
    {
        var dec = new Utf8LineDecoder();
        var raw = new List<string>();
        var clean = new List<string>();
        dec.RawLineReceived += raw.Add;
        dec.LineReceived += clean.Add;
        dec.Feed("\u001b[32mgreen\u001b[0m plain\n"u8);
        Assert.Equal(["\u001b[32mgreen\u001b[0m plain"], raw);
        Assert.Equal(["green plain"], clean);
    }

    [Fact]
    public void DebugEscape_MakesControlsVisible()
    {
        Assert.Equal("␛[32mhi␛[0m", AnsiText.DebugEscape("\x1b[32mhi\x1b[0m"));
        Assert.Equal("a␇b", AnsiText.DebugEscape("a\ab")); // \x07 followed by 'b' would merge into one hex escape
        Assert.Equal("^A", AnsiText.DebugEscape("\x01"));
    }

    [AvaloniaFact]
    public async Task DebugLog_ReceivesRawLines()
    {
        var dash = new DashboardViewModel(new FakeMonitor());
        dash.AppendDebug("␛[32mraw", "Terminal 01");
        await Task.Delay(80);
        Assert.Single(dash.DebugLog);
        Assert.Contains("␛[32m", dash.DebugLog[0].Message);
    }

    [AvaloniaFact]
    public async Task Search_FiltersActiveSessionBuffer()
    {
        var dash = new DashboardViewModel(new FakeMonitor());
        var buf = new TerminalHub.Core.Terminal.ScreenBuffer(40, 5);
        var parser = new TerminalHub.Core.Terminal.VtParser(buf);
        parser.Feed("alpha line\r\nbeta needle here\r\ngamma\r\n");
        dash.BufferSource = () => buf;

        dash.SearchQuery = "needle";
        await Task.Delay(50);
        Assert.Single(dash.SearchHits);
        Assert.Equal("1 处匹配", dash.SearchStatus);
        Assert.Contains("needle", dash.SearchHits[0].Text);

        dash.SearchQuery = "zzz";
        await Task.Delay(50);
        Assert.Empty(dash.SearchHits);
        Assert.Equal("无匹配", dash.SearchStatus);
    }
}
