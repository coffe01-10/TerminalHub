using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHub.Core.Sessions;
using TerminalHub.Core.Terminal;

namespace TerminalHub.App.ViewModels;

/// <summary>Output bookmarks: a named text snapshot plus a runtime buffer anchor
/// for jumping back to the source line. Live data is shared by the main view
/// and split panes; persisted snapshots (name/session/time/text) survive restarts.</summary>
public partial class MainWindowViewModel
{
    /// <summary>All bookmarks, newest first (live sessions + restored orphans).</summary>
    public ObservableCollection<BookmarkViewModel> Bookmarks { get; } = [];
    /// <summary>Bookmarks passing the current scope + text filter.</summary>
    public ObservableCollection<BookmarkViewModel> VisibleBookmarks { get; } = [];
    [ObservableProperty] private string _bookmarkFilter = "";
    /// <summary>false = the active session's bookmarks; true = every session's.</summary>
    [ObservableProperty] private bool _bookmarksAllSessions;
    [ObservableProperty] private string _bookmarkNotice = "";
    [ObservableProperty] private BookmarkViewModel? _selectedBookmark;
    /// <summary>Raised so the window can reveal a bookmark's anchor in its view.</summary>
    public event Action<BookmarkViewModel>? RevealBookmarkRequested;

    private void LoadBookmarks()
    {
        foreach (var saved in _settings.OutputBookmarks)
            Bookmarks.Add(BookmarkViewModel.Restored(saved));
        RefreshBookmarkList();
    }

    partial void OnBookmarkFilterChanged(string value) => RefreshBookmarkList();
    partial void OnBookmarksAllSessionsChanged(bool value) => RefreshBookmarkList();

    /// <summary>Rebuild the visible list and refresh each row's locate state.</summary>
    public void RefreshBookmarkList()
    {
        var active = ActiveSession;
        var query = BookmarkFilter?.Trim() ?? "";
        VisibleBookmarks.Clear();
        foreach (var bm in Bookmarks)
        {
            if (!BookmarksAllSessions && !ForSession(bm, active)) continue;
            if (query.Length > 0 && !bm.Matches(query)) continue;
            VisibleBookmarks.Add(bm);
        }
        foreach (var bm in Bookmarks) bm.LocateText = LocateStateText(bm);
    }

    /// <summary>Scope match: the live session itself, or restored snapshots by
    /// session name. Closed-this-run bookmarks keep their name but must not
    /// reattach to a new session that merely recycled the same auto name.</summary>
    private static bool ForSession(BookmarkViewModel bm, TerminalSessionModel? active)
        => active is not null && (ReferenceEquals(bm.Session, active)
            || (bm.Session is null && !bm.SessionClosed && bm.SessionName == active.Name));

    private string LocateStateText(BookmarkViewModel bm)
    {
        var session = bm.Session;
        if (session is null)
            return bm.SessionClosed ? "原会话已关闭" : "重启前保存";
        if (!_sessions.Sessions.Contains(session))
            return DetachedSessions.Contains(session) ? "会话在独立窗口" : "原会话已关闭";
        if (bm.Anchor is null) return "";
        var buf = session.Emulator.Buffer;
        lock (buf.SyncRoot)
        {
            // Same wording as command records: the bookmark is fine, but the
            // buffer is currently showing the other screen.
            if (bm.Anchor.Alive && bm.Anchor.Alternate != buf.OnAlternateScreen)
                return "屏幕不同";
            return buf.ResolveAnchor(bm.Anchor) is null ? "输出已不在历史中" : "可定位";
        }
    }

    /// <summary>Auto name from the first non-empty snapshot line (caller may edit).</summary>
    public string SuggestBookmarkName(string text)
    {
        var first = text.Replace("\r", "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "";
        var name = first.Length > 20 ? first[..20] + "…" : first;
        return name.Length == 0 ? "书签 " + DateTime.Now.ToString("HH:mm:ss") : name;
    }

    /// <summary>Register a bookmark on a live session. The anchor was created by
    /// the view's capture step and is stored so the jump survives history trims.</summary>
    public BookmarkViewModel AddBookmark(TerminalSessionModel session, BufferAnchor anchor,
        string name, string text)
    {
        var bm = BookmarkViewModel.Live(session, anchor, UniqueBookmarkName(name), text);
        Bookmarks.Insert(0, bm);
        PersistBookmarks();
        RefreshBookmarkList();
        return bm;
    }

    public void RenameBookmark(BookmarkViewModel bm, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        bm.Name = name.Trim();
        PersistBookmarks();
        RefreshBookmarkList();
    }

    [RelayCommand]
    private void DeleteBookmark(BookmarkViewModel? bm)
    {
        if (bm is null) return;
        RemoveBookmarkAnchor(bm);
        Bookmarks.Remove(bm);
        PersistBookmarks();
        RefreshBookmarkList();
    }

    [RelayCommand]
    private async Task CopyBookmark(BookmarkViewModel? bm)
    {
        if (bm is null || bm.Text.Length == 0) return;
        BookmarkNotice = await CopyTextToClipboardAsync(bm.Text)
            ? "已复制书签文字。"
            : "复制失败：剪贴板暂时不可用，请重试。";
    }

    /// <summary>Jump to the bookmark's origin: activate its session (assigning the
    /// focused pane in split mode), then let the window reveal the anchor.</summary>
    [RelayCommand]
    private void LocateBookmark(BookmarkViewModel? bm)
    {
        if (bm is null) return;
        var session = bm.Session;
        if (session is null || bm.Anchor is null)
        {
            BookmarkNotice = bm.SessionClosed
                ? "原会话已关闭，只能查看或复制保存的文字。"
                : "这是重启前保存的书签，只能查看或复制保存的文字。";
            return;
        }
        if (!_sessions.Sessions.Contains(session))
        {
            BookmarkNotice = DetachedSessions.Contains(session)
                ? "原会话在独立窗口中，书签文字仍可复制。"
                : "原会话已关闭，只能查看或复制保存的文字。";
            return;
        }
        if (IsSplit)
        {
            if (!ReferenceEquals(GetPane(FocusedPane), session))
                AssignToPane(FocusedPane, session);
            if (!ReferenceEquals(_sessions.Active, session))
                _sessions.Activate(session);
        }
        else
        {
            ActiveCard = SessionCards.FirstOrDefault(c => ReferenceEquals(c.Model, session));
        }
        BookmarkNotice = "";
        RevealBookmarkRequested?.Invoke(bm);
    }

    /// <summary>Mark bookmarks whose source session just closed (popout detaches
    /// keep theirs). Their snapshot stays viewable/copyable.</summary>
    private void OnBookmarkSessionClosed(TerminalSessionModel session)
    {
        var any = false;
        foreach (var bm in Bookmarks)
            if (ReferenceEquals(bm.Session, session)) { bm.MarkSessionClosed(); any = true; }
        if (any) RefreshBookmarkList();
    }

    private void RemoveBookmarkAnchor(BookmarkViewModel bm)
    {
        if (bm.Anchor is { } anchor && bm.Session is { } session)
        {
            var buf = session.Emulator.Buffer;
            lock (buf.SyncRoot) buf.Anchors.Remove(anchor);
        }
    }

    private string UniqueBookmarkName(string name)
    {
        name = name.Trim();
        if (name.Length == 0) name = "书签";
        if (Bookmarks.All(b => b.Name != name)) return name;
        for (var i = 2; ; i++)
        {
            var candidate = $"{name} ({i})";
            if (Bookmarks.All(b => b.Name != candidate)) return candidate;
        }
    }

    private void PersistBookmarks()
    {
        _settings.OutputBookmarks = Bookmarks.Select(b => b.Snapshot()).ToList();
        SaveSettingsInternal();
    }
}
