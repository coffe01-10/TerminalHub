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
    /// whitespace is quoted so it reaches ssh as one argument — ssh then fails
    /// to resolve it, which is a clear error instead of a mangled command line
    /// where the tail is silently treated as the remote command.</summary>
    [JsonIgnore]
    public string SshArguments => $"-p {Port} {QuoteIfNeeded(Target)}";

    /// <summary>Full command line (for logs / display).</summary>
    [JsonIgnore]
    public string CommandLine => $"ssh {SshArguments}";

    private static string QuoteIfNeeded(string s)
        => s.Length > 0 && s.Any(char.IsWhiteSpace) ? $"\"{s}\"" : s;
}
