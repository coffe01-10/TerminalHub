namespace TerminalHub.Core.Ssh;

/// <summary>Detects the `ssh` client binary on PATH.</summary>
public static class SshLocator
{
    public static bool Available()
    {
        var exe = OperatingSystem.IsWindows() ? "ssh.exe" : "ssh";
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                if (File.Exists(Path.Combine(dir, exe))) return true;
            }
            catch { /* unreadable PATH entry — skip */ }
        }
        return false;
    }
}
