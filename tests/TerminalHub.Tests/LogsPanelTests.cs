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
        Action? persistFilters = null,
        Func<string, bool>? activateSession = null)
    {
        var dash = new DashboardViewModel(new FakeMonitor());
        var file = new SessionLogFile();
        var logs = new LogsViewModel(dash, file, () => ["Terminal 01", "Terminal 02"],
            bufferCapacity: bufferCapacity, copyToClipboard: copyToClipboard,
            logDir: exportDir, promptExportPath: promptExportPath, persistFilters: persistFilters,
            activateSession: activateSession);
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
            await Task.CompletedTask;
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
    public async Task CopySelected_CopiesOnlySelected_FormatLineAbsolute()
    {
        var captured = new List<string>();
        var (dash, logs, _) = MakeLogs(copyToClipboard: t => { captured.Add(t); return Task.CompletedTask; });
        dash.AppendOutput("info", "plain line", "Terminal 01");
        dash.AppendOutput("warn", "watch out", "Terminal 02");
        await Until(() => logs.Entries.Count == 2);

        Assert.False(logs.HasSelectedEntry);
        logs.SelectedIndex = 1;
        Assert.True(logs.HasSelectedEntry);

        await logs.CopySelectedCommand.ExecuteAsync(null);
        var text = Assert.Single(captured);
        Assert.Equal(LogsViewModel.FormatLine(logs.Entries[1]), text);
        Assert.Contains("[warn]", text);
        Assert.Contains("(Terminal 02) watch out", text);
        Assert.DoesNotContain("plain line", text);
        Assert.Equal("已复制选中行", logs.StatusText);
    }

    [AvaloniaFact]
    public async Task CopySelected_SoftFails_WhenNoneSelected_OrNoClipboard()
    {
        // No selection → status note, clipboard untouched.
        var captured = new List<string>();
        var (dash, logs, _) = MakeLogs(copyToClipboard: t => { captured.Add(t); return Task.CompletedTask; });
        dash.AppendOutput("info", "alone", "Terminal 01");
        await Until(() => logs.Entries.Count == 1);

        Assert.Equal(-1, logs.SelectedIndex);
        await logs.CopySelectedCommand.ExecuteAsync(null);
        Assert.Empty(captured);
        Assert.Equal("没有选中的日志行", logs.StatusText);

        // Clipboard hook missing → soft fail after a valid selection.
        var (dash2, logs2, _) = MakeLogs(); // copyToClipboard: null
        dash2.AppendOutput("warn", "need clip", "Terminal 02");
        await Until(() => logs2.Entries.Count == 1);
        logs2.SelectedIndex = 0;
        await logs2.CopySelectedCommand.ExecuteAsync(null);
        Assert.Equal("剪贴板不可用", logs2.StatusText);
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
            // Captions carry live ring-buffer counts ("全部 0" / "info 0" …).
            Assert.All(new[] { "全部", "info", "warn", "error" },
                label => Assert.Contains(chips, c => ((string?)c.Content)?.StartsWith(label) == true));

            // Session filter is still the ComboBox it always was.
            Assert.Single(window.GetVisualDescendants().OfType<ComboBox>(),
                c => ReferenceEquals(c.ItemsSource, vm.Logs.SessionNames));

            // Clicking a chip drives LevelFilterIndex through the two-way binding…
            for (var i = 0; i < 3; i++)
                vm.Dashboard.AppendOutput("info", $"chip-info-{i}", "Terminal 01");
            vm.Dashboard.AppendOutput("error", "chip-error", "Terminal 01");
            await Until(() => vm.Logs.Entries.Count >= 4);
            await Until(() => vm.Logs.LevelAllCount >= 4);

            ToggleButton Chip(string prefix) =>
                chips.Single(c => ((string?)c.Content)?.StartsWith(prefix) == true);

            Chip("error").IsChecked = true;
            await Task.Delay(100);
            Assert.Equal(3, vm.Logs.LevelFilterIndex);
            Assert.True(Chip("error").IsChecked);
            Assert.False(Chip("全部").IsChecked);
            await Until(() => vm.Logs.Entries.Count == 1); // …and really filters the list
            Assert.Equal("chip-error", vm.Logs.Entries[0].Message);
            // Counts stay on the ring buffer — still see info:3 while viewing error.
            Assert.Equal(4, vm.Logs.LevelAllCount);
            Assert.Equal(3, vm.Logs.LevelInfoCount);
            Assert.Equal(1, vm.Logs.LevelErrorCount);
            Assert.Equal("全部 4", vm.Logs.LevelAllChipLabel);
            Assert.Equal("info 3", vm.Logs.LevelInfoChipLabel);
            Assert.Equal("error 1", vm.Logs.LevelErrorChipLabel);

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

            file = new SessionLogFile();
            path = file.Enable(dir);
            file.Write("Terminal 01", "info", "中文");
            var counted = (long)typeof(SessionLogFile).GetField("_writtenBytes",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(file)!;
            Assert.Equal(new FileInfo(path).Length, counted);
            file.Disable();
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
            vm.InspectorVisible = true;
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

    [AvaloniaFact]
    public async Task JumpToSession_ActivatesKnownSource_StatusNotes()
    {
        string? activated = null;
        var (dash, logs, _) = MakeLogs(activateSession: name =>
        {
            activated = name;
            return name is "Terminal 01" or "Terminal 02";
        });
        dash.AppendOutput("info", "from session", "Terminal 02");
        dash.AppendOutput("info", "system line", "deploy"); // not a session card
        await Until(() => logs.Entries.Count == 2);

        logs.SelectedIndex = 0;
        logs.JumpToSessionCommand.Execute(null);
        Assert.Equal("Terminal 02", activated);
        Assert.Equal("已跳到「Terminal 02」", logs.StatusText);

        // Direct entry (double-click path) — works without changing SelectedIndex.
        activated = null;
        logs.JumpToSessionEntry(logs.Entries[0]);
        Assert.Equal("Terminal 02", activated);
        Assert.Equal("已跳到「Terminal 02」", logs.StatusText);
    }

    [AvaloniaFact]
    public async Task JumpToSession_MissingOrUnknownSource_StatusOnly_NoCrash()
    {
        var calls = 0;
        var (dash, logs, _) = MakeLogs(activateSession: _ => { calls++; return false; });
        dash.AppendOutput("info", "orphan", "");          // empty Source
        dash.AppendOutput("info", "ghost", "Terminal 99"); // unknown session
        await Until(() => logs.Entries.Count == 2);

        logs.JumpToSessionEntry(logs.Entries[0]);
        Assert.Equal(0, calls); // never asked the host for an empty source
        Assert.Equal("该行没有会话来源", logs.StatusText);

        logs.SelectedIndex = 1;
        logs.JumpToSessionCommand.Execute(null);
        Assert.Equal(1, calls);
        Assert.Equal("未找到会话「Terminal 99」", logs.StatusText);

        logs.JumpToSessionEntry(null);
        Assert.Equal("没有选中的日志行", logs.StatusText);

        // No host hook → soft status, still no crash.
        var (dash2, logs2, _) = MakeLogs(); // activateSession: null
        dash2.AppendOutput("info", "x", "Terminal 01");
        await Until(() => logs2.Entries.Count == 1);
        logs2.JumpToSessionEntry(logs2.Entries[0]);
        Assert.Equal("无法跳到「Terminal 01」", logs2.StatusText);
    }

    [AvaloniaFact]
    public async Task JumpToSession_UI_KeepsLogsTab_ActivatesMatchingCard()
    {
        // Full MainWindow: jump activates the matching SessionCard / ActiveSession
        // while SelectedRightTab stays on Logs (2). Button is wired to the command.
        PtySessionFactory.UseMock = true;
        var dir = Path.Combine(Path.GetTempPath(), "th-jump-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new SettingsStore(Path.Combine(dir, "settings.json"));
            store.Save(new AppSettings
            {
                StartupSessions =
                [
                    new StartupSession { Name = "Terminal 01", Tag = "dev" },
                    new StartupSession { Name = "Terminal 03", Tag = "test" },
                ],
            });
            var window = new MainWindow(store) { Width = 1200, Height = 800 };
            try
            {
                window.Show();
                await Task.Delay(500);
                var vm = (MainWindowViewModel)window.DataContext!;
                await Until(() => vm.SessionCards.Count >= 2);

                // Start on Terminal 01; Logs tab open.
                var card01 = Assert.Single(vm.SessionCards, c => c.Name == "Terminal 01");
                var card03 = Assert.Single(vm.SessionCards, c => c.Name == "Terminal 03");
                vm.ActivateSessionCommand.Execute(card01);
                await Until(() => ReferenceEquals(vm.ActiveSession, card01.Model));
                vm.SelectedRightTab = 2;
                Assert.Equal(2, vm.SelectedRightTab);

                vm.Dashboard.AppendOutput("warn", "boom on 03", "Terminal 03");
                vm.Dashboard.AppendOutput("info", "noise", "deploy");
                await Until(() => vm.Logs.Entries.Count >= 2);

                var jumpBtn = window.GetVisualDescendants().OfType<Button>()
                    .Single(b => ReferenceEquals(b.Command, vm.Logs.JumpToSessionCommand));
                Assert.Equal("↗ 跳到会话", jumpBtn.Content);

                // Select the Terminal 03 row and jump via the command (button / Enter).
                vm.Logs.SelectedIndex = vm.Logs.Entries.ToList()
                    .FindIndex(e => e.Source == "Terminal 03");
                Assert.True(vm.Logs.SelectedIndex >= 0);
                jumpBtn.Command!.Execute(null);
                await Until(() => ReferenceEquals(vm.ActiveSession, card03.Model));

                Assert.Equal(2, vm.SelectedRightTab); // Logs still open
                Assert.True(card03.IsActive);
                Assert.False(card01.IsActive);
                Assert.Equal("已跳到「Terminal 03」", vm.Logs.StatusText);

                // Unknown source → status note; active session unchanged.
                vm.Logs.JumpToSessionEntry(vm.Logs.Entries.First(e => e.Source == "deploy"));
                Assert.Equal("未找到会话「deploy」", vm.Logs.StatusText);
                Assert.True(ReferenceEquals(vm.ActiveSession, card03.Model));
                Assert.Equal(2, vm.SelectedRightTab);
            }
            finally { window.Close(); }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    // ===== relative / absolute timestamp toggle =====

    [Fact]
    public void LogTimestampFormatter_Absolute_AndRelative_AgoFromNow()
    {
        var now = new DateTime(2026, 9, 28, 17, 30, 0);

        // Absolute mode always HH:mm:ss (export + default display).
        Assert.Equal("17:30:00", LogTimestampFormatter.Format(now, relative: false, now));
        Assert.Equal("09:05:07", LogTimestampFormatter.FormatAbsolute(new DateTime(2026, 1, 1, 9, 5, 7)));

        // Relative: ago from now.
        Assert.Equal("刚刚", LogTimestampFormatter.Format(now, relative: true, now));
        Assert.Equal("刚刚", LogTimestampFormatter.Format(now.AddSeconds(-1), relative: true, now));
        Assert.Equal("12s", LogTimestampFormatter.Format(now.AddSeconds(-12), relative: true, now));
        Assert.Equal("3m", LogTimestampFormatter.Format(now.AddMinutes(-3), relative: true, now));
        Assert.Equal("1h", LogTimestampFormatter.Format(now.AddHours(-1), relative: true, now));
        Assert.Equal("23h", LogTimestampFormatter.Format(now.AddHours(-23), relative: true, now));

        // Yesterday (calendar) → 昨天 HH:mm
        var yesterday = new DateTime(2026, 9, 27, 14, 22, 0);
        Assert.Equal("昨天 14:22", LogTimestampFormatter.Format(yesterday, relative: true, now));

        // Older than yesterday → fall back to absolute.
        var older = new DateTime(2026, 9, 26, 8, 0, 0);
        Assert.Equal("08:00:00", LogTimestampFormatter.Format(older, relative: true, now));

        // Future / clock skew → 刚刚 (clamped), never throws.
        Assert.Equal("刚刚", LogTimestampFormatter.Format(now.AddMinutes(5), relative: true, now));

        // Weird / default Time → soft absolute fallback, no crash.
        Assert.Equal("00:00:00", LogTimestampFormatter.Format(default, relative: true, now));
        Assert.Equal("00:00:00", LogTimestampFormatter.FormatAbsolute(DateTime.MinValue));
    }

    [AvaloniaFact]
    public async Task RelativeTimestamps_Toggle_DoesNotAffectExportAbsolute()
    {
        var dir = Path.Combine(Path.GetTempPath(), "th-logs-" + Guid.NewGuid().ToString("N"));
        var (dash, logs, _) = MakeLogs(exportDir: dir);
        try
        {
            var fixedTime = DateTime.Now.AddMinutes(-3);
            // Inject via AppendOutput (Time = DateTime.Now) then rely on FormatLine absolute.
            dash.AppendOutput("info", "rel-export", "Terminal 01");
            await Until(() => logs.Entries.Count == 1);

            logs.UseRelativeTimestamps = true;
            Assert.True(logs.UseRelativeTimestamps);
            // Display helper follows the toggle; FormatLine / export stay absolute.
            var display = logs.FormatDisplayTime(logs.Entries[0].Time, DateTime.Now);
            Assert.False(string.IsNullOrEmpty(display));
            var line = LogsViewModel.FormatLine(logs.Entries[0]);
            Assert.Matches(@"^\d{2}:\d{2}:\d{2} \[info\] \(Terminal 01\) rel-export$", line);
            Assert.DoesNotContain("刚刚", line);
            Assert.DoesNotContain("m ", line + " "); // no "3m " style in export

            await logs.ExportVisibleCommand.ExecuteAsync(null);
            var path = Assert.Single(Directory.GetFiles(dir));
            var content = File.ReadAllText(path);
            Assert.Contains("[info] (Terminal 01) rel-export", content);
            Assert.Matches(@"\d{2}:\d{2}:\d{2}", content.Split('\n')[0]);
            Assert.DoesNotContain("刚刚", content);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task RelativeTimestamps_Preference_SurvivesRestart_ViaSettingsStore()
    {
        var dir = Path.Combine(Path.GetTempPath(), "th-logs-" + Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(dir, "settings.json");
        try
        {
            var vm = new MainWindowViewModel(new FakeMonitor(), new SettingsStore(settingsPath));
            Assert.False(vm.Logs.UseRelativeTimestamps); // default absolute
            vm.Logs.UseRelativeTimestamps = true;

            var onDisk = new SettingsStore(settingsPath).Load();
            Assert.True(onDisk.LogsUseRelativeTimestamps);
            vm.Dispose();

            var vm2 = new MainWindowViewModel(new FakeMonitor(), new SettingsStore(settingsPath));
            Assert.True(vm2.Logs.UseRelativeTimestamps);
            // Restoring must not write back on its own — flip off and confirm save.
            var savesBefore = File.ReadAllText(settingsPath);
            vm2.Logs.UseRelativeTimestamps = false;
            var onDisk2 = new SettingsStore(settingsPath).Load();
            Assert.False(onDisk2.LogsUseRelativeTimestamps);
            Assert.NotEqual(savesBefore, File.ReadAllText(settingsPath));
            vm2.Dispose();
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        await Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task RelativeTimestamps_UI_ToggleBinds_AndListShowsRelativeLabel()
    {
        PtySessionFactory.UseMock = true;
        var window = new MainWindow { Width = 1200, Height = 800 };
        try
        {
            window.Show();
            await Task.Delay(400);
            var vm = (MainWindowViewModel)window.DataContext!;
            vm.InspectorVisible = true;
            vm.SelectedRightTab = 2;
            await Task.Delay(150);

            var toggle = window.GetVisualDescendants().OfType<ToggleButton>()
                .Single(t => (string?)t.Content == "相对");
            Assert.False(toggle.IsChecked);
            Assert.False(vm.Logs.UseRelativeTimestamps);

            vm.Dashboard.AppendOutput("info", "ts-row", "Terminal 01");
            await Until(() => vm.Logs.Entries.Count >= 1);

            toggle.IsChecked = true;
            await Task.Delay(150);
            Assert.True(vm.Logs.UseRelativeTimestamps);
            Assert.True(toggle.IsChecked);

            // VM display helper follows the toggle; list MultiBinding should show a relative label.
            var shown = vm.Logs.FormatDisplayTime(vm.Logs.Entries[0].Time, DateTime.Now);
            Assert.True(shown is "刚刚"
                || System.Text.RegularExpressions.Regex.IsMatch(shown, @"^\d+[smh]$")
                || shown.StartsWith("昨天 "));

            await Task.Delay(100);
            var labels = window.GetVisualDescendants().OfType<TextBlock>()
                .Select(t => t.Text)
                .Where(t => t is "刚刚"
                    || (t is not null && (
                        System.Text.RegularExpressions.Regex.IsMatch(t, @"^\d+[smh]$")
                        || t.StartsWith("昨天 "))))
                .ToList();
            Assert.NotEmpty(labels);

            var outDir = Path.Combine(AppContext.BaseDirectory, "ui-snapshots");
            Directory.CreateDirectory(outDir);
            window.CaptureRenderedFrame()?.Save(Path.Combine(outDir, "logs-timestamp-relative.png"));

            toggle.IsChecked = false;
            await Task.Delay(100);
            Assert.False(vm.Logs.UseRelativeTimestamps);
        }
        finally
        {
            if (window.DataContext is MainWindowViewModel vmCleanup)
            {
                vmCleanup.Logs.FilterText = "";
                vmCleanup.Logs.UseRegex = false;
                vmCleanup.Logs.LevelFilterIndex = 0;
                vmCleanup.Logs.UseRelativeTimestamps = false;
            }
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task WrapLines_Preference_SurvivesRestart_ViaSettingsStore()
    {
        var dir = Path.Combine(Path.GetTempPath(), "th-logs-" + Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(dir, "settings.json");
        try
        {
            var vm = new MainWindowViewModel(new FakeMonitor(), new SettingsStore(settingsPath));
            Assert.True(vm.Logs.WrapLines); // default wrap on
            vm.Logs.WrapLines = false;

            var onDisk = new SettingsStore(settingsPath).Load();
            Assert.False(onDisk.LogsWrapLines);
            vm.Dispose();

            var vm2 = new MainWindowViewModel(new FakeMonitor(), new SettingsStore(settingsPath));
            Assert.False(vm2.Logs.WrapLines);
            // Restoring must not write back on its own — flip on and confirm save.
            var savesBefore = File.ReadAllText(settingsPath);
            vm2.Logs.WrapLines = true;
            var onDisk2 = new SettingsStore(settingsPath).Load();
            Assert.True(onDisk2.LogsWrapLines);
            Assert.NotEqual(savesBefore, File.ReadAllText(settingsPath));
            vm2.Dispose();
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        await Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task WrapLines_UI_ToggleBinds_DefaultOn()
    {
        PtySessionFactory.UseMock = true;
        var window = new MainWindow { Width = 1200, Height = 800 };
        try
        {
            window.Show();
            await Task.Delay(400);
            var vm = (MainWindowViewModel)window.DataContext!;
            vm.SelectedRightTab = 2;
            await Task.Delay(150);

            var toggle = window.GetVisualDescendants().OfType<ToggleButton>()
                .Single(t => (string?)t.Content == "换行");
            Assert.True(toggle.IsChecked);
            Assert.True(vm.Logs.WrapLines);

            toggle.IsChecked = false;
            await Task.Delay(150);
            Assert.False(vm.Logs.WrapLines);
            Assert.False(toggle.IsChecked);

            var outDir = Path.Combine(AppContext.BaseDirectory, "ui-snapshots");
            Directory.CreateDirectory(outDir);
            // Capture with wrap off so the chip is unchecked — then flip back for the
            // docs screenshot which wants the chip visible/on (default).
            window.CaptureRenderedFrame()?.Save(Path.Combine(outDir, "logs-wrap-toggle-off.png"));

            toggle.IsChecked = true;
            await Task.Delay(100);
            Assert.True(vm.Logs.WrapLines);
            window.CaptureRenderedFrame()?.Save(Path.Combine(outDir, "logs-wrap-toggle.png"));
        }
        finally
        {
            if (window.DataContext is MainWindowViewModel vmCleanup)
            {
                vmCleanup.Logs.WrapLines = true;
            }
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task CompactDensity_Preference_SurvivesRestart_ViaSettingsStore()
    {
        var dir = Path.Combine(Path.GetTempPath(), "th-logs-" + Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(dir, "settings.json");
        try
        {
            var vm = new MainWindowViewModel(new FakeMonitor(), new SettingsStore(settingsPath));
            Assert.False(vm.Logs.CompactDensity); // default off
            vm.Logs.CompactDensity = true;

            var onDisk = new SettingsStore(settingsPath).Load();
            Assert.True(onDisk.LogsCompactDensity);
            vm.Dispose();

            var vm2 = new MainWindowViewModel(new FakeMonitor(), new SettingsStore(settingsPath));
            Assert.True(vm2.Logs.CompactDensity);
            // Restoring must not write back on its own — flip off and confirm save.
            var savesBefore = File.ReadAllText(settingsPath);
            vm2.Logs.CompactDensity = false;
            var onDisk2 = new SettingsStore(settingsPath).Load();
            Assert.False(onDisk2.LogsCompactDensity);
            Assert.NotEqual(savesBefore, File.ReadAllText(settingsPath));
            vm2.Dispose();
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        await Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task CompactDensity_UI_ToggleBinds_DefaultOff()
    {
        PtySessionFactory.UseMock = true;
        var window = new MainWindow { Width = 1200, Height = 800 };
        try
        {
            window.Show();
            await Task.Delay(400);
            var vm = (MainWindowViewModel)window.DataContext!;
            vm.SelectedRightTab = 2;
            await Task.Delay(150);

            var toggle = window.GetVisualDescendants().OfType<ToggleButton>()
                .Single(t => (string?)t.Content == "紧凑");
            Assert.False(toggle.IsChecked);
            Assert.False(vm.Logs.CompactDensity);

            toggle.IsChecked = true;
            await Task.Delay(150);
            Assert.True(vm.Logs.CompactDensity);
            Assert.True(toggle.IsChecked);

            var list = window.GetVisualDescendants().OfType<ListBox>()
                .Single(lb => lb.Name == "LogsList");
            Assert.Contains("compact", list.Classes);

            var outDir = Path.Combine(AppContext.BaseDirectory, "ui-snapshots");
            Directory.CreateDirectory(outDir);
            // Capture with compact on — docs screenshot wants「紧凑」visible and preferably on.
            window.CaptureRenderedFrame()?.Save(Path.Combine(outDir, "logs-compact-density.png"));

            toggle.IsChecked = false;
            await Task.Delay(100);
            Assert.False(vm.Logs.CompactDensity);
            Assert.DoesNotContain("compact", list.Classes);
        }
        finally
        {
            if (window.DataContext is MainWindowViewModel vmCleanup)
            {
                vmCleanup.Logs.CompactDensity = false;
            }
            window.Close();
        }
    }

    // ===== ring-buffer capacity presets (500 / 2000 / 5000) =====

    [Fact]
    public void NormalizeSavedBufferCapacity_PresetsAndNearest()
    {
        Assert.Equal(2000, LogsViewModel.NormalizeSavedBufferCapacity(0));
        Assert.Equal(2000, LogsViewModel.NormalizeSavedBufferCapacity(-5));
        Assert.Equal(500, LogsViewModel.NormalizeSavedBufferCapacity(500));
        Assert.Equal(2000, LogsViewModel.NormalizeSavedBufferCapacity(2000));
        Assert.Equal(5000, LogsViewModel.NormalizeSavedBufferCapacity(5000));
        Assert.Equal(500, LogsViewModel.NormalizeSavedBufferCapacity(600));   // nearer 500 than 2000
        Assert.Equal(2000, LogsViewModel.NormalizeSavedBufferCapacity(1800));
        Assert.Equal(5000, LogsViewModel.NormalizeSavedBufferCapacity(4000));
    }

    [AvaloniaFact]
    public async Task BufferCapacity_Shrink_TrimsOldest_AndRefreshesEntries()
    {
        var (dash, logs, _) = MakeLogs(bufferCapacity: 8);
        for (var i = 0; i < 8; i++)
            dash.AppendOutput("info", $"cap-{i}", "Terminal 01");
        await Until(() => logs.Entries.Count == 8);
        Assert.Equal(8, logs.BufferCapacity);
        Assert.Equal("cap-0", logs.Entries[0].Message);

        logs.BufferCapacity = 3; // shrink → drop oldest 5
        Assert.Equal(3, logs.BufferCapacity);
        Assert.Equal(3, logs.Entries.Count);
        Assert.Equal("cap-5", logs.Entries[0].Message);
        Assert.Equal("cap-7", logs.Entries[^1].Message);
        Assert.Contains("缓冲容量 → 3", logs.StatusText);

        // Growing again does not resurrect trimmed lines.
        logs.BufferCapacity = 8;
        Assert.Equal(3, logs.Entries.Count);
        Assert.Equal("cap-5", logs.Entries[0].Message);
    }

    [AvaloniaFact]
    public async Task BufferCapacity_Preference_SurvivesRestart_ViaSettingsStore()
    {
        var dir = Path.Combine(Path.GetTempPath(), "th-logs-" + Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(dir, "settings.json");
        try
        {
            var vm = new MainWindowViewModel(new FakeMonitor(), new SettingsStore(settingsPath));
            Assert.Equal(LogsViewModel.DefaultBufferCapacity, vm.Logs.BufferCapacity); // default 2000
            Assert.True(vm.Logs.Capacity2000Selected);
            vm.Logs.BufferCapacity = 500;

            var onDisk = new SettingsStore(settingsPath).Load();
            Assert.Equal(500, onDisk.LogsBufferCapacity);
            vm.Dispose();

            var vm2 = new MainWindowViewModel(new FakeMonitor(), new SettingsStore(settingsPath));
            Assert.Equal(500, vm2.Logs.BufferCapacity);
            Assert.True(vm2.Logs.Capacity500Selected);
            Assert.False(vm2.Logs.Capacity2000Selected);

            // Restoring must not write back on its own — flip to 5000 and confirm save.
            var savesBefore = File.ReadAllText(settingsPath);
            vm2.Logs.Capacity5000Selected = true;
            var onDisk2 = new SettingsStore(settingsPath).Load();
            Assert.Equal(5000, onDisk2.LogsBufferCapacity);
            Assert.NotEqual(savesBefore, File.ReadAllText(settingsPath));
            vm2.Dispose();

            // Stale / out-of-range saved value snaps to nearest preset on load.
            var store = new SettingsStore(settingsPath);
            var raw = store.Load();
            raw.LogsBufferCapacity = 999;
            store.Save(raw);
            var vm3 = new MainWindowViewModel(new FakeMonitor(), new SettingsStore(settingsPath));
            Assert.Equal(500, vm3.Logs.BufferCapacity); // nearer 500 than 2000
            vm3.Dispose();
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        await Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task BufferCapacity_UI_ChipsBind_Default2000()
    {
        PtySessionFactory.UseMock = true;
        var window = new MainWindow { Width = 1280, Height = 800 };
        try
        {
            window.Show();
            await Task.Delay(400);
            var vm = (MainWindowViewModel)window.DataContext!;
            vm.SelectedRightTab = 2;
            await Task.Delay(150);

            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
                t => t.Text == "容量");
            var chips = window.GetVisualDescendants().OfType<ToggleButton>()
                .Where(t => t.Classes.Contains("capchip")).ToList();
            Assert.Equal(3, chips.Count);
            Assert.All(new[] { "500", "2000", "5000" },
                label => Assert.Contains(chips, c => (string?)c.Content == label));

            Assert.Equal(2000, vm.Logs.BufferCapacity);
            Assert.True(chips.Single(c => (string?)c.Content == "2000").IsChecked);
            Assert.False(chips.Single(c => (string?)c.Content == "500").IsChecked);

            for (var i = 0; i < 6; i++)
                vm.Dashboard.AppendOutput("info", $"buf-cap-{i}", "Terminal 01");
            await Until(() => vm.Logs.Entries.Count >= 6);

            chips.Single(c => (string?)c.Content == "500").IsChecked = true;
            await Task.Delay(150);
            Assert.Equal(500, vm.Logs.BufferCapacity);
            Assert.True(chips.Single(c => (string?)c.Content == "500").IsChecked);
            Assert.False(chips.Single(c => (string?)c.Content == "2000").IsChecked);
            Assert.Contains("缓冲容量 → 500", vm.Logs.StatusText);

            var outDir = Path.Combine(AppContext.BaseDirectory, "ui-snapshots");
            Directory.CreateDirectory(outDir);
            window.CaptureRenderedFrame()?.Save(Path.Combine(outDir, "logs-buffer-capacity.png"));
        }
        finally
        {
            if (window.DataContext is MainWindowViewModel vmCleanup)
            {
                vmCleanup.Logs.BufferCapacity = LogsViewModel.DefaultBufferCapacity;
            }
            window.Close();
        }
    }

    // ===== level-chip live counts (ring buffer, not filtered Entries) =====

    [Fact]
    public void CountLevelsInBuffer_AndFormatLevelChipLabel_Helpers()
    {
        var empty = LogsViewModel.CountLevelsInBuffer([]);
        Assert.Equal(0, empty.All);
        Assert.Equal(0, empty.Info);
        Assert.Equal(0, empty.Warn);
        Assert.Equal(0, empty.Error);
        Assert.Equal("全部 0", LogsViewModel.FormatLevelChipLabel("全部", 0));
        Assert.Equal("info 80", LogsViewModel.FormatLevelChipLabel("info", 80));
        Assert.Equal("warn 15", LogsViewModel.FormatLevelChipLabel("warn", 15));
        Assert.Equal("error 5", LogsViewModel.FormatLevelChipLabel("error", 5));

        var now = DateTime.UtcNow;
        var buf = new[]
        {
            new LogEntry(now, "info", "a"),
            new LogEntry(now, "INFO", "b"), // case-insensitive
            new LogEntry(now, "warn", "c"),
            new LogEntry(now, "error", "d"),
            new LogEntry(now, "error", "e"),
            new LogEntry(now, "debug", "f"), // unknown → All only
        };
        var c = LogsViewModel.CountLevelsInBuffer(buf);
        Assert.Equal(6, c.All);
        Assert.Equal(2, c.Info);
        Assert.Equal(1, c.Warn);
        Assert.Equal(2, c.Error);
    }

    [AvaloniaFact]
    public async Task LevelChipCounts_TrackRingBuffer_NotFilteredEntries()
    {
        var (dash, logs, _) = MakeLogs();
        Assert.Equal(0, logs.LevelAllCount);
        Assert.Equal("全部 0", logs.LevelAllChipLabel);
        Assert.Equal("info 0", logs.LevelInfoChipLabel);

        dash.AppendOutput("info", "i1", "Terminal 01");
        dash.AppendOutput("info", "i2", "Terminal 01");
        dash.AppendOutput("warn", "w1", "Terminal 02");
        dash.AppendOutput("error", "e1", "Terminal 01");
        dash.AppendOutput("error", "e2", "Terminal 02");
        await Until(() => logs.LevelAllCount == 5);

        Assert.Equal(5, logs.LevelAllCount);
        Assert.Equal(2, logs.LevelInfoCount);
        Assert.Equal(1, logs.LevelWarnCount);
        Assert.Equal(2, logs.LevelErrorCount);
        Assert.Equal("全部 5", logs.LevelAllChipLabel);
        Assert.Equal("info 2", logs.LevelInfoChipLabel);
        Assert.Equal("warn 1", logs.LevelWarnChipLabel);
        Assert.Equal("error 2", logs.LevelErrorChipLabel);

        // Level filter narrows Entries but chip counts stay on the full ring buffer.
        logs.LevelErrorSelected = true;
        Assert.Equal(2, logs.Entries.Count);
        Assert.Equal(5, logs.LevelAllCount);
        Assert.Equal(2, logs.LevelInfoCount);
        Assert.Equal("error 2", logs.LevelErrorChipLabel);

        // Text filter likewise does not shrink chip counts.
        logs.LevelAllSelected = true;
        logs.FilterText = "nope";
        Assert.Empty(logs.Entries);
        Assert.Equal(5, logs.LevelAllCount);
        Assert.Equal("全部 5", logs.LevelAllChipLabel);
        logs.FilterText = "";

        // ClearVisible on a level slice removes those lines from the buffer → counts drop.
        logs.LevelWarnSelected = true;
        Assert.Single(logs.Entries);
        logs.ClearVisibleCommand.Execute(null);
        Assert.Equal(4, logs.LevelAllCount);
        Assert.Equal(0, logs.LevelWarnCount);
        Assert.Equal("warn 0", logs.LevelWarnChipLabel);
        Assert.Equal(2, logs.LevelInfoCount);
        Assert.Equal(2, logs.LevelErrorCount);
    }

    [AvaloniaFact]
    public async Task LevelChipCounts_TrimAndOutputClear_Recompute()
    {
        var (dash, logs, _) = MakeLogs(bufferCapacity: 4);
        for (var i = 0; i < 4; i++)
            dash.AppendOutput(i < 2 ? "info" : "error", $"t{i}", "Terminal 01");
        await Until(() => logs.LevelAllCount == 4);
        Assert.Equal(2, logs.LevelInfoCount);
        Assert.Equal(2, logs.LevelErrorCount);

        // Evict oldest (info) via capacity — counts follow the trim.
        dash.AppendOutput("warn", "tw", "Terminal 01");
        await Until(() => logs.LevelAllCount == 4 && logs.LevelWarnCount == 1);
        Assert.Equal(1, logs.LevelInfoCount); // one info evicted
        Assert.Equal(2, logs.LevelErrorCount);
        Assert.Equal("全部 4", logs.LevelAllChipLabel);

        // Shrink capacity → trim oldest + refresh counts.
        logs.BufferCapacity = 2;
        Assert.Equal(2, logs.LevelAllCount);
        Assert.Equal("全部 2", logs.LevelAllChipLabel);

        // Output clear (default) empties buffer → zeros.
        dash.ClearOutputCommand.Execute(null);
        Assert.Equal(0, logs.LevelAllCount);
        Assert.Equal(0, logs.LevelInfoCount);
        Assert.Equal("全部 0", logs.LevelAllChipLabel);
        Assert.Equal("error 0", logs.LevelErrorChipLabel);
    }

    [AvaloniaFact]
    public async Task LevelChipCounts_UI_EndToEnd_ChipsShowCounts()
    {
        PtySessionFactory.UseMock = true;
        var window = new MainWindow { Width = 1280, Height = 800 };
        try
        {
            window.Show();
            await Task.Delay(400);
            var vm = (MainWindowViewModel)window.DataContext!;
            vm.SelectedRightTab = 2;
            await Task.Delay(150);

            for (var i = 0; i < 5; i++)
                vm.Dashboard.AppendOutput("info", $"cnt-info-{i}", "Terminal 01");
            for (var i = 0; i < 2; i++)
                vm.Dashboard.AppendOutput("warn", $"cnt-warn-{i}", "Terminal 01");
            for (var i = 0; i < 3; i++)
                vm.Dashboard.AppendOutput("error", $"cnt-err-{i}", "Terminal 01");
            await Until(() => vm.Logs.LevelAllCount >= 10);

            Assert.Equal(10, vm.Logs.LevelAllCount);
            Assert.Equal(5, vm.Logs.LevelInfoCount);
            Assert.Equal(2, vm.Logs.LevelWarnCount);
            Assert.Equal(3, vm.Logs.LevelErrorCount);

            var chips = window.GetVisualDescendants().OfType<ToggleButton>()
                .Where(t => t.Classes.Contains("levelchip")).ToList();
            Assert.Contains(chips, c => (string?)c.Content == "全部 10");
            Assert.Contains(chips, c => (string?)c.Content == "info 5");
            Assert.Contains(chips, c => (string?)c.Content == "warn 2");
            Assert.Contains(chips, c => (string?)c.Content == "error 3");

            // Stay on「全部」so all counts are visible in the ship-bar screenshot.
            Assert.True(vm.Logs.LevelAllSelected);

            var outDir = Path.Combine(AppContext.BaseDirectory, "ui-snapshots");
            Directory.CreateDirectory(outDir);
            window.CaptureRenderedFrame()?.Save(Path.Combine(outDir, "logs-level-counts.png"));
        }
        finally
        {
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
    public void FindAdjacentLevel_FindsPrevNext_CaseInsensitive_NoWrap()
    {
        var t = DateTime.Now;
        LogEntry E(string level, string msg) => new(t, level, msg, "s");
        var entries = new[]
        {
            E("info", "i0"),
            E("ERROR", "e1"),   // case-insensitive
            E("warn", "w2"),
            E("error", "e3"),
            E("info", "i4"),
            E("Error", "e5"),
        };

        // Next from -1 → first error
        Assert.Equal(1, LogsViewModel.FindAdjacentLevel(entries, -1, "error", +1));
        // Next from first error → second
        Assert.Equal(3, LogsViewModel.FindAdjacentLevel(entries, 1, "error", +1));
        // Next from last error → none
        Assert.Equal(-1, LogsViewModel.FindAdjacentLevel(entries, 5, "error", +1));
        // Prev from last error → previous
        Assert.Equal(3, LogsViewModel.FindAdjacentLevel(entries, 5, "error", -1));
        // Prev from first error → none
        Assert.Equal(-1, LogsViewModel.FindAdjacentLevel(entries, 1, "error", -1));
        // Prev with nothing selected → none
        Assert.Equal(-1, LogsViewModel.FindAdjacentLevel(entries, -1, "error", -1));

        // Same helper works for warn
        Assert.Equal(2, LogsViewModel.FindAdjacentLevel(entries, -1, "warn", +1));
        Assert.Equal(-1, LogsViewModel.FindAdjacentLevel(entries, 2, "warn", +1));
        Assert.Equal(-1, LogsViewModel.FindAdjacentLevel(entries, 2, "warn", -1));

        // Empty / nullish / zero direction
        Assert.Equal(-1, LogsViewModel.FindAdjacentLevel(Array.Empty<LogEntry>(), 0, "error", +1));
        Assert.Equal(-1, LogsViewModel.FindAdjacentLevel(entries, 0, "error", 0));
        Assert.Equal(-1, LogsViewModel.FindAdjacentLevel(entries, 0, "", +1));
    }

    [AvaloniaFact]
    public async Task LevelJump_GoNextPrevError_StepsThroughErrors_NoWrap_PausesFollow()
    {
        var (dash, logs, _) = MakeLogs();
        dash.AppendOutput("info", "i0", "Terminal 01");
        dash.AppendOutput("error", "e1", "Terminal 01");
        dash.AppendOutput("warn", "w2", "Terminal 01");
        dash.AppendOutput("error", "e3", "Terminal 01");
        dash.AppendOutput("info", "i4", "Terminal 01");
        dash.AppendOutput("error", "e5", "Terminal 01");
        await Until(() => logs.Entries.Count == 6);

        // On「全部」: jump only among error rows in current Entries.
        Assert.True(logs.LevelAllSelected);
        Assert.Equal(-1, logs.SelectedIndex);
        Assert.False(logs.CanGoPrevError);
        Assert.True(logs.CanGoNextError);

        logs.GoNextErrorCommand.Execute(null);
        Assert.Equal(1, logs.SelectedIndex);
        Assert.Equal("e1", logs.SelectedEntry!.Message);
        Assert.False(logs.FollowTail); // same as match nav
        Assert.Contains("error 2/6", logs.StatusText);
        Assert.False(logs.CanGoPrevError); // first error: no wrap
        Assert.True(logs.CanGoNextError);

        logs.GoNextErrorCommand.Execute(null);
        Assert.Equal(3, logs.SelectedIndex);
        Assert.Equal("e3", logs.SelectedEntry!.Message);
        Assert.True(logs.CanGoPrevError);

        logs.GoNextErrorCommand.Execute(null);
        Assert.Equal(5, logs.SelectedIndex);
        Assert.Equal("e5", logs.SelectedEntry!.Message);
        Assert.False(logs.CanGoNextError); // last error: no wrap
        Assert.True(logs.CanGoPrevError);

        // Extra presses stay put
        for (var i = 0; i < 5; i++) logs.GoNextErrorCommand.Execute(null);
        Assert.Equal(5, logs.SelectedIndex);

        logs.GoPrevErrorCommand.Execute(null);
        Assert.Equal(3, logs.SelectedIndex);
        Assert.Contains("error 4/6", logs.StatusText);

        for (var i = 0; i < 10; i++) logs.GoPrevErrorCommand.Execute(null);
        Assert.Equal(1, logs.SelectedIndex);
        Assert.False(logs.CanGoPrevError);
    }

    [AvaloniaFact]
    public async Task LevelJump_Disabled_WhenNoErrorNeighbor_AndRespectsCurrentFilter()
    {
        var (dash, logs, _) = MakeLogs();
        dash.AppendOutput("info", "only-info", "Terminal 01");
        dash.AppendOutput("warn", "only-warn", "Terminal 01");
        await Until(() => logs.Entries.Count == 2);

        Assert.False(logs.CanGoPrevError);
        Assert.False(logs.CanGoNextError);
        logs.GoNextErrorCommand.Execute(null);
        Assert.Equal(-1, logs.SelectedIndex);
        Assert.True(logs.FollowTail); // no jump → follow stays on

        dash.AppendOutput("error", "e-hidden-by-level", "Terminal 01");
        await Until(() => logs.LevelErrorCount == 1);
        // Level filter to info → error not in Entries → buttons stay off
        logs.LevelInfoSelected = true;
        Assert.Single(logs.Entries);
        Assert.False(logs.CanGoNextError);
        Assert.False(logs.CanGoPrevError);

        // Back to「全部」→ error visible again
        logs.LevelAllSelected = true;
        Assert.True(logs.CanGoNextError);
        logs.GoNextErrorCommand.Execute(null);
        Assert.Equal("e-hidden-by-level", logs.SelectedEntry!.Message);
    }

    [AvaloniaFact]
    public async Task LevelJump_UI_EndToEnd_ButtonsBind_Screenshot()
    {
        PtySessionFactory.UseMock = true;
        var window = new MainWindow { Width = 1280, Height = 800 };
        try
        {
            window.Show();
            await Task.Delay(400);
            var vm = (MainWindowViewModel)window.DataContext!;
            vm.SelectedRightTab = 2; // Logs
            await Task.Delay(150);

            vm.Dashboard.AppendOutput("info", "jump-info-0", "Terminal 01");
            vm.Dashboard.AppendOutput("error", "jump-err-1", "Terminal 01");
            vm.Dashboard.AppendOutput("warn", "jump-warn-2", "Terminal 01");
            vm.Dashboard.AppendOutput("error", "jump-err-3", "Terminal 01");
            vm.Dashboard.AppendOutput("info", "jump-info-4", "Terminal 01");
            vm.Dashboard.AppendOutput("error", "jump-err-5", "Terminal 01");
            await Until(() => vm.Logs.Entries.Count >= 6 && vm.Logs.LevelErrorCount >= 3);

            Assert.True(vm.Logs.LevelAllSelected);

            var prevErr = window.GetVisualDescendants().OfType<Button>()
                .Single(b => ReferenceEquals(b.Command, vm.Logs.GoPrevErrorCommand));
            var nextErr = window.GetVisualDescendants().OfType<Button>()
                .Single(b => ReferenceEquals(b.Command, vm.Logs.GoNextErrorCommand));
            Assert.Equal("▲ error", prevErr.Content);
            Assert.Equal("▼ error", nextErr.Content);
            Assert.False(prevErr.IsEnabled);
            Assert.True(nextErr.IsEnabled);

            nextErr.Command!.Execute(null);
            await Task.Delay(100);
            Assert.Equal(1, vm.Logs.SelectedIndex);
            Assert.Equal("jump-err-1", vm.Logs.SelectedEntry!.Message);
            Assert.False(vm.Logs.FollowTail);
            Assert.False(prevErr.IsEnabled); // still first error
            Assert.True(nextErr.IsEnabled);

            nextErr.Command!.Execute(null);
            await Task.Delay(80);
            Assert.Equal(3, vm.Logs.SelectedIndex);
            Assert.True(prevErr.IsEnabled);

            var outDir = Path.Combine(AppContext.BaseDirectory, "ui-snapshots");
            Directory.CreateDirectory(outDir);
            window.CaptureRenderedFrame()?.Save(Path.Combine(outDir, "logs-jump-level.png"));
        }
        finally
        {
            if (window.DataContext is MainWindowViewModel vmCleanup)
            {
                vmCleanup.Logs.FilterText = "";
                vmCleanup.Logs.UseRegex = false;
                vmCleanup.Logs.LevelFilterIndex = 0;
            }
            window.Close();
        }
    }

    /// <summary>Regression: a closed session's filter combo must not survive in the
    /// name-keyed map — the next auto-named session reuses "Terminal 01" and would
    /// silently inherit the dead session's filters.</summary>
    [AvaloniaFact]
    public async Task SessionFilter_DiesWithSession_RecycledNameStartsClean()
    {
        PtySessionFactory.UseMock = true;
        var dir = Path.Combine(Path.GetTempPath(), "th-logs-recycle-" + Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(dir, "settings.json");
        MainWindowViewModel? vm = null;
        try
        {
            vm = new MainWindowViewModel(new FakeMonitor(), new SettingsStore(settingsPath));
            await vm.SpawnStartupSessionsAsync();
            await Until(() => vm.Logs.SessionNames.Count == 4);

            vm.Logs.SessionFilterIndex = 1;      // Terminal 01
            vm.Logs.FilterText = "t01-only";
            Assert.Equal("t01-only", vm.Logs.SnapshotSessionFilters()["Terminal 01"].FilterText);

            var t01 = vm.SessionCards.First(c => c.Name == "Terminal 01");
            vm.CloseSessionCommand.Execute(t01);
            await Until(() => vm.SessionCards.All(c => c.Name != "Terminal 01"));

            vm.NewSessionCommand.Execute(null);  // auto-name reuses "Terminal 01"
            await Until(() => vm.SessionCards.Any(c => c.Name == "Terminal 01"));
            await Until(() => vm.Logs.SessionNames.Contains("Terminal 01"));

            vm.Logs.SessionFilterIndex = vm.Logs.SessionNames.IndexOf("Terminal 01");
            Assert.Equal("", vm.Logs.FilterText); // the dead session's combo is gone
        }
        finally
        {
            vm?.Dispose();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Regression: renaming a session moves its remembered filter combo —
    /// the freed name must not keep it (it recycles to the next auto-named session).</summary>
    [AvaloniaFact]
    public async Task SessionFilter_FollowsRename_OldNameFreed()
    {
        PtySessionFactory.UseMock = true;
        var dir = Path.Combine(Path.GetTempPath(), "th-logs-rename-" + Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(dir, "settings.json");
        MainWindowViewModel? vm = null;
        try
        {
            vm = new MainWindowViewModel(new FakeMonitor(), new SettingsStore(settingsPath));
            await vm.SpawnStartupSessionsAsync();
            await Until(() => vm.Logs.SessionNames.Count == 4);

            vm.Logs.SessionFilterIndex = 1;      // Terminal 01
            vm.Logs.FilterText = "t01-combo";

            var card = vm.SessionCards.First(c => c.Name == "Terminal 01");
            vm.RenameSessionCommand.Execute((card, "build box"));
            await Until(() => vm.Logs.SessionNames.Contains("build box"));

            var map = vm.Logs.SnapshotSessionFilters();
            Assert.DoesNotContain("Terminal 01", map.Keys);   // freed
            Assert.Equal("t01-combo", map["build box"].FilterText); // moved

            vm.Logs.SessionFilterIndex = vm.Logs.SessionNames.IndexOf("build box");
            Assert.Equal("t01-combo", vm.Logs.FilterText);    // combo follows the session
        }
        finally
        {
            vm?.Dispose();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

}
