using Avalonia.Headless.XUnit;
using TerminalHub.App.ViewModels;
using TerminalHub.Core.Monitoring;
using Xunit;

namespace TerminalHub.Tests;

/// <summary>Processes 表头点击排序：同键翻转方向，默认 CPU 降序。</summary>
public class ProcessSortTests
{
    private sealed class FakeMonitor : ISystemMonitor
    {
        public SystemSample Current { get; } = new();
        public IReadOnlyList<ProcessInfo> Processes { get; private set; } = [];
        public event Action<ISystemMonitor>? Sampled;
        public void Push(IReadOnlyList<ProcessInfo> p) { Processes = p; Sampled?.Invoke(this); }
        public void Start(TimeSpan interval) { }
        public void Stop() { }
        public void Dispose() { }
    }

    private static ProcessInfo Proc(int pid, string name, double cpu, double mem)
        => new() { Pid = pid, Name = name, CpuPercent = cpu, MemoryBytes = mem };

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
        while (DateTime.UtcNow < deadline && !condition()) await Task.Delay(25);
    }

    [AvaloniaFact]
    public async Task Sample_DefaultSortsCpuDesc()
    {
        var mon = new FakeMonitor();
        var vm = new DashboardViewModel(mon);
        mon.Push([Proc(1, "a", 5.0, 100), Proc(2, "b", 40.0, 50), Proc(3, "c", 20.0, 300)]);

        await Until(() => vm.Processes.Count == 3);
        Assert.Equal([2, 3, 1], vm.Processes.Select(p => p.Pid));
        Assert.Equal("▼", vm.CpuMark);
    }

    [AvaloniaFact]
    public async Task HeaderClick_SameKeyFlips_NewKeyDefaults()
    {
        var mon = new FakeMonitor();
        var vm = new DashboardViewModel(mon);
        mon.Push([Proc(3, "b", 1, 10), Proc(1, "a", 2, 20), Proc(2, "c", 3, 30)]);
        await Until(() => vm.Processes.Count == 3);

        vm.SortProcessesCommand.Execute("name");
        Assert.Equal(["a", "b", "c"], vm.Processes.Select(p => p.Name));
        Assert.Equal("▲", vm.NameMark);

        vm.SortProcessesCommand.Execute("name");
        Assert.Equal(["c", "b", "a"], vm.Processes.Select(p => p.Name));

        vm.SortProcessesCommand.Execute("pid");
        Assert.Equal([1, 2, 3], vm.Processes.Select(p => p.Pid));

        vm.SortProcessesCommand.Execute("mem");
        Assert.Equal([30, 20, 10], vm.Processes.Select(p => p.MemoryBytes)); // mem → desc
    }
}
