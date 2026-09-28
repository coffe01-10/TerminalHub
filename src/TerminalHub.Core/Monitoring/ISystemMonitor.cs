namespace TerminalHub.Core.Monitoring;

/// <summary>One row of the process table.</summary>
public sealed record ProcessInfo
{
    public required int Pid { get; init; }
    public required string Name { get; init; }
    public double CpuPercent { get; init; }
    public double MemoryBytes { get; init; }
}

/// <summary>A point-in-time system resource sample.</summary>
public sealed record SystemSample
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public double CpuPercent { get; init; }
    public double MemoryUsedBytes { get; init; }
    public double MemoryTotalBytes { get; init; }
    public double DiskUsedBytes { get; init; }
    public double DiskTotalBytes { get; init; }
    public double NetworkRxBytesPerSec { get; init; }
    public double NetworkTxBytesPerSec { get; init; }

    public double MemoryPercent => MemoryTotalBytes > 0 ? MemoryUsedBytes / MemoryTotalBytes * 100 : 0;
    public double DiskPercent => DiskTotalBytes > 0 ? DiskUsedBytes / DiskTotalBytes * 100 : 0;
}

/// <summary>Periodic system + process sampler.</summary>
public interface ISystemMonitor : IDisposable
{
    /// <summary>Latest aggregated sample.</summary>
    SystemSample Current { get; }

    /// <summary>Latest process list snapshot (sorted by CPU desc).</summary>
    IReadOnlyList<ProcessInfo> Processes { get; }

    /// <summary>Raised after each sampling tick.</summary>
    event Action<ISystemMonitor>? Sampled;

    void Start(TimeSpan interval);
    void Stop();
}
