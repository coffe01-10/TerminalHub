using CommunityToolkit.Mvvm.Input;
using TerminalHub.Core.Sessions;
using TerminalHub.Core.Settings;

namespace TerminalHub.App.ViewModels;

public partial class MainWindowViewModel
{
    private sealed record WorkspaceLayout(LiveWorkspace Workspace, SessionCardViewModel[] Cards,
        TerminalSessionModel? Active, TerminalSessionModel?[] Panes, bool Split, SplitLayout Kind,
        int Focus, double Columns, double Rows, bool Maximized, PaneNode? Tree);
    private sealed record LayoutSnapshot(LiveWorkspace Active, WorkspaceLayout[] Workspaces,
        Dictionary<TerminalSessionModel, (string Group, bool Pinned)> Positions);
    private sealed record LayoutOperation(string Name, LayoutSnapshot Before, LayoutSnapshot After);
    private readonly List<LayoutOperation> _layoutUndo = [], _layoutRedo = [];
    private LayoutSnapshot? _layoutGesture;
    private string _layoutGestureName = "";
    private bool _applyingLayout;

    public bool CanUndoLayout => _layoutUndo.Count > 0;
    public bool CanRedoLayout => _layoutRedo.Count > 0;
    public string UndoLayoutText => CanUndoLayout ? "撤销布局 · " + TerminalHub.Core.Localization.Localizer.Current.Translate(_layoutUndo[^1].Name) : "撤销布局";
    public string RedoLayoutText => CanRedoLayout ? "重做布局 · " + TerminalHub.Core.Localization.Localizer.Current.Translate(_layoutRedo[^1].Name) : "重做布局";

    private LayoutSnapshot CaptureLayout()
    {
        RememberActiveWorkspace();
        return new(ActiveWorkspace, ProjectWorkspaces.Select(w => new WorkspaceLayout(w, w.Cards.ToArray(),
            w.SavedActive, w.SavedPanes.ToArray(), w.Layout.IsSplit, w.Layout.SplitLayout,
            w.Layout.FocusedPane, w.Layout.ColumnRatio, w.Layout.RowRatio,
            w == ActiveWorkspace ? PaneMaximized : w.PaneMaximized, w.SavedTree?.Clone())).ToArray(),
            AllSessionCards.ToDictionary(c => c.Model, c => (c.Model.GroupId, c.Model.Pinned)));
    }

    // A pointer gesture can hover into another workspace; capture before that preview.
    public void BeginLayoutGesture(string name)
    {
        if (_applyingLayout || _restoringProjects || _layoutGesture is not null) return;
        _layoutGesture = CaptureLayout(); _layoutGestureName = name;
    }

    public void CompleteLayoutGesture(bool commit = true)
    {
        var before = _layoutGesture;
        _layoutGesture = null;
        if (before is not null && commit) RecordLayout(_layoutGestureName, before);
    }

    private void RecordLayout(string name, LayoutSnapshot before)
    {
        if (_applyingLayout || _restoringProjects || _layoutGesture is not null) return;
        var after = CaptureLayout();
        // Ignore a click on a splitter or a drop back in its original slot.
        if (before.Workspaces.Length == after.Workspaces.Length && before.Workspaces.Zip(after.Workspaces).All(pair =>
            pair.First.Workspace == pair.Second.Workspace && pair.First.Cards.SequenceEqual(pair.Second.Cards)
            && pair.First.Panes.SequenceEqual(pair.Second.Panes) && pair.First.Split == pair.Second.Split
            && pair.First.Kind == pair.Second.Kind && pair.First.Columns == pair.Second.Columns
            && SameTree(pair.First.Tree, pair.Second.Tree) && pair.First.Rows == pair.Second.Rows && pair.First.Maximized == pair.Second.Maximized)
            && before.Positions.All(pair => after.Positions.TryGetValue(pair.Key, out var value) && pair.Value == value)) return;
        _layoutUndo.Add(new(name, before, after));
        if (_layoutUndo.Count > 100) _layoutUndo.RemoveAt(0);
        _layoutRedo.Clear();
        NotifyLayoutHistory();
        SaveSettingsInternal();
    }

    [RelayCommand(CanExecute = nameof(CanUndoLayout))]
    public void UndoLayout()
    {
        var operation = _layoutUndo[^1]; _layoutUndo.RemoveAt(_layoutUndo.Count - 1);
        ApplyLayout(operation.Before); _layoutRedo.Add(operation); NotifyLayoutHistory();
    }

    [RelayCommand(CanExecute = nameof(CanRedoLayout))]
    public void RedoLayout()
    {
        var operation = _layoutRedo[^1]; _layoutRedo.RemoveAt(_layoutRedo.Count - 1);
        ApplyLayout(operation.After); _layoutUndo.Add(operation); NotifyLayoutHistory();
    }

    private void ApplyLayout(LayoutSnapshot snapshot)
    {
        _applyingLayout = _switchingWorkspace = true;
        StopBroadcast();
        try
        {
            RememberActiveWorkspace();
            var live = AllSessionCards.ToDictionary(c => c.Model, c => c);
            var recorded = snapshot.Workspaces.SelectMany(w => w.Cards).Select(c => c.Model).ToHashSet();
            var extras = ProjectWorkspaces.ToDictionary(w => w, w => w.Cards.Where(c => !recorded.Contains(c.Model)).ToArray());
            foreach (var saved in snapshot.Workspaces.Where(s => ProjectWorkspaces.Contains(s.Workspace)))
            {
                var workspace = saved.Workspace;
                workspace.Cards.Clear();
                workspace.Cards.AddRange(saved.Cards.Where(c => live.ContainsKey(c.Model)).Select(c => live[c.Model]));
                // Splitting may have created terminals. Undo hides their panes but keeps the processes.
                workspace.Cards.AddRange(extras[workspace]);
                foreach (var card in workspace.Cards)
                {
                    _sessionWorkspaces[card.Model.Id] = workspace;
                    if (snapshot.Positions.TryGetValue(card.Model, out var position))
                    { card.Model.GroupId = position.Group; card.Model.Pinned = position.Pinned; }
                }
                workspace.SavedActive = saved.Active; workspace.SavedTree = saved.Tree?.Clone();
                Array.Copy(saved.Panes, workspace.SavedPanes, 4);
                workspace.Layout.IsSplit = saved.Split; workspace.Layout.SplitLayout = saved.Kind;
                workspace.Layout.FocusedPane = saved.Focus; workspace.Layout.ColumnRatio = saved.Columns;
                workspace.Layout.RowRatio = saved.Rows; workspace.PaneMaximized = saved.Maximized;
                NormalizeWorkspacePanes(workspace);
                workspace.Refresh();
            }
            foreach (var card in SessionCards) card.SetDisplayed(false);
            ActiveWorkspace.IsActive = false;
            ActiveWorkspace.ShelfWidth = ShelfWidth; ActiveWorkspace.ShelfAutoHide = ShelfAutoHide;
            ActiveWorkspace = ProjectWorkspaces.Contains(snapshot.Active) ? snapshot.Active : ActiveWorkspace;
            ActiveWorkspace.IsActive = true;
            SessionCards.ReplaceAll(ActiveWorkspace.Cards);
            WorkspaceName = ActiveWorkspace.Name; _settings.WorkspaceName = WorkspaceName;
            ShelfWidth = ActiveWorkspace.ShelfWidth; ShelfAutoHide = ActiveWorkspace.ShelfAutoHide;
            RestoreLiveLayout(ActiveWorkspace);
        }
        finally { _switchingWorkspace = _applyingLayout = false; }
        SyncActive(); RefreshCounts(); RefreshProjectTools(); SaveSettingsInternal();
        NotifyWorkbench(TerminalHub.Extensibility.WorkbenchEventKind.WorkspaceChanged);
    }

    private static bool SameTree(PaneNode? a, PaneNode? b) => a is null ? b is null : b is not null
        && a.Id == b.Id && a.Session == b.Session && a.Vertical == b.Vertical && a.Ratio == b.Ratio
        && SameTree(a.First, b.First) && SameTree(a.Second, b.Second);

    private void NotifyLayoutHistory()
    {
        OnPropertyChanged(nameof(CanUndoLayout)); OnPropertyChanged(nameof(CanRedoLayout));
        OnPropertyChanged(nameof(UndoLayoutText)); OnPropertyChanged(nameof(RedoLayoutText));
        UndoLayoutCommand.NotifyCanExecuteChanged(); RedoLayoutCommand.NotifyCanExecuteChanged();
    }

    private void ClearLayoutHistory()
    {
        // A closed process or transferred popout is not recreated by layout undo.
        _layoutUndo.Clear(); _layoutRedo.Clear(); _layoutGesture = null; NotifyLayoutHistory();
    }

    private void NormalizeWorkspacePanes(LiveWorkspace workspace)
    {
        var available = workspace.Cards.Select(c => c.Model).ToList();
        if (!available.Contains(workspace.SavedActive!)) workspace.SavedActive = available.FirstOrDefault();
        if (workspace.SavedTree is not null)
        {
            workspace.SavedTree = FillOrPrunePanes(workspace.SavedTree, available);
            var leaves = workspace.SavedTree?.Leaves.ToArray() ?? [];
            workspace.Layout.IsSplit = leaves.Length > 1;
            workspace.Layout.FocusedPane = Math.Clamp(workspace.Layout.FocusedPane, 0, Math.Max(0, leaves.Length - 1));
            Array.Clear(workspace.SavedPanes);
            for (var i = 0; i < Math.Min(4, leaves.Length); i++) workspace.SavedPanes[i] = leaves[i].Session;
            if (!workspace.Layout.IsSplit) workspace.PaneMaximized = false;
            return;
        }
        var count = workspace.Layout.SplitLayout == SplitLayout.Quad ? 4 : 2;
        if (!workspace.Layout.IsSplit) { Array.Clear(workspace.SavedPanes); return; }
        for (var i = 0; i < count; i++)
            if (!available.Contains(workspace.SavedPanes[i]!) || workspace.SavedPanes.Take(i).Contains(workspace.SavedPanes[i]))
                workspace.SavedPanes[i] = available.FirstOrDefault(s => !workspace.SavedPanes.Contains(s));
        var remaining = workspace.SavedPanes.Take(count).OfType<TerminalSessionModel>().Distinct().ToArray();
        var focused = workspace.SavedPanes[Math.Clamp(workspace.Layout.FocusedPane, 0, count - 1)];
        if (remaining.Length < 2) { workspace.Layout.IsSplit = false; workspace.PaneMaximized = false; Array.Clear(workspace.SavedPanes); }
        else if (remaining.Length < count)
        {
            workspace.Layout.SplitLayout = SplitLayout.Horizontal;
            Array.Clear(workspace.SavedPanes); Array.Copy(remaining, workspace.SavedPanes, 2);
            workspace.Layout.FocusedPane = remaining[1] == focused ? 1 : 0;
        }
        workspace.Layout.FocusedPane = Math.Clamp(workspace.Layout.FocusedPane, 0, workspace.Layout.SplitLayout == SplitLayout.Quad ? 3 : 1);
    }

    private void RestoreLiveLayout(LiveWorkspace workspace)
    {
        ExitSplit();
        var layout = workspace.Layout;
        SplitLayout = layout.SplitLayout; ColumnRatio = layout.ColumnRatio; RowRatio = layout.RowRatio;
        LoadPaneLayout(layout, i => workspace.Cards.ElementAtOrDefault(i)?.Model, workspace.SavedTree);
        PaneMaximized = IsSplit && workspace.PaneMaximized;
        _sessions.Activate(IsSplit ? GetPane(FocusedPane) : workspace.Active ?? SessionCards.FirstOrDefault()?.Model);
    }

    public void MoveSessionToWorkspace(SessionCardViewModel card, LiveWorkspace target, int index = -1, bool assignPane = true)
    {
        if (!ProjectWorkspaces.Contains(target) || !_sessionWorkspaces.TryGetValue(card.Model.Id, out var source)
            || !source.Cards.Contains(card)) return;
        var before = CaptureLayout();
        StopBroadcast();
        source.Cards.Remove(card);
        index = index < 0 ? target.Cards.Count : Math.Clamp(index, 0, target.Cards.Count);
        target.Cards.Insert(index, card);
        _sessionWorkspaces[card.Model.Id] = target;
        NormalizeWorkspacePanes(source);
        target.SavedActive = card.Model;
        source.Refresh(); target.Refresh();
        // Do not snapshot the old shelf again when the owner has already changed.
        _switchingWorkspace = true;
        try
        {
            foreach (var visible in SessionCards) visible.SetDisplayed(false);
            ActiveWorkspace.IsActive = false;
            ActiveWorkspace = target; target.IsActive = true;
            SessionCards.ReplaceAll(target.Cards);
            WorkspaceName = target.Name; _settings.WorkspaceName = WorkspaceName;
            ShelfWidth = target.ShelfWidth; ShelfAutoHide = target.ShelfAutoHide;
            NormalizeWorkspacePanes(target); RestoreLiveLayout(target);
            if (IsSplit && assignPane) AssignToPane(FocusedPane, card.Model);
            _sessions.Activate(card.Model);
        }
        finally { _switchingWorkspace = false; }
        SyncActive(); RefreshCounts(); RefreshProjectTools();
        NotifyWorkbench(TerminalHub.Extensibility.WorkbenchEventKind.WorkspaceChanged);
        RecordLayout(source == target ? "终端排序" : "跨工作区移动", before);
        SaveSettingsInternal();
    }
}
