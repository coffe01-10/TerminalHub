namespace TerminalHub.Core.Pty;

public static class PtyEnvironment
{
    public static Dictionary<string, string> Build(PtyOptions options)
    {
        var current = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
            if (entry.Key is string key && entry.Value is string value) current[key] = value;
        return Build(options, current);
    }

    /// <summary>Core of <see cref="Build(PtyOptions)"/> with an explicit base environment — test seam.</summary>
    public static Dictionary<string, string> Build(PtyOptions options, IDictionary<string, string> current)
    {
        var result = new Dictionary<string, string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var pair in current) result[pair.Key] = pair.Value;
        result["TERM"] = "xterm-256color";
        result["COLORTERM"] = "truecolor";
        result["TERM_PROGRAM"] = "TerminalHub";
        result["TERM_PROGRAM_VERSION"] = "1.0";
        // A child spawned with no locale variables gets POSIX/C — CJK and UTF-8
        // tools then degrade. ConPTY never has this problem; keep it non-Windows-only.
        if (!OperatingSystem.IsWindows() && !result.ContainsKey("LANG") && !result.ContainsKey("LC_ALL"))
            result["LANG"] = "C.UTF-8";
        foreach (var item in options.Environment) result[item.Key] = item.Value;
        return result;
    }
}
