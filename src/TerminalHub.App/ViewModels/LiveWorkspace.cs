using CommunityToolkit.Mvvm.ComponentModel;
using TerminalHub.Core.Settings;
using TerminalHub.Core.Sessions;

namespace TerminalHub.App.ViewModels;

public partial class LiveWorkspace : ViewModelBase
{
    public string Id { get; }
    [ObservableProperty] private string _name;
    [ObservableProperty] private bool _isActive;
    public List<SessionCardViewModel> Cards { get; } = [];
    public WorkspaceState Layout { get; set; } = new();
    public TerminalSessionModel? SavedActive { get; set; }
    public TerminalSessionModel?[] SavedPanes { get; } = new TerminalSessionModel?[4];
    public bool ShelfAutoHide { get; set; }
    public double ShelfWidth { get; set; }
    public int Count => Cards.Count;
    public TerminalSessionModel? Active => SavedActive is { } active && Cards.Any(c => c.Model == active)
        ? active : Cards.ElementAtOrDefault(Layout.ActiveIndex)?.Model;
    public LiveWorkspace(string id, string name) { Id = id; _name = name; }
    public void Refresh() => OnPropertyChanged(nameof(Count));
}
