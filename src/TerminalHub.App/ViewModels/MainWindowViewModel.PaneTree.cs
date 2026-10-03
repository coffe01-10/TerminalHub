using TerminalHub.Core.Sessions;
using TerminalHub.Core.Settings;
using CommunityToolkit.Mvvm.Input;

namespace TerminalHub.App.ViewModels;

public partial class MainWindowViewModel
{
    public PaneNode? PaneTree { get; private set; }
    public int PaneRevision { get; private set; }
    private void NotifyPaneTree()
    {
        var leaves = PaneTree?.Leaves.ToArray() ?? [];
        if (leaves.Length < 2) { PaneTree = null; leaves = []; }
        LeftPane = leaves.ElementAtOrDefault(0)?.Session; RightPane = leaves.ElementAtOrDefault(1)?.Session;
        BottomLeftPane = leaves.ElementAtOrDefault(2)?.Session; BottomRightPane = leaves.ElementAtOrDefault(3)?.Session;
        FocusedPane = Math.Clamp(FocusedPane, 0, Math.Max(0, leaves.Length - 1));
        IsSplit = leaves.Length > 1;
        // A pruned tree (panes closed out of a Quad) no longer matches the
        // recorded preset. Degrade it the same way the legacy pane path does
        // (NormalizeWorkspacePanes) so snapshots and layout undo replay the
        // right pane count instead of spawning four panes from a two-leaf tree.
        if (PaneTree is not null && leaves.Length < 4 && SplitLayout == SplitLayout.Quad)
            SplitLayout = SplitLayout.Horizontal;
        if (!IsSplit) PaneMaximized = false;
        PaneRevision++; OnPropertyChanged(nameof(PaneTree)); OnPropertyChanged(nameof(PaneRevision));
        OnPropertyChanged(nameof(PaneCount)); UpdateDisplayedCards();
    }

    private PaneNode PresetTree(SplitLayout kind, IReadOnlyList<TerminalSessionModel> panes)
    {
        PaneNode Leaf(int i) => new() { Session = panes[i] };
        if (kind != SplitLayout.Quad) return new() { Vertical = kind == SplitLayout.Vertical,
            Ratio = kind == SplitLayout.Vertical ? RowRatio : ColumnRatio, First = Leaf(0), Second = Leaf(1) };
        return new() { Vertical = true, Ratio = RowRatio,
            First = new() { Ratio = ColumnRatio, First = Leaf(0), Second = Leaf(1) },
            Second = new() { Ratio = ColumnRatio, First = Leaf(2), Second = Leaf(3) } };
    }

    private void LoadPaneLayout(WorkspaceState layout, Func<int, TerminalSessionModel?> session, PaneNode? live = null)
    {
        PaneTree = live?.Clone() ?? PaneNode.Load(layout.PaneTree, session);
        if (PaneTree is null && layout.IsSplit)
        {
            var panes = new[] { layout.LeftIndex, layout.RightIndex, layout.BottomLeftIndex, layout.BottomRightIndex }
                .Select(session).OfType<TerminalSessionModel>().Distinct().ToArray();
            if (panes.Length >= 2) PaneTree = PresetTree(layout.SplitLayout == SplitLayout.Quad && panes.Length < 4
                ? SplitLayout.Horizontal : layout.SplitLayout, panes);
        }
        FocusedPane = layout.FocusedPane; NotifyPaneTree();
    }

    [RelayCommand]
    public async Task SplitFocusedPaneAsync(string direction) => await SplitPaneAsync(FocusedPane, direction == "Vertical");

    public async Task SplitPaneAsync(int pane, bool vertical, TerminalSessionModel? session = null, bool before = false)
    {
        if (_changingLayout) return;
        var snapshot = CaptureLayout(); var workspace = ActiveWorkspace;
        var target = IsSplit ? PaneTree?.Leaves.ElementAtOrDefault(pane) : new PaneNode { Session = _sessions.Active };
        if (target?.Session is null) return;
        _changingLayout = true;
        try
        {
            var existing = PaneTree?.Leaves.Select(n => n.Session).ToHashSet() ?? [];
            session ??= workspace.Cards.Select(c => c.Model).FirstOrDefault(s => s != target.Session && !existing.Contains(s));
            if (session is null)
            {
                // The target may be an ssh session — its cwd is remote, spawn locally instead.
                session = await CreateSessionAsync(null, SessionTag.Dev, LocalSpawnCwd(target.Session));
            // SessionAdded posts the card. Its observable metadata must exist before binding the new view.
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
            }
            if (session is null || ActiveWorkspace != workspace || session == target.Session) return;
            // Moving a visible leaf into a new split removes its old view, never its PTY.
            if (!IsSplit) PaneTree = target;
            PaneTree = RemoveNode(PaneTree, session);
            if (PaneTree is null) PaneTree = target;
            var old = new PaneNode { Session = target.Session };
            var added = new PaneNode { Session = session };
            target.Session = null; target.Vertical = vertical; target.Ratio = .5;
            target.First = before ? added : old; target.Second = before ? old : added;
            PaneMaximized = false; NotifyPaneTree();
            FocusedPane = PaneTree.Leaves.ToList().FindIndex(n => n.Session == session);
            _sessions.Activate(session); RecordLayout("拆分窗格", snapshot);
        }
        finally { _changingLayout = false; }
    }

    private static PaneNode? RemoveNode(PaneNode? node, TerminalSessionModel session)
    {
        if (node is null) return null;
        if (node.IsLeaf) return node.Session == session ? null : node;
        node.First = RemoveNode(node.First, session); node.Second = RemoveNode(node.Second, session);
        return node.First is null ? node.Second : node.Second is null ? node.First : node;
    }

    [RelayCommand]
    public void RemoveFocusedPane()
    {
        if (!IsSplit || GetPane(FocusedPane) is not { } session) return;
        var before = CaptureLayout(); PaneTree = RemoveNode(PaneTree, session);
        var remaining = PaneTree?.Leaves.FirstOrDefault()?.Session;
        NotifyPaneTree();
        _sessions.Activate(remaining); RecordLayout("移除窗格", before);
    }

    public void SetPaneRatio(PaneNode node, double ratio)
    {
        node.Ratio = Math.Clamp(ratio, .1, .9);
        SaveSettingsInternal();
    }

    private static PaneNode? FillOrPrunePanes(PaneNode? tree, IEnumerable<TerminalSessionModel> sessions)
    {
        if (tree is null) return null;
        var available = sessions.ToHashSet();
        var used = tree.Leaves.Select(n => n.Session).Where(s => s is not null && available.Contains(s)).ToHashSet();
        var spare = new Queue<TerminalSessionModel>(available.Where(s => !used.Contains(s)));
        // Closing or transferring a terminal keeps the existing layout if an unused session can fill it.
        foreach (var leaf in tree.Leaves)
            if (leaf.Session is null || !available.Contains(leaf.Session))
                leaf.Session = spare.Count > 0 ? spare.Dequeue() : null;
        return PaneNode.Prune(tree, available);
    }

    partial void OnColumnRatioChanged(double value)
    {
        if (PaneTree is null) return;
        if (!PaneTree.Vertical) PaneTree.Ratio = value;
        else if (SplitLayout == SplitLayout.Quad && PaneTree.First is { IsLeaf: false, Vertical: false } first
            && PaneTree.Second is { IsLeaf: false, Vertical: false } second)
        { first.Ratio = value; second.Ratio = value; }
    }
    partial void OnRowRatioChanged(double value)
    { if (PaneTree is { Vertical: true }) PaneTree.Ratio = value; }

    public async Task DropSessionOnPaneAsync(SessionCardViewModel card, int pane, string edge)
    {
        var target = GetPane(pane) ?? _sessions.Active;
        BeginLayoutGesture("移动终端窗格");
        if (_sessionWorkspaces.GetValueOrDefault(card.Model.Id) != ActiveWorkspace)
            MoveSessionToWorkspace(card, ActiveWorkspace, assignPane: false);
        var index = PaneTree?.Leaves.ToList().FindIndex(n => n.Session == target) ?? 0;
        if (edge == "Center")
        { if (IsSplit) AssignToPane(index, card.Model); _sessions.Activate(card.Model); }
        else await SplitPaneAsync(index, edge is "Top" or "Bottom", card.Model, edge is "Top" or "Left");
        CompleteLayoutGesture();
    }
}
