using System.Collections.Specialized;
using Avalonia.Headless.XUnit;
using TerminalHub.App.ViewModels;
using TerminalHub.Core.Logging;
using TerminalHub.Core.Monitoring;
using TerminalHub.Core.Terminal;
using Xunit;

namespace TerminalHub.Tests;

/// <summary>Regression tests for the logs/dashboard performance fixes:
/// debounced filter persistence (with flush-on-dispose), debounced search for
/// typing continuations (any other edit stays immediate), and batched
/// ring-buffer maintenance under output bursts.</summary>
public class LogsDashboardDebounceBatchTests
{
    private sealed class IdleMonitor : ISystemMonitor
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

    /// <summary>Polls until <paramref name="condition"/> holds (flushes and timers
    /// arrive via the UI dispatcher).</summary>
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
        int bufferCapacity = LogsViewModel.DefaultBufferCapacity, Action? persistFilters = null)
    {
        var dash = new DashboardViewModel(new IdleMonitor());
        var file = new SessionLogFile();
        var logs = new LogsViewModel(dash, file, () => ["Terminal 01", "Terminal 02"],
            bufferCapacity: bufferCapacity, persistFilters: persistFilters);
        logs.RefreshSessions();
        return (dash, logs, file);
    }

    [AvaloniaFact]
    public async Task FilterSave_WithoutDebounce_StaysSynchronous()
    {
        var saves = 0;
        var (dash, logs, file) = MakeLogs(persistFilters: () => saves++);
        try
        {
            logs.FilterText = "a";
            logs.LevelFilterIndex = 3;
            Assert.Equal(2, saves); // default (no debounce): every change saves at once
        }
        finally { logs.Dispose(); file.Dispose(); }
        await Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task FilterSave_Debounce_CoalescesBurst_FlushesFinalStateOnDispose()
    {
        var saved = new List<string>();
        var dash = new DashboardViewModel(new IdleMonitor());
        var file = new SessionLogFile();
        LogsViewModel? logs = null;
        logs = new LogsViewModel(dash, file, () => ["Terminal 01"],
            persistFilters: () => saved.Add(logs!.FilterText),
            filterSaveDebounce: TimeSpan.FromMilliseconds(60));
        try
        {
            for (var i = 0; i < 5; i++) logs.FilterText = $"key{i}";
            Assert.Empty(saved);            // keystroke burst: nothing written per key
            await Until(() => saved.Count == 1);
            Assert.Equal("key4", saved[0]); // one trailing save carrying the last state

            logs.FilterText = "final";
            logs.Dispose();                 // teardown must not lose the pending save
            Assert.Equal(["final"], saved.Skip(1).ToList());
        }
        finally { logs?.Dispose(); file.Dispose(); }
        await Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task Search_TypingContinuation_CoalescesIntoOneTrailingRefresh()
    {
        var dash = new DashboardViewModel(new IdleMonitor());
        var buf = new ScreenBuffer(40, 5);
        new VtParser(buf).Feed("alpha needle here\r\nbeta\r\n");
        dash.BufferSource = () => buf;
        var resets = 0;
        dash.SearchHits.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset) resets++;
        };

        dash.SearchQuery = "n";   // first character → immediate refresh
        Assert.Equal(1, resets);
        dash.SearchQuery = "ne";  // typing continuation → deferred
        dash.SearchQuery = "nee"; // deferred again (timer restarts)
        Assert.Equal(1, resets);  // nothing ran for the appended characters yet
        await Until(() => resets == 2);
        await Task.Delay(400);    // settle: no further refresh may arrive
        Assert.Equal(2, resets);  // 3 keystrokes = 1 immediate + 1 trailing search
    }

    [AvaloniaFact]
    public async Task Search_NonAppendEdits_RefreshImmediately()
    {
        var dash = new DashboardViewModel(new IdleMonitor());
        var buf = new ScreenBuffer(40, 5);
        new VtParser(buf).Feed("alpha needle here\r\nbeta\r\n");
        dash.BufferSource = () => buf;
        var resets = 0;
        dash.SearchHits.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset) resets++;
        };

        dash.SearchQuery = "nee"; // wholesale set (from empty) → immediate
        Assert.Single(dash.SearchHits);
        dash.SearchQuery = "beta"; // replace, not append → immediate, cancels pending
        Assert.Single(dash.SearchHits);
        dash.SearchQuery = "";     // clear → immediate, anchors dropped now
        Assert.Empty(dash.SearchHits);
        Assert.Equal(3, resets);
        await Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task Search_ImmediateRefresh_CancelsPendingDeferredRefresh()
    {
        var dash = new DashboardViewModel(new IdleMonitor());
        var buf = new ScreenBuffer(40, 5);
        new VtParser(buf).Feed("alpha needle here\r\nbeta\r\n");
        dash.BufferSource = () => buf;
        var resets = 0;
        dash.SearchHits.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset) resets++;
        };

        dash.SearchQuery = "n";  // immediate
        dash.SearchQuery = "ne"; // deferred (pending)
        dash.RefreshSearch();    // session-switch path: immediate refresh wins
        Assert.Equal(2, resets);
        await Task.Delay(400);   // settle: the cancelled timer must not fire a duplicate
        Assert.Equal(2, resets);
    }

    [AvaloniaFact]
    public async Task OutputBurst_OneBatchedCollectionEvent_AndOneChipRefreshPerFlush()
    {
        var (dash, logs, file) = MakeLogs();
        try
        {
            var chipUpdates = 0;
            logs.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(LogsViewModel.LevelAllChipLabel)) chipUpdates++;
            };
            var addEvents = 0;
            var lastBatchSize = 0;
            ((INotifyCollectionChanged)dash.OutputLog).CollectionChanged += (_, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Add) { addEvents++; lastBatchSize = e.NewItems!.Count; }
            };

            for (var i = 0; i < 6; i++) dash.AppendOutput("info", $"line-{i}", "Terminal 01");
            await Until(() => logs.LevelAllCount == 6);

            Assert.Equal(1, addEvents);      // the whole flush is one batched Add
            Assert.Equal(6, lastBatchSize);
            Assert.Equal(1, chipUpdates);    // chip labels recomputed once, not per line
            Assert.Equal(6, logs.Entries.Count);
        }
        finally { logs.Dispose(); file.Dispose(); }
    }

    [AvaloniaFact]
    public async Task BurstOverflow_EvictsOldest_KeepsFilteredTail()
    {
        var (dash, logs, file) = MakeLogs(bufferCapacity: 5);
        try
        {
            logs.FilterText = "keep"; // only even lines carry "keep"
            for (var i = 0; i < 12; i++)
                dash.AppendOutput("info", i % 2 == 0 ? $"keep-{i}" : $"skip-{i}", "Terminal 01");
            await Until(() => logs.LevelAllCount == 5 && logs.Entries.Count == 2);

            Assert.Equal(["keep-8", "keep-10"], logs.Entries.Select(e => e.Message).ToList());
            Assert.Equal(5, logs.LevelAllCount);
            Assert.Equal("全部 5", logs.LevelAllChipLabel);
        }
        finally { logs.Dispose(); file.Dispose(); }
    }

    [AvaloniaFact]
    public async Task DashboardRingTrim_BurstKeepsNewestLines()
    {
        var dash = new DashboardViewModel(new IdleMonitor());
        for (var i = 0; i < 520; i++) dash.AppendOutput("info", $"msg-{i:000}", "s");
        await Until(() => dash.OutputLog.Count == 500);

        Assert.Equal(500, dash.OutputLog.Count);      // cap holds under a single burst
        Assert.Equal(500, dash.VisibleOutput.Count);
        Assert.Equal("msg-020", dash.OutputLog[0].Message);   // oldest 20 dropped
        Assert.Equal("msg-519", dash.OutputLog[^1].Message);  // newest kept
        Assert.Equal("msg-020", dash.VisibleOutput[0].Message);

        var problemsDash = new DashboardViewModel(new IdleMonitor());
        for (var i = 0; i < 260; i++) problemsDash.AppendOutput("error", $"err-{i:000}", "s");
        await Until(() => problemsDash.ProblemCount == 200);
        Assert.Equal(200, problemsDash.Problems.Count);
        Assert.Equal("err-060", problemsDash.Problems[0].Message); // oldest 60 dropped
    }

    [AvaloniaFact]
    public async Task DebugRingTrim_BurstKeepsNewestLines()
    {
        var dash = new DashboardViewModel(new IdleMonitor());
        for (var i = 0; i < 320; i++) dash.AppendDebug($"dbg-{i:000}", "s");
        await Until(() => dash.DebugLog.Count == 300);

        Assert.Equal(300, dash.DebugLog.Count);
        Assert.Equal("dbg-020", dash.DebugLog[0].Message);
        Assert.Equal("dbg-319", dash.DebugLog[^1].Message);
    }
}
