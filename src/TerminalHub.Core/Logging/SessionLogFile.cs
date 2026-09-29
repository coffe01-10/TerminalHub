using TerminalHub.Core.Settings;

namespace TerminalHub.Core.Logging;

/// <summary>
/// Optional on-disk sink for session output lines. Disabled by default;
/// <see cref="Enable"/> creates `terminalhub-&lt;timestamp&gt;.log` under the
/// per-user config dir (`~/.config/terminalhub/logs` on Linux). Files rotate
/// at <see cref="MaxFileBytes"/> so a long high-output session cannot grow a
/// single log without bound.
/// </summary>
public sealed class SessionLogFile : IDisposable
{
    /// <summary>Rotate to a fresh file after writing this many bytes.</summary>
    public const long MaxFileBytes = 16 * 1024 * 1024;

    private readonly object _gate = new();
    private StreamWriter? _writer;
    private string? _dir;
    private long _writtenBytes;

    public bool IsEnabled => _writer is not null;
    public string? CurrentPath { get; private set; }

    /// <summary>`<config>/terminalhub/logs`.</summary>
    public static string DefaultDir()
        => Path.Combine(Path.GetDirectoryName(SettingsStore.DefaultPath())!, "logs");

    /// <summary>Open a new timestamped log file; returns its path.</summary>
    public string Enable(string? dir = null)
    {
        lock (_gate)
        {
            CloseLocked();
            _dir = dir ?? DefaultDir();
            var path = OpenNewLocked(_dir);
            return path;
        }
    }

    /// <summary>Append one log line; no-op while disabled. Session PTY read
    /// threads write concurrently — guard the non-thread-safe StreamWriter.</summary>
    public void Write(string source, string level, string message)
    {
        lock (_gate)
        {
            if (_writer is null) return;
            var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] ({source}) {message}";
            _writer.WriteLine(line);
            _writtenBytes += line.Length + Environment.NewLine.Length;
            if (_writtenBytes >= MaxFileBytes)
            {
                // Rotate: close and open the next timestamped file. Inline (not
                // via Enable) — Enable takes the same lock.
                CloseLocked();
                OpenNewLocked(_dir ?? DefaultDir());
            }
        }
    }

    /// <summary>Close the current log file.</summary>
    public void Disable()
    {
        lock (_gate) CloseLocked();
    }

    public void Dispose() => Disable();

    // Both called with _gate held.
    private void CloseLocked()
    {
        _writer?.Dispose();
        _writer = null;
        CurrentPath = null;
        _writtenBytes = 0;
    }

    private string OpenNewLocked(string dir)
    {
        Directory.CreateDirectory(dir);
        // Millisecond precision + a collision counter: a same-second re-Enable
        // or rotation must not append a second header into the previous file.
        var stamp = DateTime.Now;
        var path = Path.Combine(dir, $"terminalhub-{stamp:yyyyMMdd-HHmmss-fff}.log");
        for (var i = 2; File.Exists(path); i++)
            path = Path.Combine(dir, $"terminalhub-{stamp:yyyyMMdd-HHmmss-fff}-{i}.log");
        _writer = new StreamWriter(
            new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        { AutoFlush = true };
        _writer.WriteLine($"# Terminal Hub session log — started {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        CurrentPath = path;
        return path;
    }
}
