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
        Assert.Equal(OperatingSystem.IsWindows() ? new[] { 1, 2, 3 } : [2, 3, 1], vm.Processes.Select(p => p.Pid));
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

    [AvaloniaFact]
    public async Task KillProcess_InvokesKiller_AndLogs()
    {
        var killed = new List<int>();
        var vm = new DashboardViewModel(new FakeMonitor(), killPid: killed.Add);
        var row = Proc(42, "victim", 1, 10);

        vm.KillProcessCommand.Execute(row);
        Assert.Equal([42], killed);

        await Until(() => vm.OutputLog.Any(l => l.Message.Contains("已结束进程 victim")));
        Assert.Contains(vm.OutputLog, l => l.Source == "proc");
    }

    [AvaloniaFact]
    public async Task KillProcess_Failure_LogsError()
    {
        var vm = new DashboardViewModel(new FakeMonitor(),
            killPid: _ => throw new InvalidOperationException("denied"));
        vm.KillProcessCommand.Execute(Proc(9, "stubborn", 0, 0));

        await Until(() => vm.OutputLog.Any());
        Assert.Contains(vm.OutputLog, l => l.Level == "error" && l.Message.Contains("无法结束 stubborn"));
    }

    [AvaloniaFact]
    public async Task WindowsDefaultOrder_PreservesProviderTieOrdering_UntilExplicitSort()
    {
        if (!OperatingSystem.IsWindows()) return;
        var monitor = new FakeMonitor();
        var vm = new DashboardViewModel(monitor);
        monitor.Push([Proc(3, "large", 0, 300), Proc(1, "small", 0, 100), Proc(2, "medium", 0, 200)]);
        await Until(() => vm.Processes.Count == 3);
        Assert.Equal([3, 1, 2], vm.Processes.Select(p => p.Pid));
        vm.SortProcessesCommand.Execute("pid");
        Assert.Equal([1, 2, 3], vm.Processes.Select(p => p.Pid));
    }
}
