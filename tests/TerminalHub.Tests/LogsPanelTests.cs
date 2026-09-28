using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using TerminalHub.App.Controls;
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
        Func<Task<string?>>? promptExportPath = null,
        Action? persistFilters = null)
    {
        var dash = new DashboardViewModel(new FakeMonitor());
        var file = new SessionLogFile();
        var logs = new LogsViewModel(dash, file, () => ["Terminal 01", "Terminal 02"],
            bufferCapacity: bufferCapacity, copyToClipboard: copyToClipboard,
            logDir: exportDir, promptExportPath: promptExportPath, persistFilters: persistFilters);
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
    public async Task LevelChips_Exclusive_AndStillFilter()
    {
        var (dash, logs, _) = MakeLogs();
        dash.AppendOutput("info", "fine", "Terminal 01");
        dash.AppendOutput("warn", "careful", "Terminal 02");
        await Until(() => logs.Entries.Count == 2);

        // Bar starts on「全部」; the other chips are off.
        Assert.True(logs.LevelAllSelected);
        Assert.False(logs.LevelWarnSelected);

        logs.LevelWarnSelected = true; // what a chip click does
        Assert.Equal(2, logs.LevelFilterIndex);
        Assert.False(logs.LevelAllSelected); // chip props follow the index → exclusive bar
        Assert.Single(logs.Entries);
        Assert.Equal("careful", logs.Entries[0].Message);

        // Clicking the checked chip cannot deselect it — exactly one chip stays on.
        logs.LevelWarnSelected = false;
        Assert.True(logs.LevelWarnSelected);
        Assert.Equal(2, logs.LevelFilterIndex);

        logs.LevelErrorSelected = true; // switching chips moves the single selection
        Assert.Equal(3, logs.LevelFilterIndex);
        Assert.False(logs.LevelWarnSelected);
        Assert.Empty(logs.Entries);
    }

    [AvaloniaFact]
    public async Task PersistedFilters_RestoreClamped_RestoreNeverSaves()
    {
        var saves = 0;
        var (dash, logs, _) = MakeLogs(persistFilters: () => saves++);
        dash.AppendOutput("error", "boom", "Terminal 01");
        await Until(() => logs.Entries.Count == 1);

        logs.ApplyPersistedFilters("boom", useRegex: true, levelFilterIndex: 9, retainHistoryOnClear: true);
        Assert.Equal("boom", logs.FilterText);
        Assert.True(logs.UseRegex);
        Assert.Equal(3, logs.LevelFilterIndex); // 9 clamps into the chip bar (error)
        Assert.True(logs.RetainHistoryOnClear);
        Assert.Equal(0, saves); // restoring is a pure load — it must not write back

        logs.LevelWarnSelected = true; // chip click → save
        Assert.Empty(logs.Entries);    // the error line no longer matches the warn chip
        logs.FilterText = "careful";   // text change → save
        Assert.Equal(2, saves);
        logs.LevelErrorSelected = true; // chip click back → save
        logs.FilterText = "boom";       // text change → save
        Assert.Equal(4, saves);
        Assert.Single(logs.Entries);
        Assert.Equal("boom", logs.Entries[0].Message); // filter state really applied
        await Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task FilterPersistence_SurvivesRestart_ViaSettingsStore()
    {
        var dir = Path.Combine(Path.GetTempPath(), "th-logs-" + Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(dir, "settings.json");
        try
        {
            // First run: filter changes land in AppSettings on disk immediately.
            var vm = new MainWindowViewModel(new FakeMonitor(), new SettingsStore(settingsPath));
            vm.Logs.FilterText = "error";
            vm.Logs.UseRegex = true;
            vm.Logs.LevelErrorSelected = true; // chip click persists the level too
            vm.Logs.RetainHistoryOnClear = true;

            var onDisk = new SettingsStore(settingsPath).Load();
            Assert.Equal("error", onDisk.LogsFilterText);
            Assert.True(onDisk.LogsUseRegex);
            Assert.Equal(3, onDisk.LogsLevelFilterIndex);
            Assert.True(onDisk.LogsRetainHistoryOnClear);
            vm.Dispose();

            // Second run: a fresh VM restores what the first run saved.
            var vm2 = new MainWindowViewModel(new FakeMonitor(), new SettingsStore(settingsPath));
            Assert.Equal("error", vm2.Logs.FilterText);
            Assert.True(vm2.Logs.UseRegex);
            Assert.Equal(3, vm2.Logs.LevelFilterIndex);
            Assert.True(vm2.Logs.LevelErrorSelected); // restored index lights the chip
            Assert.True(vm2.Logs.RetainHistoryOnClear);
            vm2.Dispose();

            // A stale/out-of-range saved index clamps into the bar instead of breaking startup.
            var store = new SettingsStore(settingsPath);
            var raw = store.Load();
            raw.LogsLevelFilterIndex = 42;
            store.Save(raw);
            var vm3 = new MainWindowViewModel(new FakeMonitor(), new SettingsStore(settingsPath));
            Assert.Equal(3, vm3.Logs.LevelFilterIndex);
            vm3.Dispose();
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    // ===== per-session filter memory（切换会话恢复各自筛选）=====

    [AvaloniaFact]
    public async Task SessionSwitch_RestoresEachSessions_FilterCombo()
    {
        var (dash, logs, _) = MakeLogs(); // SessionNames: 全部会话, Terminal 01, Terminal 02
        dash.AppendOutput("info", "t1 line", "Terminal 01");
        dash.AppendOutput("warn", "t2 line", "Terminal 02");
        await Until(() => logs.Entries.Count == 2);

        // Terminal 01 never configured → defaults, no restore hint.
        logs.SessionFilterIndex = 1;
        Assert.Equal("", logs.FilterText);
        Assert.False(logs.UseRegex);
        Assert.Equal(0, logs.LevelFilterIndex);
        Assert.False(logs.RetainHistoryOnClear);
        Assert.DoesNotContain("已恢复", logs.StatusText);

        logs.FilterText = "err";
        logs.UseRegex = true;
        logs.LevelErrorSelected = true;
        logs.RetainHistoryOnClear = true;

        // Terminal 02 also never configured → defaults (Terminal 01's combo hid away).
        logs.SessionFilterIndex = 2;
        Assert.Equal("", logs.FilterText);
        Assert.False(logs.UseRegex);
        Assert.Equal(0, logs.LevelFilterIndex);
        Assert.False(logs.RetainHistoryOnClear);
        Assert.Single(logs.Entries); // session criterion still filters while at it
        Assert.Equal("Terminal 02", logs.Entries[0].Source);

        logs.FilterText = "warn-ish";
        logs.LevelWarnSelected = true;

        // Back to Terminal 01 → its combo returns, with the brief hint.
        logs.SessionFilterIndex = 1;
        Assert.Equal("err", logs.FilterText);
        Assert.True(logs.UseRegex);
        Assert.Equal(3, logs.LevelFilterIndex);
        Assert.True(logs.RetainHistoryOnClear);
        Assert.Contains("已恢复「Terminal 01」筛选", logs.StatusText);

        // And Terminal 02 kept its own.
        logs.SessionFilterIndex = 2;
        Assert.Equal("warn-ish", logs.FilterText);
        Assert.Equal(2, logs.LevelFilterIndex);
        Assert.Contains("已恢复「Terminal 02」筛选", logs.StatusText);
        await Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task AllSessionsCombo_GlobalSlot_IndependentOfSessionCombos()
    {
        var (dash, logs, _) = MakeLogs();

        // 「全部会话」(index 0) owns the global fields.
        logs.FilterText = "global-text";
        logs.LevelWarnSelected = true;
        logs.RetainHistoryOnClear = true;

        logs.SessionFilterIndex = 1; // leaving 全部 → session defaults show
        Assert.Equal("", logs.FilterText);
        Assert.Equal(0, logs.LevelFilterIndex);
        Assert.False(logs.RetainHistoryOnClear);
        logs.FilterText = "t01-text";

        logs.SessionFilterIndex = 0; // back to 全部会话 → global combo restored
        Assert.Equal("global-text", logs.FilterText);
        Assert.Equal(2, logs.LevelFilterIndex);
        Assert.True(logs.RetainHistoryOnClear);

        logs.SessionFilterIndex = 1; // session combo untouched by the global one
        Assert.Equal("t01-text", logs.FilterText);
        await Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task SessionFilterMap_SnapshotAndApply_RoundTripsAcrossVMs()
    {
        var (_, logs, _) = MakeLogs();
        logs.SessionFilterIndex = 1;
        logs.FilterText = "alpha";
        logs.LevelInfoSelected = true;
        var snapshot = logs.SnapshotSessionFilters();
        var saved = snapshot["Terminal 01"];
        Assert.Equal("alpha", saved.FilterText);
        Assert.Equal(1, saved.LevelFilterIndex);

        // A fresh VM seeded with the snapshot restores it on switch (clone, not alias).
        var (_, logs2, _) = MakeLogs();
        logs2.ApplySessionFilterMap(snapshot);
        logs2.SessionFilterIndex = 1;
        Assert.Equal("alpha", logs2.FilterText);
        Assert.Equal(1, logs2.LevelFilterIndex);
        Assert.Contains("已恢复「Terminal 01」筛选", logs2.StatusText);
        // Corrupt entries (null state / empty name) are skipped, not thrown on.
        logs2.ApplySessionFilterMap(new Dictionary<string, LogsSessionFilterState>
        {
            [""] = new(),
            ["Terminal 02"] = null!, // what `"Terminal 02": null` in settings.json gives
        });
        logs2.SessionFilterIndex = 2; // → defaults
        Assert.Equal("", logs2.FilterText);
        await Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task SessionFilters_SurviveRestart_ViaSettingsStore()
    {
        PtySessionFactory.UseMock = true;
        var dir = Path.Combine(Path.GetTempPath(), "th-logs-" + Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(dir, "settings.json");
        try
        {
            // First run: global combo on 全部会话, a different combo on Terminal 01.
            var vm = new MainWindowViewModel(new FakeMonitor(), new SettingsStore(settingsPath));
            await vm.SpawnStartupSessionsAsync();
            await Until(() => vm.Logs.SessionNames.Count == 4); // 全部会话 + 3 startup cards
            Assert.Equal("Terminal 01", vm.Logs.SessionNames[1]);

            vm.Logs.FilterText = "global-combo"; // index 0 → global fields
            vm.Logs.SessionFilterIndex = 1;
            vm.Logs.FilterText = "t01-combo";
            vm.Logs.UseRegex = true;
            vm.Logs.LevelErrorSelected = true;
            vm.Logs.RetainHistoryOnClear = true;

            var onDisk = new SettingsStore(settingsPath).Load();
            Assert.Equal("global-combo", onDisk.LogsFilterText); // globals = 全部会话's combo
            Assert.Equal(["Terminal 01"], onDisk.LogsSessionFilters.Keys); // only the edited session
            var t01 = onDisk.LogsSessionFilters["Terminal 01"];
            Assert.Equal("t01-combo", t01.FilterText);
            Assert.True(t01.UseRegex);
            Assert.Equal(3, t01.LevelFilterIndex);
            Assert.True(t01.RetainHistoryOnClear);
            vm.Dispose();

            // Second run: globals restore at startup, Terminal 01's combo on switch.
            var vm2 = new MainWindowViewModel(new FakeMonitor(), new SettingsStore(settingsPath));
            await vm2.SpawnStartupSessionsAsync();
            await Until(() => vm2.Logs.SessionNames.Count == 4);
            Assert.Equal("global-combo", vm2.Logs.FilterText); // index 0 + globals
            vm2.Logs.SessionFilterIndex = 1;
            Assert.Equal("t01-combo", vm2.Logs.FilterText);
            Assert.True(vm2.Logs.UseRegex);
            Assert.True(vm2.Logs.LevelErrorSelected);
            Assert.True(vm2.Logs.RetainHistoryOnClear);
            vm2.Logs.SessionFilterIndex = 0;
            Assert.Equal("global-combo", vm2.Logs.FilterText); // 全部会话 slot intact
            vm2.Dispose();
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
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
            // Filter state persists to the shared settings.json now — leave it clean.
            if (window.DataContext is MainWindowViewModel vmCleanup)
            {
                vmCleanup.Logs.FilterText = "";
                vmCleanup.Logs.UseRegex = false;
                vmCleanup.Logs.LevelFilterIndex = 0;
            }
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
            // Filter state persists to the shared settings.json now — leave it clean.
            if (window.DataContext is MainWindowViewModel vmCleanup)
            {
                vmCleanup.Logs.FilterText = "";
                vmCleanup.Logs.UseRegex = false;
                vmCleanup.Logs.LevelFilterIndex = 0;
            }
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task LevelChips_UI_EndToEnd_ChipsReplaceLevelComboBox()
    {
        // Real window + real bindings: the level selector is a chip bar (levelchip
        // ToggleButtons driving LevelFilterIndex two-way), not a level ComboBox;
        // the session ComboBox survives next to it.
        PtySessionFactory.UseMock = true;
        var window = new MainWindow { Width = 1200, Height = 800 };
        try
        {
            window.Show();
            await Task.Delay(400);
            var vm = (MainWindowViewModel)window.DataContext!;
            vm.SelectedRightTab = 2; // Logs
            await Task.Delay(150);

            var chips = window.GetVisualDescendants().OfType<ToggleButton>()
                .Where(t => t.Classes.Contains("levelchip")).ToList();
            Assert.Equal(4, chips.Count);
            Assert.All(new[] { "全部", "info", "warn", "error" },
                label => Assert.Contains(chips, c => (string?)c.Content == label));

            // Session filter is still the ComboBox it always was.
            Assert.Single(window.GetVisualDescendants().OfType<ComboBox>(),
                c => ReferenceEquals(c.ItemsSource, vm.Logs.SessionNames));

            // Clicking a chip drives LevelFilterIndex through the two-way binding…
            for (var i = 0; i < 3; i++)
                vm.Dashboard.AppendOutput("info", $"chip-info-{i}", "Terminal 01");
            vm.Dashboard.AppendOutput("error", "chip-error", "Terminal 01");
            await Until(() => vm.Logs.Entries.Count >= 4);

            chips.Single(c => (string?)c.Content == "error").IsChecked = true;
            await Task.Delay(100);
            Assert.Equal(3, vm.Logs.LevelFilterIndex);
            Assert.True(chips.Single(c => (string?)c.Content == "error").IsChecked);
            Assert.False(chips.Single(c => (string?)c.Content == "全部").IsChecked);
            await Until(() => vm.Logs.Entries.Count == 1); // …and really filters the list
            Assert.Equal("chip-error", vm.Logs.Entries[0].Message);

            // Frame for docs: the chip bar with「error」selected, list filtered to it.
            for (var i = 0; i < 3; i++)
                vm.Dashboard.AppendOutput("error", $"chip-error-more-{i}", "Terminal 01");
            await Until(() => vm.Logs.Entries.Count == 4);
            var outDir = Path.Combine(AppContext.BaseDirectory, "ui-snapshots");
            Directory.CreateDirectory(outDir);
            window.CaptureRenderedFrame()?.Save(Path.Combine(outDir, "logs-level-chips.png"));
        }
        finally
        {
            // Filter state persists to the shared settings.json now — leave it clean.
            if (window.DataContext is MainWindowViewModel vmCleanup)
            {
                vmCleanup.Logs.FilterText = "";
                vmCleanup.Logs.UseRegex = false;
                vmCleanup.Logs.LevelFilterIndex = 0;
            }
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

    // ===== search highlight (HighlightTextBlock) +「上一条/下一条」match navigation =====

    /// <summary>A highlighted span: the same yellow-bold style the Search tab uses.</summary>
    private static bool IsMatchRun(Run r)
        => r.Foreground is ISolidColorBrush b
           && b.Color == Color.Parse("#FDE047") && r.FontWeight == FontWeight.Bold;

    private static List<Run> Runs(HighlightTextBlock h) => h.Inlines!.OfType<Run>().ToList();

    [AvaloniaFact]
    public void HighlightTextBlock_Literal_EveryCaseInsensitiveMatch_YellowBold()
    {
        var h = new HighlightTextBlock { LineText = "GET /api/get?get=1" };
        h.Query = "get";
        // The Search tab's default mode: plain literal, case-insensitive, all spans.
        var runs = Runs(h);
        Assert.Equal(new[] { "GET", " /api/", "get", "?", "get", "=1" },
            runs.Select(r => r.Text));
        Assert.True(IsMatchRun(runs[0]));   // GET  (case differs from query)
        Assert.False(IsMatchRun(runs[1]));  // separator stays plain
        Assert.True(IsMatchRun(runs[2]));
        Assert.False(IsMatchRun(runs[3]));
        Assert.True(IsMatchRun(runs[4]));
        Assert.False(IsMatchRun(runs[5]));

        // No query → one plain run; the whole text survives verbatim.
        h.Query = "";
        var plain = Assert.Single(Runs(h));
        Assert.Equal("GET /api/get?get=1", plain.Text);
        Assert.False(IsMatchRun(plain));
    }

    [AvaloniaFact]
    public void HighlightTextBlock_Regex_HighlightsMatches_PlainOnInvalidOrEmpty()
    {
        var h = new HighlightTextBlock { LineText = "GET /a 200 in 12ms" };
        h.UseRegex = true;
        h.Query = @"\d+";
        var runs = Runs(h);
        Assert.Equal(new[] { "GET /a ", "200", " in ", "12", "ms" }, runs.Select(r => r.Text));
        Assert.False(IsMatchRun(runs[0]));
        Assert.True(IsMatchRun(runs[1]));   // 200
        Assert.True(IsMatchRun(runs[3]));   // 12
        Assert.False(IsMatchRun(runs[4]));

        // Regex semantics really apply (literal mode would not match "\d" at all).
        h.Query = @"2\d\d";
        runs = Runs(h);
        var hit = Assert.Single(runs, IsMatchRun);
        Assert.Equal("200", hit.Text);

        // Broken pattern: never throws, renders the line plain (the VM matches nothing).
        h.Query = "([unclosed";
        runs = Runs(h);
        var plain = Assert.Single(runs);
        Assert.Equal("GET /a 200 in 12ms", plain.Text);
        Assert.False(IsMatchRun(plain));

        // Zero-width matches highlight nothing but keep the text intact.
        h.Query = "x*";
        plain = Assert.Single(Runs(h));
        Assert.Equal("GET /a 200 in 12ms", plain.Text);

        // Toggle back to literal: pattern chars are taken verbatim again.
        h.UseRegex = false;
        h.Query = "2\\d\\d"; // the literal backslashes, not the regex class
        plain = Assert.Single(Runs(h));
        Assert.Equal("GET /a 200 in 12ms", plain.Text);
        Assert.False(IsMatchRun(plain));
    }

    [AvaloniaFact]
    public async Task MatchNav_GoNextPrev_StepsThroughMatches_NoWrap_PausesFollow()
    {
        var (dash, logs, _) = MakeLogs();
        for (var i = 0; i < 5; i++)
            dash.AppendOutput("info", $"hit {i}", "Terminal 01");
        dash.AppendOutput("info", "unrelated", "Terminal 01");
        await Until(() => logs.Entries.Count == 6);

        logs.FilterText = "hit";
        Assert.Equal(5, logs.Entries.Count);
        Assert.Equal(-1, logs.SelectedIndex); // fresh filter → nothing selected
        Assert.False(logs.CanGoPrevMatch);    // nothing before the start
        Assert.True(logs.CanGoNextMatch);

        logs.GoNextMatchCommand.Execute(null); // first press selects the first match
        Assert.Equal(0, logs.SelectedIndex);
        Assert.Equal("hit 0", logs.SelectedEntry!.Message);
        Assert.False(logs.CanGoPrevMatch);    // at the top: no wrap
        Assert.True(logs.CanGoNextMatch);
        Assert.False(logs.FollowTail);        // navigating pauses tail-follow
        Assert.Contains("匹配 1/5", logs.StatusText);

        logs.GoNextMatchCommand.Execute(null);
        logs.GoNextMatchCommand.Execute(null); // → 2
        logs.GoPrevMatchCommand.Execute(null); // → 1
        Assert.Equal(1, logs.SelectedIndex);
        Assert.True(logs.CanGoPrevMatch);
        Assert.Contains("匹配 2/5", logs.StatusText);

        for (var i = 0; i < 10; i++) logs.GoNextMatchCommand.Execute(null);
        Assert.Equal(4, logs.SelectedIndex);  // clamped at the last match
        Assert.False(logs.CanGoNextMatch);    // at the bottom: no wrap
        Assert.True(logs.CanGoPrevMatch);

        for (var i = 0; i < 10; i++) logs.GoPrevMatchCommand.Execute(null);
        Assert.Equal(0, logs.SelectedIndex);  // clamped at the first match
        Assert.False(logs.CanGoPrevMatch);
    }

    [AvaloniaFact]
    public async Task MatchNav_Disabled_WhenNoFilter_NoMatches_OrBrokenRegex()
    {
        var (dash, logs, _) = MakeLogs();
        dash.AppendOutput("info", "alpha", "Terminal 01");
        await Until(() => logs.Entries.Count == 1);

        // No text filter → both nav buttons inert even with entries present.
        Assert.False(logs.CanGoPrevMatch);
        Assert.False(logs.CanGoNextMatch);
        logs.GoNextMatchCommand.Execute(null);
        Assert.Equal(-1, logs.SelectedIndex);
        Assert.True(logs.FollowTail);

        // Filter that matches nothing → no entries to navigate.
        logs.FilterText = "zzz";
        Assert.Empty(logs.Entries);
        Assert.False(logs.CanGoPrevMatch);
        Assert.False(logs.CanGoNextMatch);

        // Broken regex → filter matches nothing, nav stays disabled.
        logs.UseRegex = true;
        logs.FilterText = "([bad";
        Assert.NotEmpty(logs.FilterError);
        Assert.False(logs.CanGoPrevMatch);
        Assert.False(logs.CanGoNextMatch);

        // Repairing the pattern re-enables navigation over the matches.
        logs.FilterText = "al.*a";
        Assert.True(logs.CanGoNextMatch);
        logs.GoNextMatchCommand.Execute(null);
        Assert.Equal(0, logs.SelectedIndex);
        Assert.Equal("alpha", logs.SelectedEntry!.Message);
    }

    [AvaloniaFact]
    public async Task MatchNav_FilterChange_ResetsSelection_ClickStyleSelectionWorks()
    {
        var (dash, logs, _) = MakeLogs();
        for (var i = 0; i < 3; i++)
            dash.AppendOutput("info", $"hit {i}", "Terminal 01");
        await Until(() => logs.Entries.Count == 3);

        logs.FilterText = "hit";
        logs.GoNextMatchCommand.Execute(null);
        logs.GoNextMatchCommand.Execute(null);
        logs.GoNextMatchCommand.Execute(null);
        Assert.Equal(2, logs.SelectedIndex);

        // Refilter invalidates positions → selection restarts from「nothing selected」.
        logs.FilterText = "hit 2";
        Assert.Single(logs.Entries);
        Assert.Equal(-1, logs.SelectedIndex);
        Assert.False(logs.CanGoPrevMatch);

        // A plain list click (two-way SelectedIndex) keeps the button states honest.
        logs.FilterText = "hit";
        logs.SelectedIndex = 2;
        Assert.Equal("hit 2", logs.SelectedEntry!.Message);
        Assert.True(logs.CanGoPrevMatch);
        Assert.False(logs.CanGoNextMatch); // clicked the last row
        logs.SelectedIndex = 0;
        Assert.False(logs.CanGoPrevMatch);
        Assert.True(logs.CanGoNextMatch);

        // Clearing the filter drops back to no-selection, both disabled.
        logs.FilterText = "";
        Assert.Equal(3, logs.Entries.Count);
        Assert.Equal(-1, logs.SelectedIndex);
        Assert.False(logs.CanGoPrevMatch);
        Assert.False(logs.CanGoNextMatch);
    }

    [AvaloniaFact]
    public async Task MatchNav_SelectionTracksEviction_KeepsSameEntry()
    {
        // Live churn must not strand the selection: buffer eviction shifts the index
        // so the same log line stays selected; losing it entirely deselects cleanly.
        var (dash, logs, _) = MakeLogs(bufferCapacity: 5);
        for (var i = 0; i < 5; i++)
            dash.AppendOutput("info", $"l{i}", "Terminal 01");
        await Until(() => logs.Entries.Count == 5);
        logs.FilterText = "l";

        for (var i = 0; i < 5; i++) logs.GoNextMatchCommand.Execute(null); // -1 → 0..4
        Assert.Equal(4, logs.SelectedIndex);
        Assert.Equal("l4", logs.SelectedEntry!.Message);

        dash.AppendOutput("info", "l5", "Terminal 01");
        dash.AppendOutput("info", "l6", "Terminal 01");
        await Until(() => logs.Entries.Count == 5 && logs.Entries[^1].Message == "l6");
        Assert.Equal(2, logs.SelectedIndex);       // l0/l1 evicted → index shifted down
        Assert.Equal("l4", logs.SelectedEntry!.Message); // …same line still selected
        Assert.True(logs.CanGoPrevMatch);
        Assert.True(logs.CanGoNextMatch);
    }

    [AvaloniaFact]
    public async Task SearchHighlight_UI_EndToEnd_RowsHighlight_NavButtonsBind()
    {
        // Full chain on the real MainWindow: rows re-render with yellow-bold spans
        // for the active filter (literal and regex), the ▲/▼ buttons bind to the
        // nav commands with honest IsEnabled, and selection reaches the real ListBox.
        PtySessionFactory.UseMock = true;
        var window = new MainWindow { Width = 1200, Height = 800 };
        try
        {
            window.Show();
            await Task.Delay(400);
            var vm = (MainWindowViewModel)window.DataContext!;
            vm.SelectedRightTab = 2; // Logs
            await Task.Delay(150);

            for (var i = 0; i < 6; i++)
                vm.Dashboard.AppendOutput("info", $"needle line {i}", "Terminal 01");
            vm.Dashboard.AppendOutput("info", "unrelated noise", "Terminal 01");
            await Until(() => vm.Logs.Entries.Count >= 7);

            var prevBtn = window.GetVisualDescendants().OfType<Button>()
                .Single(b => ReferenceEquals(b.Command, vm.Logs.GoPrevMatchCommand));
            var nextBtn = window.GetVisualDescendants().OfType<Button>()
                .Single(b => ReferenceEquals(b.Command, vm.Logs.GoNextMatchCommand));
            var list = window.GetVisualDescendants().OfType<ListBox>()
                .Single(l => ReferenceEquals(l.ItemsSource, vm.Logs.Entries));

            vm.Logs.FilterText = "needle";
            await Until(() => vm.Logs.Entries.Count == 6);
            await Task.Delay(150); // row bindings deliver Query/UseRegex
            Assert.False(prevBtn.IsEnabled); // nothing selected yet
            Assert.True(nextBtn.IsEnabled);

            // Every visible Message row highlights the matched span, source stays plain.
            var rows = window.GetVisualDescendants().OfType<HighlightTextBlock>()
                .Where(h => h.LineText!.Contains("needle")).ToList();
            Assert.Equal(6, rows.Count);
            Assert.All(rows, h =>
            {
                var runs = Runs(h);
                Assert.Equal(new[] { "needle", h.LineText!["needle".Length..] }, // " line {i}"
                    runs.Select(r => r.Text));
                Assert.True(IsMatchRun(runs[0]));
                Assert.False(IsMatchRun(runs[1]));
            });

            // ▼ 下一条: selects through the two-way binding, pauses follow-tail.
            nextBtn.Command!.Execute(null);
            await Task.Delay(100);
            Assert.Equal(0, vm.Logs.SelectedIndex);
            Assert.Equal(0, list.SelectedIndex); // reached the real ListBox
            Assert.False(vm.Logs.FollowTail);
            Assert.False(prevBtn.IsEnabled);      // still on the first match
            nextBtn.Command.Execute(null);
            await Task.Delay(100);
            Assert.Equal(1, vm.Logs.SelectedIndex);
            Assert.True(prevBtn.IsEnabled);

            // Frame for docs: filtered rows with yellow spans + selected match.
            var outDir = Path.Combine(AppContext.BaseDirectory, "ui-snapshots");
            Directory.CreateDirectory(outDir);
            window.CaptureRenderedFrame()?.Save(Path.Combine(outDir, "logs-search-highlight.png"));

            // Regex mode: the whole match (needle line N) becomes one span.
            vm.Logs.UseRegex = true;
            vm.Logs.FilterText = @"needle line \d";
            await Until(() => vm.Logs.Entries.Count == 6);
            await Task.Delay(150);
            rows = window.GetVisualDescendants().OfType<HighlightTextBlock>()
                .Where(h => h.LineText!.Contains("needle")).ToList();
            Assert.Equal(6, rows.Count);
            Assert.All(rows, h =>
            {
                var run = Assert.Single(Runs(h));
                Assert.Equal(h.LineText, run.Text);
                Assert.True(IsMatchRun(run));
            });

            // Broken pattern: nothing matches (no rows to mis-highlight), no crash, nav disabled.
            // The invalid-pattern-renders-plain guarantee itself is covered by the
            // HighlightTextBlock unit tests above.
            vm.Logs.FilterText = "([oops";
            await Until(() => vm.Logs.Entries.Count == 0);
            Assert.NotEmpty(vm.Logs.FilterError);
            Assert.False(nextBtn.IsEnabled);
        }
        finally
        {
            // Filter state persists to the shared settings.json now — leave it clean.
            if (window.DataContext is MainWindowViewModel vmCleanup)
            {
                vmCleanup.Logs.FilterText = "";
                vmCleanup.Logs.UseRegex = false;
                vmCleanup.Logs.LevelFilterIndex = 0;
            }
            window.Close();
        }
    }
}
