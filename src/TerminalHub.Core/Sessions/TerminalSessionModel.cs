using TerminalHub.Core.Pty;
using TerminalHub.Core.Terminal;

namespace TerminalHub.Core.Sessions;

/// <summary>A terminal session: display metadata + emulator (PTY + screen).</summary>
public sealed class TerminalSessionModel : IDisposable
{
    public Guid Id => Emulator.Pty.Id;
    public required string Name { get; set; }
    public SessionTag Tag { get; set; } = SessionTag.None;
    public bool IsRemote => Tag == SessionTag.Ssh || Path.GetFileNameWithoutExtension(Shell).Equals("ssh", StringComparison.OrdinalIgnoreCase);
    public required TerminalEmulator Emulator { get; init; }
    public string WorkingDirectory { get; set; } = "";
    /// <summary>Shell command this session was spawned with (pwsh, ssh…); saved
    /// into the workspace layout so a restart respawns the same kind of session.</summary>
    public string Shell { get; set; } = "";
    public string ShellArguments { get; set; } = "";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

    public IPtySession Pty => Emulator.Pty;
    public bool IsRunning => Pty.IsRunning;

    /// <summary>True while the session lives in a popout window (off the main
    /// session list, still owned by the shell VM). Lets SessionAdded/SessionRemoved
    /// tell a detach/reattach cycle from create/close so PTY wiring and cwd
    /// history survive the round trip.</summary>
    public bool Detached { get; set; }

    /// <summary>One-shot task sessions (a publish run) are left out of the saved
    /// workspace — restoring them would re-run the script on every launch.</summary>
    public bool ExcludeFromWorkspace { get; set; }

    public void Dispose() => Emulator.Dispose();
}
