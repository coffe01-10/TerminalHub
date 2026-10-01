using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using TerminalHub.Core.Monitoring;

namespace TerminalHub.App.ViewModels;

/// <summary>Bottom-panel log entry (Output tab). Source is the session name, "deploy" for publish output, or "" for app events.</summary>
public sealed record LogEntry(DateTime Time, string Level, string Message, string Source = "");

/// <summary>Right dashboard + bottom Output/Debug/Problems/Search panel.</summary>
public partial class DashboardViewModel : ViewModelBase
{
    private readonly ISystemMonitor _monitor;
    private readonly SparklineBuffer _cpu = new(60);
    private readonly SparklineBuffer _mem = new(60);
    private readonly SparklineBuffer _netRx = new(60);
    private readonly SparklineBuffer _netTx = new(60);

    public ObservableCollection<ProcessInfo> Processes { get; } = [];
    public ObservableCollection<LogEntry> OutputLog { get; } = [];
    /// <summary>Rows the Output tab actually shows — <see cref="OutputLog"/>
    /// filtered by <see cref="OutputLevelFilter"/>. Entries are never dropped.</summary>
    public ObservableCollection<LogEntry> VisibleOutput { get; } = [];
    public ObservableCollection<LogEntry> Problems { get; } = [];
    /// <summary>Raw (pre-ANSI-strip, escaped) session lines for the Debug tab.</summary>
    public ObservableCollection<LogEntry> DebugLog { get; } = [];
    /// <summary>Search hits over the active session's scrollback+screen.</summary>
    public ObservableCollection<TerminalHub.Core.Terminal.ScreenBuffer.SearchHit> SearchHits { get; } = [];

    /// <summary>Process table sort keys (column headers cycle asc/desc).</summary>
    public enum ProcSort { Pid, Name, Cpu, Mem }

    [ObservableProperty] private ProcSort _processSort = ProcSort.Cpu;
    [ObservableProperty] private bool _processSortAsc;      // cpu/mem desc, pid/name asc by default
    private IReadOnlyList<ProcessInfo> _lastProcesses = [];

    [ObservableProperty] private int _selectedRightTab;       // 0 Proc 1 Files 2 Logs 3 Ssh
    [ObservableProperty] private int _selectedBottomTab;      // 0 Output 1 Debug 2 Problems 3 Search
    /// <summary>Output tab level filter: 0 全部 · 1 info · 2 warn · 3 error.</summary>
    [ObservableProperty] private int _outputLevelFilter;
    [ObservableProperty] private int _problemCount;
    [ObservableProperty] private string _searchQuery = "";
    [ObservableProperty] private string _searchStatus = "";

    /// <summary>Supplies the buffer to search (active session); set by the shell VM.</summary>
    public Func<TerminalHub.Core.Terminal.ScreenBuffer?>? BufferSource { get; set; }

    partial void OnSearchQueryChanged(string value) => RefreshSearch();
    partial void OnSelectedBottomTabChanged(int value)
    {
        if (value == 3) RefreshSearch();
    }

    public void RefreshSearch()
    {
        SearchHits.Clear();
        var buf = BufferSource?.Invoke();
        var q = SearchQuery;
        if (buf is null)
        {
            SearchStatus = "无活动会话";
            return;
        }
        if (string.IsNullOrWhiteSpace(q))
        {
            SearchStatus = "";
            return;
        }
        List<TerminalHub.Core.Terminal.ScreenBuffer.SearchHit> hits;
        lock (buf.SyncRoot) hits = buf.SearchLines(q.Trim());
        foreach (var h in hits) SearchHits.Add(h);
        SearchStatus = SearchHits.Count == 0 ? "无匹配" : $"{SearchHits.Count} 处匹配";
    }

    public bool HasProblems => ProblemCount > 0;
    partial void OnProblemCountChanged(int value) => OnPropertyChanged(nameof(HasProblems));
    [ObservableProperty] private string _cpuPercent = "0%";
    [ObservableProperty] private string _memUsage = "";
    [ObservableProperty] private string _memTotal = "";
    [ObservableProperty] private string _diskUsage = "";
    [ObservableProperty] private string _diskPercent = "0%";
    [ObservableProperty] private double _diskFraction;
    [ObservableProperty] private string _netDown = "0 B/s";
    [ObservableProperty] private string _netUp = "0 B/s";
    [ObservableProperty] private double[] _cpuSpark = [];
    [ObservableProperty] private double[] _memSpark = [];
    [ObservableProperty] private double[] _netDownSpark = [];
    [ObservableProperty] private double[] _netUpSpark = [];

    public DashboardViewModel(ISystemMonitor monitor)
    {
        _monitor = monitor;
        _monitor.Sampled += OnSampled;
    }

    private void OnSampled(ISystemMonitor m)
    {
        var s = m.Current;
        _cpu.Add(s.CpuPercent);
        _mem.Add(s.MemoryPercent);
        _netRx.Add(s.NetworkRxBytesPerSec / 1048576.0);
        _netTx.Add(s.NetworkTxBytesPerSec / 1048576.0);

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            CpuPercent = $"{(int)s.CpuPercent}%";
            MemUsage = MainWindowViewModel.FmtGb(s.MemoryUsedBytes);
            MemTotal = $"/ {MainWindowViewModel.FmtGb(s.MemoryTotalBytes)}";
            DiskUsage = $"{FmtShort(s.DiskUsedBytes)} / {FmtShort(s.DiskTotalBytes)}";
            DiskPercent = $"{(int)s.DiskPercent}%";
            DiskFraction = s.DiskPercent / 100.0;
            NetDown = $"↓ {FmtRate(s.NetworkRxBytesPerSec)}";
            NetUp = $"↑ {FmtRate(s.NetworkTxBytesPerSec)}";
            CpuSpark = _cpu.ToArray();
            MemSpark = _mem.ToArray();
            NetDownSpark = _netRx.ToArray();
            NetUpSpark = _netTx.ToArray();

            _lastProcesses = m.Processes;
            ResortProcesses();
        });
    }

    /// <summary>Sort indicator for the active column header ("" elsewhere).</summary>
    public string PidMark => SortMark(ProcSort.Pid);
    public string NameMark => SortMark(ProcSort.Name);
    public string CpuMark => SortMark(ProcSort.Cpu);
    public string MemMark => SortMark(ProcSort.Mem);
    private string SortMark(ProcSort k) => ProcessSort == k ? (ProcessSortAsc ? "▲" : "▼") : "";

    partial void OnProcessSortChanged(ProcSort value) { ResortProcesses(); RefreshSortMarks(); }
    partial void OnProcessSortAscChanged(bool value) { ResortProcesses(); RefreshSortMarks(); }

    private void RefreshSortMarks()
    {
        OnPropertyChanged(nameof(PidMark));
        OnPropertyChanged(nameof(NameMark));
        OnPropertyChanged(nameof(CpuMark));
        OnPropertyChanged(nameof(MemMark));
    }

    /// <summary>Header click: same key flips direction; pid/name default asc, cpu/mem desc.</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void SortProcesses(string key)
    {
        var k = key switch
        {
            "pid" => ProcSort.Pid,
            "name" => ProcSort.Name,
            "mem" => ProcSort.Mem,
            _ => ProcSort.Cpu,
        };
        if (ProcessSort == k) ProcessSortAsc = !ProcessSortAsc;
        else { ProcessSort = k; ProcessSortAsc = k is ProcSort.Pid or ProcSort.Name; }
    }

    private void ResortProcesses()
    {
        var list = _lastProcesses.ToList();
        int Cmp(ProcessInfo a, ProcessInfo b) => ProcessSort switch
        {
            ProcSort.Pid => a.Pid.CompareTo(b.Pid),
            ProcSort.Name => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
            ProcSort.Mem => a.MemoryBytes.CompareTo(b.MemoryBytes),
            _ => a.CpuPercent.CompareTo(b.CpuPercent),
        };
        list.Sort((a, b) => ProcessSortAsc ? Cmp(a, b) : Cmp(b, a));
        Processes.Clear();
        foreach (var p in list) Processes.Add(p);
    }

    private const int MaxOutputLines = 500;
    private const int MaxProblemLines = 200;
    private const int MaxDebugLines = 300;

    // PTY read threads can emit thousands of lines per second — one
    // Dispatcher.Post per line floods the UI queue and freezes the window.
    // Lines queue here and a single scheduled flush drains them all.
    private readonly ConcurrentQueue<(string level, string message, string source)> _pendingOutput = new();
    private readonly ConcurrentQueue<(string message, string source)> _pendingDebug = new();
    private int _outputFlushScheduled;
    private int _debugFlushScheduled;

    /// <summary>Append a raw (ANSI-escaped) line to the Debug tab.</summary>
    public void AppendDebug(string message, string source = "")
    {
        _pendingDebug.Enqueue((message, source));
        if (Interlocked.Exchange(ref _debugFlushScheduled, 1) == 0)
            Avalonia.Threading.Dispatcher.UIThread.Post(FlushDebug, Avalonia.Threading.DispatcherPriority.Background);
    }

    private void FlushDebug()
    {
        // Release the flag BEFORE draining so lines enqueued mid-drain schedule
        // another flush instead of waiting for the next Append.
        _debugFlushScheduled = 0;
        var now = DateTime.Now;
        while (_pendingDebug.TryDequeue(out var e))
        {
            DebugLog.Add(new LogEntry(now, "debug", e.message, e.source));
        }
        while (DebugLog.Count > MaxDebugLines) DebugLog.RemoveAt(0);
    }

    /// <summary>Append a line from a real session stream (PTY output) or an app event.</summary>
    public void AppendOutput(string level, string message, string source = "")
    {
        _pendingOutput.Enqueue((level, message, source));
        if (Interlocked.Exchange(ref _outputFlushScheduled, 1) == 0)
            Avalonia.Threading.Dispatcher.UIThread.Post(FlushOutput, Avalonia.Threading.DispatcherPriority.Background);
    }

    private void FlushOutput()
    {
        _outputFlushScheduled = 0;
        var now = DateTime.Now;
        while (_pendingOutput.TryDequeue(out var e))
        {
            var entry = new LogEntry(now, e.level, e.message, e.source);
            OutputLog.Add(entry);
            if (PassesOutputFilter(entry)) VisibleOutput.Add(entry);
            if (e.level == "error")
                Problems.Add(new LogEntry(now, e.level, e.message, e.source));
        }
        while (OutputLog.Count > MaxOutputLines)
        {
            var dropped = OutputLog[0];
            OutputLog.RemoveAt(0);
            VisibleOutput.Remove(dropped);
        }
        while (VisibleOutput.Count > MaxOutputLines) VisibleOutput.RemoveAt(0);
        while (Problems.Count > MaxProblemLines) Problems.RemoveAt(0);
        ProblemCount = Problems.Count;
    }

    private static readonly string[] OutputLevelNames = ["", "info", "warn", "error"];

    private bool PassesOutputFilter(LogEntry e)
        => OutputLevelFilter is < 1 or > 3 || e.Level == OutputLevelNames[OutputLevelFilter];

    partial void OnOutputLevelFilterChanged(int value)
    {
        VisibleOutput.Clear();
        foreach (var e in OutputLog)
            if (PassesOutputFilter(e)) VisibleOutput.Add(e);
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    public void ClearOutput()
    {
        OutputLog.Clear();
        VisibleOutput.Clear();
        ClearProblems();
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    public void ClearProblems()
    {
        Problems.Clear();
        ProblemCount = 0;
    }

    private static string FmtShort(double bytes)
    {
        if (bytes >= 1L << 30) return $"{bytes / (1L << 30):0} GB";
        if (bytes >= 1L << 20) return $"{bytes / (1L << 20):0} MB";
        return $"{bytes / 1024:0} KB";
    }

    private static string FmtRate(double bytesPerSec)
    {
        if (bytesPerSec >= 1L << 20) return $"{bytesPerSec / (1L << 20):0.0} MB/s";
        if (bytesPerSec >= 1L << 10) return $"{bytesPerSec / (1L << 10):0.0} KB/s";
        return $"{bytesPerSec:0} B/s";
    }
}
