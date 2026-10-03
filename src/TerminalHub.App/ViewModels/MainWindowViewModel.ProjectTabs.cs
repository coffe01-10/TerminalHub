using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;
using TerminalHub.Core.Sessions;
using TerminalHub.Core.Settings;

namespace TerminalHub.App.ViewModels;

public partial class MainWindowViewModel
{
    public ObservableCollection<LiveWorkspace> ProjectWorkspaces { get; } = [];
    [ObservableProperty] private LiveWorkspace _activeWorkspace = null!;
    private readonly ConcurrentDictionary<Guid, LiveWorkspace> _sessionWorkspaces = new();
    private bool _switchingWorkspace, _restoringProjects;
    public IEnumerable<SessionCardViewModel> AllSessionCards => ProjectWorkspaces.SelectMany(w => w.Cards);

    private void InitializeProjectTabs()
    {
        foreach (var saved in _settings.ProjectWorkspaces)
            ProjectWorkspaces.Add(new(saved.Id, saved.Name)
            { Layout = saved.Layout, ShelfAutoHide = saved.ShelfAutoHide, ShelfWidth = saved.ShelfWidth });
        if (ProjectWorkspaces.Count == 0)
            ProjectWorkspaces.Add(new(Guid.NewGuid().ToString("N"), WorkspaceName)
            { ShelfAutoHide = ShelfAutoHide, ShelfWidth = ShelfWidth });
        ActiveWorkspace = ProjectWorkspaces.FirstOrDefault(w => w.Id == _settings.ActiveProjectWorkspaceId) ?? ProjectWorkspaces[0];
        ActiveWorkspace.IsActive = true;
        foreach (var workspace in ProjectWorkspaces)
            workspace.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(LiveWorkspace.Name) && workspace == ActiveWorkspace)
                { WorkspaceName = workspace.Name; _settings.WorkspaceName = workspace.Name; }
            };
    }

    [RelayCommand]
    private async Task NewProjectWorkspace()
    {
        var workspace = new LiveWorkspace(Guid.NewGuid().ToString("N"), $"工作区 {ProjectWorkspaces.Count + 1}");
        workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LiveWorkspace.Name) && workspace == ActiveWorkspace)
            { WorkspaceName = workspace.Name; _settings.WorkspaceName = workspace.Name; }
        };
        ProjectWorkspaces.Add(workspace);
        SwitchProjectWorkspace(workspace);
        await NewSession();
    }

    [RelayCommand]
    public void SwitchProjectWorkspace(LiveWorkspace? workspace)
    {
        if (workspace is null || workspace == ActiveWorkspace || _switchingWorkspace) return;
        _switchingWorkspace = true;
        try
        {
            RememberActiveWorkspace();
            ActiveWorkspace.ShelfAutoHide = ShelfAutoHide;
            ActiveWorkspace.ShelfWidth = ShelfWidth;
            ActiveWorkspace.IsActive = false;
            foreach (var card in SessionCards) card.SetDisplayed(false);
            StopBroadcast();
            ExitSplit();
            ActiveWorkspace = workspace;
            SessionCards.ReplaceAll(workspace.Cards);
            workspace.IsActive = true;
            WorkspaceName = workspace.Name;
            _settings.WorkspaceName = workspace.Name;
            ShelfAutoHide = workspace.ShelfAutoHide;
            ShelfWidth = workspace.ShelfWidth;
            RestoreLiveLayout(workspace);
            SyncActive();
            RefreshProjectTools();
            RefreshCounts();
        }
        finally { _switchingWorkspace = false; }
        NotifyWorkbench(TerminalHub.Extensibility.WorkbenchEventKind.WorkspaceChanged);
    }

    [RelayCommand]
    private void CloseProjectWorkspace(LiveWorkspace? workspace)
    {
        if (workspace is null) return;
        ClearLayoutHistory();
        if (workspace == ActiveWorkspace)
        {
            var next = ProjectWorkspaces.FirstOrDefault(w => w != workspace);
            if (next is null)
            {
                next = new(Guid.NewGuid().ToString("N"), "工作区");
                ProjectWorkspaces.Add(next);
            }
            SwitchProjectWorkspace(next);
        }
        foreach (var session in _sessionWorkspaces.Where(pair => pair.Value == workspace)
            .Select(pair => _sessions.Sessions.Concat(DetachedSessions).FirstOrDefault(s => s.Id == pair.Key)).OfType<TerminalSessionModel>().ToArray())
        {
            if (session.Detached) _popouts.FirstOrDefault(w => w.Session == session)?.Close();
            _sessions.Close(session);
        }
        ProjectWorkspaces.Remove(workspace);
        _allProjectTasks.RemoveAll(task => task.WorkspaceId == workspace.Id);
        SaveSettingsInternal();
    }

    public void MoveProjectWorkspace(LiveWorkspace workspace, int index)
    {
        var from = ProjectWorkspaces.IndexOf(workspace);
        if (from < 0 || from == index) return;
        ProjectWorkspaces.Move(from, index);
        SaveSettingsInternal();
    }

    private async Task RestoreProjectWorkspacesAsync()
    {
        _restoringProjects = true;
        var activeId = _settings.ActiveProjectWorkspaceId;
        try
        {
            foreach (var saved in _settings.ProjectWorkspaces.ToArray())
            {
                var workspace = ProjectWorkspaces.First(w => w.Id == saved.Id);
                SwitchProjectWorkspace(workspace);
                await RestoreWorkspaceAsync(saved.Layout);
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                workspace.Layout = CaptureWorkspace();
            }
            SwitchProjectWorkspace(ProjectWorkspaces.FirstOrDefault(w => w.Id == activeId) ?? ProjectWorkspaces[0]);
        }
        finally { _restoringProjects = false; }
        SyncActive();
    }

    private void SnapshotProjectTabs()
    {
        RememberActiveWorkspace();
        ActiveWorkspace.ShelfAutoHide = ShelfAutoHide;
        ActiveWorkspace.ShelfWidth = ShelfWidth;
        _settings.ActiveProjectWorkspaceId = ActiveWorkspace.Id;
        _settings.ProjectWorkspaces = ProjectWorkspaces.Select(workspace => new ProjectWorkspaceState
        { Id = workspace.Id, Name = workspace.Name, Layout = CaptureWorkspace(workspace),
            ShelfAutoHide = workspace.ShelfAutoHide, ShelfWidth = workspace.ShelfWidth }).ToList();
    }
    private void RememberActiveWorkspace()
    {
        ActiveWorkspace.ShelfAutoHide = ShelfAutoHide; ActiveWorkspace.ShelfWidth = ShelfWidth;
        ActiveWorkspace.Layout = CaptureWorkspace(); ActiveWorkspace.SavedActive = _sessions.Active;
        ActiveWorkspace.PaneMaximized = PaneMaximized;
        ActiveWorkspace.SavedTree = PaneTree?.Clone();
        for (var i = 0; i < 4; i++) ActiveWorkspace.SavedPanes[i] = GetPane(i);
    }
}
