using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using TerminalHub.Core.Logging;

namespace TerminalHub.App.ViewModels;

/// <summary>
/// Right-rail Logs tab: filtered view over the live session-output stream
/// (the same lines shown in the bottom Output tab) + optional file sink.
/// </summary>
public partial class LogsViewModel : ViewModelBase, IDisposable
{
    private const int MaxShown = 400;
    private static readonly string[] LevelNames = ["info", "warn", "error"];

    private readonly DashboardViewModel _dashboard;
    private readonly SessionLogFile _file;
    private readonly string? _logDir;
    private readonly Func<IReadOnlyList<string>> _sessionNames;
    private readonly Action<bool>? _persistFileLogging;

    /// <summary>Filtered view of <see cref="DashboardViewModel.OutputLog"/>.</summary>
    public ObservableCollection<LogEntry> Entries { get; } = [];

    /// <summary>Session filter options; index 0 = all sessions.</summary>
    public ObservableCollection<string> SessionNames { get; } = ["全部会话"];

    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private int _levelFilterIndex;    // 0 all, 1 info, 2 warn, 3 error
    [ObservableProperty] private int _sessionFilterIndex;
    [ObservableProperty] private bool _fileLogging;
    [ObservableProperty] private string _fileStatus = "";

    public LogsViewModel(
        DashboardViewModel dashboard,
        SessionLogFile file,
        Func<IReadOnlyList<string>> sessionNames,
        bool fileLogging = false,
        Action<bool>? persistFileLogging = null,
        string? logDir = null)
    {
        _dashboard = dashboard;
        _file = file;
        _logDir = logDir;
        _sessionNames = sessionNames;
        _persistFileLogging = persistFileLogging;
        ((INotifyCollectionChanged)_dashboard.OutputLog).CollectionChanged += OnLogChanged;
        Refilter();
        if (fileLogging) FileLogging = true; // goes through OnFileLoggingChanged
    }

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
        {
            foreach (LogEntry entry in e.NewItems)
            {
                if (Matches(entry)) Entries.Add(entry);
            }
            while (Entries.Count > MaxShown) Entries.RemoveAt(0);
        }
        else if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            Entries.Clear();
        }
        else
        {
            Refilter();
        }
    }

    private bool Matches(LogEntry e)
    {
        if (LevelFilterIndex > 0 && !string.Equals(e.Level, LevelNames[LevelFilterIndex - 1], StringComparison.OrdinalIgnoreCase))
            return false;
        if (SessionFilterIndex > 0 && SessionFilterIndex - 1 < SessionNames.Count - 1)
        {
            if (!string.Equals(e.Source, SessionNames[SessionFilterIndex], StringComparison.Ordinal))
                return false;
        }
        if (!string.IsNullOrEmpty(FilterText)
            && !e.Message.Contains(FilterText, StringComparison.OrdinalIgnoreCase)
            && !e.Source.Contains(FilterText, StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private void Refilter()
    {
        Entries.Clear();
        foreach (var e in _dashboard.OutputLog)
            if (Matches(e)) Entries.Add(e);
    }

    partial void OnFilterTextChanged(string value) => Refilter();
    partial void OnLevelFilterIndexChanged(int value) => Refilter();
    partial void OnSessionFilterIndexChanged(int value) => Refilter();

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

    /// <summary>Rebuild the session-name filter list (call when the Logs tab opens).</summary>
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
