using TerminalHub.Core.Pty;

namespace TerminalHub.Core.Sessions;

/// <summary>A terminal session: display metadata + PTY handle.</summary>
public sealed class TerminalSessionModel : IDisposable
{
    public Guid Id => Pty.Id;
    public required string Name { get; set; }
    public SessionTag Tag { get; set; } = SessionTag.None;
    public required IPtySession Pty { get; init; }
    public string WorkingDirectory { get; set; } = "";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

    public bool IsRunning => Pty.IsRunning;

    /// <summary>Preview text for the thumbnail card (last N lines).</summary>
    public string PreviewText { get; set; } = "";

    public void Dispose() => Pty.Dispose();
}
