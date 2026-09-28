using System.Text;
using TerminalHub.Core.Pty;

namespace TerminalHub.Core.Terminal;

/// <summary>
/// Glues an <see cref="IPtySession"/> to a <see cref="ScreenBuffer"/> via <see cref="VtParser"/>.
/// UI renders the buffer and forwards keystrokes through <see cref="SendText"/>.
/// </summary>
public sealed class TerminalEmulator : IDisposable
{
    private readonly IPtySession _pty;
    private bool _ownsPty;

    public ScreenBuffer Buffer { get; }
    public VtParser Parser { get; }

    /// <summary>Raised (on a background thread) when the buffer changed.</summary>
    public event Action? Changed;
    public event Action<string>? TitleChanged
    {
        add => Buffer.TitleChanged += value;
        remove => Buffer.TitleChanged -= value;
    }

    public event Action<string>? CwdChanged
    {
        add => Buffer.CwdChanged += value;
        remove => Buffer.CwdChanged -= value;
    }

    public TerminalEmulator(IPtySession? pty = null, int columns = 120, int rows = 30)
    {
        Buffer = new ScreenBuffer(columns, rows);
        Parser = new VtParser(Buffer, bytes => _pty?.Write(bytes));
        _pty = pty ?? new MockPtySession();
        _ownsPty = true;
        _pty.OutputReceived += OnPtyOutput;
        Parser.BufferChanged += () => Changed?.Invoke();
    }

    public IPtySession Pty => _pty;

    public Task StartAsync(PtyOptions options, CancellationToken ct = default)
        => _pty.StartAsync(options with { Columns = Buffer.Columns, Rows = Buffer.Rows }, ct);

    /// <summary>Send user text input to the child process.</summary>
    public void SendText(string text) => _pty.Write(Encoding.UTF8.GetBytes(text));
    public void SendBytes(byte[] bytes) => _pty.Write(bytes);

    /// <summary>Resize the grid and the underlying PTY.</summary>
    public void Resize(int columns, int rows)
    {
        Buffer.Resize(columns, rows);
        _pty.Resize(columns, rows);
        Changed?.Invoke();
    }

    private void OnPtyOutput(IPtySession _, ReadOnlyMemory<byte> data)
        => Parser.Feed(data.Span);

    public void Dispose()
    {
        _pty.OutputReceived -= OnPtyOutput;
        if (_ownsPty) _pty.Dispose();
    }
}
