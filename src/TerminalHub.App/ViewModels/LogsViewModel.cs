using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHub.Core.Logging;

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
    private readonly int _capacity;

    /// <summary>True while <see cref="ApplyPersistedFilters"/> is replaying saved values —
    /// suppresses the write-back so a load never triggers a save.</summary>
    private bool _restoringFilters;

    /// <summary>Deep history of session lines; independent of Output's display cap.</summary>
    private readonly List<LogEntry> _buffer = [];

    private Regex? _regex;      // compiled when UseRegex && pattern valid
    private bool _regexInvalid; // pattern present but broken → match nothing, show error

    /// <summary>Filtered view of the internal buffer.</summary>
    public ObservableCollection<LogEntry> Entries { get; } = [];

    /// <summary>Session filter options; index 0 = all sessions.</summary>
    public ObservableCollection<string> SessionNames { get; } = ["全部会话"];

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

    /// <summary>On (default): the list stays pinned to the newest line — new lines auto-scroll
    /// to the bottom. User scroll-up pauses it; the「⬇ 跟随」button or scrolling back to
    /// the bottom resumes. New lines while paused never flip this back on.</summary>
    [ObservableProperty] private bool _followTail = true;

    /// <summary>True while following is paused — drives the floating「⬇ 跟随」button.</summary>
    public bool FollowPaused => !FollowTail;

    partial void OnFollowTailChanged(bool value) => OnPropertyChanged(nameof(FollowPaused));

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
        Action? persistFilters = null)
    {
        _dashboard = dashboard;
        _file = file;
        _logDir = logDir;
        _sessionNames = sessionNames;
        _persistFileLogging = persistFileLogging;
        _capacity = Math.Max(1, bufferCapacity);
        _copyToClipboard = copyToClipboard;
        _promptExportPath = promptExportPath;
        _persistFilters = persistFilters;
        ((INotifyCollectionChanged)_dashboard.OutputLog).CollectionChanged += OnLogChanged;
        Refilter();
        if (fileLogging) FileLogging = true; // goes through OnFileLoggingChanged
    }

    /// <summary>Restore the filter state saved from a previous run (level index clamped to
    /// the bar). The restore itself never writes back: <see cref="PersistFilters"/> is
    /// suppressed while applying, so loading cannot trigger a save.</summary>
    public void ApplyPersistedFilters(string? filterText, bool useRegex, int levelFilterIndex, bool retainHistoryOnClear)
    {
        _restoringFilters = true;
        try
        {
            FilterText = filterText ?? "";
            UseRegex = useRegex;
            LevelFilterIndex = Math.Clamp(levelFilterIndex, 0, LevelNames.Length);
            RetainHistoryOnClear = retainHistoryOnClear;
        }
        finally { _restoringFilters = false; }
    }

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems is not null:
                foreach (LogEntry entry in e.NewItems)
                {
                    _buffer.Add(entry);
                    while (_buffer.Count > _capacity)
                    {
                        var evicted = _buffer[0];
                        _buffer.RemoveAt(0);
                        var shown = Entries.IndexOf(evicted);
                        if (shown >= 0) Entries.RemoveAt(shown);
                    }
                    if (Matches(entry)) Entries.Add(entry);
                }
                break;
            case NotifyCollectionChangedAction.Reset:
                // Output panel cleared. Default: follow (drop our history too);
                // 「保留历史」 keeps the deep buffer for later review.
                if (!RetainHistoryOnClear)
                {
                    _buffer.Clear();
                    Entries.Clear();
                }
                break;
            default:
                // Output trimmed its oldest line (Remove) — our buffer keeps its own copy.
                break;
        }
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

    partial void OnFilterTextChanged(string value) { UpdateRegex(); Refilter(); PersistFilters(); }
    partial void OnUseRegexChanged(bool value) { UpdateRegex(); Refilter(); PersistFilters(); }
    partial void OnLevelFilterIndexChanged(int value)
    {
        Refilter();
        foreach (var chip in LevelChipProps) OnPropertyChanged(chip);
        PersistFilters();
    }
    partial void OnSessionFilterIndexChanged(int value) => Refilter();
    partial void OnRetainHistoryOnClearChanged(bool value) => PersistFilters();

    /// <summary>Filter change → save callback (wired by MainWindowViewModel to settings + disk).</summary>
    private void PersistFilters()
    {
        if (!_restoringFilters) _persistFilters?.Invoke();
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
                FileStatus = $"日志文件打开失败: {ex.Message}";
            }
        }
        else
        {
            _file.Disable();
            FileStatus = "";
        }
        _persistFileLogging?.Invoke(value);
    }

    /// <summary>One line as shown in the panel: HH:mm:ss [level] (source) message.</summary>
    public static string FormatLine(LogEntry e)
        => $"{e.Time:HH:mm:ss} [{e.Level}]{(string.IsNullOrEmpty(e.Source) ? "" : $" ({e.Source})")} {e.Message}";

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

    /// <summary>Dismiss the current filter results — the visible lines are removed
    /// from the buffer as well, so they don't reappear when the filter changes.</summary>
    [RelayCommand]
    private void ClearVisible()
    {
        if (Entries.Count == 0) return;
        var dismissed = new HashSet<LogEntry>(Entries, ReferenceEqualityComparer.Instance);
        _buffer.RemoveAll(e => dismissed.Contains(e));
        var n = Entries.Count;
        Entries.Clear();
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

    /// <summary>Rebuild the session-name filter list (call when sessions change).</summary>
    public void RefreshSessions()
    {
        var selected = SessionFilterIndex > 0 && SessionFilterIndex < SessionNames.Count
            ? SessionNames[SessionFilterIndex] : null;
        SessionNames.Clear();
        SessionNames.Add("全部会话");
        foreach (var n in _sessionNames()) SessionNames.Add(n);
        var idx = selected is null ? 0 : SessionNames.IndexOf(selected);
        SessionFilterIndex = idx >= 0 ? idx : 0;
    }

    public void Dispose()
        => ((INotifyCollectionChanged)_dashboard.OutputLog).CollectionChanged -= OnLogChanged;
}
