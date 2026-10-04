using System.Reflection;
using System.Text;
using System.Threading.Channels;
using TerminalHub.Core.Terminal;
using Xunit;

namespace TerminalHub.Tests;

public class RecordingCongestionTests
{
    private sealed class PausedStream : MemoryStream
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            await base.WriteAsync(buffer, cancellationToken);
        }
    }

    private static TerminalRecorder Create(TerminalEmulator terminal, string path, PausedStream stream) =>
        (TerminalRecorder)Activator.CreateInstance(typeof(TerminalRecorder), BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { terminal, path, "congestion", stream }, null)!;

    private static Channel<RecordingEvent> Queue(TerminalRecorder recorder) =>
        (Channel<RecordingEvent>)typeof(TerminalRecorder).GetField("_queue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(recorder)!;

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task GapBreaksPartialControlOrUtf8_AndPendingResizePrecedesResumedOutput(bool utf8)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".threc");
        using var terminal = new TerminalEmulator(columns: 6, rows: 3);
        using var stream = new PausedStream();
        var recorder = Create(terminal, path, stream);
        try
        {
            terminal.Parser.Feed(new string('a', 5000));
            await stream.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            for (var i = 0; i < 1023; i++) terminal.Parser.Feed("x");
            terminal.Parser.Feed(utf8 ? new byte[] { 0xe4, 0xb8 } : Encoding.ASCII.GetBytes("\x1b["));
            Assert.Equal(1024, Queue(recorder).Reader.Count);
            terminal.Parser.Feed(utf8 ? new byte[] { 0xad } : Encoding.ASCII.GetBytes("31m")); // dropped
            terminal.Resize(10, 3); // queue is still full
            stream.Release.TrySetResult();
            var deadline = Environment.TickCount64 + 5000;
            while (Queue(recorder).Reader.Count != 0 && Environment.TickCount64 < deadline) await Task.Delay(10);
            Assert.Equal(0, Queue(recorder).Reader.Count);
            terminal.Parser.Feed("ok");
            await recorder.DisposeAsync();
            await File.WriteAllBytesAsync(path, stream.ToArray());
            using var playback = await TerminalPlayback.LoadAsync(path);
            var gap = Assert.Single(playback.Events, e => e.Kind == "gap");
            var resize = Assert.Single(playback.Events, e => e.Kind == "resize");
            var resumed = Assert.Single(playback.Events, e => e.Kind == "output" && Encoding.UTF8.GetString(e.Data!) == "ok");
            Assert.True(resize.TimeMs <= resumed.TimeMs && gap.TimeMs <= resumed.TimeMs);
            playback.Seek(playback.DurationMs);
            Assert.Equal(10, playback.Emulator.Buffer.Columns);
            Assert.Contains("ok", playback.Emulator.Buffer.TailText(3));
            Assert.DoesNotContain("\uFFFD", playback.Emulator.Buffer.TailText(3));
            playback.Seek(0); playback.Seek(playback.DurationMs);
            Assert.Contains("ok", playback.Emulator.Buffer.TailText(3));
        }
        finally
        {
            stream.Release.TrySetResult();
            await recorder.DisposeAsync();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task StopWithFullQueueFlushesLastResizeGapAndEnd()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".threc");
        using var terminal = new TerminalEmulator(columns: 6, rows: 3);
        using var stream = new PausedStream();
        var recorder = Create(terminal, path, stream);
        try
        {
            terminal.Parser.Feed(new string('a', 5000));
            await stream.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            for (var i = 0; i < 1024; i++) terminal.Parser.Feed("x");
            terminal.Parser.Feed("lost");
            terminal.Resize(10, 4);
            var stop = recorder.DisposeAsync().AsTask();
            Assert.False(stop.IsCompleted);
            stream.Release.TrySetResult();
            await stop.WaitAsync(TimeSpan.FromSeconds(5));
            await File.WriteAllBytesAsync(path, stream.ToArray());
            using var playback = await TerminalPlayback.LoadAsync(path);
            Assert.Equal(new[] { "resize", "gap", "end" }, playback.Events.TakeLast(3).Select(e => e.Kind));
            playback.Seek(playback.DurationMs);
            Assert.Equal(10, playback.Emulator.Buffer.Columns);
            Assert.Equal(4, playback.Emulator.Buffer.Rows);
        }
        finally
        {
            stream.Release.TrySetResult();
            await recorder.DisposeAsync();
            File.Delete(path);
        }
    }
}
