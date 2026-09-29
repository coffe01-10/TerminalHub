using System.Collections.ObjectModel;
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
    public ObservableCollection<LogEntry> Problems { get; } = [];
    /// <summary>Raw (pre-ANSI-strip, escaped) session lines for the Debug tab.</summary>
    public ObservableCollection<LogEntry> DebugLog { get; } = [];
    /// <summary>Search hits over the active session's scrollback+screen.</summary>
    public ObservableCollection<TerminalHub.Core.Terminal.ScreenBuffer.SearchHit> SearchHits { get; } = [];

    [ObservableProperty] private int _selectedRightTab;       // 0 Proc 1 Files 2 Logs 3 Ssh 4 Codex
    [ObservableProperty] private int _selectedBottomTab;      // 0 Output 1 Debug 2 Problems 3 Search
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

            Processes.Clear();
            foreach (var p in m.Processes) Processes.Add(p);
        });
    }

    private const int MaxOutputLines = 500;
    private const int MaxProblemLines = 200;
    private const int MaxDebugLines = 300;

    /// <summary>Append a raw (ANSI-escaped) line to the Debug tab.</summary>
    public void AppendDebug(string message, string source = "")
        => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            DebugLog.Add(new LogEntry(DateTime.Now, "debug", message, source));
            while (DebugLog.Count > MaxDebugLines) DebugLog.RemoveAt(0);
        });

    /// <summary>Append a line from a real session stream (PTY output) or an app event.</summary>
    public void AppendOutput(string level, string message, string source = "")
        => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            OutputLog.Add(new LogEntry(DateTime.Now, level, message, source));
            while (OutputLog.Count > MaxOutputLines) OutputLog.RemoveAt(0);
            if (level == "error")
            {
                Problems.Add(new LogEntry(DateTime.Now, level, message, source));
                while (Problems.Count > MaxProblemLines) Problems.RemoveAt(0);
                ProblemCount = Problems.Count;
            }
        });

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    public void ClearOutput()
    {
        OutputLog.Clear();
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
