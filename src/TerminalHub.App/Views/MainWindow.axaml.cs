using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TerminalHub.App.ViewModels;
using TerminalHub.App.Controls;
using TerminalHub.Core.Deploy;
using TerminalHub.Core.Settings;
using TerminalHub.Core.Terminal;

namespace TerminalHub.App.Views;

public partial class MainWindow : Window
{
    public static readonly StyledProperty<double> ThumbnailHeightProperty =
        AvaloniaProperty.Register<MainWindow, double>(nameof(ThumbnailHeight), 208);
    public double ThumbnailHeight { get => GetValue(ThumbnailHeightProperty); set => SetValue(ThumbnailHeightProperty, value); }
    private int _selectionGeneration;
    private readonly DispatcherTimer _dockHideTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private bool _stageReady;
    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;

    /// <summary>Inner ScrollViewer of the Logs list; drives the follow-tail state machine.</summary>
    private ScrollViewer? _logsScroll;

    public MainWindow() : this(null)
    {
    }

    public MainWindow(SettingsStore? settingsStore = null, Action<string>? openFolder = null,
        Func<string, bool>? shellAvailable = null)
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel(settingsStore: settingsStore, openFolder: openFolder, shellAvailable: shellAvailable);
        Vm.PaletteRequested += OpenPalette;
        Vm.RevealCommandRequested += OnRevealCommand;
        InitializeOutputTools();
        Vm.PropertyChanged += OnStageSelectionChanged;
        SessionShelf.SelectionChanged += OnShelfSelectionChanged;
        SessionShelf.SizeChanged += (_, _) => UpdateStageLayout();
        Vm.SessionCards.CollectionChanged += (_, _) => UpdateStageLayout();
        SizeChanged += (_, _) => UpdateStageLayout();
        AddHandler(InputElement.PointerMovedEvent, OnDockPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        PointerExited += (_, _) => ScheduleDockHide();
        _dockHideTimer.Tick += (_, _) =>
        {
            if (ActionDock.IsPointerOver || DeployDockButton.ContextFlyout?.IsOpen == true) return;
            _dockHideTimer.Stop();
            SetDockRevealed(false);
        };
        // Button marks pointer events handled before instance handlers, so listen with handledEventsToo.
        // Press runs before the release click, which is what DockSelectCommand executes.
        DeployDockButton.AddHandler(InputElement.PointerPressedEvent, OnDeployPointerPressed,
            RoutingStrategies.Bubble, handledEventsToo: true);
        DeployDockButton.AddHandler(InputElement.PointerReleasedEvent, OnDeployPointerReleased,
            RoutingStrategies.Bubble, handledEventsToo: true);
        // Session shortcuts must win over the focused control (a terminal would
        // otherwise eat Ctrl+W as kill-word / Ctrl+Tab as a shell byte).
        AddHandler(InputElement.KeyDownEvent, OnSessionShortcutKeyDown,
            RoutingStrategies.Tunnel);
        // TerminalView marks its PointerPressed handled (drag selection), so pane
        // focus taps need handledEventsToo to still reach these pane wrappers.
        LeftPaneBox.AddHandler(InputElement.PointerPressedEvent,
            (_, _) => Vm.FocusPane(0), RoutingStrategies.Bubble, handledEventsToo: true);
        RightPaneBox.AddHandler(InputElement.PointerPressedEvent,
            (_, _) => Vm.FocusPane(1), RoutingStrategies.Bubble, handledEventsToo: true);
        // Shelf drag reorder — threshold-gated so plain clicks still just select.
        SessionShelf.AddHandler(InputElement.PointerPressedEvent, OnShelfPointerPressed,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        SessionShelf.AddHandler(InputElement.PointerMovedEvent, OnShelfPointerMoved,
            RoutingStrategies.Bubble, handledEventsToo: true);
        SessionShelf.AddHandler(InputElement.PointerReleasedEvent, OnShelfPointerReleased,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        SessionShelf.PointerCaptureLost += (_, _) => EndShelfDrag(commit: false);
        _shelfDragTimer.Tick += (_, _) => UpdateShelfDrag(autoScroll: true);
        Opened += async (_, _) =>
        {
            try
            {
                AppWindowIcon.Refresh(this);
                FitToScreen();
                await Vm.SpawnStartupSessionsAsync();
                _stageReady = true;
                UpdateDockMode();
                var pending = Program.TakePendingLaunch();
                if (pending.Error is not null) Vm.ShowLaunchNotice(pending.Error);
                else if (pending.Directory is not null) await Vm.OpenLaunchDirectoryAsync(pending.Directory);
            }
            finally
            {
                _stageReady = true;
                UpdateDockMode();
            }
        };
        // The Logs tab may start hidden (IsVisible), so template/loaded race each other —
        // attach idempotently from whichever fires first.
        LogsList.TemplateApplied += (_, _) => AttachLogsScrollViewer();
        LogsList.Loaded += (_, _) => AttachLogsScrollViewer();
        Vm.Logs.PropertyChanged += OnLogsPropertyChanged;
        // Deep CWDs overflow the crumb bar; keep the tail (current dir) in view.
        Vm.Files.Breadcrumbs.CollectionChanged += (_, _) =>
            Dispatcher.UIThread.Post(CrumbScroll.ScrollToEnd, DispatcherPriority.Loaded);
    }

    // StageLayout grid columns: 0=shelf, 1=shelf splitter, 2=stage, 3=inspector gutter, 4=inspector.
    private const int ShelfColumn = 0, InspectorGutterColumn = 3, InspectorColumn = 4;

    private void UpdateStageLayout()
    {
        if (_dragCard is not null) return;
        // Keep enough height for the full terminal grid; overflow remains scrollable.
        var visibleCards = Math.Clamp(Vm.SessionCards.Count, 1, 5);
        ThumbnailHeight = Math.Clamp((SessionShelf.Bounds.Height - 34 - (visibleCards - 1) * 16) / visibleCards, 208, 268);
        StageLayout.ColumnDefinitions[ShelfColumn].Width = new GridLength(Bounds.Width < 1250 ? 232 : 280);
        StageLayout.ColumnDefinitions[InspectorGutterColumn].Width = new GridLength(Vm.InspectorVisible ? 12 : 0);
        StageLayout.ColumnDefinitions[InspectorColumn].Width = new GridLength(Vm.InspectorVisible ? (Bounds.Width < 1250 ? 300 : 326) : 0);
        Dispatcher.UIThread.Post(() =>
        {
            if (_stageReady && IsVisible && Vm.ActiveCard is { } active && Vm.ShelfItems.Contains(active))
                SessionShelf.ScrollIntoView(active);
        }, DispatcherPriority.Loaded);
    }

    private void OnStageSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.InspectorVisible)) UpdateStageLayout();
        if (e.PropertyName == nameof(MainWindowViewModel.DockVisibilityMode)) UpdateDockMode();
        if (e.PropertyName == nameof(MainWindowViewModel.IsSplit) && Vm.IsSplit)
        {
            ++_selectionGeneration;
            StageWindow.FinishActivation();
        }
        if (!_stageReady || e.PropertyName != nameof(MainWindowViewModel.ActiveSession)) return;
        var generation = ++_selectionGeneration;
        // SyncActive finishes binding the new card before we locate its visual.
        Dispatcher.UIThread.Post(() =>
        {
            if (!_stageReady || generation != _selectionGeneration || !IsVisible || _dragCard is not null) return;
            if (Vm.ActiveCard is { } active && Vm.ShelfItems.Contains(active)) SessionShelf.ScrollIntoView(active);
            // Apply a shelf scroll before reading the card's visible origin.
            SessionShelf.UpdateLayout();
            var card = SessionShelf.GetVisualDescendants().OfType<StageCard>()
                .FirstOrDefault(c => ReferenceEquals(c.DataContext, Vm.ActiveCard));
            // Measure in the shared parent: StageWindow itself is transformed
            // during a flight, so translating into it would distort the origin.
            if (!Vm.IsSplit && card is not null && Vm.ActiveSession is not null)
            {
                var corners = new[] { new Point(), new Point(card.Bounds.Width, 0),
                    new Point(0, card.Bounds.Height), new Point(card.Bounds.Width, card.Bounds.Height) }
                    .Select(p => card.TranslatePoint(p, StageLayout)!.Value).ToArray();
                var left = corners.Min(p => p.X);
                var top = corners.Min(p => p.Y);
                StageWindow.ActivateFrom(new Rect(left - StageWindow.Bounds.X, top - StageWindow.Bounds.Y,
                    corners.Max(p => p.X) - left, corners.Max(p => p.Y) - top));
            }
            if (IsActive && !Vm.IsSplit && Vm.ActiveSession is not null) MainTerminal.Focus();
        });
    }

    private void OnDockPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_stageReady || Vm.DockVisibilityMode != 0) return;
        var point = e.GetPosition(this);
        var inBottomZone = point.Y >= Bounds.Height - 72 &&
            Math.Abs(point.X - Bounds.Width / 2) < Math.Max(280, ActionDock.Bounds.Width / 2 + 32);
        if (inBottomZone || ActionDock.IsPointerOver)
        {
            _dockHideTimer.Stop();
            SetDockRevealed(true);
        }
        else ScheduleDockHide();
    }

    private void ScheduleDockHide()
    {
        if (_stageReady && Vm.DockVisibilityMode == 0 && !_dockHideTimer.IsEnabled)
            _dockHideTimer.Start();
    }

    private void UpdateDockMode()
    {
        _dockHideTimer.Stop();
        SetDockRevealed(Vm.DockVisibilityMode == 1);
    }

    private void SetDockRevealed(bool revealed)
    {
        ActionDock.Classes.Set("revealed", revealed);
        DockHint.Opacity = revealed ? 0 : 1;
        DockHint.IsHitTestVisible = !revealed;
    }

    private void OnDockHintEntered(object? sender, PointerEventArgs e)
    {
        _dockHideTimer.Stop();
        SetDockRevealed(true);
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else
            BeginMoveDrag(e);
    }

    private void OnRenameCard(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: SessionCardViewModel card }) _ = RenameSessionAsync(card);
        e.Handled = true;
    }

    private void OnRenameActive(object? sender, RoutedEventArgs e)
    {
        if (Vm.ActiveCard is { } card) _ = RenameSessionAsync(card);
        e.Handled = true;
    }

    private async Task RenameSessionAsync(SessionCardViewModel card)
    {
        var name = new TextBox { Text = card.Name, Watermark = "终端名称", Name = "SessionNameInput" };
        var save = new Button { Content = "保存名称", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        var dialog = new Window
        {
            Title = "重命名终端", Width = 380, Height = 180, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = ThemeManager.Brush("Surface"),
            Content = new StackPanel { Margin = new Thickness(22), Spacing = 14,
                Children = { new TextBlock { Text = "终端名称", Foreground = ThemeManager.Brush("Ink") }, name, save } }
        };
        save.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(name.Text)) dialog.Close(name.Text.Trim()); };
        name.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(name.Text)) dialog.Close(name.Text.Trim());
            else if (e.Key == Key.Escape) dialog.Close(null);
        };
        dialog.Opened += (_, _) => { name.Focus(); name.SelectAll(); };
        try
        {
            if (await dialog.ShowDialog<string?>(this) is { } value && _stageReady)
                Vm.RenameSession((card, value));
        }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Rename dialog closed: {ex.Message}"); }
    }

    private void AttachLogsScrollViewer()
    {
        if (_logsScroll is not null) return;
        _logsScroll = LogsList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (_logsScroll is not null)
            _logsScroll.ScrollChanged += OnLogsScrollChanged;
    }

    /// <summary>Follow-tail, view side. A scroll whose offset changed while the extent
    /// did not carries user intent (wheel / thumb / keyboard): at the bottom → follow,
    /// elsewhere → pause. Extent growth (new lines) with following on → pin to the newest
    /// line; this runs inside ScrollChanged, when the extent already includes the new
    /// rows, so ScrollToEnd lands on the real bottom instead of a stale one.</summary>
    private void OnLogsScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_logsScroll is not { } sv) return;
        if (e.OffsetDelta.Y != 0 && e.ExtentDelta.Y == 0)
            Vm.Logs.UpdateFollowFromScroll(sv.Offset.Y + sv.Viewport.Height >= sv.Extent.Height - 4);
        else if (e.ExtentDelta.Y > 0 && Vm.Logs.FollowTail)
            sv.ScrollToEnd();
    }

    /// <summary>「⬇ 跟随」clicked (FollowTail went true) — jump to the newest line.
    /// 「上一条/下一条」(SelectedIndex changed) — bring the selected match into view.</summary>
    private void OnLogsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LogsViewModel.FollowTail) && Vm.Logs.FollowTail)
            _logsScroll?.ScrollToEnd();
        else if (e.PropertyName == nameof(LogsViewModel.SelectedIndex)
                 && Vm.Logs.SelectedEntry is { } entry)
            LogsList.ScrollIntoView(entry);
    }

    /// <summary>Never open larger than the working area — the floating dock must stay on-screen.</summary>
    private void FitToScreen()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;
        // WorkingArea is physical pixels; window Width/Height are device-independent.
        var maxW = screen.WorkingArea.Width / screen.Scaling - 24;
        var maxH = screen.WorkingArea.Height / screen.Scaling - 24;
        if (Width > maxW) Width = maxW;
        if (Height > maxH) Height = maxH;
    }

    private void OnDeployPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var left = e.GetCurrentPoint(DeployDockButton).Properties.IsLeftButtonPressed;
        Vm.ArmDeployCtrl(left && e.KeyModifiers.HasFlag(KeyModifiers.Control));
    }

    private void OnDeployPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // Class handler already ran the command. Drop a modifier that never became a click.
        Vm.ArmDeployCtrl(false);
    }

    private void OnRepublishMenuClick(object? sender, RoutedEventArgs e)
        => Vm.DeployFromDock(forceRepublish: true);

    private void OnCancelPublishMenuClick(object? sender, RoutedEventArgs e)
        => Vm.CancelPublishCommand.Execute(null);

    private void OnOpenLastSuccessClick(object? sender, RoutedEventArgs e)
        => Vm.OpenLastSuccessfulArtifact();

    private void OnCopyLastSuccessPathClick(object? sender, RoutedEventArgs e)
        => Vm.CopyLastSuccessfulArtifactPath();

    private void OnClearLastPublishResultClick(object? sender, RoutedEventArgs e)
        => Vm.ClearLastPublishResult();

    /// <summary>The terminal showing the session Search/Output follows — the
    /// focused split pane, or the single main view outside split mode.</summary>
    private TerminalView? ActiveTerminal()
        => Vm.IsSplit ? (Vm.FocusedPane == 0 ? LeftTerminal : RightTerminal) : MainTerminal;

    private void OnScrollToBottomClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control c && c.Parent is Panel panel)
            panel.Children.OfType<TerminalView>().FirstOrDefault()?.ScrollToBottom();
    }

    private void OnSearchPrevMatch(object? sender, RoutedEventArgs e) => ActiveTerminal()?.GoToMatch(-1);
    private void OnSearchNextMatch(object? sender, RoutedEventArgs e) => ActiveTerminal()?.GoToMatch(+1);

    /// <summary>Double-click a search result row → scroll the terminal to that
    /// absolute buffer line (keeps the scrolled-up state, no snap to bottom).</summary>
    private void OnSearchHitDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Control c && c.DataContext is ScreenBuffer.SearchHit hit)
        {
            ActiveTerminal()?.RevealSearchHit(hit);
            Vm.Dashboard.RefreshSearch();
        }
    }

    // Preview slot changes during the drag; commit collection order on release.
    private SessionCardViewModel? _dragCard;
    private Point _dragOrigin;
    private Point _dragPosition;
    private bool _cardDragging;
    private int _dragFrom, _dragTo;
    private double _dragScrollStart;
    private ScrollViewer? _shelfScroll;
    private readonly DispatcherTimer _shelfDragTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private (SessionCardViewModel Model, StageCard Card, double Top)[] _dragSlots = [];

    private bool _activateHeader;

    public void AcceptLaunch(string? path, string? error)
    {
        if (!string.IsNullOrWhiteSpace(error)) Vm.ShowLaunchNotice(error);
        else if (!string.IsNullOrWhiteSpace(path)) _ = Vm.OpenLaunchDirectoryAsync(path);
    }

    private void OnRevealCommand(CommandRecord record)
    {
        if (ActiveTerminal()?.TryRevealAnchor(record.Start) == true) return;
        Vm.CommandNotice = "这段输出已被历史缓冲裁掉，无法定位。";
    }

    private void OnShelfSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_shelfSelectionGuard) return;
        _shelfSelectionGuard = true;
        try
        {
            if (SessionShelf.SelectedItem is SessionGroupHeader header)
            {
                if (_activateHeader)
                {
                    Vm.ToggleGroup(header);
                    _activateHeader = false;
                    SessionShelf.SelectedItem = Vm.ActiveCard;
                    return;
                }
                var items = Vm.ShelfItems;
                var index = items.IndexOf(header);
                var originIndex = e.RemovedItems.Count > 0 ? items.IndexOf(e.RemovedItems[0]!) : -1;
                if (originIndex >= 0 && originIndex != index)
                {
                    var step = originIndex < index ? 1 : -1;
                    SessionCardViewModel? next = null;
                    for (var i = index + step; i >= 0 && i < items.Count; i += step)
                        if (items[i] is SessionCardViewModel candidate) { next = candidate; break; }
                    if (next is null)
                        for (var i = index - step; i >= 0 && i < items.Count; i -= step)
                            if (items[i] is SessionCardViewModel candidate) { next = candidate; break; }
                    SessionShelf.SelectedItem = next ?? (object?)Vm.ActiveCard;
                    if (next is not null && !ReferenceEquals(next, Vm.ActiveCard)) Vm.ActiveCard = next;
                }
                else SessionShelf.SelectedItem = Vm.ActiveCard;
                return;
            }
            _activateHeader = false;
            if (SessionShelf.SelectedItem is SessionCardViewModel card && !ReferenceEquals(card, Vm.ActiveCard))
                Vm.ActiveCard = card;
        }
        finally { _shelfSelectionGuard = false; }
    }

    private bool _shelfSelectionGuard;

    private void OnShelfPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _activateHeader = IsGroupHeader(e.Source);
        if (!e.GetCurrentPoint(SessionShelf).Properties.IsLeftButtonPressed) return;
        _dragCard = (e.Source as Control)?.FindAncestorOfType<StageCard>(includeSelf: true)
            ?.DataContext as SessionCardViewModel;
        if (_dragCard is null) return;
        _dragOrigin = e.GetPosition(SessionShelf);
        _dragPosition = _dragOrigin;
        _shelfScroll = SessionShelf.GetVisualDescendants().OfType<ScrollViewer>().First();
        _dragScrollStart = _shelfScroll.Offset.Y;
        _dragSlots = SessionShelf.GetVisualDescendants().OfType<StageCard>()
            .Select(c => ((SessionCardViewModel)c.DataContext!, c,
                c.FindAncestorOfType<ListBoxItem>()!.TranslatePoint(default, SessionShelf)!.Value.Y))
            .OrderBy(slot => slot.Item3).ToArray();
        _dragFrom = Array.FindIndex(_dragSlots, slot => ReferenceEquals(slot.Model, _dragCard));
        _dragTo = _dragFrom;
        e.Pointer.Capture(SessionShelf);
        // Defer selection until release so dragging a background terminal does
        // not launch its foreground animation or scroll the shelf underneath us.
        e.Handled = true;
    }

    private void OnShelfPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragCard is null) return;
        _dragPosition = e.GetPosition(SessionShelf);
        if (!_cardDragging && Math.Abs(_dragPosition.Y - _dragOrigin.Y) + Math.Abs(_dragPosition.X - _dragOrigin.X) < 8)
            return;
        if (!_cardDragging)
        {
            _cardDragging = true;
            _dragSlots[_dragFrom].Card.SetDragging(true);
            _shelfDragTimer.Start();
        }
        UpdateShelfDrag(autoScroll: false);
        e.Handled = true;
    }

    private void UpdateShelfDrag(bool autoScroll)
    {
        if (!_cardDragging || _shelfScroll is null) return;
        if (autoScroll)
        {
            var speed = _dragPosition.Y < 36 ? -8 : _dragPosition.Y > SessionShelf.Bounds.Height - 36 ? 8 : 0;
            _shelfScroll.Offset = new Vector(0, Math.Clamp(_shelfScroll.Offset.Y + speed, 0,
                Math.Max(0, _shelfScroll.Extent.Height - _shelfScroll.Viewport.Height)));
        }
        var delta = _dragPosition.Y - _dragOrigin.Y + _shelfScroll.Offset.Y - _dragScrollStart;
        var center = _dragSlots[_dragFrom].Top + ThumbnailHeight / 2 + delta;
        // Compare against fixed slot centers, with a small hysteresis for hand
        // jitter. Never hit-test the animated cards to decide the next order.
        while (_dragTo < _dragSlots.Length - 1 && center > (_dragSlots[_dragTo].Top + _dragSlots[_dragTo + 1].Top + ThumbnailHeight) / 2 + 8) _dragTo++;
        while (_dragTo > 0 && center < (_dragSlots[_dragTo].Top + _dragSlots[_dragTo - 1].Top + ThumbnailHeight) / 2 - 8) _dragTo--;
        for (var i = 0; i < _dragSlots.Length; i++)
        {
            var slot = _dragSlots[i];
            if (i == _dragFrom) slot.Card.SetSlotOffset(delta, immediate: true);
            else
            {
                var destination = i;
                if (_dragFrom < i && i <= _dragTo) destination--;
                if (_dragTo <= i && i < _dragFrom) destination++;
                slot.Card.SetSlotOffset(_dragSlots[destination].Top - slot.Top);
            }
        }
    }

    private void OnShelfPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragCard is null) return;
        var clicked = _cardDragging ? null : _dragCard;
        EndShelfDrag(commit: true);
        e.Pointer.Capture(null);
        if (clicked is not null) Vm.ActiveCard = clicked;
        e.Handled = true;
    }

    private void EndShelfDrag(bool commit)
    {
        _shelfDragTimer.Stop();
        if (_dragCard is null) return;
        var painted = _dragSlots.ToDictionary(s => s.Model, s => s.Top + s.Card.SlotOffset);
        if (commit && _cardDragging) Vm.MoveSessionCard(_dragCard, _dragSlots[_dragTo].Model);
        SessionShelf.UpdateLayout();
        foreach (var card in SessionShelf.GetVisualDescendants().OfType<StageCard>())
        {
            card.SetDragging(false);
            if (card.DataContext is SessionCardViewModel model && painted.TryGetValue(model, out var top))
            {
                var newTop = card.FindAncestorOfType<ListBoxItem>()!.TranslatePoint(default, SessionShelf)!.Value.Y;
                card.SetSlotOffset(top - newTop - ((_shelfScroll?.Offset.Y ?? 0) - _dragScrollStart), immediate: true);
            }
            card.SetSlotOffset(0);
        }
        _dragCard = null;
        _cardDragging = false;
        _dragSlots = [];
    }

    private void OnDeployMenuOpening(object? sender, EventArgs e) => RefreshDeployContextMenu();

    /// <summary>Rebuild profile and recent-artifact submenus from settings and disk.</summary>
    public void RefreshDeployContextMenu()
    {
        if (DeployDockButton.ContextFlyout is MenuFlyout flyout)
            PopulateDeployMenu(flyout);
    }

    private void PopulateDeployMenu(MenuFlyout flyout)
    {
        if (FindDeployMenu(flyout, static h => h.StartsWith("取消打包", StringComparison.Ordinal)) is { } cancel)
            cancel.IsEnabled = Vm.IsPublishRunning;
        if (FindDeployMenu(flyout, static h => h.StartsWith("打开上次成功产物", StringComparison.Ordinal)) is { } last)
            last.IsEnabled = Vm.CanOpenLastSuccessfulArtifact;
        if (FindDeployMenu(flyout, static h => h.StartsWith("复制上次成功产物路径", StringComparison.Ordinal)) is { } copy)
            copy.IsEnabled = Vm.CanCopyLastSuccessfulArtifact;
        if (FindDeployMenu(flyout, static h => h.StartsWith("清除上次发布结果", StringComparison.Ordinal)) is { } clear)
            clear.IsEnabled = Vm.CanClearLastPublishResult;
        if (FindDeployMenu(flyout, static h => h.StartsWith("配置档", StringComparison.Ordinal)) is { } profiles)
            FillProfileMenu(profiles, activate: true);
        if (FindDeployMenu(flyout, static h => h.StartsWith("删除配置档", StringComparison.Ordinal)) is { } deletes)
            FillProfileMenu(deletes, activate: false);
        if (FindDeployMenu(flyout, static h => h.StartsWith("最近产物", StringComparison.Ordinal)) is { } recent)
            FillRecentMenu(recent);
    }

    private static MenuItem? FindDeployMenu(MenuFlyout flyout, Func<string, bool> match) =>
        flyout.Items.OfType<MenuItem>().FirstOrDefault(i => i.Header is string header && match(header));

    private void FillProfileMenu(MenuItem menu, bool activate)
    {
        menu.Items.Clear();
        var profiles = Vm.ListPublishProfiles();
        if (profiles.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "（无已存配置）", IsEnabled = false });
            return;
        }

        foreach (var profile in profiles)
        {
            var id = profile.Id;
            var active = Vm.IsActivePublishProfile(id);
            var item = new MenuItem
            {
                Header = activate && active ? $"✓ {profile.Name}" : profile.Name,
                Tag = id,
            };
            ToolTip.SetTip(item, ProfileTip(profile));
            if (activate)
                item.Click += (_, _) => Vm.ActivatePublishProfile(id);
            else
                item.Click += (_, _) => Vm.DeletePublishProfile(id);
            menu.Items.Add(item);
        }
    }

    private static string ProfileTip(PublishProfile profile)
    {
        var root = string.IsNullOrWhiteSpace(profile.RepoRoot) ? "cwd（当前目录向上查找）" : profile.RepoRoot;
        var rid = string.IsNullOrWhiteSpace(profile.Rid) ? "host（当前系统）" : profile.Rid;
        var note = string.IsNullOrWhiteSpace(profile.Note) ? "" : "\n" + profile.Note;
        return $"root: {root}\nrid: {rid}{note}";
    }

    private void FillRecentMenu(MenuItem menu)
    {
        menu.Items.Clear();
        var rows = Vm.QueryRecentArtifacts();
        if (rows.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "（暂无产物）", IsEnabled = false });
            return;
        }

        foreach (var row in rows)
        {
            var path = row.Path;
            var item = new MenuItem
            {
                Header = $"{row.Rid}   {MainWindowViewModel.FmtBytes(row.SizeBytes)}   {row.Modified.ToLocalTime():MM-dd HH:mm}",
                Tag = path,
            };
            ToolTip.SetTip(item, $"{path}\n{row.FileCount} files");
            item.Click += (_, _) => Vm.OpenRecentArtifact(path);
            menu.Items.Add(item);
        }
    }

    private async void OnSavePublishProfileClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new PublishProfileWindow();
        dialog.ApplyDraft(Vm.CurrentPublishDraft());
        if (await dialog.ShowDialog<bool>(this) != true) return;
        Vm.SavePublishProfile(dialog.ProfileName, dialog.RepoRoot, dialog.SelectedRid, dialog.NoteText);
    }

    /// <summary>Window-level session shortcuts (tunneling, before TerminalView).
    /// App chords use Ctrl+Shift+letter so bare Ctrl+letter control bytes
    /// (tmux prefix ^B, readline ^W/^J, next-history ^N, …) still reach the PTY.
    /// Ctrl+Tab / Ctrl+Shift+Tab cycle cards; F2 renames.</summary>
    private void OnSessionShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        if (PalettePanel.IsVisible) { HandlePaletteKey(e); return; }
        if (BookmarkPanel.IsVisible) { HandleBookmarkKey(e); return; }
        if (e.Source is ShortcutEditor) return;
        if (Vm.HandleSessionShortcut(e)) { e.Handled = true; return; }
        // Grok uses F2 for settings; terminal-focused function keys belong to the CLI.
        if (e.Key == Key.F2 && e.KeyModifiers == KeyModifiers.None
            && e.Source is not (TextBox or TerminalView)) { OnRenameActive(sender, e); return; }
        // AltGr arrives as Ctrl+Alt. Those keys belong to the character, not font zoom.
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Alt)) return;
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        // Font zoom lives on Ctrl+non-letter keys — the bare Ctrl+A..Z control
        // bytes below keep flowing to the shell untouched.
        if (e.Key is Key.OemPlus or Key.Add) { Vm.AdjustFontSize(+1); e.Handled = true; }
        else if (e.Key is Key.OemMinus or Key.Subtract) { Vm.AdjustFontSize(-1); e.Handled = true; }
        else if (e.Key is Key.D0 or Key.NumPad0) { Vm.ResetFontSize(); e.Handled = true; }
        else if (!shift) return;   // bare Ctrl+letter → control byte for the shell
        else if (e.Key == Key.J)
        {
            Vm.ToggleOutputCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.B)
        {
            Vm.ToggleInspectorCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.W)
        {
            Vm.CloseActiveSessionCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.N)
        {
            Vm.NewSessionCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnMinimizeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void OnFileEntryDoubleTapped(object? sender, TappedEventArgs e)
    {
        // DoubleTapped sits on the ListBox; the tapped element carries the FileEntry DataContext.
        if (e.Source is Control c && c.DataContext is TerminalHub.Core.Files.FileEntry entry)
            Vm.Files.Open(entry);
    }

    private void OnFilesListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Vm.Files.OpenSelected();
            e.Handled = true;
        }
    }


    /// <summary>Double-click a log row → activate the session named in Source.
    /// Prefer double-click over single-click so match-nav selection stays usable.</summary>
    private void OnLogsEntryDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Control c && c.DataContext is LogEntry entry)
            Vm.Logs.JumpToSessionEntry(entry);
    }

    private void OnLogsListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Vm.Logs.JumpToSessionCommand.Execute(null);
            e.Handled = true;
            return;
        }
        // Ctrl+C copies the selected row only while the list itself is focused
        // (this handler is on LogsList — filter TextBox never reaches here).
        if (e.Key == Key.C
            && e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && !e.KeyModifiers.HasFlag(KeyModifiers.Alt)
            && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            Vm.Logs.CopySelectedCommand.Execute(null);
            e.Handled = true;
        }
    }

    private static bool IsGroupHeader(object? source)
    {
        for (var node = source as Visual; node is not null; node = node.GetVisualParent())
        {
            if (node is Control { DataContext: SessionGroupHeader }) return true;
            if (node is ListBox) return false;
        }
        return false;
    }

    private void OnSessionMenuOpening(object? sender, EventArgs e)
    {
        if (PinSessionItem is not null)
            PinSessionItem.Header = Vm.ActiveCard?.Model.Pinned == true ? "取消置顶" : "置顶";
        if (SaveSelectionItem is not null)
            SaveSelectionItem.IsEnabled = ActiveTerminal()?.HasSelection == true;
        if (ToggleGroupItem is not null)
        {
            var group = Vm.SessionGroups.FirstOrDefault(item => item.Id == Vm.ActiveCard?.Model.GroupId);
            ToggleGroupItem.IsEnabled = group is not null;
            ToggleGroupItem.Header = group?.Collapsed == true ? "展开当前分组" : "折叠当前分组";
        }
        if (MoveToGroupMenu is null) return;
        MoveToGroupMenu.Items.Clear();
        foreach (var group in Vm.SessionGroups)
        {
            var item = new MenuItem { Header = group.Name, Tag = group.Id };
            item.Click += (_, _) =>
            {
                if (Vm.ActiveCard is { } card) Vm.MoveCardToGroup(card, group.Id);
            };
            MoveToGroupMenu.Items.Add(item);
        }
        if (MoveToGroupMenu.Items.Count == 0)
            MoveToGroupMenu.Items.Add(new MenuItem { Header = "还没有分组", IsEnabled = false });
    }

    private async void OnCreateGroupForActive(object? sender, RoutedEventArgs e)
    {
        var name = await PromptTextAsync("新建分组", "分组名称", "");
        if (!string.IsNullOrWhiteSpace(name)) Vm.CreateGroup(name, Vm.ActiveCard);
    }

    private async void OnCreateEmptyGroup(object? sender, RoutedEventArgs e)
    {
        var name = await PromptTextAsync("新建分组", "分组名称", "");
        if (!string.IsNullOrWhiteSpace(name)) Vm.CreateGroup(name);
    }

    private async void OnRenameActiveGroup(object? sender, RoutedEventArgs e)
    {
        var group = Vm.SessionGroups.FirstOrDefault(item => item.Id == Vm.ActiveCard?.Model.GroupId);
        if (group is null)
        {
            Vm.Dashboard.AppendOutput("info", "当前终端不在分组中。", "ui");
            return;
        }
        var name = await PromptTextAsync("重命名分组", "分组名称", group.Name);
        if (!string.IsNullOrWhiteSpace(name)) Vm.RenameGroup(group, name);
    }

    private void OnCommandDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: CommandRecordViewModel item })
            Vm.LocateCommandCommand.Execute(item);
    }

    private async void OnExportTemplate(object? sender, RoutedEventArgs e)
    {
        var json = Vm.ExportSelectedTemplateJson();
        if (json.Length == 0)
        {
            Vm.TemplateMessage = "先选择一个模板。";
            return;
        }
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "导出工作区模板",
                SuggestedFileName = (Vm.SelectedTemplate?.Name ?? "模板") + ".json",
                FileTypeChoices = [new FilePickerFileType("工作区模板") { Patterns = ["*.json"] }],
            });
            if (file is null) return;
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(json);
            Vm.TemplateMessage = "已导出模板。";
        }
        catch (Exception ex)
        {
            Vm.TemplateMessage = "导出失败：" + ex.Message;
        }
    }

    private async void OnImportTemplate(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "导入工作区模板",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("工作区模板") { Patterns = ["*.json"] }],
            });
            if (files.Count == 0) return;
            await using var stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            Vm.ImportTemplateJson(await reader.ReadToEndAsync());
        }
        catch (Exception ex)
        {
            Vm.TemplateMessage = "导入失败：" + ex.Message;
        }
    }

    private async Task<string?> PromptTextAsync(string title, string label, string initial)
    {
        var input = new TextBox { Text = initial, Watermark = label };
        var save = new Button { Content = "确定", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        var dialog = new Window
        {
            Title = title, Width = 380, Height = 180, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = ThemeManager.Brush("Surface"),
            Content = new StackPanel
            {
                Margin = new Thickness(22), Spacing = 14,
                Children = { new TextBlock { Text = label, Foreground = ThemeManager.Brush("Ink") }, input, save }
            }
        };
        save.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(input.Text)) dialog.Close(input.Text.Trim()); };
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(input.Text)) dialog.Close(input.Text.Trim());
            else if (e.Key == Key.Escape) dialog.Close(null);
        };
        dialog.Opened += (_, _) => { input.Focus(); input.SelectAll(); };
        try { return await dialog.ShowDialog<string?>(this); }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Text dialog closed: {ex.Message}");
            return null;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        Vm.PaletteRequested -= OpenPalette;
        Vm.RevealCommandRequested -= OnRevealCommand;
        Vm.RevealBookmarkRequested -= OnRevealBookmark;
        _toastTimer.Stop();
        _stageReady = false;
        Vm.PropertyChanged -= OnStageSelectionChanged;
        ++_selectionGeneration;
        _dockHideTimer.Stop();
        EndShelfDrag(commit: false);
        Vm.Dispose(); // persists settings + kills PTYs
        base.OnClosing(e);
    }
}
