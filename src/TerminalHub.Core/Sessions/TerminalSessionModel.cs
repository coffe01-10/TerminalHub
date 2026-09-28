using TerminalHub.Core.Pty;
using TerminalHub.Core.Terminal;

namespace TerminalHub.Core.Sessions;

/// <summary>A terminal session: display metadata + emulator (PTY + screen).</summary>
public sealed class TerminalSessionModel : IDisposable
{
    public Guid Id => Emulator.Pty.Id;
    public required string Name { get; set; }
    public SessionTag Tag { get; set; } = SessionTag.None;
    public required TerminalEmulator Emulator { get; init; }
    public string WorkingDirectory { get; set; } = "";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

    public IPtySession Pty => Emulator.Pty;
    public bool IsRunning => Pty.IsRunning;

    /// <summary>Preview text for the thumbnail card (last N lines).</summary>
    public string PreviewText => Emulator.Buffer.TailText(8);

    public void Dispose() => Emulator.Dispose();
}
