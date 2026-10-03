using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHub.Core.Logging;
using TerminalHub.Core.Settings;

namespace TerminalHub.App.ViewModels;

/// <summary>
/// Right-rail Logs tab: filtered view over the live session-output stream
/// (the same lines shown in the bottom Output tab) + optional file sink.
/// Keeps its own ring buffer (default <see cref="DefaultBufferCapacity"/>
/// lines, deeper than the Output panel's 500-line cap) and replays filters
/// over that buffer.
/// </summary>
public partial class LogsViewModel : ViewModelBase, IDisposable
{
    /// <summary>Logs history depth; the bottom Output panel caps at 500 shown lines.</summary>
    public const int DefaultBufferCapacity = 2000;

    /// <summary>User-selectable ring-buffer presets shown in the toolbar (容量 chips).</summary>
    public static readonly int[] BufferCapacityPresets = [500, 2000, 5000];

    private static readonly string[] LevelNames = ["info", "warn", "error"];
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    private readonly DashboardViewModel _dashboard;
    private readonly SessionLogFile _file;
    private readonly string? _logDir;
    private readonly Func<IReadOnlyList<string>> _sessionNames;
    private readonly Action<bool>? _persistFileLogging;
    private readonly Func<string, Task>? _copyToClipboard;
    private readonly Func<Task<string?>>? _promptExportPath;
    private readonly Action? _persistFilters;
    /// <summary>Host activates a session by card name; returns false when unknown.</summary>
    private readonly Func<string, bool>? _activateSession;
    private int _capacity;

    /// <summary>True while saved values are being replayed (<see cref="ApplyPersistedFilters"/>
    /// or a session-switch restore) — suppresses the write-back so a load never triggers a save.</summary>
    private bool _restoringFilters;

    /// <summary>Per-session filter memory: session name → its last filter combo
    /// (seeded from settings via <see cref="ApplySessionFilterMap"/>). Survives
    /// <see cref="RefreshSessions"/> rebinding — entries are only ever added/replaced.</summary>
    private readonly Dictionary<string, LogsSessionFilterState> _sessionFilters = new();

    /// <summary>「全部会话」(dropdown index 0)'s remembered combo — the global fallback.
    /// Kept in the VM (not read live) so it stays correct even while a named
    /// session's filters are showing.</summary>
    private LogsSessionFilterState _globalFilters = new();

    /// <summary>Deep history of session lines; independent of Output's display cap.</summary>
    private readonly List<LogEntry> _buffer = [];

    private Regex? _regex;      // compiled when UseRegex && pattern valid
    private bool _regexInvalid; // pattern present but broken → match nothing, show error

    /// <summary>Filtered view of the internal buffer.</summary>
    public ObservableCollection<LogEntry> Entries { get; } = [];

    /// <summary>Session filter options; index 0 = all sessions.</summary>
    public ObservableCollection<string> SessionNames { get; } = [TerminalHub.Core.Localization.Localizer.Current.Translate("全部会话")];

    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private bool _useRegex;
    [ObservableProperty] private string _filterError = "";
    [ObservableProperty] private int _levelFilterIndex;    // 0 all, 1 info, 2 warn, 3 error
    [ObservableProperty] private int _sessionFilterIndex;
    [ObservableProperty] private bool _fileLogging;
    [ObservableProperty] private string _fileStatus = "";
    [ObservableProperty] private string _statusText = "";
    /// <summary>On: clearing the Output panel keeps this panel's buffered history. Off (default): follow the clear.</summary>
    [ObservableProperty] private bool _retainHistoryOnClear;

    /// <summary>On: list timestamps use relative "ago from now" labels (刚刚/12s/3m/1h/昨天 HH:mm).
    /// Off (default): absolute <c>HH:mm:ss</c>. Global preference (not per-session). Export always absolute.</summary>
    [ObservableProperty] private bool _useRelativeTimestamps;

    /// <summary>On (default): wrap long Message lines in the Logs list.
    /// Off: NoWrap for dense one-line scanning (list gains horizontal scroll). Global preference.</summary>
    [ObservableProperty] private bool _wrapLines = true;

    /// <summary>On: denser Logs list (FontSize ~8.5, Padding ~2,0). Off (default): FontSize 9.5, Padding 4,1.
    /// Global preference (not per-session).</summary>
    [ObservableProperty] private bool _compactDensity;

    /// <summary>Ring-buffer capacity (any ≥1; toolbar chips offer 500/2000/5000).
    /// Changing trims oldest lines when over the new limit and refreshes <see cref="Entries"/>.</summary>
    public int BufferCapacity
    {
        get => _capacity;
        set => ApplyBufferCapacity(value, announce: true);
    }

    /// <summary>Chip bar for capacity presets — exclusive like the level chips.</summary>
    public bool Capacity500Selected
    {
        get => BufferCapacity == 500;
        set => SelectCapacityChip(500, value);
    }

    public bool Capacity2000Selected
    {
        get => BufferCapacity == 2000;
        set => SelectCapacityChip(2000, value);
    }

    public bool Capacity5000Selected
    {
        get => BufferCapacity == 5000;
        set => SelectCapacityChip(5000, value);
    }

    /// <summary>On (default): the list stays pinned to the newest line — new lines auto-scroll
    /// to the bottom. User scroll-up pauses it; the「⬇ 跟随」button or scrolling back to
    /// the bottom resumes. New lines while paused never flip this back on.</summary>
    [ObservableProperty] private bool _followTail = true;

    /// <summary>Index of the selected entry inside <see cref="Entries"/> (-1 = none);
    /// two-way bound to the list, driven by「上一条/下一条」or a plain click.</summary>
    [ObservableProperty] private int _selectedIndex = -1;

    /// <summary>The selected entry, or null — what the view scrolls into view on nav.</summary>
    public LogEntry? SelectedEntry
        => SelectedIndex >= 0 && SelectedIndex < Entries.Count ? Entries[SelectedIndex] : null;

    /// <summary>True when a list row is selected — drives「复制选中」enabled state.</summary>
    public bool HasSelectedEntry => SelectedEntry is not null;

    /// <summary>Text filter active and not broken → match navigation is meaningful.</summary>
    public bool HasTextFilter => !string.IsNullOrEmpty(FilterText) && !_regexInvalid;

    /// <summary>「上一条」enabled: a match before the selection exists (no wrap).</summary>
    public bool CanGoPrevMatch => HasTextFilter && SelectedIndex > 0;

    /// <summary>「下一条」enabled: a match after the selection exists (from -1, the first).</summary>
    public bool CanGoNextMatch => HasTextFilter && Entries.Count > 0 && SelectedIndex < Entries.Count - 1;

    /// <summary>「▲ error」enabled: an error-level row exists before the selection (no wrap).</summary>
    public bool CanGoPrevError => FindAdjacentLevel(Entries, SelectedIndex, "error", -1) >= 0;

    /// <summary>「▼ error」enabled: an error-level row exists after the selection (from -1, the first).</summary>
    public bool CanGoNextError => FindAdjacentLevel(Entries, SelectedIndex, "error", +1) >= 0;

    /// <summary>True while following is paused — drives the floating「⬇ 跟随」button.</summary>
    public bool FollowPaused => !FollowTail;

    partial void OnFollowTailChanged(bool value) => OnPropertyChanged(nameof(FollowPaused));


    /// <summary>Tallies of <see cref="_buffer"/> by level — chip counts ignore the active
    /// level/text/session filter so e.g. error:3 stays visible while「全部」is selected.</summary>
    public readonly record struct LevelCounts(int All, int Info, int Warn, int Error);

    /// <summary>Count info/warn/error (+ all) over a ring-buffer snapshot. Unknown levels
    /// count toward <see cref="LevelCounts.All"/> only.</summary>
    public static LevelCounts CountLevelsInBuffer(IEnumerable<LogEntry> buffer)
    {
        var info = 0; var warn = 0; var error = 0; var all = 0;
        foreach (var e in buffer)
        {
            all++;
            if (string.Equals(e.Level, "info", StringComparison.OrdinalIgnoreCase)) info++;
            else if (string.Equals(e.Level, "warn", StringComparison.OrdinalIgnoreCase)) warn++;
            else if (string.Equals(e.Level, "error", StringComparison.OrdinalIgnoreCase)) error++;
        }
        return new LevelCounts(all, info, warn, error);
    }

    /// <summary>Chip caption style matching the toolbar: <c>全部 120</c> / <c>info 80</c>.</summary>
    public static string FormatLevelChipLabel(string name, int count) => $"{name} {count}";

    /// <summary>Nearest index of <paramref name="level"/> relative to <paramref name="fromIndex"/>
    /// in the current (filtered) list. <paramref name="direction"/> &lt; 0 searches backward,
    /// &gt; 0 forward. Case-insensitive; no wrap. Returns -1 when no neighbor exists.
    /// Shared by error jump (and cheaply usable for warn).</summary>
    public static int FindAdjacentLevel(IReadOnlyList<LogEntry> entries, int fromIndex, string level, int direction)
    {
        if (entries is null || entries.Count == 0 || direction == 0
            || string.IsNullOrEmpty(level))
            return -1;

        if (direction > 0)
        {
            var start = Math.Max(fromIndex + 1, 0);
            for (var i = start; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].Level, level, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }
        else
        {
            var start = fromIndex < 0 ? -1 : Math.Min(fromIndex - 1, entries.Count - 1);
            for (var i = start; i >= 0; i--)
            {
                if (string.Equals(entries[i].Level, level, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }
        return -1;
    }

    /// <summary>Live totals from the ring buffer (not the filtered <see cref="Entries"/> view).</summary>
    public int LevelAllCount { get; private set; }
    public int LevelInfoCount { get; private set; }
    public int LevelWarnCount { get; private set; }
    public int LevelErrorCount { get; private set; }

    /// <summary>Bindable chip captions — recomputed whenever the buffer appends/trims/clears.</summary>
    public string LevelAllChipLabel { get; private set; } = FormatLevelChipLabel("全部", 0);
    public string LevelInfoChipLabel { get; private set; } = FormatLevelChipLabel("info", 0);
    public string LevelWarnChipLabel { get; private set; } = FormatLevelChipLabel("warn", 0);
    public string LevelErrorChipLabel { get; private set; } = FormatLevelChipLabel("error", 0);

    private void CountLevel(LogEntry entry, int delta)
    {
        LevelAllCount += delta;
        if (string.Equals(entry.Level, "info", StringComparison.OrdinalIgnoreCase)) LevelInfoCount += delta;
        else if (string.Equals(entry.Level, "warn", StringComparison.OrdinalIgnoreCase)) LevelWarnCount += delta;
        else if (string.Equals(entry.Level, "error", StringComparison.OrdinalIgnoreCase)) LevelErrorCount += delta;
    }

    /// <summary>Publish totals maintained when the deep buffer appends/trims/clears.</summary>
    private void RefreshLevelCounts()
    {
        LevelAllChipLabel = FormatLevelChipLabel("全部", LevelAllCount);
        LevelInfoChipLabel = FormatLevelChipLabel("info", LevelInfoCount);
        LevelWarnChipLabel = FormatLevelChipLabel("warn", LevelWarnCount);
        LevelErrorChipLabel = FormatLevelChipLabel("error", LevelErrorCount);
        OnPropertyChanged(nameof(LevelAllCount));
        OnPropertyChanged(nameof(LevelInfoCount));
        OnPropertyChanged(nameof(LevelWarnCount));
        OnPropertyChanged(nameof(LevelErrorCount));
        OnPropertyChanged(nameof(LevelAllChipLabel));
        OnPropertyChanged(nameof(LevelInfoChipLabel));
        OnPropertyChanged(nameof(LevelWarnChipLabel));
        OnPropertyChanged(nameof(LevelErrorChipLabel));
    }

    /// <summary>Chip names for the exclusive level bar — index i ↔ <see cref="LevelFilterIndex"/> i.</summary>
    private static readonly string[] LevelChipProps =
        [nameof(LevelAllSelected), nameof(LevelInfoSelected), nameof(LevelWarnSelected), nameof(LevelErrorSelected)];

    /// <summary>Level chip bar (全部/info/warn/error). Each chip mirrors <see cref="LevelFilterIndex"/>;
    /// checking one selects it, and the checked chip cannot be unchecked (the bar is exclusive).</summary>
    public bool LevelAllSelected
    {
        get => LevelFilterIndex == 0;
        set => SelectLevelChip(0, value);
    }

    public bool LevelInfoSelected
    {
        get => LevelFilterIndex == 1;
        set => SelectLevelChip(1, value);
    }

    public bool LevelWarnSelected
    {
        get => LevelFilterIndex == 2;
        set => SelectLevelChip(2, value);
    }

    public bool LevelErrorSelected
    {
        get => LevelFilterIndex == 3;
        set => SelectLevelChip(3, value);
    }

    private void SelectLevelChip(int index, bool selected)
    {
        if (selected) { LevelFilterIndex = index; return; }
        // Clicking the checked chip tries to uncheck it — re-assert instead so the
        // bar always has exactly one selection.
        if (LevelFilterIndex == index) OnPropertyChanged(LevelChipProps[index]);
    }

    /// <summary>Snap a persisted capacity to the nearest toolbar preset (or default when &lt;1).
    /// Unit tests may still construct with any ≥1 capacity; this is for settings load.</summary>
    public static int NormalizeSavedBufferCapacity(int value)
    {
        if (value < 1) return DefaultBufferCapacity;
        if (BufferCapacityPresets.Contains(value)) return value;
        return BufferCapacityPresets.MinBy(p => Math.Abs(p - value));
    }

    private void SelectCapacityChip(int capacity, bool selected)
    {
        if (selected) { BufferCapacity = capacity; return; }
        if (BufferCapacity == capacity)
            OnPropertyChanged(CapacityChipPropName(capacity));
    }

    private static string CapacityChipPropName(int capacity) => capacity switch
    {
        500 => nameof(Capacity500Selected),
        2000 => nameof(Capacity2000Selected),
        5000 => nameof(Capacity5000Selected),
        _ => nameof(BufferCapacity),
    };

    private void NotifyCapacityChips()
    {
        OnPropertyChanged(nameof(Capacity500Selected));
        OnPropertyChanged(nameof(Capacity2000Selected));
        OnPropertyChanged(nameof(Capacity5000Selected));
    }

    /// <summary>Apply a new ring-buffer capacity: trim oldest if over limit, refresh
    /// visible Entries, soft status note, persist via <see cref="SaveCurrentFilters"/>.</summary>
    private void ApplyBufferCapacity(int value, bool announce)
    {
        var next = Math.Max(1, value);
        if (next == _capacity)
        {
            NotifyCapacityChips();
            return;
        }
        _capacity = next;
        var trimmed = false;
        while (_buffer.Count > _capacity)
        {
            CountLevel(_buffer[0], -1);
            _buffer.RemoveAt(0);
            trimmed = true;
        }
        if (trimmed)
        {
            Refilter();
            RefreshLevelCounts();
        }
        OnPropertyChanged(nameof(BufferCapacity));
        NotifyCapacityChips();
        if (announce)
            StatusText = $"缓冲容量 → {_capacity}";
        SaveCurrentFilters();
    }

    public LogsViewModel(
        DashboardViewModel dashboard,
        SessionLogFile file,
        Func<IReadOnlyList<string>> sessionNames,
        bool fileLogging = false,
        Action<bool>? persistFileLogging = null,
        string? logDir = null,
        int bufferCapacity = DefaultBufferCapacity,
        Func<string, Task>? copyToClipboard = null,
        Func<Task<string?>>? promptExportPath = null,
        Action? persistFilters = null,
        Func<string, bool>? activateSession = null)
    {
        _dashboard = dashboard;
        _file = file;
        _file.Failed += OnFileWriteFailed;
        _logDir = logDir;
        _sessionNames = sessionNames;
        _persistFileLogging = persistFileLogging;
        _capacity = Math.Max(1, bufferCapacity);
        _copyToClipboard = copyToClipboard;
        _promptExportPath = promptExportPath;
        _persistFilters = persistFilters;
        _activateSession = activateSession;
        ((INotifyCollectionChanged)_dashboard.OutputLog).CollectionChanged += OnLogChanged;
        Entries.CollectionChanged += OnEntriesChanged;
        Refilter();
        RefreshLevelCounts();
        if (fileLogging) FileLogging = true; // goes through OnFileLoggingChanged
    }

    /// <summary>Restore the filter state saved from a previous run (level index clamped to
    /// the bar). The restore itself never writes back: <see cref="SaveCurrentFilters"/> is
    /// suppressed while applying, so loading cannot trigger a save.</summary>
    public void ApplyPersistedFilters(string? filterText, bool useRegex, int levelFilterIndex, bool retainHistoryOnClear,
        bool useRelativeTimestamps = false, bool wrapLines = true, bool compactDensity = false)
    {
        _restoringFilters = true;
        try
        {
            FilterText = filterText ?? "";
            UseRegex = useRegex;
            LevelFilterIndex = Math.Clamp(levelFilterIndex, 0, LevelNames.Length);
            RetainHistoryOnClear = retainHistoryOnClear;
            UseRelativeTimestamps = useRelativeTimestamps;
            WrapLines = wrapLines;
            CompactDensity = compactDensity;
        }
        finally { _restoringFilters = false; }
        // These are「全部会话」's globals (startup always selects index 0) — remember
        // them as that slot's combo so switching back to index 0 restores them.
        if (SessionFilterIndex == 0)
            _globalFilters = CurrentFilterState();
    }

    /// <summary>Seed the per-session filter memory (session name → last combo), as loaded
    /// from settings. Replaces any previous seed; entries are cloned so the VM never
    /// aliases the settings objects.</summary>
    public void ApplySessionFilterMap(IReadOnlyDictionary<string, LogsSessionFilterState>? map)
    {
        _sessionFilters.Clear();
        if (map is null) return;
        foreach (var (name, state) in map)
        {
            if (string.IsNullOrEmpty(name) || state is null) continue; // corrupt JSON — skip
            _sessionFilters[name] = Clone(state);
        }
    }

    /// <summary>Copy of the per-session filter map (for persistence).</summary>
    public Dictionary<string, LogsSessionFilterState> SnapshotSessionFilters()
        => _sessionFilters.ToDictionary(kv => kv.Key, kv => Clone(kv.Value));

    /// <summary>Drop a closed session's remembered filter combo. Auto-names recycle
    /// ("Terminal 01" → the next auto-named session), and a recycled name must not
    /// inherit the dead session's filters — the map is keyed by name.</summary>
    public void ForgetSessionFilter(string name) => _sessionFilters.Remove(name);

    /// <summary>Rename moves a session's remembered combo to the new name — the old
    /// name (often an auto-name) frees up without handing its filters to whatever
    /// session recycles it later.</summary>
    public void RenameSessionFilter(string oldName, string newName)
    {
        if (string.Equals(oldName, newName, StringComparison.Ordinal)) return;
        if (_sessionFilters.Remove(oldName, out var state))
            _sessionFilters[newName] = state;
    }

    /// <summary>Copy of「全部会话」's remembered combo — the global-slot values
    /// <c>AppSettings.LogsFilterText</c> &amp; co. persist.</summary>
    public LogsSessionFilterState SnapshotGlobalFilters() => Clone(_globalFilters);

    /// <summary>The currently visible filter combo as a persistable snapshot.</summary>
    private LogsSessionFilterState CurrentFilterState() => new()
    {
        FilterText = FilterText,
        UseRegex = UseRegex,
        LevelFilterIndex = LevelFilterIndex,
        RetainHistoryOnClear = RetainHistoryOnClear,
    };

    private static LogsSessionFilterState Clone(LogsSessionFilterState s) => new()
    {
        FilterText = s.FilterText,
        UseRegex = s.UseRegex,
        LevelFilterIndex = s.LevelFilterIndex,
        RetainHistoryOnClear = s.RetainHistoryOnClear,
    };

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var bufferChanged = false;
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems is not null:
                foreach (LogEntry entry in e.NewItems)
                {
                    _buffer.Add(entry);
                    CountLevel(entry, +1);
                    while (_buffer.Count > _capacity)
                    {
                        var evicted = _buffer[0];
                        CountLevel(evicted, -1);
                        _buffer.RemoveAt(0);
                        var shown = Entries.IndexOf(evicted);
                        if (shown >= 0) Entries.RemoveAt(shown);
                    }
                    if (Matches(entry)) Entries.Add(entry);
                }
                bufferChanged = true;
                break;
            case NotifyCollectionChangedAction.Reset:
                // Output panel cleared. Default: follow (drop our history too);
                // 「保留历史」 keeps the deep buffer for later review.
                if (!RetainHistoryOnClear)
                {
                    _buffer.Clear();
                    LevelAllCount = LevelInfoCount = LevelWarnCount = LevelErrorCount = 0;
                    Entries.Clear();
                    bufferChanged = true;
                }
                break;
            default:
                // Output trimmed its oldest line (Remove) — our buffer keeps its own copy.
                break;
        }
        if (bufferChanged) RefreshLevelCounts();
    }

    /// <summary>Selection bookkeeping over the filtered view: a refilter invalidates
    /// the selection outright, live churn only shifts/clamps it, and the nav buttons'
    /// enabled state follows the new count either way.</summary>
    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Reset:
                SelectedIndex = -1; // filter/level/session changed → restart from the first match
                break;
            case NotifyCollectionChangedAction.Remove when SelectedIndex >= 0 && e.OldStartingIndex >= 0:
                var removed = e.OldItems?.Count ?? 0;
                if (e.OldStartingIndex + removed <= SelectedIndex)
                    SelectedIndex -= removed;                 // evicted before the selection → keep the same entry
                else if (e.OldStartingIndex <= SelectedIndex)
                    SelectedIndex = e.OldStartingIndex - 1;   // the selected entry itself was evicted
                break;
        }
        if (SelectedIndex >= Entries.Count)
            SelectedIndex = Entries.Count - 1;
        OnPropertyChanged(nameof(CanGoPrevMatch));
        OnPropertyChanged(nameof(CanGoNextMatch));
        OnPropertyChanged(nameof(CanGoPrevError));
        OnPropertyChanged(nameof(CanGoNextError));
    }

    partial void OnSelectedIndexChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedEntry));
        OnPropertyChanged(nameof(HasSelectedEntry));
        OnPropertyChanged(nameof(CanGoPrevMatch));
        OnPropertyChanged(nameof(CanGoNextMatch));
        OnPropertyChanged(nameof(CanGoPrevError));
        OnPropertyChanged(nameof(CanGoNextError));
    }

    private bool Matches(LogEntry e)
    {
        if (LevelFilterIndex > 0 && !string.Equals(e.Level, LevelNames[LevelFilterIndex - 1], StringComparison.OrdinalIgnoreCase))
            return false;
        if (SessionFilterIndex > 0 && SessionFilterIndex < SessionNames.Count
            && !string.Equals(e.Source, SessionNames[SessionFilterIndex], StringComparison.Ordinal))
            return false;
        if (!string.IsNullOrEmpty(FilterText))
        {
            if (_regexInvalid) return false; // broken pattern matches nothing until fixed
            if (UseRegex && _regex is not null)
            {
                if (!SafeIsMatch(_regex, e.Message) && !SafeIsMatch(_regex, e.Source)) return false;
            }
            else if (!e.Message.Contains(FilterText, StringComparison.OrdinalIgnoreCase)
                     && !e.Source.Contains(FilterText, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private static bool SafeIsMatch(Regex regex, string input)
    {
        try { return regex.IsMatch(input); }
        catch (RegexMatchTimeoutException) { return false; } // catastrophic pattern → no match
    }

    private void Refilter()
    {
        Entries.Clear();
        foreach (var e in _buffer)
            if (Matches(e)) Entries.Add(e);
    }

    partial void OnFilterTextChanged(string value) { UpdateRegex(); Refilter(); SaveCurrentFilters(); }
    partial void OnUseRegexChanged(bool value) { UpdateRegex(); Refilter(); SaveCurrentFilters(); }
    partial void OnLevelFilterIndexChanged(int value)
    {
        Refilter();
        foreach (var chip in LevelChipProps) OnPropertyChanged(chip);
        SaveCurrentFilters();
    }
    partial void OnRetainHistoryOnClearChanged(bool value) => SaveCurrentFilters();
    partial void OnUseRelativeTimestampsChanged(bool value) => SaveCurrentFilters();
    partial void OnWrapLinesChanged(bool value) => SaveCurrentFilters();
    partial void OnCompactDensityChanged(bool value) => SaveCurrentFilters();

    /// <summary>Session switch → swap the visible combo to the new selection's remembered
    /// state. Nothing needs saving on the way out: every filter change (and every
    /// restore) leaves the outgoing selection's slot holding exactly its combo, so the
    /// map is already current. (Saving here by index would also be wrong —
    /// <see cref="RefreshSessions"/> rebinds names before setting the index.)</summary>
    partial void OnSessionFilterIndexChanged(int value)
    {
        RestoreFiltersFor(value);
        Refilter();
    }

    /// <summary>Restore the filter combo belonging to <paramref name="index"/>: its map
    /// entry for a named session (defaults when never configured), or
    /// <see cref="_globalFilters"/> for「全部会话」. Suppressed via
    /// <see cref="_restoringFilters"/> so the restore itself never writes back or loops.</summary>
    private void RestoreFiltersFor(int index)
    {
        var name = index > 0 && index < SessionNames.Count ? SessionNames[index] : null;
        LogsSessionFilterState? saved = null;
        if (name is not null)
            _sessionFilters.TryGetValue(name, out saved);
        else if (index == 0)
            saved = _globalFilters;
        var restore = saved ?? new LogsSessionFilterState();
        _restoringFilters = true;
        try
        {
            FilterText = restore.FilterText;
            UseRegex = restore.UseRegex;
            LevelFilterIndex = Math.Clamp(restore.LevelFilterIndex, 0, LevelNames.Length);
            RetainHistoryOnClear = restore.RetainHistoryOnClear;
        }
        finally { _restoringFilters = false; }
        // Brief hint when a named session's saved (non-default) combo came back.
        if (name is not null && saved is not null && IsNonDefault(saved))
            StatusText = $"已恢复「{name}」筛选";
    }

    private static bool IsNonDefault(LogsSessionFilterState s)
        => !string.IsNullOrEmpty(s.FilterText) || s.UseRegex
           || s.LevelFilterIndex != 0 || s.RetainHistoryOnClear;

    /// <summary>Filter change → record the combo under the active selection's key
    /// (its map entry for a named session, <see cref="_globalFilters"/> for
    /// 「全部会话」), then hand it to the save callback (wired by MainWindowViewModel
    /// to settings + disk).</summary>
    private void SaveCurrentFilters()
    {
        if (_restoringFilters) return;
        if (SessionFilterIndex == 0)
            _globalFilters = CurrentFilterState();
        else if (SessionFilterIndex > 0 && SessionFilterIndex < SessionNames.Count)
            _sessionFilters[SessionNames[SessionFilterIndex]] = CurrentFilterState();
        _persistFilters?.Invoke();
    }

    private void UpdateRegex()
    {
        _regex = null;
        _regexInvalid = false;
        FilterError = "";
        if (!UseRegex || string.IsNullOrEmpty(FilterText)) return;
        try
        {
            _regex = new Regex(FilterText, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (ArgumentException ex)
        {
            _regexInvalid = true;
            FilterError = $"正则无效: {ex.Message}";
        }
    }

    partial void OnFileLoggingChanged(bool value)
    {
        if (value)
        {
            try
            {
                var path = _file.Enable(_logDir);
                FileStatus = $"写入 {path}";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                FileLogging = false;
                FileStatus = $"日志文件打开失败: {ex.Message}";
                return;
            }
        }
        else
        {
            _file.Disable();
            FileStatus = "";
        }
        _persistFileLogging?.Invoke(value);
    }

    private void OnFileWriteFailed(string message)
        => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            FileLogging = false;
            FileStatus = $"日志写入失败，已停止: {message}";
        });

    /// <summary>One line for export / clipboard: always absolute HH:mm:ss [level] (source) message.
    /// Display in the list may use relative labels via <see cref="FormatDisplayTime"/>; export must not.</summary>
    public static string FormatLine(LogEntry e)
        => $"{LogTimestampFormatter.FormatAbsolute(e.Time)} [{e.Level}]{(string.IsNullOrEmpty(e.Source) ? "" : $" ({e.Source})")} {e.Message}";

    /// <summary>List-cell timestamp: relative or absolute per <see cref="UseRelativeTimestamps"/>.</summary>
    public string FormatDisplayTime(DateTime time, DateTime? now = null)
        => LogTimestampFormatter.Format(time, UseRelativeTimestamps, now);

    /// <summary>All currently visible (filtered) lines joined for export.</summary>
    public string BuildVisibleText()
        => string.Join(Environment.NewLine, Entries.Select(FormatLine));

    /// <summary>Copy the currently visible (filtered) lines to the clipboard.</summary>
    [RelayCommand]
    private async Task CopyVisibleAsync()
    {
        if (Entries.Count == 0)
        {
            StatusText = "没有可复制的行";
            return;
        }
        if (_copyToClipboard is null)
        {
            StatusText = "剪贴板不可用";
            return;
        }
        await _copyToClipboard(BuildVisibleText());
        StatusText = $"已复制 {Entries.Count} 行";
    }

    /// <summary>Copy the currently selected log row (absolute <see cref="FormatLine"/>) to the clipboard.
    /// Soft-fails with a status note when nothing is selected or the clipboard hook is missing.</summary>
    [RelayCommand]
    private async Task CopySelectedAsync()
    {
        if (SelectedEntry is null)
        {
            StatusText = "没有选中的日志行";
            return;
        }
        if (_copyToClipboard is null)
        {
            StatusText = "剪贴板不可用";
            return;
        }
        await _copyToClipboard(FormatLine(SelectedEntry));
        StatusText = "已复制选中行";
    }

    /// <summary>Dismiss the current filter results — the visible lines are removed
    /// from the buffer as well, so they don't reappear when the filter changes.</summary>
    [RelayCommand]
    private void ClearVisible()
    {
        if (Entries.Count == 0) return;
        var dismissed = new HashSet<LogEntry>(Entries, ReferenceEqualityComparer.Instance);
        _buffer.RemoveAll(e =>
        {
            if (!dismissed.Contains(e)) return false;
            CountLevel(e, -1);
            return true;
        });
        var n = Entries.Count;
        Entries.Clear();
        RefreshLevelCounts();
        StatusText = $"已清空 {n} 行";
    }

    /// <summary>Called by the view on a *user-driven* scroll (offset changed, extent
    /// did not): landing at the bottom resumes following, anywhere else pauses it.
    /// Extent-only events (new lines, resize) carry no user intent and must not
    /// go through here — otherwise incoming lines would cancel the follow.</summary>
    public void UpdateFollowFromScroll(bool atBottom) => FollowTail = atBottom;

    /// <summary>「⬇ 跟随」button: resume tail-following; the view scrolls to bottom.</summary>
    [RelayCommand]
    private void ResumeFollow() => FollowTail = true;

    /// <summary>「上一条」: step to the previous filtered entry; stops at the top (no wrap).</summary>
    [RelayCommand]
    private void GoPrevMatch()
    {
        if (!CanGoPrevMatch) return;
        SelectMatch(SelectedIndex - 1);
    }

    /// <summary>「下一条」: step to the next filtered entry; stops at the bottom (no wrap).
    /// With nothing selected yet, starts at the first match.</summary>
    [RelayCommand]
    private void GoNextMatch()
    {
        if (!CanGoNextMatch) return;
        SelectMatch(Math.Max(SelectedIndex + 1, 0));
    }

    /// <summary>Select a match by index: pause tail-following (the user is inspecting,
    /// new lines must not yank the view away) and show the position in the status row.</summary>
    private void SelectMatch(int index)
    {
        SelectedIndex = Math.Clamp(index, 0, Entries.Count - 1);
        FollowTail = false;
        StatusText = $"匹配 {SelectedIndex + 1}/{Entries.Count}";
    }

    /// <summary>「▲ error」: jump to the previous error-level row in current Entries (no wrap).</summary>
    [RelayCommand]
    private void GoPrevError() => JumpToAdjacentLevel("error", -1);

    /// <summary>「▼ error」: jump to the next error-level row in current Entries (no wrap).
    /// With nothing selected yet, lands on the first error.</summary>
    [RelayCommand]
    private void GoNextError() => JumpToAdjacentLevel("error", +1);

    /// <summary>Select the adjacent level neighbor: pause follow-tail (same as match nav)
    /// and soft-status the row position. No-op when no neighbor exists.</summary>
    private void JumpToAdjacentLevel(string level, int direction)
    {
        var idx = FindAdjacentLevel(Entries, SelectedIndex, level, direction);
        if (idx < 0) return;
        SelectedIndex = idx;
        FollowTail = false;
        StatusText = $"{level} {SelectedIndex + 1}/{Entries.Count}";
    }

    /// <summary>Default export destination: `export-&lt;ts&gt;.log` in the file sink's dir.</summary>
    public string DefaultExportPath()
        => Path.Combine(_logDir ?? SessionLogFile.DefaultDir(),
            $"export-{DateTime.Now:yyyyMMdd-HHmmss}.log");

    /// <summary>One-shot export of the currently visible (filtered) lines to .log/.txt.
    /// With a picker hook the user picks the path (cancel → status note, no write);
    /// without one (tests / headless) it writes <see cref="DefaultExportPath"/>.
    /// Independent of the「⬇ 写文件」live sink, which keeps streaming untouched.</summary>
    [RelayCommand]
    private async Task ExportVisibleAsync()
    {
        if (Entries.Count == 0)
        {
            StatusText = "没有可导出的行";
            return;
        }
        string? path;
        if (_promptExportPath is null)
            path = DefaultExportPath();
        else
        {
            path = await _promptExportPath();
            if (path is null)
            {
                StatusText = "已取消导出";
                return;
            }
        }
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(path, BuildVisibleText() + Environment.NewLine);
            StatusText = $"已导出 {Entries.Count} 行 → {path}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = $"导出失败: {ex.Message}";
        }
    }


    /// <summary>「跳到会话」: activate the session named in the selected entry's
    /// <see cref="LogEntry.Source"/>. Missing/unknown source → status note, no crash.
    /// The host keeps the Logs tab open (activation only flips ActiveSession/card).</summary>
    [RelayCommand]
    private void JumpToSession() => JumpToSessionEntry(SelectedEntry);

    /// <summary>Double-click / Enter path — jump using the tapped entry (not just
    /// the current selection), so a double-tap does not fight match-nav selection.</summary>
    public void JumpToSessionEntry(LogEntry? entry)
    {
        if (entry is null)
        {
            StatusText = "没有选中的日志行";
            return;
        }
        var source = entry.Source;
        if (string.IsNullOrEmpty(source))
        {
            StatusText = "该行没有会话来源";
            return;
        }
        if (_activateSession is null)
        {
            StatusText = $"无法跳到「{source}」";
            return;
        }
        if (_activateSession(source))
            StatusText = $"已跳到「{source}」";
        else
            StatusText = $"未找到会话「{source}」";
    }

    /// <summary>Rebuild the session-name filter list (call when sessions change). The
    /// selection follows its name; when the rebind lands on a different index, the new
    /// selection's filters are restored as with any switch. The per-session filter
    /// map is never cleared here — dropped sessions simply keep their entry.</summary>
    public void RefreshSessions()
    {
        var selected = SessionFilterIndex > 0 && SessionFilterIndex < SessionNames.Count
            ? SessionNames[SessionFilterIndex] : null;
        SessionNames.Clear();
        SessionNames.Add(TerminalHub.Core.Localization.Localizer.Current.Translate("全部会话"));
        foreach (var n in _sessionNames()) SessionNames.Add(n);
        var idx = selected is null ? 0 : SessionNames.IndexOf(selected);
        SessionFilterIndex = idx >= 0 ? idx : 0;
    }

    public void Dispose()
    {
        _file.Failed -= OnFileWriteFailed;
        ((INotifyCollectionChanged)_dashboard.OutputLog).CollectionChanged -= OnLogChanged;
    }
    public void RefreshLanguage()
    { if (SessionNames.Count > 0) SessionNames[0] = TerminalHub.Core.Localization.Localizer.Current.Translate("全部会话"); }
}
