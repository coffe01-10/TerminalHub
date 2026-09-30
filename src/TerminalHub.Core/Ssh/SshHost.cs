using System.Text;
using System.Text.Json.Serialization;

namespace TerminalHub.Core.Ssh;

/// <summary>A saved SSH connection shown in the right-rail SSH tab.</summary>
public sealed record SshHost
{
    /// <summary>Friendly label; defaults to the target when empty.</summary>
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public string User { get; set; } = "";
    public int Port { get; set; } = 22;

    /// <summary>`user@host` or just `host`.</summary>
    [JsonIgnore]
    public string Target => string.IsNullOrWhiteSpace(User) ? Host : $"{User}@{Host}";

    /// <summary>Label used for the session card / tab.</summary>
    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Target : Name;

    /// <summary>Arguments passed to the `ssh` binary. A target containing
    /// whitespace or quotes is quoted so it reaches ssh as one argument — ssh then fails
    /// to resolve it, which is a clear error instead of a mangled command line
    /// where the tail is silently treated as the remote command. A target that
    /// starts with '-' is placed after `--` so ssh does not treat it as an option.</summary>
    [JsonIgnore]
    public string SshArguments
    {
        get
        {
            var target = QuoteIfNeeded(Target);
            var separator = Target.StartsWith('-') ? "-- " : "";
            return $"-p {Port} {separator}{target}";
        }
    }

    /// <summary>Full command line (for logs / display).</summary>
    [JsonIgnore]
    public string CommandLine => $"ssh {SshArguments}";

    private static string QuoteIfNeeded(string s)
    {
        if (OperatingSystem.IsWindows()) return QuoteWindows(s);
        return QuotePosix(s);
    }

    /// <summary>CommandLineToArgvW quoting. Ordinary targets stay unquoted.</summary>
    private static string QuoteWindows(string s)
    {
        if (s.Length == 0) return "\"\"";
        if (s.IndexOfAny([' ', '\t', '"']) < 0) return s;
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        var slashes = 0;
        foreach (var c in s)
        {
            if (c == '\\') { slashes++; continue; }
            if (c == '"')
            {
                sb.Append('\\', slashes * 2 + 1);
                sb.Append('"');
                slashes = 0;
                continue;
            }
            if (slashes > 0) { sb.Append('\\', slashes); slashes = 0; }
            sb.Append(c);
        }
        if (slashes > 0) sb.Append('\\', slashes * 2);
        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>POSIX single quotes. An embedded quote is closed, escaped, and reopened.</summary>
    private static string QuotePosix(string s)
    {
        if (s.Length == 0) return "''";
        if (s.IndexOfAny([' ', '\t', '\'', '"', '\\']) < 0) return s;
        return "'" + s.Replace("'", "'\\''") + "'";
    }
}
