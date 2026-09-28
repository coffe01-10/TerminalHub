namespace TerminalHub.Core.Pty;

/// <summary>
/// Best-effort read of a child process's current working directory.
/// On Linux uses <c>/proc/&lt;pid&gt;/cwd</c>; elsewhere returns null (caller falls back
/// to the session's remembered path).
/// </summary>
public static class ProcessCwd
{
    public static string? TryRead(IPtySession? pty) =>
        pty is null ? null : TryRead(pty.ProcessId);

    public static string? TryRead(int? processId)
    {
        if (processId is not int pid || pid <= 0) return null;
        if (!OperatingSystem.IsLinux()) return null;
        try
        {
            var link = $"/proc/{pid}/cwd";
            var target = Directory.ResolveLinkTarget(link, returnFinalTarget: true);
            if (target is not null)
                return Path.GetFullPath(target.FullName);
            // Fallback: some runtimes expose LinkTarget without ResolveLinkTarget succeeding.
            var info = new DirectoryInfo(link);
            if (!string.IsNullOrEmpty(info.LinkTarget))
                return Path.GetFullPath(info.LinkTarget);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException or NotSupportedException)
        {
            // process gone / permission / bad pid
        }
        return null;
    }
}
