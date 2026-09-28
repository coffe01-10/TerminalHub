using TerminalHub.Core.Settings;

namespace TerminalHub.Core.Logging;

/// <summary>
/// Optional on-disk sink for session output lines. Disabled by default;
/// <see cref="Enable"/> creates `terminalhub-&lt;timestamp&gt;.log` under the
/// per-user config dir (`~/.config/terminalhub/logs` on Linux).
/// </summary>
public sealed class SessionLogFile : IDisposable
{
    private StreamWriter? _writer;

    public bool IsEnabled => _writer is not null;
    public string? CurrentPath { get; private set; }

    /// <summary>`<config>/terminalhub/logs`.</summary>
    public static string DefaultDir()
        => Path.Combine(Path.GetDirectoryName(SettingsStore.DefaultPath())!, "logs");

    /// <summary>Open a new timestamped log file; returns its path.</summary>
    public string Enable(string? dir = null)
    {
        Disable();
        var d = dir ?? DefaultDir();
        Directory.CreateDirectory(d);
        var path = Path.Combine(d, $"terminalhub-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        _writer = new StreamWriter(
            new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        { AutoFlush = true };
        _writer.WriteLine($"# Terminal Hub session log — started {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        CurrentPath = path;
        return path;
    }

    /// <summary>Append one log line; no-op while disabled.</summary>
    public void Write(string source, string level, string message)
        => _writer?.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{level}] ({source}) {message}");

    /// <summary>Close the current log file.</summary>
    public void Disable()
    {
        _writer?.Dispose();
        _writer = null;
        CurrentPath = null;
    }

    public void Dispose() => Disable();
}
