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
    /// <summary>Keep the latest 32 logs; older files still in use survive until closed.</summary>
    public const int RetainedFileCount = 32;

    private readonly object _gate = new();
    private StreamWriter? _writer;
    private string? _dir;
    private long _writtenBytes;

    public bool IsEnabled => _writer is not null;
    public string? CurrentPath { get; private set; }
    public event Action<string>? Failed;

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
            try { return OpenNewLocked(_dir); }
            catch
            {
                CloseLocked();
                throw;
            }
        }
    }

    /// <summary>Append one log line; no-op while disabled. Session PTY read
    /// threads write concurrently — guard the non-thread-safe StreamWriter.</summary>
    public void Write(string source, string level, string message)
    {
        string? failure = null;
        lock (_gate)
        {
            if (_writer is null) return;
            try
            {
                var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] ({source}) {message}";
                _writer.WriteLine(line);
                CountBytes(line);
                if (_writtenBytes >= MaxFileBytes)
                {
                    // Rotate: close and open the next timestamped file. Inline (not
                    // via Enable) — Enable takes the same lock.
                    CloseLocked();
                    OpenNewLocked(_dir ?? DefaultDir());
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
            {
                // A full disk or a locked log must not escape into the PTY read callback.
                CloseLocked();
                failure = ex.Message;
            }
        }
        if (failure is not null) Failed?.Invoke(failure);
    }

    /// <summary>Close the current log file.</summary>
    public void Disable()
    {
        lock (_gate)
        {
            CloseLocked();
            PruneLogsLocked();
        }
    }

    public void Dispose() => Disable();

    // Both called with _gate held.
    private void CloseLocked()
    {
        var writer = _writer;
        _writer = null;
        CurrentPath = null;
        _writtenBytes = 0;
        try { writer?.Dispose(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            System.Diagnostics.Debug.WriteLine($"Closing session log failed: {ex.Message}");
        }
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
        var header = $"# Terminal Hub session log — started {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        _writer.WriteLine(header);
        _writtenBytes = _writer.Encoding.GetPreamble().Length;
        CountBytes(header);
        CurrentPath = path;
        PruneLogsLocked();
        return path;
    }

    // DeleteOnClose with exclusive sharing refuses an active writer on Windows
    // and takes an exclusive advisory flock on local Unix files. Only our own
    // timestamped logs are candidates; exports and other files are untouched.
    private void PruneLogsLocked()
    {
        if (_dir is null) return;
        try
        {
            var oldFiles = new DirectoryInfo(_dir).GetFiles("terminalhub-*.log")
                .Where(f => IsSessionLogName(f.Name))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ThenByDescending(f => f.Name, StringComparer.Ordinal)
                .Skip(RetainedFileCount);
            foreach (var file in oldFiles)
            {
                try
                {
                    using var closedLog = new FileStream(file.FullName, FileMode.Open,
                        FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    System.Diagnostics.Debug.WriteLine($"Retaining session log {file.Name}: {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"Listing session logs failed: {ex.Message}");
        }
    }

    private static bool IsSessionLogName(string name)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        if (stem.Length < 31 || !DateTime.TryParseExact(stem.AsSpan(12, 19),
            "yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out _)) return false;
        return stem.Length == 31 || stem[31] == '-' && int.TryParse(stem.AsSpan(32), out var collision) && collision >= 2;
    }

    // Called with _gate held. Counts UTF-8 bytes, including the newline, not UTF-16 chars.
    private void CountBytes(string line)
    {
        if (_writer is null) return;
        _writtenBytes += _writer.Encoding.GetByteCount(line);
        _writtenBytes += _writer.Encoding.GetByteCount(_writer.NewLine);
    }
}
