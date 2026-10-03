using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TerminalHub.App.Controls;
using TerminalHub.App.Localization;
using TerminalHub.App.ViewModels;
using TerminalHub.Core.Sessions;

namespace TerminalHub.App.Views;

public partial class MainWindow
{
    private readonly Dictionary<TerminalSessionModel, TerminalView> _treeTerminals = [];
    private readonly List<(PaneNode Node, Border Box)> _treeBoxes = [];
    private Border? _paneDropPreview;
    private int _dropPane;
    private string _dropEdge = "Center";

    private void RenderPaneTree()
    {
        ClearPaneDrop();
        foreach (var terminal in _treeTerminals.Values)
            if (terminal.Parent is Panel parent) parent.Children.Remove(terminal);
        SplitHost.Content = null; _treeBoxes.Clear();
        foreach (var dead in _treeTerminals.Keys.Where(s => !Vm.AllSessionCards.Any(c => c.Model == s)).ToArray())
            _treeTerminals.Remove(dead);
        SplitHost.IsVisible = Vm.IsSplit;
        if (!Vm.IsSplit || Vm.PaneTree is null) return;
        SplitHost.Content = Vm.PaneMaximized && Vm.PaneTree.Leaves.ElementAtOrDefault(Vm.FocusedPane) is { } leaf
            ? CreatePane(leaf) : CreateBranch(Vm.PaneTree);
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (IsActive && !Vm.SettingsOpen && !Vm.RecentSwitcherOpen && !PalettePanel.IsVisible && _dragCard is null)
                ActiveTerminal()?.Focus();
        });
    }

    private Control CreateBranch(PaneNode node)
    {
        if (node.IsLeaf) return CreatePane(node);
        var grid = new Grid(); var vertical = node.Vertical;
        if (vertical) grid.RowDefinitions = new($"{node.Ratio}*,5,{1 - node.Ratio}*");
        else grid.ColumnDefinitions = new($"{node.Ratio}*,5,{1 - node.Ratio}*");
        var first = CreateBranch(node.First!); var second = CreateBranch(node.Second!);
        var divider = new GridSplitter { ResizeDirection = vertical ? GridResizeDirection.Rows : GridResizeDirection.Columns,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext, HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch, Focusable = true };
        divider.Bind(TemplatedControl.BackgroundProperty, new DynamicResourceExtension("UiBorder"));
        if (vertical) { Grid.SetRow(divider, 1); Grid.SetRow(second, 2); }
        else { Grid.SetColumn(divider, 1); Grid.SetColumn(second, 2); }
        grid.Children.Add(first); grid.Children.Add(divider); grid.Children.Add(second);
        void SaveRatio()
        {
            grid.UpdateLayout();
            var a = vertical ? grid.RowDefinitions[0].ActualHeight : grid.ColumnDefinitions[0].ActualWidth;
            var b = vertical ? grid.RowDefinitions[2].ActualHeight : grid.ColumnDefinitions[2].ActualWidth;
            if (a + b > 0) Vm.SetPaneRatio(node, a / (a + b));
            Vm.CompleteLayoutGesture();
        }
        divider.AddHandler(PointerPressedEvent, (_, _) => Vm.BeginLayoutGesture("调整分屏比例"), RoutingStrategies.Tunnel, true);
        divider.AddHandler(PointerReleasedEvent, (_, _) => SaveRatio(), RoutingStrategies.Bubble, true);
        divider.PointerCaptureLost += (_, _) => SaveRatio();
        divider.AddHandler(KeyDownEvent, (_, e) => { if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down) Vm.BeginLayoutGesture("调整分屏比例"); }, RoutingStrategies.Tunnel, true);
        divider.AddHandler(KeyUpEvent, (_, e) => { if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down) SaveRatio(); }, RoutingStrategies.Bubble, true);
        return grid;
    }

    private Border CreatePane(PaneNode leaf)
    {
        var session = leaf.Session!;
        var card = Vm.AllSessionCards.FirstOrDefault(c => c.Model == session);
        if (!_treeTerminals.TryGetValue(session, out var terminal))
        {
            terminal = new TerminalView { Emulator = session.Emulator, InputSender = Vm.SendTerminalInput,
                WorkingDirectory = session.WorkingDirectory, ShellCommand = session.Shell, IsRemote = session.IsRemote };
            terminal.Bind(TerminalView.TerminalFontSizeProperty, new Binding(nameof(Vm.FontSize)) { Source = Vm });
            terminal.Bind(TerminalView.TerminalFontFamilyProperty, new Binding(nameof(Vm.TerminalFont)) { Source = Vm });
            terminal.Bind(TerminalView.SearchQueryProperty, new Binding("Dashboard.SearchQuery") { Source = Vm });
            terminal.GotFocus += (_, _) => FocusTreePane(session);
            _treeTerminals.Add(session, terminal);
        }
        terminal.WorkingDirectory = session.WorkingDirectory;
        if (card is not null) terminal.Bind(TerminalView.WorkingDirectoryProperty,
            new Binding(nameof(card.WorkingDirectory)) { Source = card });
        var box = new Border { BorderThickness = new(1.5), CornerRadius = new(6), Margin = new(2), ClipToBounds = true };
        box.Bind(Border.BorderBrushProperty, new DynamicResourceExtension(Vm.GetPane(Vm.FocusedPane) == session ? "UiAccent" : "UiBorder"));
        box.AddHandler(PointerPressedEvent, (_, _) => FocusTreePane(session), RoutingStrategies.Bubble, true);
        var content = new Grid { RowDefinitions = new("30,*") };
        var title = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        title.Bind(TextBlock.TextProperty, new Binding(nameof(session.Name)) { Source = (object?)card ?? session });
        title.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("UiInk"));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        Button Action(string glyph, string hint, Action callback)
        {
            var button = new Button { Content = glyph, Padding = new(6,2), FontSize = 11 };
            button.Bind(ToolTip.TipProperty, UiText.Binding(hint));
            button.Click += (_, _) => { FocusTreePane(session); callback(); }; actions.Children.Add(button); return button;
        }
        Action("↔", "左右拆分窗格", () => _ = Vm.SplitFocusedPaneAsync("Horizontal"));
        Action("↕", "上下拆分窗格", () => _ = Vm.SplitFocusedPaneAsync("Vertical"));
        Action("□", "最大化／恢复窗格", () => Vm.TogglePaneMaximizedCommand.Execute(null));
        Action("−", "移除窗格（保留会话）", Vm.RemoveFocusedPane);
        Action("×", "关闭会话", () => Vm.CloseSessionCommand.Execute(Vm.SessionCards.FirstOrDefault(c => c.Model == session)));
        var header = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new(8,0,4,0) };
        header.Children.Add(title); Grid.SetColumn(actions,1); header.Children.Add(actions); content.Children.Add(header);
        var viewport = new Panel { Children = { terminal } }; Grid.SetRow(viewport,1); content.Children.Add(viewport);
        var bottom = new Button { Content = "↓", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new(4), Padding = new(6,2) };
        bottom.Bind(IsVisibleProperty, new Binding(nameof(TerminalView.IsScrolledUp)) { Source = terminal });
        bottom.Click += (_, _) => { terminal.ScrollToBottom(); terminal.Focus(); }; viewport.Children.Add(bottom);
        var scrollbar = new ScrollBar { Orientation = Orientation.Vertical, Width = 8, HorizontalAlignment = HorizontalAlignment.Right, Opacity = .5 };
        scrollbar.Bind(RangeBase.MaximumProperty, new Binding(nameof(TerminalView.ScrollbackSize)) { Source = terminal });
        scrollbar.Bind(ScrollBar.ViewportSizeProperty, new Binding(nameof(TerminalView.ViewportRows)) { Source = terminal });
        scrollbar.Bind(RangeBase.ValueProperty, new Binding(nameof(TerminalView.ScrollPosition)) { Source = terminal, Mode = BindingMode.TwoWay });
        scrollbar.Bind(ScrollBar.VisibilityProperty, new Binding(nameof(TerminalView.OverlayScrollBarVisibility)) { Source = terminal }); viewport.Children.Add(scrollbar);
        box.Child = content; _treeBoxes.Add((leaf, box)); return box;
    }

    private void FocusTreePane(TerminalSessionModel session)
    {
        var index = Vm.PaneTree?.Leaves.ToList().FindIndex(n => n.Session == session) ?? -1;
        if (index < 0) return;
        Vm.FocusPane(index);
        foreach (var pair in _treeBoxes) pair.Box.Bind(Border.BorderBrushProperty,
            new DynamicResourceExtension(pair.Node.Session == session ? "UiAccent" : "UiBorder"));
    }

    private bool PreviewPaneDrop(Point point)
    {
        ClearPaneDrop();
        var boxes = Vm.IsSplit ? _treeBoxes.Select(p => (p.Box, Vm.PaneTree!.Leaves.ToList().IndexOf(p.Node)))
            : new[] { (TerminalViewport, 0) };
        foreach (var (box, index) in boxes)
        {
            if (box.TranslatePoint(default, this) is not { } origin || !new Rect(origin, box.Bounds.Size).Contains(point)) continue;
            var x = (point.X - origin.X) / box.Bounds.Width; var y = (point.Y - origin.Y) / box.Bounds.Height;
            _dropEdge = x < .23 ? "Left" : x > .77 ? "Right" : y < .23 ? "Top" : y > .77 ? "Bottom" : "Center";
            _dropPane = index;
            _paneDropPreview = new Border { IsHitTestVisible = false, Opacity = .4, CornerRadius = new(6) };
            _paneDropPreview.Bind(Border.BackgroundProperty, new DynamicResourceExtension("UiAccent"));
            var overlay = (Panel)TerminalViewport.Child!;
            _paneDropPreview.HorizontalAlignment = HorizontalAlignment.Left; _paneDropPreview.VerticalAlignment = VerticalAlignment.Top;
            var local = this.TranslatePoint(origin, overlay) ?? default;
            var w = box.Bounds.Width; var h = box.Bounds.Height;
            if (_dropEdge is "Left" or "Right") w /= 2;
            if (_dropEdge is "Top" or "Bottom") h /= 2;
            if (_dropEdge == "Right") local += new Vector(w, 0);
            if (_dropEdge == "Bottom") local += new Vector(0, h);
            _paneDropPreview.Width = w; _paneDropPreview.Height = h; _paneDropPreview.Margin = new(local.X,local.Y,0,0);
            overlay.Children.Add(_paneDropPreview); return true;
        }
        return false;
    }
    private void ClearPaneDrop()
    { if (_paneDropPreview?.Parent is Panel panel) panel.Children.Remove(_paneDropPreview); _paneDropPreview = null; }
}
