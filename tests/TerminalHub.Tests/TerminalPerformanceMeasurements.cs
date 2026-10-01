using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using TerminalHub.App.Controls;
using TerminalHub.Core.Terminal;
using Xunit;

namespace TerminalHub.Tests;

public sealed class PerformanceMeasurementFactAttribute : FactAttribute
{
    public PerformanceMeasurementFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TERMINALHUB_PERF_REPORT")))
            Skip = "Set TERMINALHUB_PERF_REPORT to an output JSON path to run measurements.";
    }
}

// An opt-in measurement, without pass/fail performance thresholds. Exercises the
// real parser, stable frames and main/preview drawing in Avalonia Headless.
public class TerminalPerformanceMeasurements
{
    private sealed record Measurement(int Sessions, string Load, int Rounds, double WallMs,
        double CpuMs, double ParseMs, double CaptureMs, double RenderMs,
        double AllocatedMiB, double WorkingSetMiB, double SwitchP95Ms);

    [PerformanceMeasurementFact]
    public Task MeasureParserFramesAndViews() =>
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(TerminalPerformanceMeasurements).Assembly)
            .Dispatch(() =>
            {
                var measurements = new List<Measurement>();
                foreach (var count in new[] { 1, 5, 10 })
                    foreach (var load in new[] { "idle", "one", "all" })
                    {
                        Run(count, load, 30); // warm fonts, JIT and drawing caches
                        for (var repeat = 0; repeat < 3; repeat++)
                            measurements.Add(Run(count, load, 300));
                    }
                var path = Environment.GetEnvironmentVariable("TERMINALHUB_PERF_REPORT")!;
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                File.WriteAllText(path, JsonSerializer.Serialize(new
                {
                    CapturedUtc = DateTime.UtcNow,
                    Environment = new { System.Environment.OSVersion, System.Environment.ProcessorCount,
                        Framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription },
                    Grid = "100x28", MainSize = "900x500", PreviewSize = "240x135",
                    Method = "300 logical 60-Hz rounds, previews every third round; no real-time pacing or PTY/desktop compositor",
                    Measurements = measurements
                }, new JsonSerializerOptions { WriteIndented = true }));
                return Task.FromResult(true);
            }, CancellationToken.None);

    private static Measurement Run(int count, string load, int rounds)
    {
        var terminals = Enumerable.Range(0, count).Select(_ => new TerminalEmulator(columns: 100, rows: 28)).ToArray();
        var main = new TerminalView { Emulator = terminals[0] };
        main.Arrange(new Rect(0, 0, 900, 500));
        var previews = terminals.Select(t => new StagePreview { Emulator = t }).ToArray();
        foreach (var preview in previews) preview.Arrange(new Rect(0, 0, 240, 135));
        const string initial = "\x1b[36mworker\x1b[0m build step completed 0123456789 abcdefghijklmnopqrstuvwxyz\r\n";
        foreach (var t in terminals) t.Parser.Feed(string.Concat(Enumerable.Repeat(initial, 28)));
        Draw(main);
        foreach (var preview in previews) Draw(preview);
        using var process = Process.GetCurrentProcess();
        var allocated = GC.GetTotalAllocatedBytes(true);
        var cpu = process.TotalProcessorTime;
        var wall = Stopwatch.StartNew();
        long parse = 0, capture = 0, render = 0;
        try
        {
            for (var round = 0; round < rounds; round++)
            {
                var active = load == "idle" ? 0 : load == "one" ? 1 : count;
                var start = Stopwatch.GetTimestamp();
                for (var i = 0; i < active; i++)
                    terminals[i].Parser.Feed($"\x1b[32m{round:D4}\x1b[0m worker {i} build output abcdefghijklmnopqrstuvwxyz 0123456789\r\n");
                parse += Stopwatch.GetTimestamp() - start;
                start = Stopwatch.GetTimestamp();
                // Querying cached frames models main + thumbnail consumers.
                foreach (var t in terminals) t.Buffer.CaptureFrame();
                capture += Stopwatch.GetTimestamp() - start;
                start = Stopwatch.GetTimestamp();
                if (active > 0 || round % 32 == 0) Draw(main);
                if (round % 3 == 0)
                    for (var i = 0; i < active; i++) Draw(previews[i]);
                render += Stopwatch.GetTimestamp() - start;
            }
            wall.Stop();
            process.Refresh();
            var cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds;
            var allocatedMiB = (GC.GetTotalAllocatedBytes(true) - allocated) / 1048576.0;
            var switches = new List<double>();
            for (var i = 0; i < 50; i++)
            {
                var start = Stopwatch.GetTimestamp();
                main.Emulator = terminals[i % count];
                Draw(main);
                switches.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }
            switches.Sort();
            return new(count, load, rounds, wall.Elapsed.TotalMilliseconds, cpuMs,
                parse * 1000.0 / Stopwatch.Frequency, capture * 1000.0 / Stopwatch.Frequency,
                render * 1000.0 / Stopwatch.Frequency, allocatedMiB, process.WorkingSet64 / 1048576.0,
                switches[47]);
        }
        finally { foreach (var terminal in terminals) terminal.Dispose(); }
    }

    private static void Draw(TerminalView view)
    {
        using var context = new DrawingGroup().Open();
        view.Render(context);
    }
}
