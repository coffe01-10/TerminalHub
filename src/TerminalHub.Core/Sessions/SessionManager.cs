using System.Collections.ObjectModel;
using TerminalHub.Core.Pty;
using TerminalHub.Core.Terminal;

namespace TerminalHub.Core.Sessions;

/// <summary>Tracks live terminal sessions: create / switch / close.</summary>
public sealed class SessionManager
{
    private readonly HashSet<TerminalSessionModel> _detached = [];

    public ObservableCollection<TerminalSessionModel> Sessions { get; } = [];
    private volatile IReadOnlyList<TerminalSessionModel> _snapshot = [];
    /// <summary>Stable array refreshed on each mutation (mutations always run on the
    /// caller's thread) — safe for other threads to enumerate, e.g. the monitor's
    /// cwd-poll, where iterating <see cref="Sessions"/> could throw mid-change.</summary>
    public IReadOnlyList<TerminalSessionModel> Snapshot => _snapshot;
    public TerminalSessionModel? Active { get; private set; }

    public event Action<TerminalSessionModel>? SessionAdded;
    public event Action<TerminalSessionModel>? SessionRemoved;
    public event Action<TerminalSessionModel?>? ActiveChanged;
    /// <summary>Raised when a session's child process state changes (e.g. it exited).</summary>
    public event Action<TerminalSessionModel>? SessionStateChanged;

    /// <summary>Number of sessions whose child process is alive.</summary>
    public int RunningCount => Sessions.Count(s => s.IsRunning);

    public async Task<TerminalSessionModel> CreateAsync(
        Func<IPtySession> ptyFactory,
        PtyOptions options,
        string? name = null,
        SessionTag tag = SessionTag.None,
        int columns = 120,
        int rows = 30,
        CancellationToken cancellationToken = default)
    {
        var emulator = new TerminalEmulator(ptyFactory(), columns, rows);
        try
        {
            await emulator.StartAsync(options, cancellationToken);
        }
        catch
        {
            // A half-started PTY owns a live process/handles — dispose it here
            // or every failed spawn leaks one.
            emulator.Dispose();
            throw;
        }

        var session = new TerminalSessionModel
        {
            Name = name ?? NextAutoName(),
            Tag = tag,
            Emulator = emulator,
            WorkingDirectory = options.WorkingDirectory,
            Shell = options.Shell,
            ShellArguments = options.Arguments,
        };
        emulator.Pty.Exited += (_, _) => SessionStateChanged?.Invoke(session);

        Sessions.Add(session);
        _snapshot = Sessions.ToArray();
        SessionAdded?.Invoke(session);
        Activate(session);
        return session;
    }

    /// <summary>Reuse the lowest free number, including names held by popouts.</summary>
    private string NextAutoName()
    {
        string candidate;
        var number = 0;
        do { candidate = $"Terminal {++number:D2}"; }
        while (Sessions.Concat(_detached).Any(s => s.Name == candidate));
        return candidate;
    }

    public void Activate(TerminalSessionModel? session)
    {
        if (ReferenceEquals(Active, session)) return;
        Active = session;
        ActiveChanged?.Invoke(session);
    }

    public void Close(TerminalSessionModel session)
    {
        session.Pty.Kill();
        Sessions.Remove(session);
        _detached.Remove(session);
        _snapshot = Sessions.ToArray();
        SessionRemoved?.Invoke(session);
        if (ReferenceEquals(Active, session))
            Activate(Sessions.LastOrDefault());
        session.Dispose();
    }

    /// <summary>Detach a session without killing it: leaves the list, fires
    /// <see cref="SessionRemoved"/>, drops active selection — but the child process
    /// and emulator stay alive. Ownership transfers to the caller (e.g. a popout
    /// window); hand it back via <see cref="Reattach"/> or dispose it yourself.
    /// Returns null when the session is not in this manager.</summary>
    public TerminalSessionModel? Detach(TerminalSessionModel session)
    {
        if (!Sessions.Remove(session)) return null;
        _detached.Add(session);
        _snapshot = Sessions.ToArray();
        SessionRemoved?.Invoke(session);
        if (ReferenceEquals(Active, session))
            Activate(Sessions.LastOrDefault());
        return session;
    }

    /// <summary>Re-adopt a detached session into the list and activate it.</summary>
    public void Reattach(TerminalSessionModel session)
    {
        if (Sessions.Contains(session)) return;
        _detached.Remove(session);
        Sessions.Add(session);
        _snapshot = Sessions.ToArray();
        SessionAdded?.Invoke(session);
        Activate(session);
    }

    /// <summary>Rename a session (tab title).</summary>
    public void Rename(TerminalSessionModel session, string name) => session.Name = name;
}
