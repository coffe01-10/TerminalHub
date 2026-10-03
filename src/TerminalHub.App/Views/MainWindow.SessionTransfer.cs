using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using TerminalHub.App.Controls;
using TerminalHub.App.ViewModels;

namespace TerminalHub.App.Views;

public partial class MainWindow
{
    private LiveWorkspace? _sessionDragOrigin, _sessionHoverWorkspace;
    private SessionCardViewModel? _sessionDragActive;
    private IPointer? _sessionDragPointer;
    private Point _sessionDragPosition;
    private long _sessionHoverStarted;
    private Border? _sessionDropTab;
    private StageCard? _sessionDropCard;
    private string _sessionDropHint = "";

    private void InitializeSessionTransfer()
    {
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (_dragCard is null || e.Key != Key.Escape) return;
            EndShelfDrag(false); e.Handled = true;
        }, RoutingStrategies.Tunnel);
        Deactivated += (_, _) => EndShelfDrag(false);
    }

    private bool UpdateSessionTransfer()
    {
        var tab = WorkspaceTabs.GetVisualDescendants().OfType<Border>().FirstOrDefault(b =>
            b.Classes.Contains("workspace-tab") && b.TranslatePoint(default, this) is { } origin
            && new Rect(origin, b.Bounds.Size).Contains(_sessionDragPosition)
            && WorkspaceTabScroll.TranslatePoint(default, this) is { } scrollOrigin
            && new Rect(scrollOrigin, WorkspaceTabScroll.Bounds.Size).Contains(_sessionDragPosition));
        if (_sessionDropTab != tab)
        {
            _sessionDropTab?.Classes.Remove("session-drop");
            _sessionDropTab = tab; tab?.Classes.Add("session-drop");
        }
        var hovered = tab?.DataContext as LiveWorkspace;
        var destination = hovered ?? (Vm.ActiveWorkspace != _sessionDragOrigin ? Vm.ActiveWorkspace : null);
        var hint = destination is not null && _dragCard is not null ? $"将“{_dragCard.Name}”移动到“{destination.Name}”" : "";
        if (hint != _sessionDropHint)
        {
            _sessionDropHint = hint;
            TerminalHub.App.Localization.UiText.Set(SessionTransferText, TextBlock.TextProperty, hint);
            SessionTransferHint.IsVisible = hint.Length > 0;
        }
        if (_sessionHoverWorkspace != hovered)
        { _sessionHoverWorkspace = hovered; _sessionHoverStarted = Environment.TickCount64; }
        if (hovered is not null && hovered != Vm.ActiveWorkspace && Environment.TickCount64 - _sessionHoverStarted >= 500)
        {
            foreach (var slot in _dragSlots) { slot.Card.SetSlotOffset(0, immediate: true); slot.Card.SetDragging(false); }
            Vm.SwitchProjectWorkspace(hovered);
            if (Vm.ShelfAutoHide) SetShelfRevealed(true);
            SessionShelf.UpdateLayout();
        }
        var scrollPoint = this.TranslatePoint(_sessionDragPosition, WorkspaceTabScroll) ?? default;
        if (tab is not null)
        {
            var speed = scrollPoint.X < 24 ? -10 : scrollPoint.X > WorkspaceTabScroll.Bounds.Width - 24 ? 10 : 0;
            WorkspaceTabScroll.Offset = new Vector(Math.Clamp(WorkspaceTabScroll.Offset.X + speed, 0,
                Math.Max(0, WorkspaceTabScroll.Extent.Width - WorkspaceTabScroll.Viewport.Width)), 0);
        }
        _sessionDropCard?.Classes.Remove("session-drop"); _sessionDropCard = null;
        if (Vm.ActiveWorkspace != _sessionDragOrigin)
        {
            _sessionDropCard = SessionShelf.GetVisualDescendants().OfType<StageCard>().FirstOrDefault(c =>
                c.TranslatePoint(default, this) is { } origin && new Rect(origin, c.Bounds.Size).Contains(_sessionDragPosition));
            _sessionDropCard?.Classes.Add("session-drop");
        }
        // The auto-hide drawer overlays the terminal. A card drop belongs to the drawer while it is open.
        var overShelf = _shelfRevealed && ShelfHost.TranslatePoint(default, this) is { } shelfOrigin
            && new Rect(shelfOrigin, ShelfHost.Bounds.Size).Contains(_sessionDragPosition);
        if (tab is not null || overShelf) ClearPaneDrop();
        var paneDrop = tab is null && !overShelf && PreviewPaneDrop(_sessionDragPosition);
        // Original reorder slots only exist in the source workspace.
        return paneDrop || tab is not null || Vm.ActiveWorkspace != _sessionDragOrigin;
    }

    private bool TryDropSessionTransfer(Point point)
    {
        if (!_cardDragging || _dragCard is null) return false;
        if (_paneDropPreview is not null)
        {
            var moving = _dragCard; var pane = _dropPane; var edge = _dropEdge;
            EndShelfDrag(false, restoreWorkspace: false);
            _ = DropOnPaneAsync(moving, pane, edge); return true;
        }
        LiveWorkspace? target = _sessionDropTab?.DataContext as LiveWorkspace;
        var index = -1;
        if (target is null && Vm.ActiveWorkspace != _sessionDragOrigin &&
            ShelfHost.TranslatePoint(default, this) is { } origin && new Rect(origin, ShelfHost.Bounds.Size).Contains(point))
        {
            target = Vm.ActiveWorkspace;
            if (_sessionDropCard?.DataContext is SessionCardViewModel before) index = target.Cards.IndexOf(before);
        }
        if (target is null || target == _sessionDragOrigin) return false;
        var card = _dragCard;
        // Clear pointer state before updating the shelf (layout changes can release capture).
        EndShelfDrag(false, restoreWorkspace: false);
        Vm.MoveSessionToWorkspace(card, target, index);
        Vm.CompleteLayoutGesture();
        ActiveTerminal()?.Focus();
        return true;
    }

    private async Task DropOnPaneAsync(SessionCardViewModel card, int pane, string edge)
    { await Vm.DropSessionOnPaneAsync(card, pane, edge); ActiveTerminal()?.Focus(); }

    private void ClearSessionTransfer(bool restoreWorkspace)
    {
        ClearPaneDrop();
        SessionTransferHint.IsVisible = false; _sessionDropHint = "";
        _sessionDropTab?.Classes.Remove("session-drop"); _sessionDropTab = null;
        _sessionDropCard?.Classes.Remove("session-drop"); _sessionDropCard = null;
        var source = _sessionDragOrigin; var active = _sessionDragActive;
        _sessionDragOrigin = _sessionHoverWorkspace = null; _sessionDragActive = null;
        var pointer = _sessionDragPointer; _sessionDragPointer = null; pointer?.Capture(null);
        if (restoreWorkspace && source is not null && Vm.ProjectWorkspaces.Contains(source))
        {
            Vm.SwitchProjectWorkspace(source);
            if (active is not null && source.Cards.Contains(active)) Vm.ActivateCard(active);
        }
    }
}
