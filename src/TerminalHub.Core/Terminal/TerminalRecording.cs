using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace TerminalHub.Core.Terminal;

public sealed record RecordingEvent(double TimeMs, string Kind, byte[]? Data = null, int Columns = 0, int Rows = 0);
public sealed record RecordingHeader(int Version, string Title, int Columns, int Rows, TerminalColorScheme ColorScheme, byte[] Initial);

public sealed class TerminalRecorder : IAsyncDisposable
{
    private readonly TerminalEmulator _emulator;
    // Bounded: JSON+磁盘吞吐永远追不上洪水输出，无界队列会让内存随输入速率
    // 线性涨。写不下就丢事件并标记溢出，回放端用 "gap" 事件标示缺口。
    private readonly Channel<RecordingEvent> _queue = Channel.CreateBounded<RecordingEvent>(new BoundedChannelOptions(1024) { SingleReader = true });
    private readonly long _started = Stopwatch.GetTimestamp();
    private readonly Task _writer;
    private int _overflow;
    // Latest unwritten resize, kept so a congested queue can't silently drop
    // it — playback would keep old dimensions and misplace every later cell.
    private int _resizeCols = -1, _resizeRows;
    private bool _stopped;
    public string Path { get; }
    public TerminalRecorder(TerminalEmulator emulator, string path, string title)
        : this(emulator, path, title, new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 65536, true)) { }

    private TerminalRecorder(TerminalEmulator emulator, string path, string title, Stream stream)
    {
        _emulator = emulator; Path = path;
        RecordingHeader header;
        lock (emulator.Buffer.SyncRoot)
        {
            var frame = emulator.Buffer.CaptureFrame();
            header = new(1, title, frame.Columns, frame.Rows, emulator.ColorScheme, Snapshot(frame, emulator.Buffer).Concat(emulator.Parser.PendingInput).ToArray());
            emulator.Parser.DataApplied += OnData;
            emulator.Resized += OnResize;
        }
        // Closing the main window waits for the file to flush; the writer must never need the UI thread.
        _writer = Task.Run(() => WriteAsync(stream, header));
    }
    private double Time => Stopwatch.GetElapsedTime(_started).TotalMilliseconds;
    private void OnData(ReadOnlyMemory<byte> data)
    {
        // Ordering contract: a pending resize and the gap marker must land
        // before any later output, otherwise the recording silently merges
        // non-contiguous bytes / stale dimensions into a continuous stream.
        if (!FlushResize()) { Interlocked.Exchange(ref _overflow, 1); return; }
        if (_overflow == 1)
        {
            if (!_queue.Writer.TryWrite(new(Time, "gap"))) return;
            Interlocked.Exchange(ref _overflow, 0);
        }
        if (!_queue.Writer.TryWrite(new(Time, "output", data.ToArray())))
            Interlocked.Exchange(ref _overflow, 1);
    }
    private bool FlushResize()
    {
        if (_resizeCols < 0) return true;
        if (!_queue.Writer.TryWrite(new(Time, "resize", null, _resizeCols, _resizeRows))) return false;
        _resizeCols = -1;
        return true;
    }
    private void OnResize(int columns, int rows)
    {
        // Only the latest size matters; on failure keep it pending for OnData.
        if (_queue.Writer.TryWrite(new(Time, "resize", null, columns, rows)))
            _resizeCols = -1;
        else
        {
            _resizeCols = columns; _resizeRows = rows;
        }
    }
    private async Task WriteAsync(Stream stream, RecordingHeader header)
    {
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        try
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(header));
            await foreach (var item in _queue.Reader.ReadAllAsync()) await writer.WriteLineAsync(JsonSerializer.Serialize(item));
            await writer.FlushAsync();
        }
        catch (Exception ex) { _queue.Writer.TryComplete(ex); throw; }
    }
    public async ValueTask DisposeAsync()
    {
        bool stop = false;
        RecordingEvent? resize = null, gap = null;
        lock (_emulator.Buffer.SyncRoot)
        {
            if (!_stopped)
            {
                _stopped = true; _emulator.Parser.DataApplied -= OnData; _emulator.Resized -= OnResize;
                stop = true;
                if (_resizeCols >= 0) resize = new(Time, "resize", null, _resizeCols, _resizeRows);
                if (_overflow != 0) gap = new(Time, "gap");
            }
        }
        if (stop)
        {
            // Once detached, waiting for disk capacity is safe outside SyncRoot.
            // A full queue at stop must not lose its final size, gap, or end marker.
            if (resize is not null) await _queue.Writer.WriteAsync(resize).ConfigureAwait(false);
            if (gap is not null) await _queue.Writer.WriteAsync(gap).ConfigureAwait(false);
            await _queue.Writer.WriteAsync(new(Time, "end")).ConfigureAwait(false);
            _queue.Writer.TryComplete();
        }
        await _writer.ConfigureAwait(false);
    }
    private static byte[] Snapshot(TerminalFrame frame, ScreenBuffer buffer)
    {
        var s = new StringBuilder("\u001bc");
        if (frame.AlternateScreen)
        {
            if (buffer.CaptureRecordingPrimary() is { } primary)
            {
                AppendScreen(s, primary);
                var style = buffer.RecordingPrimaryStyle;
                s.Append($"\x1b[{primary.CursorY + 1};{primary.CursorX + 1}H\x1b[0");
                AppendAttributes(s, style.Attrs); AppendColor(s, style.Fg, 38); AppendColor(s, style.Bg, 48); s.Append('m');
            }
            s.Append("\x1b[?1049h");
        }
        AppendScreen(s, frame);
        s.Append($"\x1b[{buffer.ScrollRegionTop + 1};{buffer.ScrollRegionBottom + 1}r");
        if (buffer.OriginMode) s.Append("\x1b[?6h");
        var cursorRow = frame.CursorY + 1 - (buffer.OriginMode ? buffer.ScrollRegionTop : 0);
        s.Append($"\x1b[0m\x1b[?7{(buffer.AutoWrap ? 'h' : 'l')}\x1b[{cursorRow};{frame.CursorX + 1}H\x1b[?25{(frame.CursorVisible ? 'h' : 'l')}");
        if (buffer.PendingWrap)
        {
            var col = frame.CursorX; if (frame.Cells[frame.CursorY * frame.Columns + col].IsWideContinuation) col--;
            var cell = frame.Cells[frame.CursorY * frame.Columns + col];
            s.Append($"\x1b[{cursorRow};{col + 1}H\x1b[0"); AppendAttributes(s, cell.Attrs);
            AppendColor(s, cell.Fg, 38); AppendColor(s, cell.Bg, 48); s.Append('m'); cell.AppendText(s);
        }
        s.Append("\x1b[0"); AppendAttributes(s, buffer.CurrentAttrs); AppendColor(s, buffer.CurrentFg, 38); AppendColor(s, buffer.CurrentBg, 48); s.Append('m');
        if (buffer.InsertMode) s.Append("\x1b[4h");
        if (buffer.ApplicationCursorKeys) s.Append("\x1b[?1h");
        if (buffer.BracketedPaste) s.Append("\x1b[?2004h");
        return Encoding.UTF8.GetBytes(s.ToString());
    }
    private static void AppendColor(StringBuilder s, TerminalColor color, int code)
    {
        if (color.Kind == TerminalColor.ColorKind.Indexed) s.Append($";{code};5;{color.Value}");
        else if (color.Kind == TerminalColor.ColorKind.Rgb) s.Append($";{code};2;{color.Value >> 16 & 255};{color.Value >> 8 & 255};{color.Value & 255}");
    }
    private static void AppendAttributes(StringBuilder s, CellAttrs attrs)
    {
        foreach (var (attr, code) in new[] { (CellAttrs.Bold, 1), (CellAttrs.Dim, 2), (CellAttrs.Italic, 3), (CellAttrs.Underline, 4),
            (CellAttrs.Blink, 5), (CellAttrs.Inverse, 7), (CellAttrs.Hidden, 8), (CellAttrs.Strike, 9) })
            if (attrs.HasFlag(attr)) s.Append(';').Append(code);
    }
    private static void AppendScreen(StringBuilder s, TerminalFrame frame)
    {
        s.Append("\x1b[?7l");
        for (var row = 0; row < frame.Rows; row++)
            for (var col = 0; col < frame.Columns; col++)
            {
                var cell = frame.Cells[row * frame.Columns + col]; if (cell.IsWideContinuation) continue;
                s.Append($"\x1b[{row + 1};{col + 1}H\x1b[0"); AppendAttributes(s, cell.Attrs);
                AppendColor(s, cell.Fg, 38); AppendColor(s, cell.Bg, 48); s.Append('m'); cell.AppendText(s);
            }
    }
}

/// <summary>Playback owns an emulator without a running PTY. Seeking rebuilds from the initial frame.</summary>
public sealed class TerminalPlayback : IDisposable
{
    public RecordingHeader Header { get; }
    public IReadOnlyList<RecordingEvent> Events { get; }
    public TerminalEmulator Emulator { get; private set; } = null!;
    public double DurationMs => Events.LastOrDefault()?.TimeMs ?? 0;
    public double PositionMs { get; private set; }
    private int _next;
    private TerminalPlayback(RecordingHeader header, List<RecordingEvent> events) { Header = header; Events = events; Reset(); }
    public static async Task<TerminalPlayback> LoadAsync(string path)
    {
        using var reader = File.OpenText(path);
        var header = JsonSerializer.Deserialize<RecordingHeader>(await reader.ReadLineAsync() ?? "") ?? throw new IOException("录制文件缺少文件头。");
        if (header.Version != 1) throw new IOException("不支持的录制版本。");
        var events = new List<RecordingEvent>(); string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            if (line.Length == 0) continue;
            events.Add(JsonSerializer.Deserialize<RecordingEvent>(line) ?? throw new IOException("录制事件为空。"));
        }
        return new(header, events);
    }
    private void Reset()
    {
        Emulator?.Dispose(); Emulator = new(columns: Header.Columns, rows: Header.Rows) { ColorScheme = Header.ColorScheme };
        Emulator.Parser.Feed(Header.Initial); _next = 0; PositionMs = 0;
    }
    public void Seek(double timeMs)
    {
        timeMs = Math.Clamp(timeMs, 0, DurationMs); if (timeMs < PositionMs) Reset();
        while (_next < Events.Count && Events[_next].TimeMs <= timeMs)
        {
            var item = Events[_next++];
            if (item.Kind == "output" && item.Data is { } data) Emulator.Parser.Feed(data);
            else if (item.Kind == "resize") Emulator.Resize(item.Columns, item.Rows);
            else if (item.Kind == "gap") Emulator.Parser.DiscardPendingInput();
        }
        PositionMs = timeMs;
    }
    public void Dispose() => Emulator.Dispose();
}
