namespace TerminalHub.Core.Pty;

/// <summary>
/// Abstraction over a pseudo-terminal process (Windows ConPTY, Linux forkpty, mock for tests).
/// </summary>
public interface IPtySession : IDisposable
{
    /// <summary>Unique id for this PTY session.</summary>
    Guid Id { get; }

    /// <summary>True while the child process is alive.</summary>
    bool IsRunning { get; }

    /// <summary>Exit code after the child process terminates, if known.</summary>
    int? ExitCode { get; }

    /// <summary>OS process id of the child shell, if known (used to probe CWD on Linux).</summary>
    int? ProcessId { get; }

    /// <summary>Raw output bytes emitted by the child process.</summary>
    event Action<IPtySession, ReadOnlyMemory<byte>>? OutputReceived;

    /// <summary>Raised when the child process exits.</summary>
    event Action<IPtySession, int>? Exited;

    /// <summary>Spawn the shell inside the pseudo-terminal.</summary>
    Task StartAsync(PtyOptions options, CancellationToken cancellationToken = default);

    /// <summary>Send input bytes (keystrokes, paste) to the child stdin.</summary>
    void Write(ReadOnlySpan<byte> data);

    /// <summary>Resize the pseudo-terminal.</summary>
    void Resize(int columns, int rows);

    /// <summary>Terminate the child process.</summary>
    void Kill();
}
