using CommunityToolkit.Mvvm.ComponentModel;
using TerminalHub.Core.Sessions;
using TerminalHub.Core.Settings;
using TerminalHub.Core.Terminal;

namespace TerminalHub.App.ViewModels;

/// <summary>One saved-output bookmark. <see cref="Session"/> and <see cref="Anchor"/>
/// exist only while the source session is alive in this run — a restored or
/// orphaned bookmark still shows and copies its saved text.</summary>
public sealed partial class BookmarkViewModel : ObservableObject
{
    public string Id { get; }
    [ObservableProperty] private string _name = "";
    public string SessionName { get; }
    public DateTimeOffset CreatedAt { get; }
    /// <summary>Saved text snapshot — always viewable/copyable.</summary>
    public string Text { get; }
    /// <summary>First lines joined into one preview row for the list.</summary>
    public string Preview { get; }
    /// <summary>Live source session; null once it closed (or for restored bookmarks).</summary>
    public TerminalSessionModel? Session { get; private set; }
    /// <summary>Runtime locate anchor — never persisted.</summary>
    public BufferAnchor? Anchor { get; private set; }
    /// <summary>The source session was closed after the bookmark was taken.</summary>
    public bool SessionClosed { get; private set; }
    [ObservableProperty] private string _locateText = "";

    public string Meta => $"{SessionName} · {CreatedAt:MM-dd HH:mm}";

    private BookmarkViewModel(string id, string name, string sessionName,
        DateTimeOffset createdAt, string text)
    {
        Id = id;
        _name = name;
        SessionName = sessionName;
        CreatedAt = createdAt;
        Text = text;
        Preview = MakePreview(text);
    }

    public static BookmarkViewModel Live(TerminalSessionModel session, BufferAnchor anchor,
        string name, string text)
        => new(Guid.NewGuid().ToString("N"), name, session.Name, DateTimeOffset.Now, text)
        { Session = session, Anchor = anchor };

    public static BookmarkViewModel Restored(OutputBookmark saved)
        => new(saved.Id, saved.Name, saved.SessionName, saved.CreatedAt, saved.Text);

    public void MarkSessionClosed()
    {
        Session = null;
        SessionClosed = true;
    }

    /// <summary>Filter match over name, source session and saved text.</summary>
    public bool Matches(string query)
        => query.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(part =>
            (Name + " " + SessionName + " " + Text).Contains(part, StringComparison.OrdinalIgnoreCase));

    public OutputBookmark Snapshot() => new()
    {
        Id = Id,
        Name = Name,
        SessionName = SessionName,
        CreatedAt = CreatedAt,
        Text = Text,
    };

    private static string MakePreview(string text)
    {
        var lines = text.Replace("\r", "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var joined = string.Join(" ⏎ ", lines.Take(2));
        return joined.Length > 80 ? joined[..80] + "…" : joined;
    }
}
