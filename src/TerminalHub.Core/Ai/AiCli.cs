using System.Globalization;
using TerminalHub.Core.Terminal;

namespace TerminalHub.Core.Ai;

/// <summary>One spawnable AI CLI: the executable names it may appear under,
/// the argument template that hands it the first task prompt, and whether a
/// reliable frame-based "awaiting input" detector exists for it.</summary>
public sealed record AiCliSpec(
    string Id,
    string DisplayName,
    string[] Executables,
    string? LocatorEnv,
    string PromptArgs,
    bool DetectsAwaitingInput);

/// <summary>A catalog entry resolved to a real executable path.</summary>
public sealed record AiCli(AiCliSpec Spec, string Path)
{
    public string Id => Spec.Id;
    public string DisplayName => Spec.DisplayName;
    public bool DetectsAwaitingInput => Spec.DetectsAwaitingInput;
}

/// <summary>Known AI CLIs and where to find them on disk.</summary>
public static class AiCliCatalog
{
    public static readonly AiCliSpec[] Known =
    [
        new("codex", "OpenAI Codex", ["codex.cmd", "codex.exe", "codex"],
            "TERMINALHUB_CODEX_PATH", "\"{0}\"", DetectsAwaitingInput: true),
        new("claude", "Claude Code", ["claude.cmd", "claude.exe", "claude"],
            "TERMINALHUB_CLAUDE_PATH", "\"{0}\"", DetectsAwaitingInput: false),
        new("grok", "Grok CLI", ["grok.cmd", "grok.exe", "grok"],
            "TERMINALHUB_GROK_PATH", "\"{0}\"", DetectsAwaitingInput: false),
        new("gemini", "Gemini CLI", ["gemini.cmd", "gemini.exe", "gemini"],
            "TERMINALHUB_GEMINI_PATH", "\"{0}\"", DetectsAwaitingInput: false),
        new("aider", "Aider", ["aider.cmd", "aider.exe", "aider"],
            null, "--message \"{0}\"", DetectsAwaitingInput: false),
    ];

    /// <summary>Directories searched in addition to PATH (npm-global shims are
    /// the common install location for these CLIs).</summary>
    public static IEnumerable<string> ExtraSearchDirs()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (roaming.Length > 0) yield return Path.Combine(roaming, "npm");
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length > 0) yield return Path.Combine(profile, ".local", "bin");
        yield return "/usr/local/bin";
    }

    /// <summary>Resolves every known CLI that exists on this machine. The
    /// optional <paramref name="locate"/> hook (name → path or null) replaces
    /// filesystem probing in tests.</summary>
    public static IReadOnlyList<AiCli> Detect(Func<string, string?>? locate = null)
    {
        var found = new List<AiCli>();
        foreach (var spec in Known)
        {
            var path = locate is not null ? locate(spec.Id) : Locate(spec);
            if (path is not null) found.Add(new(spec, path));
        }
        return found;
    }

    private static string? Locate(AiCliSpec spec)
    {
        if (spec.LocatorEnv is { } env && Environment.GetEnvironmentVariable(env) is { Length: > 0 } envPath
            && File.Exists(envPath)) return envPath;
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat(ExtraSearchDirs());
        foreach (var dir in dirs)
            foreach (var name in spec.Executables)
            {
                try { if (File.Exists(Path.Combine(dir, name))) return Path.Combine(dir, name); }
                catch (Exception) when (!OperatingSystem.IsWindows()) { }
            }
        return null;
    }

    /// <summary>(shell, arguments) that launches <paramref name="cli"/> with
    /// <paramref name="task"/> as its initial prompt. .cmd/.bat shims must go
    /// through cmd.exe (same pattern the Codex editing tests use).</summary>
    public static (string Shell, string Arguments) SpawnCommand(AiCli cli, string task)
    {
        var safe = task.Replace('"', '\'').Replace("\r", " ").Replace("\n", " ");
        var args = string.Format(CultureInfo.InvariantCulture, cli.Spec.PromptArgs, safe);
        if (cli.Path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
            || cli.Path.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            return ("cmd.exe", $"/d /s /c \"\"{cli.Path}\" {args}\"");
        return (cli.Path, args);
    }

    /// <summary>Does this frame show the CLI waiting for user input? Only
    /// Codex has a verified detector today; others stay "running" until the
    /// process exits.</summary>
    public static bool IsAwaitingInput(AiCli cli, TerminalFrame frame) =>
        cli.DetectsAwaitingInput && cli.Id == "codex" && CodexPrompt.IsEditing(frame);
}
