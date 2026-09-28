using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TerminalHub.App.ViewModels;
using TerminalHub.App.Views;
using TerminalHub.Core.Logging;
using TerminalHub.Core.Monitoring;
using TerminalHub.Core.Settings;
using TerminalHub.Pty;
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

    /// <summary>Polls until <paramref name="condition"/> holds (OutputLog appends arrive via the UI dispatcher).</summary>
    private static async Task Until(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(8));
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(25);
        }
    }

    private static (DashboardViewModel dash, LogsViewModel logs, SessionLogFile file) MakeLogs(
        int bufferCapacity = LogsViewModel.DefaultBufferCapacity,
        Func<string, Task>? copyToClipboard = null,
        string? exportDir = null,
        Func<Task<string?>>? promptExportPath = null)
    {
        var dash = new DashboardViewModel(new FakeMonitor());
        var file = new SessionLogFile();
        var logs = new LogsViewModel(dash, file, () => ["Terminal 01", "Terminal 02"],
            bufferCapacity: bufferCapacity, copyToClipboard: copyToClipboard,
            logDir: exportDir, promptExportPath: promptExportPath);
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

    [AvaloniaFact]
    public async Task DeepBuffer_KeepsHistory_BeyondOutputCap()
    {
        var (dash, logs, _) = MakeLogs(); // default capacity 2000, Output caps at 500
        for (var i = 0; i < 520; i++)
            dash.AppendOutput("info", $"deep-{i:D4}", "Terminal 01");
        await Until(() => dash.OutputLog.Count == 500 && logs.Entries.Count == 520);

        Assert.Equal(500, dash.OutputLog.Count);
        Assert.Equal(520, logs.Entries.Count);
        Assert.Equal("deep-0000", logs.Entries[0].Message); // oldest line survived here
        Assert.Equal("deep-0519", logs.Entries[^1].Message);
    }

    [AvaloniaFact]
    public async Task BufferCapacity_IsConfigurable_AndTrimsOldest()
    {
        var (dash, logs, _) = MakeLogs(bufferCapacity: 5);
        for (var i = 0; i < 8; i++)
            dash.AppendOutput("info", $"l{i}", "Terminal 01");
        await Until(() => logs.Entries.Count == 5);

        Assert.Equal(5, logs.Entries.Count);
        Assert.Equal("l3", logs.Entries[0].Message); // l0–l2 evicted
        Assert.Equal("l7", logs.Entries[^1].Message);
    }

    [AvaloniaFact]
    public async Task TrimmedOutputLine_StillFilterable_FromLogsBuffer()
    {
        var (dash, logs, _) = MakeLogs();
        for (var i = 0; i < 520; i++)
            dash.AppendOutput("info", $"deep-{i:D4}", "Terminal 01");
        await Until(() => dash.OutputLog.Count == 500 && logs.Entries.Count == 520);
        // Output has dropped deep-0000 (it only holds deep-0020..deep-0519)…
        Assert.DoesNotContain(dash.OutputLog, e => e.Message == "deep-0000");
        // …but the Logs filter can still find it by replaying its own buffer.
        logs.FilterText = "deep-0000";
        Assert.Single(logs.Entries);
        Assert.Equal("deep-0000", logs.Entries[0].Message);
    }

    [AvaloniaFact]
    public async Task OutputClear_Default_FollowsClear_AndDropsHistory()
    {
        var (dash, logs, _) = MakeLogs();
        for (var i = 0; i < 3; i++)
            dash.AppendOutput("info", $"line {i}", "Terminal 01");
        await Until(() => logs.Entries.Count == 3);

        dash.ClearOutputCommand.Execute(null);
        Assert.Empty(logs.Entries);

        dash.AppendOutput("info", "after clear", "Terminal 01");
        await Until(() => logs.Entries.Count == 1);
        logs.FilterText = "zzz";
        logs.FilterText = ""; // replay must not resurrect the pre-clear lines
        Assert.Single(logs.Entries);
        Assert.Equal("after clear", logs.Entries[0].Message);
    }

    [AvaloniaFact]
    public async Task OutputClear_WithRetainHistoryToggle_KeepsBuffer()
    {
        var (dash, logs, _) = MakeLogs();
        logs.RetainHistoryOnClear = true;
        for (var i = 0; i < 3; i++)
            dash.AppendOutput("info", $"line {i}", "Terminal 01");
        await Until(() => logs.Entries.Count == 3);

        dash.ClearOutputCommand.Execute(null);
        Assert.Equal(3, logs.Entries.Count); // retained

        logs.FilterText = "zzz";
        Assert.Empty(logs.Entries);
        logs.FilterText = "";
        Assert.Equal(3, logs.Entries.Count); // replay from retained buffer

        dash.AppendOutput("info", "post-clear line", "Terminal 01");
        await Until(() => logs.Entries.Count == 4);
    }

    [AvaloniaFact]
    public async Task RegexFilter_Matches_And_BadPatternShowsErrorWithoutCrash()
    {
        var (dash, logs, _) = MakeLogs();
        dash.AppendOutput("info", "GET /a 200", "Terminal 01");
        dash.AppendOutput("info", "GET /b 404", "Terminal 01");
        await Until(() => logs.Entries.Count == 2);

        logs.UseRegex = true;
        logs.FilterText = "404$";
        Assert.Single(logs.Entries);
        Assert.Equal("GET /b 404", logs.Entries[0].Message);

        // Broken pattern: no crash, visible hint, nothing matches until fixed.
        logs.FilterText = "([unclosed";
        Assert.NotEmpty(logs.FilterError);
        Assert.Empty(logs.Entries);

        logs.FilterText = "G.T";
        Assert.Empty(logs.FilterError);
        Assert.Equal(2, logs.Entries.Count);

        // Toggle off → literal substring semantics again.
        logs.UseRegex = false;
        logs.FilterText = "404$";
        Assert.Empty(logs.Entries);
        logs.FilterText = "404";
        Assert.Single(logs.Entries);
    }

    [AvaloniaFact]
    public async Task ClearVisible_RemovesMatches_FromBufferAndView()
    {
        var (dash, logs, _) = MakeLogs();
        for (var i = 0; i < 3; i++)
            dash.AppendOutput("info", $"fine {i}", "Terminal 01");
        dash.AppendOutput("warn", "careful 1", "Terminal 02");
        dash.AppendOutput("warn", "careful 2", "Terminal 02");
        await Until(() => logs.Entries.Count == 5);

        logs.LevelFilterIndex = 2; // warn only
        Assert.Equal(2, logs.Entries.Count);
        logs.ClearVisibleCommand.Execute(null);
        Assert.Empty(logs.Entries);
        Assert.Contains("已清空 2 行", logs.StatusText);

        logs.LevelFilterIndex = 0; // warns are gone from the buffer, infos remain
        Assert.Equal(3, logs.Entries.Count);
        Assert.All(logs.Entries, e => Assert.Equal("info", e.Level));
    }

    [AvaloniaFact]
    public async Task CopyVisible_FormatsAllFields_AndReportsStatus()
    {
        var captured = new List<string>();
        var (dash, logs, _) = MakeLogs(copyToClipboard: t => { captured.Add(t); return Task.CompletedTask; });
        dash.AppendOutput("info", "plain line", "Terminal 01");
        dash.AppendOutput("warn", "watch out", "Terminal 02");
        await Until(() => logs.Entries.Count == 2);

        await logs.CopyVisibleCommand.ExecuteAsync(null);
        var text = Assert.Single(captured);
        Assert.Contains("[info]", text);
        Assert.Contains("(Terminal 01) plain line", text);
        Assert.Contains("[warn]", text);
        Assert.Contains("(Terminal 02) watch out", text);
        Assert.Equal(2, text.Split('\n').Length);
        Assert.Contains("已复制 2 行", logs.StatusText);
    }

    [AvaloniaFact]
    public async Task FollowTail_DefaultsTrue_ScrollUpPauses_NewLinesDoNotSilentlyResume()
    {
        var (dash, logs, _) = MakeLogs();
        Assert.True(logs.FollowTail); // pinned to the newest line by default

        logs.UpdateFollowFromScroll(atBottom: false); // user scrolled away from the bottom
        Assert.False(logs.FollowTail);

        dash.AppendOutput("info", "paused 1", "Terminal 01");
        dash.AppendOutput("info", "paused 2", "Terminal 01");
        await Until(() => logs.Entries.Count == 2);
        Assert.False(logs.FollowTail); // incoming lines must not flip following back on

        logs.UpdateFollowFromScroll(atBottom: true); // scrolled back to the bottom
        Assert.True(logs.FollowTail);
    }

    [AvaloniaFact]
    public void ResumeFollowCommand_RestoresFollow()
    {
        var (dash, logs, _) = MakeLogs();
        logs.UpdateFollowFromScroll(atBottom: false);
        Assert.False(logs.FollowTail);

        logs.ResumeFollowCommand.Execute(null); // 「⬇ 跟随」 button
        Assert.True(logs.FollowTail);

        // Pausing again still works after a resume (button ↔ scroll cycle).
        logs.UpdateFollowFromScroll(atBottom: false);
        Assert.False(logs.FollowTail);
    }

    [AvaloniaFact]
    public async Task ExportVisible_WritesOnlyFilteredLines_ToDefaultDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "th-logs-" + Guid.NewGuid().ToString("N"));
        var (dash, logs, _) = MakeLogs(exportDir: dir); // dir intentionally not pre-created
        try
        {
            dash.AppendOutput("info", "keep me", "Terminal 01");
            dash.AppendOutput("info", "drop me", "Terminal 02");
            await Until(() => logs.Entries.Count == 2);
            logs.FilterText = "keep";
            Assert.Single(logs.Entries);

            await logs.ExportVisibleCommand.ExecuteAsync(null);
            Assert.Contains("已导出 1 行", logs.StatusText);
            Assert.Contains(dir, logs.StatusText);

            var path = Assert.Single(Directory.GetFiles(dir));
            Assert.EndsWith(".log", path);
            var content = File.ReadAllText(path);
            Assert.Contains("[info]", content);
            Assert.Contains("(Terminal 01) keep me", content);
            Assert.DoesNotContain("drop me", content); // export follows the active filter
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task ExportVisible_EmptyEntries_ReportsAndWritesNothing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "th-logs-" + Guid.NewGuid().ToString("N"));
        var (_, logs, _) = MakeLogs(exportDir: dir);

        await logs.ExportVisibleCommand.ExecuteAsync(null);
        Assert.Contains("没有可导出的行", logs.StatusText);
        Assert.False(Directory.Exists(dir));
    }

    [AvaloniaFact]
    public async Task ExportVisible_UsesPickerPath_AndCancelWritesNothing()
    {
        var (dash, logs, _) = MakeLogs();
        dash.AppendOutput("info", "export me", "Terminal 01");
        await Until(() => logs.Entries.Count == 1);

        // Dismissed dialog → no write, status notes the cancel.
        string? chosen = null;
        var pickerDash = new DashboardViewModel(new FakeMonitor());
        var pickerLogs = new LogsViewModel(pickerDash, new SessionLogFile(),
            () => [], promptExportPath: () => Task.FromResult(chosen));
        pickerDash.AppendOutput("info", "never written", "Terminal 01");
        await Until(() => pickerLogs.Entries.Count == 1);
        await pickerLogs.ExportVisibleCommand.ExecuteAsync(null);
        Assert.Contains("已取消导出", pickerLogs.StatusText);
        pickerLogs.Dispose();

        // Chosen path (.txt) → written there verbatim.
        var tmp = Path.Combine(Path.GetTempPath(), "th-export-" + Guid.NewGuid().ToString("N") + ".txt");
        var (dash2, logs2, _) = MakeLogs(promptExportPath: () => Task.FromResult<string?>(tmp));
        try
        {
            dash2.AppendOutput("info", "via picker", "Terminal 01");
            await Until(() => logs2.Entries.Count == 1);
            await logs2.ExportVisibleCommand.ExecuteAsync(null);
            Assert.Contains("已导出 1 行", logs2.StatusText);
            Assert.True(File.Exists(tmp));
            Assert.Contains("via picker", File.ReadAllText(tmp));
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [AvaloniaFact]
    public async Task ExportVisible_UI_EndToEnd_ButtonBinds_AndWritesThroughRealWindow()
    {
        // Full chain on the real MainWindow: XAML button → command → picker wiring
        // (headless: no dialog → default path) → file on disk. Avoids the shared
        // DISPLAY flakiness of live verification.
        PtySessionFactory.UseMock = true;
        var logsDir = Path.Combine(Path.GetDirectoryName(SettingsStore.DefaultPath())!, "logs");
        var before = Directory.Exists(logsDir)
            ? Directory.GetFiles(logsDir, "export-*.log").ToHashSet() : [];
        var window = new MainWindow { Width = 1200, Height = 800 };
        try
        {
            window.Show();
            await Task.Delay(400);
            var vm = (MainWindowViewModel)window.DataContext!;
            vm.SelectedRightTab = 2; // Logs
            await Task.Delay(150);

            // The ⬇ 导出 button in the real visual tree is bound to the export command.
            var exportBtn = window.GetVisualDescendants().OfType<Button>()
                .Single(b => ReferenceEquals(b.Command, vm.Logs.ExportVisibleCommand));
            Assert.NotNull(exportBtn);

            for (var i = 0; i < 5; i++)
                vm.Dashboard.AppendOutput("info", $"ui-export-{i}", "Terminal 01");
            await Until(() => vm.Logs.Entries.Count >= 5);
            vm.Logs.FilterText = "ui-export-"; // isolate our lines from any startup noise
            await Task.Delay(100);
            Assert.Equal(5, vm.Logs.Entries.Count);

            await vm.Logs.ExportVisibleCommand.ExecuteAsync(null);
            Assert.Contains("已导出 5 行", vm.Logs.StatusText);

            var created = Directory.GetFiles(logsDir, "export-*.log")
                .Where(f => !before.Contains(f)).ToList();
            var file = Assert.Single(created);
            try
            {
                Assert.EndsWith(".log", file);
                var content = File.ReadAllText(file);
                Assert.Contains("(Terminal 01) ui-export-4", content);
            }
            finally { File.Delete(file); }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task FollowTail_UI_EndToEnd_ButtonVisibility_And_Resume()
    {
        // Real window + real bindings. (The ScrollViewer's ScrollChanged doesn't fire
        // under the headless platform, so the scroll→pause wiring is live-verified on
        // a real X display; here we verify the parts the headless UI can prove: the
        // ⬇ 跟随 button exists, binds to ResumeFollowCommand, mirrors FollowPaused,
        // and the command restores FollowTail.)
        PtySessionFactory.UseMock = true;
        var window = new MainWindow { Width = 1200, Height = 800 };
        try
        {
            window.Show();
            await Task.Delay(400);
            var vm = (MainWindowViewModel)window.DataContext!;
            vm.SelectedRightTab = 2; // Logs
            await Task.Delay(150);

            var followBtn = window.GetVisualDescendants().OfType<Button>()
                .Single(b => ReferenceEquals(b.Command, vm.Logs.ResumeFollowCommand));

            Assert.True(vm.Logs.FollowTail);   // default: pinned to newest
            Assert.False(followBtn.IsVisible); // …so the resume button is hidden

            vm.Logs.UpdateFollowFromScroll(atBottom: false); // what a user scroll-up does
            Assert.False(vm.Logs.FollowTail);
            Assert.True(followBtn.IsVisible);  // paused → floating ⬇ 跟随 appears

            // Frames for docs: paused (button floating over the list) and resumed.
            var outDir = Path.Combine(AppContext.BaseDirectory, "ui-snapshots");
            Directory.CreateDirectory(outDir);
            window.CaptureRenderedFrame()?.Save(Path.Combine(outDir, "logs-follow-paused.png"));

            vm.Logs.ResumeFollowCommand.Execute(null); // clicking it
            Assert.True(vm.Logs.FollowTail);
            Assert.False(followBtn.IsVisible); // …hides again
            window.CaptureRenderedFrame()?.Save(Path.Combine(outDir, "logs-follow-on.png"));

            // Incoming lines don't disturb the follow state in either direction.
            vm.Logs.UpdateFollowFromScroll(atBottom: false);
            for (var i = 0; i < 3; i++)
                vm.Dashboard.AppendOutput("info", $"ui-follow-{i}", "Terminal 01");
            await Until(() => vm.Logs.Entries.Count >= 3);
            Assert.False(vm.Logs.FollowTail); // new lines never silently resume
        }
        finally
        {
            window.Close();
        }
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

    [AvaloniaFact]
    public async Task SessionNames_LiveUpdate_OnCreateRenameClose()
    {
        if (!OperatingSystem.IsLinux()) return; // spawns a real PTY session

        var dir = Path.Combine(Path.GetTempPath(), "th-logs-" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new FakeMonitor(),
            new SettingsStore(Path.Combine(dir, "settings.json")));
        try
        {
            await vm.SpawnStartupSessionsAsync(); // default settings spawn 3 startup sessions
            await Until(() => vm.SessionCards.Count == 3);
            Assert.Equal(4, vm.Logs.SessionNames.Count); // "全部会话" + 3 cards
            Assert.All(vm.SessionCards, c => Assert.Contains(c.Name, vm.Logs.SessionNames));

            var before = vm.SessionCards.Count;
            await vm.NewSessionCommand.ExecuteAsync(null); // new session → live-added
            await Until(() => vm.SessionCards.Count == before + 1);
            Assert.Equal(before + 2, vm.Logs.SessionNames.Count);
            var added = vm.SessionCards[^1]; // ObservableCollection appends → newest last
            Assert.Contains(added.Name, vm.Logs.SessionNames);

            vm.RenameSessionCommand.Execute((added, "Web 01"));
            Assert.Equal(before + 2, vm.Logs.SessionNames.Count); // rename keeps the count
            Assert.Single(vm.Logs.SessionNames, n => n == "Web 01"); // live swap to the new name
            Assert.Equal("Web 01", added.Name);

            vm.CloseSessionCommand.Execute(vm.SessionCards.First(c => c.Name == "Web 01"));
            Assert.Equal(before + 1, vm.Logs.SessionNames.Count); // renamed card removed
            Assert.DoesNotContain("Web 01", vm.Logs.SessionNames);
        }
        finally
        {
            vm.Dispose();
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
