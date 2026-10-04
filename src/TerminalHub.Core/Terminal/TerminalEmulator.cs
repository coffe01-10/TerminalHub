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

    public ScreenBuffer Buffer { get; }
    public VtParser Parser { get; }
    public IReadOnlyList<OutputRuleMatcher> OutputRules { get; set; } = [];
    public event Action<int, int>? Resized;
    private TerminalColorScheme _colorScheme;
    public TerminalColorScheme ColorScheme
    {
        get => _colorScheme;
        set
        {
            if (_colorScheme == value) return;
            _colorScheme = value;
            Changed?.Invoke();
        }
    }
    private long _outputVersion;
    /// <summary>PTY output only; resizing or theme changes are not unread output.</summary>
    public long OutputVersion => Interlocked.Read(ref _outputVersion);

    /// <summary>Raised (on a background thread) when the buffer changed.</summary>
    public event Action? Changed;
    /// <summary>PTY rang the bell (BEL in ground state).</summary>
    public event Action? Bell;
    private long _commandStarted;
    public ShellCommandState? CommandState { get; private set; }
    public CommandJournal Commands { get; } = new();
    public event Action? CommandsChanged;
    public event Action<ShellCommandState>? CommandCompleted;
    public event Action? CommandStarted;
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
        _pty.OutputReceived += OnPtyOutput;
        Parser.BufferChanged += () => Changed?.Invoke();
        Parser.Bell += () => Bell?.Invoke();
        Parser.ObserveCommands = markers => Commands.Apply(markers, Buffer);
        Parser.CommandsObserved += () => CommandsChanged?.Invoke();
        Parser.CommandMarker += (marker, code) =>
        {
            if (marker == 'C')
            {
                _commandStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                CommandState = new(true, null, TimeSpan.Zero);
                CommandStarted?.Invoke();
            }
            else if (marker == 'D' && CommandState?.Running == true)
            {
                CommandState = new(false, code, System.Diagnostics.Stopwatch.GetElapsedTime(_commandStarted));
                CommandCompleted?.Invoke(CommandState);
            }
        };
    }

    public IPtySession Pty => _pty;
    public void RefreshDisplay() => Changed?.Invoke();

    public Task StartAsync(PtyOptions options, CancellationToken ct = default)
        => _pty.StartAsync(options with { Columns = Buffer.Columns, Rows = Buffer.Rows }, ct);

    /// <summary>Send user text input to the child process.</summary>
    public void SendText(string text) => _pty.Write(Encoding.UTF8.GetBytes(text));
    public void SendBytes(byte[] bytes) => _pty.Write(bytes);

    public void PasteText(string text)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (Buffer.BracketedPaste)
            // The payload must not contain the bracket terminator: pasted
            // "\x1b[201~" would close the bracket early and run the rest as
            // typed input (clipboard paste injection).
            SendText("\x1b[200~" + text.Replace("\x1b[201~", "") + "\x1b[201~");
        else
            SendText(text.Replace('\n', '\r'));
    }

    /// <summary>xterm mouse report; coordinates are zero-based terminal cells.</summary>
    public void SendMouse(int button, int column, int row, bool released = false,
        bool motion = false, int modifiers = 0, int count = 1)
    {
        var tracking = Buffer.MouseTracking;
        if (tracking == 0 || (motion && (tracking == 1000 || (tracking == 1002 && button == 3)))) return;
        if (count < 1) count = 1;
        var code = button | modifiers | (motion ? 32 : 0);
        var x = Math.Clamp(column, 0, Buffer.Columns - 1) + 1;
        var y = Math.Clamp(row, 0, Buffer.Rows - 1) + 1;
        if (Buffer.SgrMouse)
        {
            // count > 1 (wheel gestures): emit the report N times in ONE write.
            var one = $"\x1b[<{code};{x};{y}{(released ? 'm' : 'M')}";
            SendText(count == 1 ? one : string.Concat(Enumerable.Repeat(one, count)));
        }
        else if (x <= 223 && y <= 223)
        {
            var bytes = new byte[6 * count];
            var cb = (byte)((released ? 3 | modifiers : code) + 32);
            for (var i = 0; i < count; i++)
            {
                bytes[i * 6] = 0x1b;
                bytes[i * 6 + 1] = (byte)'[';
                bytes[i * 6 + 2] = (byte)'M';
                bytes[i * 6 + 3] = cb;
                bytes[i * 6 + 4] = (byte)(x + 32);
                bytes[i * 6 + 5] = (byte)(y + 32);
            }
            SendBytes(bytes);
        }
    }

    public void SendFocus(bool focused)
    {
        if (Buffer.FocusReporting) SendText(focused ? "\x1b[I" : "\x1b[O");
    }

    /// <summary>Resize the grid and the underlying PTY.</summary>
    public void Resize(int columns, int rows)
    {
        lock (Buffer.SyncRoot)
        {
            Buffer.Resize(columns, rows);
            Resized?.Invoke(columns, rows);
        }
        _pty.Resize(columns, rows);
        Changed?.Invoke();
    }

    private void OnPtyOutput(IPtySession _, ReadOnlyMemory<byte> data)
    {
        if (!data.IsEmpty) Interlocked.Increment(ref _outputVersion);
        Parser.Feed(data.Span);
    }

    public void Dispose()
    {
        _pty.OutputReceived -= OnPtyOutput;
        _pty.Dispose();
    }
}
