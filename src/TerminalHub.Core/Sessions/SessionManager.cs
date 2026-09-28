using System.Collections.ObjectModel;
using TerminalHub.Core.Pty;

namespace TerminalHub.Core.Sessions;

/// <summary>Tracks live terminal sessions: create / switch / close.</summary>
public sealed class SessionManager
{
    private int _counter;

    public ObservableCollection<TerminalSessionModel> Sessions { get; } = [];
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
        CancellationToken cancellationToken = default)
    {
        var pty = ptyFactory();
        await pty.StartAsync(options, cancellationToken);

        var session = new TerminalSessionModel
        {
            Name = name ?? $"Terminal {++_counter:D2}",
            Tag = tag,
            Pty = pty,
            WorkingDirectory = options.WorkingDirectory,
        };
        pty.Exited += (_, _) => SessionStateChanged?.Invoke(session);

        Sessions.Add(session);
        SessionAdded?.Invoke(session);
        Activate(session);
        return session;
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
        SessionRemoved?.Invoke(session);
        if (ReferenceEquals(Active, session))
            Activate(Sessions.LastOrDefault());
        session.Dispose();
    }

    /// <summary>Rename a session (tab title).</summary>
    public void Rename(TerminalSessionModel session, string name) => session.Name = name;
}
