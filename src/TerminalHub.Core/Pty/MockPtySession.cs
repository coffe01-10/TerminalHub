using System.Text;

namespace TerminalHub.Core.Pty;

/// <summary>
/// Deterministic in-memory PTY used on platforms without a real PTY and in tests.
/// Emits a canned prompt and echoes input back like a trivial shell.
/// </summary>
public sealed class MockPtySession : IPtySession
{
    private readonly StringBuilder _line = new();
    private bool _disposed;

    /// <summary>Verbatim text of every <see cref="Write"/> so far — test hook to
    /// observe exactly which bytes keyboard input delivered to the PTY.</summary>
    public readonly StringBuilder RawInput = new();

    public Guid Id { get; } = Guid.NewGuid();
    public bool IsRunning { get; private set; }
    public int? ExitCode { get; private set; }
    public int? ProcessId => null;

    public event Action<IPtySession, ReadOnlyMemory<byte>>? OutputReceived;
    public event Action<IPtySession, int>? Exited;

    public Task StartAsync(PtyOptions options, CancellationToken cancellationToken = default)
    {
        IsRunning = true;
        Emit($"\x1b[36mmock-shell\x1b[0m {options.Shell}\r\n");
        Prompt();
        return Task.CompletedTask;
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        if (!IsRunning) return;
        var text = Encoding.UTF8.GetString(data);
        RawInput.Append(text);
        foreach (var ch in text)
        {
            if (ch is '\r' or '\n')
            {
                Emit("\r\n");
                var cmd = _line.ToString().Trim();
                _line.Clear();
                if (cmd.Length > 0)
                {
                    if (cmd == "exit")
                    {
                        Emit("logout\r\n");
                        IsRunning = false;
                        ExitCode = 0;
                        Exited?.Invoke(this, 0);
                        return;
                    }
                    Emit($"mock: {cmd}\r\n");
                }
                Prompt();
            }
            else if (ch == '\b' || ch == 0x7f)
            {
                if (_line.Length > 0)
                {
                    _line.Length--;
                    Emit("\b \b");
                }
            }
            else
            {
                _line.Append(ch);
                Emit(ch.ToString());
            }
        }
    }

    public void Resize(int columns, int rows) { }

    public void Kill()
    {
        if (!IsRunning) return;
        IsRunning = false;
        ExitCode = -1;
        Exited?.Invoke(this, -1);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Kill();
    }

    private void Prompt() => Emit("\x1b[32mPS\x1b[0m \x1b[34m~\x1b[0m> ");

    private void Emit(string text)
    {
        if (OutputReceived is { } handler)
            handler(this, Encoding.UTF8.GetBytes(text));
    }
}
