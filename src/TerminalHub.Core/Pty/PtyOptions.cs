namespace TerminalHub.Core.Pty;

/// <summary>Options used to spawn a PTY session.</summary>
public sealed record PtyOptions
{
    /// <summary>Shell executable or command line (pwsh, cmd, wsl, bash…).</summary>
    public required string Shell { get; init; }

    /// <summary>Optional arguments appended to <see cref="Shell"/>.</summary>
    public string Arguments { get; init; } = string.Empty;

    /// <summary>Working directory for the child process.</summary>
    public string WorkingDirectory { get; init; } = string.Empty;

    public int Columns { get; init; } = 120;
    public int Rows { get; init; } = 30;

    /// <summary>Extra environment variables (merged over defaults).</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } =
        new Dictionary<string, string>();
}
