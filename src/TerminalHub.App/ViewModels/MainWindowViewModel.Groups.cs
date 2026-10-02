using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHub.Core.Sessions;
using TerminalHub.Core.Settings;

namespace TerminalHub.App.ViewModels;

public partial class MainWindowViewModel
{
    public ObservableCollection<SessionGroup> SessionGroups { get; } = [];
    public ObservableCollection<object> ShelfItems { get; } = [];
    private readonly Dictionary<string, SessionGroupHeader> _groupHeaders = new();
    private bool _rebuildingShelf;

    private void LoadGroups()
    {
        foreach (var group in _settings.SessionGroups)
            if (!string.IsNullOrWhiteSpace(group.Id))
            {
                SessionGroups.Add(group);
                WatchGroup(group);
            }
        RebuildShelf();
    }

    private void WatchGroup(SessionGroup group)
    {
        group.PropertyChanged += (_, _) =>
        {
            if (!_disposed) UpdateGroupActivity();
        };
    }

    public string DescribeSession(SessionCardViewModel card)
    {
        var group = SessionGroups.FirstOrDefault(item => item.Id == card.Model.GroupId)?.Name;
        var prefix = card.Model.Pinned ? "置顶 · " : "";
        if (!string.IsNullOrEmpty(group)) prefix += group + " · ";
        return prefix + card.WorkingDirectory + " · " + card.Model.Shell;
    }

    public void ActivateCard(SessionCardViewModel card)
    {
        ExpandGroupOf(card);
        ActiveCard = card;
    }

    private void ExpandGroupOf(SessionCardViewModel card)
    {
        if (card.Model.Pinned || string.IsNullOrEmpty(card.Model.GroupId)) return;
        var group = SessionGroups.FirstOrDefault(item => item.Id == card.Model.GroupId);
        if (group is not { Collapsed: true }) return;
        group.Collapsed = false;
        RebuildShelf();
    }

    [RelayCommand]
    private void TogglePinActive()
    {
        if (ActiveCard is null) return;
        SetPinned(ActiveCard, !ActiveCard.Model.Pinned);
    }

    [RelayCommand]
    private void RemoveActiveFromGroup()
    {
        if (ActiveCard is null) return;
        MoveCardToGroup(ActiveCard, null);
    }

    [RelayCommand]
    private void DeleteGroup(SessionGroup? group)
    {
        if (group is null) return;
        foreach (var card in SessionCards)
            if (card.Model.GroupId == group.Id) card.Model.GroupId = "";
        SessionGroups.Remove(group);
        _groupHeaders.Remove(group.Id);
        RebuildShelf();
        SaveSettingsInternal();
    }

    public void CreateGroup(string name, SessionCardViewModel? card = null)
    {
        name = name.Trim();
        if (name.Length == 0) return;
        var group = new SessionGroup { Name = name };
        SessionGroups.Add(group);
        WatchGroup(group);
        if (card is not null) MoveCardToGroup(card, group.Id);
        else
        {
            RebuildShelf();
            SaveSettingsInternal();
        }
    }

    public void RenameGroup(SessionGroup group, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        group.Name = name.Trim();
        UpdateGroupActivity();
        SaveSettingsInternal();
    }

    public void SetPinned(SessionCardViewModel card, bool pinned)
    {
        card.Model.Pinned = pinned;
        SessionCards.Remove(card);
        var index = pinned ? SessionCards.TakeWhile(item => item.Model.Pinned).Count() : IndexFor(card);
        SessionCards.Insert(Math.Clamp(index, 0, SessionCards.Count), card);
        SaveSettingsInternal();
    }

    public void MoveCardToGroup(SessionCardViewModel card, string? groupId)
    {
        card.Model.GroupId = groupId ?? "";
        SessionCards.Remove(card);
        SessionCards.Insert(Math.Clamp(IndexFor(card), 0, SessionCards.Count), card);
        SaveSettingsInternal();
    }

    public void ToggleGroup(SessionGroupHeader header)
    {
        if (header.Group is null) return;
        header.Group.Collapsed = !header.Group.Collapsed;
        RebuildShelf();
        SaveSettingsInternal();
    }

    private int IndexFor(SessionCardViewModel card)
    {
        if (card.Model.Pinned) return SessionCards.TakeWhile(item => item.Model.Pinned).Count();
        if (string.IsNullOrEmpty(card.Model.GroupId)) return SessionCards.Count;
        var last = -1;
        for (var i = 0; i < SessionCards.Count; i++)
            if (!SessionCards[i].Model.Pinned && SessionCards[i].Model.GroupId == card.Model.GroupId) last = i;
        if (last >= 0) return last + 1;
        var order = SessionGroups.Select(group => group.Id).ToList();
        var mine = order.IndexOf(card.Model.GroupId);
        for (var i = 0; i < SessionCards.Count; i++)
        {
            var other = SessionCards[i];
            if (other.Model.Pinned) continue;
            var otherIndex = string.IsNullOrEmpty(other.Model.GroupId) ? int.MaxValue : order.IndexOf(other.Model.GroupId);
            if (otherIndex < 0) otherIndex = int.MaxValue;
            if (otherIndex > mine) return i;
        }
        return SessionCards.Count;
    }

    private void RebuildShelf()
    {
        if (_disposed) return;
        var next = new List<object>();
        var pinSeen = false;
        string? current = null;
        foreach (var card in SessionCards)
        {
            if (card.Model.Pinned)
            {
                if (!pinSeen)
                {
                    next.Add(Header("pin", null));
                    pinSeen = true;
                    current = null;
                }
                next.Add(card);
                continue;
            }
            if (pinSeen) { pinSeen = false; current = null; }
            var group = string.IsNullOrEmpty(card.Model.GroupId) ? null : SessionGroups.FirstOrDefault(item => item.Id == card.Model.GroupId);
            if (group is null)
            {
                current = "";
                next.Add(card);
                continue;
            }
            if (current != group.Id)
            {
                next.Add(Header(group.Id, group));
                current = group.Id;
            }
            if (!group.Collapsed) next.Add(card);
        }
        foreach (var group in SessionGroups)
        {
            if (next.OfType<SessionGroupHeader>().Any(header => ReferenceEquals(header.Group, group))) continue;
            next.Add(Header(group.Id, group));
        }
        // A card is exempt from the overlap margin when it tops a stack
        // segment: the first shelf item, or the card right below a header —
        // overlapping there would cover the header's bottom half.
        for (var i = 0; i < next.Count; i++)
            if (next[i] is SessionCardViewModel card)
                card.IsStackTop = i == 0 || next[i - 1] is not SessionCardViewModel;
        var keep = ActiveCard;
        _rebuildingShelf = true;
        try
        {
            for (var i = 0; i < next.Count; i++)
            {
                if (i < ShelfItems.Count && ReferenceEquals(ShelfItems[i], next[i])) continue;
                if (i < ShelfItems.Count) ShelfItems[i] = next[i];
                else ShelfItems.Add(next[i]);
            }
            while (ShelfItems.Count > next.Count) ShelfItems.RemoveAt(ShelfItems.Count - 1);
            UpdateGroupActivity();
        }
        finally { _rebuildingShelf = false; }
        if (keep is not null && !ReferenceEquals(ActiveCard, keep)) ActiveCard = keep;
    }

    private SessionGroupHeader Header(string key, SessionGroup? group)
    {
        if (!_groupHeaders.TryGetValue(key, out var header))
            _groupHeaders[key] = header = new SessionGroupHeader(group);
        return header;
    }

    private void UpdateGroupActivity()
    {
        foreach (var card in SessionCards) card.SetShelfCaption(ShelfCaption(card));
        foreach (var header in ShelfItems.OfType<SessionGroupHeader>()) header.Refresh(SessionCards);
    }

    private string ShelfCaption(SessionCardViewModel card)
    {
        var group = SessionGroups.FirstOrDefault(item => item.Id == card.Model.GroupId)?.Name;
        var prefix = card.Model.Pinned ? "置顶 · " : "";
        if (!string.IsNullOrEmpty(group)) prefix += group + " · ";
        return prefix + card.DirectoryName;
    }

    private void RememberSessionLayout(TerminalSessionModel model, WorkspaceSession session)
    {
        model.GroupId = session.GroupId ?? "";
        model.Pinned = session.Pinned;
        model.Emulator.ColorScheme = session.ColorScheme;
        if (string.IsNullOrEmpty(model.GroupId) || SessionGroups.Any(group => group.Id == model.GroupId)) return;
        if (string.IsNullOrWhiteSpace(session.GroupName)) { model.GroupId = ""; return; }
        var group = new SessionGroup { Id = model.GroupId, Name = session.GroupName };
        SessionGroups.Add(group);
        WatchGroup(group);
    }

    [RelayCommand]
    private void ToggleActiveGroup()
    {
        if (ActiveCard is null || string.IsNullOrEmpty(ActiveCard.Model.GroupId)) return;
        var header = ShelfItems.OfType<SessionGroupHeader>()
            .FirstOrDefault(item => item.Group?.Id == ActiveCard.Model.GroupId);
        if (header is not null) ToggleGroup(header);
    }
}
