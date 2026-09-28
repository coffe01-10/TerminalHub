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
}
