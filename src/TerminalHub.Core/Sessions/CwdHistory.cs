namespace TerminalHub.Core.Sessions;

/// <summary>Browser-style back/forward stack of working-directory paths.</summary>
public sealed class CwdHistory
{
    private readonly List<string> _stack = [];
    private int _index = -1;

    public string? Current => _index >= 0 && _index < _stack.Count ? _stack[_index] : null;
    public bool CanGoBack => _index > 0;
    public bool CanGoForward => _index >= 0 && _index < _stack.Count - 1;
    public int Count => _stack.Count;

    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try
        {
            path = Path.GetFullPath(path.Trim());
            // Collapse trailing separators so "/tmp/x" and "/tmp/x/" compare equal.
            var root = Path.GetPathRoot(path) ?? "";
            while (path.Length > root.Length
                   && (path.EndsWith(Path.DirectorySeparatorChar)
                       || path.EndsWith(Path.AltDirectorySeparatorChar)))
                path = path[..^1];
            return path;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path.Trim();
        }
    }

    /// <summary>
    /// Record a newly observed path. No-op when equal to <see cref="Current"/>.
    /// Drops any forward entries (same as a browser navigating to a new URL).
    /// </summary>
    public void Push(string path)
    {
        path = Normalize(path);
        if (path.Length == 0) return;
        if (Current is { } cur && PathsEqual(cur, path)) return;
        if (_index >= 0 && _index < _stack.Count - 1)
            _stack.RemoveRange(_index + 1, _stack.Count - _index - 1);
        _stack.Add(path);
        _index = _stack.Count - 1;
    }

    /// <summary>Record a path that is not local (a remote shell's cwd over ssh).
    /// Stored verbatim — <see cref="Normalize"/> would anchor "/home/u" at the
    /// current drive, and only verbatim remote paths are cd-able on the remote.</summary>
    public void PushRaw(string path)
    {
        if (path.Length == 0) return;
        if (Current is { } cur && PathsEqual(cur, path)) return;
        if (_index >= 0 && _index < _stack.Count - 1)
            _stack.RemoveRange(_index + 1, _stack.Count - _index - 1);
        _stack.Add(path);
        _index = _stack.Count - 1;
    }

    public string? Back()
    {
        if (!CanGoBack) return null;
        _index--;
        return Current;
    }

    public string? Forward()
    {
        if (!CanGoForward) return null;
        _index++;
        return Current;
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(a, b,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
