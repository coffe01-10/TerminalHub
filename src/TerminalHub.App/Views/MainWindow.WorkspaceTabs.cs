using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TerminalHub.App.ViewModels;

namespace TerminalHub.App.Views;

public partial class MainWindow
{
    private readonly DispatcherTimer _workspaceDragTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private LiveWorkspace? _dragWorkspace;
    private IPointer? _workspaceDragPointer;
    private bool _workspaceDragging;
    private Point _workspaceDragOrigin, _workspaceDragPosition;
    private double _workspaceScrollStart;
    private int _workspaceDragFrom, _workspaceDragTo;
    private (LiveWorkspace Workspace, Border Tab, double Left, double Width)[] _workspaceDragSlots = [];

    private void InitializeWorkspaceTabs()
    {
        WorkspaceTabs.AddHandler(PointerPressedEvent, OnWorkspaceTabPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        WorkspaceTabs.AddHandler(PointerMovedEvent, OnWorkspaceTabMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        WorkspaceTabs.AddHandler(PointerReleasedEvent, OnWorkspaceTabReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        WorkspaceTabs.PointerCaptureLost += (_, _) => EndWorkspaceTabDrag(commit: false);
        AddHandler(KeyDownEvent, OnWorkspaceDragKeyDown, RoutingStrategies.Tunnel);
        Deactivated += (_, _) => EndWorkspaceTabDrag(commit: false);
        Vm.ProjectWorkspaces.CollectionChanged += (_, _) => EndWorkspaceTabDrag(commit: false);
        _workspaceDragTimer.Tick += (_, _) => UpdateWorkspaceTabDrag(autoScroll: true);
    }

    private void OnWorkspaceTabPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(WorkspaceTabs).Properties.IsLeftButtonPressed) return;
        var button = (e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true);
        if (button is null || !button.Classes.Contains("workspace-name") || button.Tag is not LiveWorkspace workspace) return;
        _dragWorkspace = workspace;
        _workspaceDragPointer = e.Pointer;
        _workspaceDragOrigin = _workspaceDragPosition = e.GetPosition(WorkspaceTabScroll);
        _workspaceScrollStart = WorkspaceTabScroll.Offset.X;
        _workspaceDragSlots = WorkspaceTabs.GetVisualDescendants().OfType<Border>()
            .Where(tab => tab.Classes.Contains("workspace-tab"))
            .Select(tab => ((LiveWorkspace)tab.DataContext!, tab,
                tab.TranslatePoint(default, WorkspaceTabs)!.Value.X, tab.Bounds.Width)).OrderBy(slot => slot.Item3).ToArray();
        _workspaceDragFrom = _workspaceDragTo = Array.FindIndex(_workspaceDragSlots, slot => slot.Workspace == workspace);
        button.Focus();
        e.Pointer.Capture(WorkspaceTabs);
        // Own the gesture so Button's release click cannot switch after a drag.
        // Keyboard/automation activation continues through the normal Click handler.
        e.Handled = true;
    }

    private void OnWorkspaceTabMoved(object? sender, PointerEventArgs e)
    {
        if (_dragWorkspace is null) return;
        _workspaceDragPosition = e.GetPosition(WorkspaceTabScroll);
        if (!_workspaceDragging && Math.Abs(_workspaceDragPosition.X - _workspaceDragOrigin.X)
            + Math.Abs(_workspaceDragPosition.Y - _workspaceDragOrigin.Y) < 8) return;
        if (!_workspaceDragging)
        {
            _workspaceDragging = true;
            var tab = _workspaceDragSlots[_workspaceDragFrom].Tab;
            tab.Classes.Add("dragging"); tab.ZIndex = 10;
            _workspaceDragTimer.Start();
        }
        UpdateWorkspaceTabDrag(autoScroll: false);
        e.Handled = true;
    }

    private void UpdateWorkspaceTabDrag(bool autoScroll)
    {
        if (!_workspaceDragging) return;
        if (autoScroll)
        {
            var speed = _workspaceDragPosition.X < 24 ? -12 : _workspaceDragPosition.X > WorkspaceTabScroll.Bounds.Width - 24 ? 12 : 0;
            WorkspaceTabScroll.Offset = new Vector(Math.Clamp(WorkspaceTabScroll.Offset.X + speed, 0,
                Math.Max(0, WorkspaceTabScroll.Extent.Width - WorkspaceTabScroll.Viewport.Width)), 0);
        }
        var delta = _workspaceDragPosition.X - _workspaceDragOrigin.X + WorkspaceTabScroll.Offset.X - _workspaceScrollStart;
        var dragged = _workspaceDragSlots[_workspaceDragFrom];
        var center = dragged.Left + dragged.Width / 2 + delta;
        _workspaceDragTo = _workspaceDragSlots.Count(slot => slot.Workspace != _dragWorkspace && slot.Left + slot.Width / 2 < center);
        // Preview the final order using actual tab widths; names may differ in length.
        var order = _workspaceDragSlots.ToList();
        order.RemoveAt(_workspaceDragFrom); order.Insert(_workspaceDragTo, dragged);
        var left = _workspaceDragSlots[0].Left;
        foreach (var slot in order)
        {
            slot.Tab.RenderTransform = new TranslateTransform(slot.Workspace == _dragWorkspace ? delta : left - slot.Left, 0);
            left += slot.Width + 6;
        }
    }

    private void OnWorkspaceTabReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragWorkspace is null || e.InitialPressMouseButton != MouseButton.Left) return;
        var workspace = _dragWorkspace;
        var dragged = _workspaceDragging;
        var point = e.GetPosition(WorkspaceTabScroll);
        var inside = new Rect(WorkspaceTabScroll.Bounds.Size).Contains(point);
        EndWorkspaceTabDrag(commit: dragged && inside);
        if (!dragged && inside) Vm.SwitchProjectWorkspace(workspace);
        ActiveTerminal()?.Focus();
        e.Handled = true;
    }

    private void OnWorkspaceDragKeyDown(object? sender, KeyEventArgs e)
    {
        if (_dragWorkspace is null || e.Key != Key.Escape) return;
        EndWorkspaceTabDrag(commit: false);
        ActiveTerminal()?.Focus();
        e.Handled = true;
    }

    private void EndWorkspaceTabDrag(bool commit)
    {
        if (_dragWorkspace is null) return;
        var workspace = _dragWorkspace;
        var target = _workspaceDragTo;
        var pointer = _workspaceDragPointer;
        _dragWorkspace = null; _workspaceDragPointer = null; _workspaceDragging = false;
        _workspaceDragTimer.Stop();
        foreach (var slot in _workspaceDragSlots)
        {
            slot.Tab.RenderTransform = null; slot.Tab.ZIndex = 0; slot.Tab.Classes.Remove("dragging");
        }
        _workspaceDragSlots = [];
        pointer?.Capture(null);
        if (commit) Vm.MoveProjectWorkspace(workspace, target);
    }
}
