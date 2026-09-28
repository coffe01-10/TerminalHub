namespace TerminalHub.Core.Deploy;

/// <summary>What the Deploy dock should do for this click.</summary>
public enum DeployAction
{
    /// <summary>Artifacts exist and the user did not ask to rebuild — open the folder.</summary>
    OpenArtifacts,

    /// <summary>No artifacts, or an explicit republish — run the platform publish script.</summary>
    StartPublish,
}

/// <summary>Which publish script a session should run.</summary>
public enum PublishPlatform
{
    Linux,
    Windows,
}

/// <summary>Spawn plan for the repo publish script inside a terminal session.</summary>
public sealed record PublishPlan(
    string RepoRoot,
    string Shell,
    string Arguments,
    string WorkingDirectory,
    string DisplayCommand);

/// <summary>
/// Deploy-dock policy and publish-script lookup.
/// Plain click: no artifacts → publish; artifacts present → open the folder.
/// Ctrl+click or the dock's「重新打包」menu passes force and always publishes.
/// </summary>
public static class PublishPlanner
{
    public const string LinuxScriptRelative = "scripts/publish-linux.sh";
    public const string WindowsScriptRelative = "scripts/publish-windows.ps1";

    public static PublishPlatform CurrentPlatform =>
        OperatingSystem.IsWindows() ? PublishPlatform.Windows : PublishPlatform.Linux;

    /// <summary>
    /// Missing artifacts start a publish. Existing artifacts open the folder
    /// unless <paramref name="forceRepublish"/> is set (Ctrl+click / 重新打包).
    /// </summary>
    public static DeployAction Decide(bool hasArtifacts, bool forceRepublish) =>
        forceRepublish || !hasArtifacts ? DeployAction.StartPublish : DeployAction.OpenArtifacts;

    /// <summary>
    /// Nearest directory at or above <paramref name="startDir"/> that contains
    /// <c>scripts/publish-linux.sh</c> or <c>scripts/publish-windows.ps1</c>.
    /// </summary>
    public static string? FindRepoRoot(string? startDir)
    {
        if (string.IsNullOrWhiteSpace(startDir)) return null;

        DirectoryInfo? dir;
        try { dir = new DirectoryInfo(startDir); }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return null;
        }

        while (dir is not null)
        {
            if (ScriptExists(dir.FullName, LinuxScriptRelative)
                || ScriptExists(dir.FullName, WindowsScriptRelative))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>
    /// Build the shell command for <paramref name="platform"/>.
    /// Linux runs <c>bash ./scripts/publish-linux.sh</c> (argv has no spaces).
    /// Windows runs <c>pwsh</c> or <c>powershell</c> with <c>-File scripts\publish-windows.ps1</c>.
    /// Returns null when that platform's script is not in the walked repo.
    /// </summary>
    public static PublishPlan? TryPlan(string? startDir, PublishPlatform platform, string? windowsShell = null)
    {
        var root = FindRepoRoot(startDir);
        if (root is null) return null;

        if (platform == PublishPlatform.Windows)
        {
            if (!ScriptExists(root, WindowsScriptRelative)) return null;
            var shell = string.IsNullOrWhiteSpace(windowsShell) ? "pwsh" : windowsShell.Trim();
            const string args = "-NoProfile -ExecutionPolicy Bypass -File scripts\\publish-windows.ps1";
            return new PublishPlan(root, shell, args, root, $"{shell} {args}");
        }

        if (!ScriptExists(root, LinuxScriptRelative)) return null;
        const string sh = "./scripts/publish-linux.sh";
        return new PublishPlan(root, "bash", sh, root, sh);
    }

    /// <summary>Prefer pwsh, then Windows PowerShell. Falls back to pwsh so the session can show the error.</summary>
    public static string ResolveWindowsShell(Func<string, bool> existsOnPath)
    {
        if (existsOnPath("pwsh")) return "pwsh";
        if (existsOnPath("powershell")) return "powershell";
        return "pwsh";
    }

    /// <summary>True when <paramref name="name"/> resolves to a file on a PATH-like string.</summary>
    public static bool NameOnPath(string name, string? pathEnv, bool windows)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(pathEnv)) return false;
        var sep = windows ? ';' : ':';
        foreach (var dir in pathEnv.Split(sep, StringSplitOptions.RemoveEmptyEntries))
        {
            if (windows)
            {
                if (File.Exists(Path.Combine(dir, name + ".exe"))) return true;
                if (File.Exists(Path.Combine(dir, name + ".cmd"))) return true;
            }
            if (File.Exists(Path.Combine(dir, name))) return true;
        }
        return false;
    }

    private static bool ScriptExists(string root, string relative) =>
        File.Exists(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
}
