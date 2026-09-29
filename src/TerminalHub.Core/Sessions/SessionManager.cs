using System.Collections.ObjectModel;
using TerminalHub.Core.Pty;
using TerminalHub.Core.Terminal;

namespace TerminalHub.Core.Sessions;

/// <summary>Tracks live terminal sessions: create / switch / close.</summary>
public sealed class SessionManager
{
    private int _counter;

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
        await emulator.StartAsync(options, cancellationToken);

        var session = new TerminalSessionModel
        {
            Name = name ?? NextAutoName(),
            Tag = tag,
            Emulator = emulator,
            WorkingDirectory = options.WorkingDirectory,
        };
        emulator.Pty.Exited += (_, _) => SessionStateChanged?.Invoke(session);

        Sessions.Add(session);
        _snapshot = Sessions.ToArray();
        SessionAdded?.Invoke(session);
        Activate(session);
        return session;
    }

    /// <summary>Auto-numbered names must skip names already taken — startup
    /// sessions carry explicit names that never touched <see cref="_counter"/>,
    /// and renames can claim any number.</summary>
    private string NextAutoName()
    {
        string candidate;
        do { candidate = $"Terminal {++_counter:D2}"; }
        while (Sessions.Any(s => s.Name == candidate));
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
        Sessions.Add(session);
        _snapshot = Sessions.ToArray();
        SessionAdded?.Invoke(session);
        Activate(session);
    }

    /// <summary>Rename a session (tab title).</summary>
    public void Rename(TerminalSessionModel session, string name) => session.Name = name;
}
