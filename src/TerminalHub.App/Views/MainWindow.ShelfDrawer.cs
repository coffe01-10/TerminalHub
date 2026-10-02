using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TerminalHub.App.Controls;
using TerminalHub.App.ViewModels;

namespace TerminalHub.App.Views;

public partial class MainWindow
{
    private readonly DispatcherTimer _shelfHideTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly DispatcherTimer _shelfCenterTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private ScrollViewer? _shelfFadeScroll;
    private ScrollViewer? _shelfCenterScroll;
    private double _shelfCenterFrom, _shelfCenterTarget;
    private long _shelfCenterStarted;
    private bool _shelfRevealed;

    private void InitializeShelfDrawer()
    {
        AddHandler(InputElement.PointerMovedEvent, OnShelfEdgePointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        PointerExited += (_, _) => ScheduleShelfHide();
        SessionShelf.Loaded += (_, _) =>
        {
            if (_shelfFadeScroll is not null) _shelfFadeScroll.ScrollChanged -= OnShelfScrolled;
            _shelfFadeScroll = SessionShelf.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (_shelfFadeScroll is not null) _shelfFadeScroll.ScrollChanged += OnShelfScrolled;
        };
        SessionShelf.AddHandler(InputElement.PointerWheelChangedEvent, OnShelfWheel,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        _shelfCenterTimer.Tick += (_, _) =>
        {
            if (_shelfCenterScroll is not { } scroll) return;
            var progress = Math.Clamp((Environment.TickCount64 - _shelfCenterStarted) / 220.0, 0, 1);
            var eased = 1 - Math.Pow(1 - progress, 3);
            scroll.Offset = new Vector(scroll.Offset.X, _shelfCenterFrom + (_shelfCenterTarget - _shelfCenterFrom) * eased);
            SessionShelf.UpdateLayout();
            UpdateShelfFade();
            if (progress >= 1) _shelfCenterTimer.Stop();
        };
        _shelfHideTimer.Tick += (_, _) =>
        {
            // A popup lives outside the window; crossing into it must not collapse its owner.
            if (ShelfHost.IsPointerOver || _dragCard is not null || ShelfMenuOpen()) return;
            _shelfHideTimer.Stop();
            SetShelfRevealed(false);
        };
        UpdateShelfMode(immediate: true);
    }

    private void UpdateShelfMode(bool immediate = false)
    {
        _shelfHideTimer.Stop();
        _shelfCenterTimer.Stop();
        SessionShelf.AutoScrollToSelectedItem = !Vm.ShelfAutoHide;
        ScrollViewer.SetVerticalScrollBarVisibility(SessionShelf, Vm.ShelfAutoHide ? Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden
            : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        ShelfContent.RowDefinitions[0].Height = new GridLength(Vm.ShelfAutoHide ? 34 : 30);
        SetShelfRevealed(!Vm.ShelfAutoHide, immediate);
        UpdateStageLayout();
    }

    private void OnShelfEdgePointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_stageReady || !Vm.ShelfAutoHide) return;
        var point = e.GetPosition(StageLayout);
        var nearEdge = point.X >= -StageLayout.Margin.Left && point.X <= 0
            && point.Y >= 0 && point.Y <= StageLayout.Bounds.Height;
        // Include the gap between window edge and drawer so entry remains continuous during the slide.
        var insideDrawer = _shelfRevealed && point.X >= -StageLayout.Margin.Left
            && point.X <= ShelfHost.Bounds.Width && point.Y >= 0 && point.Y <= StageLayout.Bounds.Height;
        if (nearEdge || insideDrawer)
        {
            _shelfHideTimer.Stop();
            if (!_shelfRevealed) SetShelfRevealed(true);
        }
        else ScheduleShelfHide();
    }

    private void ScheduleShelfHide()
    {
        if (Vm.ShelfAutoHide && _shelfRevealed && !_shelfHideTimer.IsEnabled) _shelfHideTimer.Start();
    }

    private bool ShelfMenuOpen() => SessionShelf.GetVisualDescendants().OfType<Button>()
        .Any(button => button.Flyout?.IsOpen == true || button.ContextFlyout?.IsOpen == true);

    private void SetShelfRevealed(bool revealed, bool immediate = false)
    {
        _shelfRevealed = revealed;
        ShelfHost.SetRevealed(revealed, immediate);
        if (revealed)
            Dispatcher.UIThread.Post(() =>
            {
                SessionShelf.UpdateLayout();
                if (_shelfRevealed && Vm.ActiveCard is { } card && Vm.ShelfItems.Contains(card)) RevealShelfCard(card);
            }, DispatcherPriority.Loaded);
    }

    private void CenterShelf(ScrollViewer scroll, double target, bool animate)
    {
        _shelfCenterTimer.Stop();
        if (!animate || Math.Abs(scroll.Offset.Y - target) < .5)
        {
            scroll.Offset = new Vector(scroll.Offset.X, target);
            return;
        }
        // Starting from the currently painted offset makes rapid wheel reversals continuous.
        _shelfCenterScroll = scroll;
        _shelfCenterFrom = scroll.Offset.Y;
        _shelfCenterTarget = target;
        _shelfCenterStarted = Environment.TickCount64;
        _shelfCenterTimer.Start();
    }

    private void OnShelfScrolled(object? sender, ScrollChangedEventArgs e) => UpdateShelfFade();

    private void UpdateShelfFade()
    {
        if (!Vm.ShelfAutoHide) return;
        foreach (var card in SessionShelf.GetVisualDescendants().OfType<StageCard>())
            if (card.TranslatePoint(new Point(0, card.Bounds.Height / 2), SessionShelf) is { } center)
                card.CenterDistance = (center.Y - SessionShelf.Bounds.Height / 2)
                    / Math.Max(1, ThumbnailHeight + SessionCardViewModel.ShelfSpacing - SessionCardViewModel.ShelfOverlap);
    }

    private void OnShelfWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!Vm.ShelfAutoHide || !_shelfRevealed || e.Delta.Y == 0 || _dragCard is not null || ShelfMenuOpen()) return;
        var cards = Vm.ShelfItems.OfType<SessionCardViewModel>().ToList();
        var index = Vm.ActiveCard is { } active ? cards.IndexOf(active) : -1;
        if (index >= 0)
        {
            var next = Math.Clamp(index + (e.Delta.Y < 0 ? 1 : -1), 0, cards.Count - 1);
            Vm.ActivateCard(cards[next]);
        }
        else if (cards.Count > 0)
        {
            // Collapsing the active session's group removes its card from the wheel.
            // Continue with the nearest visible session in the requested direction.
            var order = Vm.ActiveCard is { } hidden ? Vm.SessionCards.IndexOf(hidden) : -1;
            var next = e.Delta.Y < 0
                ? cards.FindIndex(card => Vm.SessionCards.IndexOf(card) > order)
                : cards.FindLastIndex(card => Vm.SessionCards.IndexOf(card) < order);
            if (next < 0) next = e.Delta.Y < 0 ? cards.Count - 1 : 0;
            Vm.ActivateCard(cards[next]);
        }
        e.Handled = true;
    }
}
