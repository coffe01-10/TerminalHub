using TerminalHub.Core.Settings;

namespace TerminalHub.Core.Pty;

public sealed record AvailableShell(string Name, string Command, ShellKind Kind);

/// <summary>Discover installed shells without starting processes.</summary>
public static class ShellDiscovery
{
    public static bool Exists(string command)
    {
        if (Path.IsPathRooted(command) || command.Contains(Path.DirectorySeparatorChar)
            || command.Contains(Path.AltDirectorySeparatorChar)) return File.Exists(command);
        var names = OperatingSystem.IsWindows() && string.IsNullOrEmpty(Path.GetExtension(command))
            ? new[] { command + ".exe", command + ".cmd", command + ".bat" } : new[] { command };
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            foreach (var name in names)
                if (directory.Length > 0 && File.Exists(Path.Combine(directory.Trim('"'), name))) return true;
        return false;
    }

    public static List<AvailableShell> Find(Func<string, bool>? exists = null)
    {
        exists ??= Exists;
        AvailableShell[] candidates = OperatingSystem.IsWindows()
            ? [new("PowerShell 7", "pwsh", ShellKind.PowerShell),
               new("Windows PowerShell", "powershell.exe", ShellKind.Custom),
               new("命令提示符", "cmd.exe", ShellKind.Cmd),
               new("WSL", "wsl.exe", ShellKind.Wsl), new("Bash", "bash", ShellKind.Bash)]
            : [new("Bash", "bash", ShellKind.Bash), new("PowerShell 7", "pwsh", ShellKind.PowerShell),
               new("Zsh", "zsh", ShellKind.Custom), new("Sh", "sh", ShellKind.Custom)];
        return candidates.Where(s => exists(s.Command)).ToList();
    }
}
