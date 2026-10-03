using CommunityToolkit.Mvvm.ComponentModel;
using TerminalHub.Core.Settings;

namespace TerminalHub.App.ViewModels;

public sealed partial class SessionGroupHeader : ObservableObject
{
    public SessionGroup? Group { get; }
    public bool IsPinSection => Group is null;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _activity = "";

    public SessionGroupHeader(SessionGroup? group) => Group = group;

    public void Refresh(IReadOnlyList<SessionCardViewModel> cards)
    {
        var members = Group is null
            ? cards.Where(card => card.Model.Pinned).ToList()
            : cards.Where(card => card.Model.GroupId == Group.Id).ToList();
        var mark = Group is null ? TerminalHub.Core.Localization.Localizer.Current.Translate("置顶") : Group.Collapsed ? Group.Name + "  ▸" : Group.Name + "  ▾";
        Title = $"{mark}  {members.Count}";
        Activity = members.Any(card => card.HasUnreadOutput) ? "有新输出"
            : members.Any(card => !card.Model.IsRunning) ? "有终端已退出"
            : "";
    }
}
