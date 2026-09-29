namespace TerminalHub.Core.Pty;

public static class PtyEnvironment
{
    public static Dictionary<string, string> Build(PtyOptions options)
    {
        var result = new Dictionary<string, string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
            if (entry.Key is string key && entry.Value is string value) result[key] = value;
        result["TERM"] = "xterm-256color";
        result["COLORTERM"] = "truecolor";
        result["TERM_PROGRAM"] = "TerminalHub";
        result["TERM_PROGRAM_VERSION"] = "1.0";
        foreach (var item in options.Environment) result[item.Key] = item.Value;
        return result;
    }
}
