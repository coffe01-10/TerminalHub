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
using TerminalHub.Core.Ssh;
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
    private Control? _lastPanelTrigger;
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
        ActionDock.PropertyChanged += (_, e) =>
        {
            if (e.Property != DropletDock.RevealProperty) return;
            DockHint.Opacity = Math.Clamp(1 - ActionDock.Reveal * 4, 0, 1);
            DockHint.IsHitTestVisible = ActionDock.Reveal < .65;
        };
        AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (e.Source is Visual source)
                _lastPanelTrigger = source.GetSelfAndVisualAncestors().OfType<Button>().FirstOrDefault();
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        DataContext = new MainWindowViewModel(settingsStore: settingsStore, openFolder: openFolder, shellAvailable: shellAvailable);
        if (OperatingSystem.IsWindows()) ApplyWindowsChrome();
        Vm.PaletteRequested += OpenPalette;
        Vm.RevealCommandRequested += OnRevealCommand;
        InitializeOutputTools();
        Vm.PropertyChanged += OnStageSelectionChanged;
        SessionShelf.SelectionChanged += OnShelfSelectionChanged;
        SessionShelf.SizeChanged += (_, _) => UpdateStageLayout();
        Vm.SessionCards.CollectionChanged += (_, _) => UpdateStageLayout();
        // Group collapse/pin/reorder change header composition without touching
        // SessionCards — ThumbnailHeight accounts for headers, so recompute on
        // every shelf rebuild.
        Vm.ShelfItems.CollectionChanged += (_, _) => UpdateStageLayout();
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
        var boxes = new[] { LeftPaneBox, RightPaneBox, BottomLeftPaneBox, BottomRightPaneBox };
        for (var i = 0; i < boxes.Length; i++)
        {
            var pane = i;
            new[] { LeftTerminal, RightTerminal, BottomLeftTerminal, BottomRightTerminal }[i].GotFocus += (_, _) => Vm.FocusPane(pane);
            boxes[i].AddHandler(InputElement.PointerPressedEvent,
                (_, _) => Vm.FocusPane(pane), RoutingStrategies.Bubble, handledEventsToo: true);
        }
        SplitGrid.AddHandler(InputElement.PointerReleasedEvent, (_, _) => SavePaneRatios(),
            RoutingStrategies.Bubble, handledEventsToo: true);
        Vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainWindowViewModel.IsSplit) or nameof(MainWindowViewModel.SplitLayout)
                or nameof(MainWindowViewModel.PaneMaximized) or nameof(MainWindowViewModel.FocusedPane)
                or nameof(MainWindowViewModel.ColumnRatio) or nameof(MainWindowViewModel.RowRatio)) UpdateSplitLayout();
        };
        UpdateSplitLayout();
        // Shelf drag reorder — threshold-gated so plain clicks still just select.
        SessionShelf.AddHandler(InputElement.PointerPressedEvent, OnShelfPointerPressed,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        SessionShelf.AddHandler(InputElement.PointerMovedEvent, OnShelfPointerMoved,
            RoutingStrategies.Bubble, handledEventsToo: true);
        SessionShelf.AddHandler(InputElement.PointerReleasedEvent, OnShelfPointerReleased,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        SessionShelf.PointerCaptureLost += (_, _) => EndShelfDrag(commit: false);
        _shelfDragTimer.Tick += (_, _) => UpdateShelfDrag(autoScroll: true);
        // Files → terminal drag-out: a held row dragged far enough starts an
        // OS-level Files payload; the terminal treats it like an Explorer drop.
        FilesList.AddHandler(InputElement.PointerPressedEvent, OnFilesDragPointerPressed,
            RoutingStrategies.Bubble, handledEventsToo: true);
        FilesList.AddHandler(InputElement.PointerMovedEvent, OnFilesDragPointerMoved,
            RoutingStrategies.Bubble, handledEventsToo: true);
        FilesList.AddHandler(InputElement.PointerReleasedEvent, OnFilesDragPointerReleased,
            RoutingStrategies.Bubble, handledEventsToo: true);
        FilesList.PointerCaptureLost += (_, e) =>
        {
            if (ReferenceEquals(e.Pointer, _filesDragPointer)) EndFilesDrag();
        };
        Closed += (_, _) => EndFilesDrag();
        FilesList.AddHandler(DragDrop.DragOverEvent, OnFilesDragOver, RoutingStrategies.Bubble);
        FilesList.AddHandler(DragDrop.DragLeaveEvent, OnFilesDragLeave, RoutingStrategies.Bubble);
        FilesList.AddHandler(DragDrop.DropEvent, OnFilesDrop, RoutingStrategies.Bubble);
        OutputResizeHandle.AddHandler(InputElement.PointerPressedEvent, OnOutputResizePressed,
            RoutingStrategies.Bubble, handledEventsToo: true);
        OutputResizeHandle.AddHandler(InputElement.PointerMovedEvent, OnOutputResizeMoved,
            RoutingStrategies.Bubble, handledEventsToo: true);
        OutputResizeHandle.AddHandler(InputElement.PointerReleasedEvent, OnOutputResizeReleased,
            RoutingStrategies.Bubble, handledEventsToo: true);
        InspectorResizeHandle.AddHandler(InputElement.PointerPressedEvent, OnInspectorResizePressed,
            RoutingStrategies.Bubble, handledEventsToo: true);
        InspectorResizeHandle.AddHandler(InputElement.PointerMovedEvent, OnInspectorResizeMoved,
            RoutingStrategies.Bubble, handledEventsToo: true);
        InspectorResizeHandle.AddHandler(InputElement.PointerReleasedEvent, OnInspectorResizeReleased,
            RoutingStrategies.Bubble, handledEventsToo: true);
        ShelfResizeHandle.AddHandler(InputElement.PointerPressedEvent, OnShelfResizePressed,
            RoutingStrategies.Bubble, handledEventsToo: true);
        ShelfResizeHandle.AddHandler(InputElement.PointerMovedEvent, OnShelfResizeMoved,
            RoutingStrategies.Bubble, handledEventsToo: true);
        ShelfResizeHandle.AddHandler(InputElement.PointerReleasedEvent, OnShelfResizeReleased,
            RoutingStrategies.Bubble, handledEventsToo: true);
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

    // Keep the Windows title + directory toolbar height and button positions.
    // The shared controls retain their bindings, flyout and live terminal instance.
    private void ApplyWindowsChrome()
    {
        StageContentGrid.RowDefinitions = new RowDefinitions("84,*,Auto");
        TerminalViewport.BorderThickness = default;
        NewDockButton.IsVisible = false;
        SettingsDockButton.IsVisible = false;
        TerminalChrome.RowDefinitions = new RowDefinitions("44,40");
        TerminalChrome.Margin = new Thickness(8, 0, 4, 0);
        TerminalChrome.ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto,Auto,Auto");
        Grid.SetColumnSpan(TerminalChromeTitle, 4);
        TerminalChromeTitle.MaxWidth = double.PositiveInfinity;
        TerminalChromeTitle.Margin = new Thickness(0, 0, 12, 4);
        TerminalChromeIcon.Margin = new Thickness(0, 0, 10, 4);
        ChromeNew.Margin = new Thickness(0, 0, 0, 4);
        ChromeClose.Margin = new Thickness(0, 0, 0, 4);
        foreach (var dot in TerminalChromeIcon.Children.OfType<Avalonia.Controls.Shapes.Ellipse>()) dot.IsVisible = false;
        void Place(Control control, int row, int column)
        { Grid.SetRow(control, row); Grid.SetColumn(control, column); }
        Place(ChromeNew, 0, 5); Place(ChromeClose, 0, 6);
        Place(ChromeBack, 1, 0); Place(ChromeRefresh, 1, 1); Place(ChromeForward, 1, 2);
        Place(ChromeDirectory, 1, 3); Place(ChromeSplit, 1, 4);
        Place(ChromePopout, 1, 5); Place(SessionMenuButton, 1, 6);
        void Label(Button button, string text)
        {
            var icon = (PathIcon)button.Content!;
            button.Content = null;
            button.Content = new StackPanel
            { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6,
                Children = { icon, new TextBlock { Text = text, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center } } };
        }
        Label(ChromeSplit, "分屏"); Label(ChromePopout, "弹出");
    }

    private void UpdateStageLayout()
    {
        if (_dragCard is not null) return;
        // Keep enough height for the full terminal grid; overflow remains scrollable.
        // Cards tuck ShelfOverlap px under the previous one (except segment
        // tops) and pin/group headers occupy their own ~29px row — count both
        // from the actual shelf items, capped at the first visibleCards cards.
        var visibleCards = Math.Clamp(Vm.ShelfItems.OfType<SessionCardViewModel>().Count(), 1, 5);
        var overlap = SessionCardViewModel.ShelfOverlap;
        // Measure a realized group header when one is on screen; fall back to
        // the template's ~29px (12 padding + ~15 text + border) pre-layout.
        var headerHeight = SessionShelf.GetVisualDescendants().OfType<Border>()
            .FirstOrDefault(b => b.DataContext is SessionGroupHeader)?.Bounds.Height ?? 29;
        var tucked = 0; var headerH = 0.0; var seen = 0;
        foreach (var item in Vm.ShelfItems)
        {
            if (seen >= visibleCards) break;
            if (item is SessionGroupHeader) headerH += headerHeight;
            else if (item is SessionCardViewModel c) { seen++; if (!c.IsStackTop) tucked++; }
        }
        ThumbnailHeight = Math.Clamp(
            (SessionShelf.Bounds.Height - 34 - headerH
                - (visibleCards - 1) * SessionCardViewModel.ShelfSpacing + tucked * overlap) / visibleCards, 208, 268);
        var shelfWidth = Vm.ShelfWidth > 0 ? Vm.ShelfWidth : (Bounds.Width < 1250 ? 232 : 280);
        shelfWidth = Math.Clamp(shelfWidth, 180, Math.Max(280, Bounds.Width * 0.45));
        // Clamp locally so a transient window shrink cannot overwrite the
        // user's persisted height — the panel regrows with the window.
        OutputPanel.Height = Math.Clamp(Vm.OutputHeight, 90, Math.Max(140, Bounds.Height * 0.6));
        StageLayout.ColumnDefinitions[InspectorGutterColumn].Width = new GridLength(Vm.InspectorVisible ? 12 : 0);
        var inspectorWidth = Vm.InspectorWidth > 0 ? Vm.InspectorWidth : (Bounds.Width < 1250 ? 300 : 326);
        inspectorWidth = Math.Clamp(inspectorWidth, 240, Math.Max(320, Bounds.Width * 0.45));
        (shelfWidth, inspectorWidth) = FitRailWidths(shelfWidth, Vm.InspectorVisible ? inspectorWidth : 0);
        StageLayout.ColumnDefinitions[ShelfColumn].Width = new GridLength(shelfWidth);
        StageLayout.ColumnDefinitions[InspectorColumn].Width = new GridLength(inspectorWidth);
        Dispatcher.UIThread.Post(() =>
        {
            if (_stageReady && IsVisible && Vm.ActiveCard is { } active && Vm.ShelfItems.Contains(active))
                RevealShelfCard(active);
        }, DispatcherPriority.Loaded);
    }

    /// <summary>Scroll the shelf so the card's painted bounds are fully inside
    /// the viewport. ScrollIntoView only aligns the item slot, while a tucked
    /// card renders <see cref="SessionCardViewModel.ShelfOverlap"/> px above it
    /// — so measure the card itself and scroll by the exact delta. Measuring
    /// before any scroll keeps the geometry self-consistent; only an
    /// unrealized card needs a ScrollIntoView + layout first.</summary>
    private void RevealShelfCard(SessionCardViewModel active)
    {
        var card = FindShelfCard(active);
        if (card is null)
        {
            SessionShelf.ScrollIntoView(active);
            SessionShelf.UpdateLayout();
            card = FindShelfCard(active);
        }
        var scroll = SessionShelf.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (card is null || scroll is null) return;
        if (card.TranslatePoint(default, SessionShelf) is not { } top) return;
        var topPad = SessionShelf.Padding.Top;
        var bottom = top.Y + card.Bounds.Height;
        var viewBottom = SessionShelf.Bounds.Height - SessionShelf.Padding.Bottom;
        var dy = top.Y < topPad ? top.Y - topPad
            : bottom > viewBottom ? Math.Min(bottom - viewBottom, top.Y - topPad)
            : 0;
        if (dy != 0)
            scroll.Offset = new Vector(scroll.Offset.X,
                Math.Clamp(scroll.Offset.Y + dy, 0,
                    Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height)));
    }

    // The chrome and terminal need room after both rails and gutters are deducted.
    private const double MinimumStageWidth = 460;
    private double RailBudget => Math.Max(180 + (Vm.InspectorVisible ? 240 : 0),
        (Bounds.Width - StageLayout.Margin.Left - StageLayout.Margin.Right)
        - 18 - (Vm.InspectorVisible ? 12 : 0) - MinimumStageWidth);

    private (double shelf, double inspector) FitRailWidths(double shelf, double inspector)
    {
        if (shelf + inspector <= RailBudget) return (shelf, inspector);
        var inspectorMin = Vm.InspectorVisible ? 240 : 0;
        var extra = shelf - 180 + inspector - inspectorMin;
        var scale = (RailBudget - 180 - inspectorMin) / extra;
        return (180 + (shelf - 180) * scale,
            inspectorMin + (inspector - inspectorMin) * scale);
    }

    private StageCard? FindShelfCard(SessionCardViewModel active)
        => SessionShelf.GetVisualDescendants().OfType<StageCard>()
            .FirstOrDefault(c => ReferenceEquals(c.DataContext, active));

    private void OnStageSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SettingsOpen) && Vm.SettingsOpen)
            DropletExpansion.SetOrigin(SettingsPanel, _lastPanelTrigger);
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
            if (Vm.ActiveCard is { } active && Vm.ShelfItems.Contains(active)) RevealShelfCard(active);
            // Apply a shelf scroll before reading the card's visible origin.
            SessionShelf.UpdateLayout();
            var card = SessionShelf.GetVisualDescendants().OfType<StageCard>()
                .FirstOrDefault(c => ReferenceEquals(c.DataContext, Vm.ActiveCard));
            // Measure in the shared parent: StageWindow itself is transformed
            // during a flight, so translating into it would distort the origin.
            if (!Vm.IsSplit && card is not null && Vm.ActiveSession is not null)
            {
                var body = card.Child ?? card;
                var corners = new[] { new Point(), new Point(body.Bounds.Width, 0),
                    new Point(0, body.Bounds.Height), new Point(body.Bounds.Width, body.Bounds.Height) }
                    .Select(p => body.TranslatePoint(p, StageLayout)!.Value).ToArray();
                var left = corners.Min(p => p.X);
                var top = corners.Min(p => p.Y);
                StageWindow.ActivateFrom(new Rect(left - StageWindow.Bounds.X, top - StageWindow.Bounds.Y,
                    corners.Max(p => p.X) - left, corners.Max(p => p.Y) - top));
            }
            if (IsActive && Vm.ActiveSession is not null) ActiveTerminal()?.Focus();
        });
    }

    private void OnDockPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_stageReady || Vm.DockVisibilityMode != 0) return;
        if (DockHint.IsPointerOver || (ActionDock.IsHitTestVisible && ActionDock.IsPointerOver))
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
        ActionDock.IsVisible = Vm.DockVisibilityMode != 2;
        SetDockRevealed(Vm.DockVisibilityMode == 1);
    }

    private void SetDockRevealed(bool revealed)
    {
        ActionDock.Reveal = revealed ? 1 : 0;
    }

    private void OnDockHintEntered(object? sender, PointerEventArgs e)
    {
        _dockHideTimer.Stop();
        if (Vm.DockVisibilityMode == 0) SetDockRevealed(true);
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
        // DoubleTapped bubbles from the deepest element: a double-click on the
        // card's ⋯ button would toggle the flyout AND open rename — skip it.
        if ((e.Source as Control)?.FindAncestorOfType<Button>(includeSelf: true) is not null) return;
        if (sender is Control { DataContext: SessionCardViewModel card }) _ = RenameSessionAsync(card);
        e.Handled = true;
    }

    private void OnRenameActive(object? sender, RoutedEventArgs e)
    {
        if (Vm.ActiveCard is { } card) _ = RenameSessionAsync(card);
        e.Handled = true;
    }

    // Clicking the already-selected SSH row fires no SelectionChanged, so with
    // the form collapsed the row looked dead. Re-drive the select → fill path.
    private void OnSshRowTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Control)?.FindAncestorOfType<Button>(includeSelf: true) is not null) return;
        var ssh = Vm.Ssh;
        if ((e.Source as Control)?.FindAncestorOfType<ListBoxItem>()?.DataContext is SshHost h
            && ssh.Selected == h && !ssh.Editing)
        {
            ssh.Selected = null;
            ssh.Selected = h;
        }
    }

    /// <summary>Enter on the host list = connect the selected row.</summary>
    private void OnSshListKeyDown(object? sender, KeyEventArgs e)
    {
        if (OperatingSystem.IsWindows()) return;
        if (e.Key == Key.Enter && Vm.Ssh.Selected is { } h)
        {
            Vm.Ssh.ConnectCommand.Execute(h);
            e.Handled = true;
        }
    }

    /// <summary>Esc inside the side rail or bottom panel returns focus to the
    /// active terminal — the uniform "back to typing" affordance.</summary>
    private void OnPanelEscapeKeyDown(object? sender, KeyEventArgs e)
    {
        if (OperatingSystem.IsWindows()) return;
        if (e.Key == Key.Escape)
        {
            ActiveTerminal()?.Focus();
            e.Handled = true;
        }
    }

    /// <summary>Enter anywhere in the SSH form = 添加/更新 (its own validation shows errors).
    /// Esc returns focus to the terminal; an empty host list keeps its add form.</summary>
    private void OnSshFormKeyDown(object? sender, KeyEventArgs e)
    {
        if (OperatingSystem.IsWindows()) return;
        if (e.Key == Key.Escape)
        {
            Vm.Ssh.ToggleEditingCommand.Execute(null);
            ActiveTerminal()?.Focus();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Enter)
        {
            Vm.Ssh.AddOrUpdateCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>Double-click a saved host = connect (single click loads the edit form).</summary>
    private void OnSshRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (OperatingSystem.IsWindows()) return;
        if ((e.Source as Control)?.FindAncestorOfType<Button>(includeSelf: true) is not null) return;
        if ((e.Source as Control)?.FindAncestorOfType<ListBoxItem>()?.DataContext is SshHost h
            && Vm.Ssh.ConnectCommand.CanExecute(h))
            Vm.Ssh.ConnectCommand.Execute(h);
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
        => Vm.IsSplit ? Vm.FocusedPane switch
        { 0 => LeftTerminal, 1 => RightTerminal, 2 => BottomLeftTerminal, _ => BottomRightTerminal } : MainTerminal;

    private void OnScrollToBottomClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control c && c.Parent is Panel panel)
            panel.Children.OfType<TerminalView>().FirstOrDefault()?.ScrollToBottom();
    }

    private void OnSearchPrevMatch(object? sender, RoutedEventArgs e) => StepSearch(-1);
    private void OnSearchNextMatch(object? sender, RoutedEventArgs e) => StepSearch(1);
    private void StepSearch(int direction)
    {
        var count = SearchResults.ItemCount;
        if (count == 0) { Vm.Dashboard.RefreshSearch(); count = SearchResults.ItemCount; }
        if (count == 0) return;
        var index = SearchResults.SelectedIndex;
        SearchResults.SelectedIndex = index < 0 ? direction > 0 ? 0 : count - 1 : (index + direction + count) % count;
        SearchResults.ScrollIntoView(SearchResults.SelectedItem!);
        if (SearchResults.SelectedItem is SessionSearchResult result) _ = LocateSearchResultAsync(result);
    }
    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        StepSearch(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
        e.Handled = true;
    }
    private void OnSearchHitDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Control c && c.DataContext is SessionSearchResult result) _ = LocateSearchResultAsync(result);
    }
    public async Task<bool> LocateSearchResultAsync(SessionSearchResult result)
    {
        if (Vm.Dashboard.SearchNavigationInProgress) return false;
        var buffer = result.Buffer;
        if (!result.CanLocate())
        { Vm.Dashboard.SearchStatus = "原输出已被裁剪、覆盖或屏幕已切换，请重新搜索。"; return false; }
        Vm.Dashboard.SearchNavigationInProgress = true;
        try
        {
            if (result.Session is { } session)
            {
                if (Vm.DetachedSessions.Contains(session))
                {
                    Vm.Popouts.FirstOrDefault(w => ((SessionWindowViewModel?)w.DataContext)?.Model == session)?.Close();
                    var reattached = new TaskCompletionSource();
                    Dispatcher.UIThread.Post(() => reattached.SetResult());
                    await reattached.Task;
                }
                if (!Vm.SessionCards.Any(c => ReferenceEquals(c.Model, session)))
                { Vm.Dashboard.SearchStatus = "原会话已关闭，请重新搜索。"; return false; }
                Vm.ActivateSearchSession(session);
            }
            var located = new TaskCompletionSource<bool>();
            Dispatcher.UIThread.Post(() =>
            {
                UpdateLayout();
                var view = ActiveTerminal();
                var revealed = ReferenceEquals(view?.Emulator?.Buffer, buffer) && result.CanLocate() && view.TryRevealAnchor(result.Anchor);
                Vm.Dashboard.SearchStatus = revealed ? $"已定位到 {result.SessionName}" : "原输出已被裁剪、覆盖或屏幕已切换，请重新搜索。";
                located.SetResult(revealed);
            });
            return await located.Task;
        }
        finally { Vm.Dashboard.SearchNavigationInProgress = false; }
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

    /// <summary>Browser tab-strip convention: double-tap the empty shelf area
    /// spawns a session. Taps on a card or group header keep their own meaning.</summary>
    private void OnShelfBackgroundDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (OperatingSystem.IsWindows()) return;
        if (e.Source is Visual v && v.FindAncestorOfType<ListBoxItem>(includeSelf: true) is null)
        {
            Vm.NewSessionCommand.Execute(null);
            e.Handled = true;
        }
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
        // Presses on a card's ⋯ menu button must not start a drag or activate
        // the card — the button owns the click.
        if ((e.Source as Control)?.FindAncestorOfType<Button>(includeSelf: true) is not null) return;
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
        // Compare painted card centers: a tucked card renders ShelfOverlap px
        // above its slot, so slot tops alone are off by the overlap.
        double CardCenter(int i) => _dragSlots[i].Top + ThumbnailHeight / 2
            - (_dragSlots[i].Model.IsStackTop ? 0 : SessionCardViewModel.ShelfOverlap);
        var center = CardCenter(_dragFrom) + delta;
        // Compare against painted card centers, with a small hysteresis for
        // hand jitter. Never hit-test the animated cards to decide the order.
        while (_dragTo < _dragSlots.Length - 1 && center > (CardCenter(_dragTo) + CardCenter(_dragTo + 1)) / 2 + 8) _dragTo++;
        while (_dragTo > 0 && center < (CardCenter(_dragTo) + CardCenter(_dragTo - 1)) / 2 - 8) _dragTo--;
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
        // Middle-click a card = close it (browser/terminal tab convention);
        // the left-drag path below only ever arms on the left button — and a
        // stray middle release during an armed left-drag must not close it.
        if (e.InitialPressMouseButton == MouseButton.Middle)
        {
            if (OperatingSystem.IsWindows()) return;
            if (_dragCard is null
                && (e.Source as Control)?.FindAncestorOfType<Button>(includeSelf: true) is null
                && (e.Source as Control)?.FindAncestorOfType<StageCard>(includeSelf: true)
                    ?.DataContext is SessionCardViewModel card)
            {
                Vm.CloseSessionCommand.Execute(card);
                e.Handled = true;
            }
            return;
        }
        if (_dragCard is null) return;
        // Dragging a card well past the shelf's right edge (onto the stage)
        // pops it out — the browser "drag a tab off the strip" convention.
        // Vertical reorder keeps working; only clear horizontal overflow detaches.
        var pos = e.GetPosition(SessionShelf);
        var detach = !OperatingSystem.IsWindows() && _cardDragging
            && (pos.X > SessionShelf.Bounds.Width + 24 || pos.X < -24);
        var clicked = _cardDragging ? null : _dragCard;
        var dragged = _dragCard;
        EndShelfDrag(commit: !detach);
        e.Pointer.Capture(null);
        if (detach) Vm.OpenInNewWindowCommand.Execute(dragged);
        else if (clicked is not null) Vm.ActiveCard = clicked;
        e.Handled = true;
    }

    private void EndShelfDrag(bool commit)
    {
        _shelfDragTimer.Stop();
        if (_dragCard is null) return;
        // Painted tops, not slot tops: a tucked card renders ShelfOverlap px
        // above its slot, and its IsStackTop may flip when the drop crosses a
        // segment boundary — comparing slot tops alone jumps it by 16px.
        var painted = _dragSlots.ToDictionary(s => s.Model,
            s => s.Top - (s.Model.IsStackTop ? 0 : SessionCardViewModel.ShelfOverlap) + s.Card.SlotOffset);
        if (commit && _cardDragging) Vm.MoveSessionCard(_dragCard, _dragSlots[_dragTo].Model);
        SessionShelf.UpdateLayout();
        foreach (var card in SessionShelf.GetVisualDescendants().OfType<StageCard>())
        {
            card.SetDragging(false);
            if (card.DataContext is SessionCardViewModel model && painted.TryGetValue(model, out var top))
            {
                var newTop = card.FindAncestorOfType<ListBoxItem>()!.TranslatePoint(default, SessionShelf)!.Value.Y
                    - (model.IsStackTop ? 0 : SessionCardViewModel.ShelfOverlap);
                card.SetSlotOffset(top - newTop - ((_shelfScroll?.Offset.Y ?? 0) - _dragScrollStart), immediate: true);
            }
            card.SetSlotOffset(0);
        }
        _dragCard = null;
        _cardDragging = false;
        _dragSlots = [];
        // SessionCards.CollectionChanged fired mid-drag was skipped by the
        // _dragCard guard — header composition may have changed on the drop.
        UpdateStageLayout();
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
        _lastPanelTrigger = null;
        if (PalettePanel.IsVisible) { HandlePaletteKey(e); return; }
        if (BookmarkPanel.IsVisible) { HandleBookmarkKey(e); return; }
        if (e.Source is ShortcutEditor) return;
        if (Vm.HandleSessionShortcut(e)) { e.Handled = true; return; }
        // Grok uses F2 for settings; terminal-focused function keys belong to the CLI.
        // F2 on the Files list must reach its own bubble handler (file rename);
        // everywhere else it opens the session rename dialog.
        if (e.Key == Key.F2 && e.KeyModifiers == KeyModifiers.None
            && e.Source is not (TextBox or TerminalView)
            && (e.Source as Visual)?.FindAncestorOfType<ListBox>(includeSelf: true) != FilesList)
        { OnRenameActive(sender, e); return; }
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
        else if (!OperatingSystem.IsWindows() && e.Key == Key.F)
        {
            // Ctrl+Shift+F: open the bottom panel on Search and focus the box.
            Vm.OutputVisible = true;
            Vm.Dashboard.SelectedBottomTab = DashboardViewModel.SearchTabIndex;
            FindBox.Focus();
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
        else if (e.Key == Key.Back
                 || (e.Key == Key.Up && e.KeyModifiers.HasFlag(KeyModifiers.Alt)))
        {
            Vm.Files.UpCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            Vm.Files.CopyPathCommand.Execute(Vm.Files.SelectedEntry);
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            Vm.Files.RefreshCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.H && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            Vm.Files.ShowHidden = !Vm.Files.ShowHidden;
            e.Handled = true;
        }
        else if (e.Key == Key.F2)
        {
            _ = RenameFileEntryAsync(Vm.Files.SelectedEntry);
            e.Handled = true;
        }
        else if (e.Key == Key.V && e.KeyModifiers == KeyModifiers.Control)
        {
            _ = PasteClipboardFilesAsync();
            e.Handled = true;
        }
    }

    /// <summary>Empty-area context menu: create folder/file in the current dir.
    /// The prompt enforces non-empty; cancel → no-op.</summary>
    private async void OnFilesNewFolderClick(object? sender, RoutedEventArgs e)
    {
        var name = await PromptTextAsync("新建文件夹", "文件夹名称", "");
        if (name is not null) Vm.Files.NewFolder(name);
    }

    private async void OnFilesNewTextFileClick(object? sender, RoutedEventArgs e)
    {
        var name = await PromptTextAsync("新建文本文件", "文件名称", "");
        if (name is not null) Vm.Files.NewTextFile(name);
    }

    /// <summary>Row context 重命名 / F2: prompt prefilled with the current name.</summary>
    private async void OnFilesRenameClick(object? sender, RoutedEventArgs e)
    {
        var entry = (sender as MenuItem)?.DataContext as TerminalHub.Core.Files.FileEntry;
        await RenameFileEntryAsync(entry);
    }

    private async Task RenameFileEntryAsync(TerminalHub.Core.Files.FileEntry? entry)
    {
        if (entry is null) return;
        var name = await PromptTextAsync("重命名", "新名称", entry.Name);
        if (name is not null) Vm.Files.Rename(entry, name);
    }

    // Drag-out gesture: press records the row, a >6px move starts the real
    // drag. Single click stays a pure selection (and previews the file).
    private Point? _filesDragStart;
    private TerminalHub.Core.Files.FileEntry? _filesDragEntry;
    private CancellationTokenSource? _filesDragPending;
    private IPointer? _filesDragPointer;

    private void EndFilesDrag()
    {
        _filesDragStart = null;
        _filesDragEntry = null;
        _filesDragPending?.Cancel();
        _filesDragPending?.Dispose();
        _filesDragPending = null;
        var pointer = _filesDragPointer;
        _filesDragPointer = null;
        if (ReferenceEquals(pointer?.Captured, FilesList)) pointer.Capture(null);
    }

    private void OnFilesDragPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        EndFilesDrag();
        var point = e.GetCurrentPoint(FilesList);
        // Explorer convention: mouse back button navigates to the parent dir.
        if (point.Properties.IsXButton1Pressed)
        {
            Vm.Files.UpCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (e.Source is Control { DataContext: TerminalHub.Core.Files.FileEntry entry })
        {
            // Explorer convention: right-click selects the row before its menu.
            if (point.Properties.IsRightButtonPressed)
                Vm.Files.SelectedEntry = entry;
            else if (point.Properties.IsLeftButtonPressed)
            {
                _filesDragStart = e.GetPosition(FilesList);
                _filesDragEntry = entry;
            }
        }
    }

    private void OnFilesDragPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        EndFilesDrag();
    }

    private async void OnFilesDragPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_filesDragStart is not { } start || _filesDragEntry is not { } entry) return;
        if (!e.GetCurrentPoint(FilesList).Properties.IsLeftButtonPressed)
        {
            EndFilesDrag();
            return;
        }
        var delta = e.GetPosition(FilesList) - start;
        if (delta.X * delta.X + delta.Y * delta.Y < 36) return;
        _filesDragStart = null;
        _filesDragEntry = null;
        var pending = new CancellationTokenSource();
        _filesDragPending = pending;
        var token = pending.Token;
        // Capture while resolving a slow storage path so release outside the list
        // still cancels this gesture before an OS drag can start.
        _filesDragPointer = e.Pointer;
        e.Pointer.Capture(FilesList);
        try
        {
            var data = await FilesDragData.CreateAsync(StorageProvider, entry, token);
            if (token.IsCancellationRequested) return;
            EndFilesDrag();
            if (data is not null) await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!token.IsCancellationRequested)
            {
                Vm.Files.StatusIsError = true;
                Vm.Files.StatusText = $"无法拖出文件: {ex.Message}";
            }
        }
        finally
        {
            if (ReferenceEquals(_filesDragPending, pending)) EndFilesDrag();
        }
    }

    // Explorer-style drop feedback: hovering a directory row during an OS file
    // drag marks it (the drop lands inside that directory).
    private ListBoxItem? _dropHotItem;

    private void OnFilesDragOver(object? sender, DragEventArgs e)
    {
        var hasFiles = e.Data.Contains(DataFormats.Files);
        e.DragEffects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        var item = hasFiles
            ? (e.Source as Control)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)
            : null;
        if (item?.DataContext is not TerminalHub.Core.Files.FileEntry { IsDirectory: true })
            item = null;
        if (!ReferenceEquals(item, _dropHotItem))
        {
            _dropHotItem?.Classes.Remove("drop-hot");
            item?.Classes.Add("drop-hot");
            _dropHotItem = item;
        }
        e.Handled = true;
    }

    private void OnFilesDragLeave(object? sender, DragEventArgs e)
    {
        _dropHotItem?.Classes.Remove("drop-hot");
        _dropHotItem = null;
    }

    // Drag-in: dropping OS files/dirs copies them into the hovered directory
    // row, or the current directory on empty space. Paths inside the target
    // are skipped so an in-panel drag is a no-op.
    private void OnFilesDrop(object? sender, DragEventArgs e)
    {
        OnFilesDragLeave(sender, e);
        var paths = e.Data.GetFiles()?
            .Select(item => item.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();
        // A directory row under the cursor is the drop target (Explorer rule).
        var target = (e.Source as Control)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)
            ?.DataContext is TerminalHub.Core.Files.FileEntry { IsDirectory: true } dir
            ? dir.FullPath : null;
        if (paths is { Count: > 0 }) _ = Vm.Files.ImportPathsAsync(paths!, target);
        e.Handled = true;
    }

    private async void OnFilesPasteClick(object? sender, RoutedEventArgs e)
        => await PasteClipboardFilesAsync();

    private async Task PasteClipboardFilesAsync()
    {
        // Same guard as terminal paste: Win32's clipboard throws while another
        // process holds it open — and the context-menu path here is async void.
        try
        {
            var clip = TopLevel.GetTopLevel(this)?.Clipboard;
            var paths = clip is null ? null : (await clip.GetDataAsync(DataFormats.Files)) switch
            {
                IEnumerable<IStorageItem> items => items
                    .Select(i => i.TryGetLocalPath()).OfType<string>().ToList(),
                IEnumerable<string> raw => raw.Where(p => File.Exists(p) || Directory.Exists(p)).ToList(),
                _ => [],
            };
            if (paths is { Count: > 0 }) _ = Vm.Files.ImportPathsAsync(paths);
        }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Files paste failed: {ex.Message}"); }
    }

    /// <summary>Grey out 粘贴 when the clipboard holds no file list.</summary>
    private async void OnFilesContextMenuOpened(object? sender, RoutedEventArgs e)
    {
        var clip = TopLevel.GetTopLevel(this)?.Clipboard;
        var hasFiles = false;
        try
        {
            hasFiles = clip is not null
                && await clip.GetDataAsync(DataFormats.Files) is System.Collections.IEnumerable;
        }
        catch { /* clipboard busy — leave the item enabled rather than grey a valid paste */ }
        PasteMenuItem.IsEnabled = hasFiles;
    }

    // Bottom-panel height: capture the pointer on the 8px top-edge handle and
    // drag — up grows the panel. Clamped between a readable minimum and 60%
    // of the window so the terminal never disappears entirely.
    private double _outputDragStartY, _outputDragStartH;

    private void OnOutputResizePressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(OutputResizeHandle).Properties.IsLeftButtonPressed) return;
        _outputDragStartY = e.GetPosition(this).Y;
        _outputDragStartH = OutputPanel.Height;
        e.Pointer.Capture(OutputResizeHandle);
        e.Handled = true;
    }

    private void OnOutputResizeMoved(object? sender, PointerEventArgs e)
    {
        if (!Equals(e.Pointer.Captured, OutputResizeHandle)) return;
        var delta = _outputDragStartY - e.GetPosition(this).Y;
        Vm.OutputHeight = OutputPanel.Height =
            Math.Clamp(_outputDragStartH + delta, 90, Math.Max(140, Bounds.Height * 0.6));
        e.Handled = true;
    }

    private void OnOutputResizeReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (Equals(e.Pointer.Captured, OutputResizeHandle)) e.Pointer.Capture(null);
    }

    // Inspector rail left-edge drag — left grows the panel. The explicit width
    // is stored on the VM so later UpdateStageLayout passes keep it (clamped).
    private double _inspectorDragStartX, _inspectorDragStartW;

    private void OnInspectorResizePressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(InspectorResizeHandle).Properties.IsLeftButtonPressed) return;
        _inspectorDragStartX = e.GetPosition(this).X;
        _inspectorDragStartW = StageLayout.ColumnDefinitions[InspectorColumn].ActualWidth;
        e.Pointer.Capture(InspectorResizeHandle);
        e.Handled = true;
    }

    private void OnInspectorResizeMoved(object? sender, PointerEventArgs e)
    {
        if (!Equals(e.Pointer.Captured, InspectorResizeHandle)) return;
        var delta = _inspectorDragStartX - e.GetPosition(this).X;
        var max = Math.Max(240, RailBudget - StageLayout.ColumnDefinitions[ShelfColumn].ActualWidth);
        Vm.InspectorWidth = Math.Clamp(_inspectorDragStartW + delta, 240, max);
        StageLayout.ColumnDefinitions[InspectorColumn].Width = new GridLength(Vm.InspectorWidth);
        e.Handled = true;
    }

    private void OnInspectorResizeReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (Equals(e.Pointer.Captured, InspectorResizeHandle)) e.Pointer.Capture(null);
    }

    // Session shelf right-edge drag — right grows the shelf. Same pin-on-drag
    // convention as the inspector rail.
    private double _shelfDragStartX, _shelfDragStartW;

    private void OnShelfResizePressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(ShelfResizeHandle).Properties.IsLeftButtonPressed) return;
        _shelfDragStartX = e.GetPosition(this).X;
        _shelfDragStartW = StageLayout.ColumnDefinitions[ShelfColumn].ActualWidth;
        e.Pointer.Capture(ShelfResizeHandle);
        e.Handled = true;
    }

    private void OnShelfResizeMoved(object? sender, PointerEventArgs e)
    {
        if (!Equals(e.Pointer.Captured, ShelfResizeHandle)) return;
        var delta = e.GetPosition(this).X - _shelfDragStartX;
        var max = Math.Max(180, RailBudget - StageLayout.ColumnDefinitions[InspectorColumn].ActualWidth);
        Vm.ShelfWidth = Math.Clamp(_shelfDragStartW + delta, 180, max);
        StageLayout.ColumnDefinitions[ShelfColumn].Width = new GridLength(Vm.ShelfWidth);
        e.Handled = true;
    }

    private void OnShelfResizeReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (Equals(e.Pointer.Captured, ShelfResizeHandle)) e.Pointer.Capture(null);
    }


    /// <summary>Double-click a log row → activate the session named in Source.
    /// Prefer double-click over single-click so match-nav selection stays usable.</summary>
    private void OnLogsEntryDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Control c && c.DataContext is LogEntry entry)
            Vm.Logs.JumpToSessionEntry(entry);
    }

    /// <summary>Same as Logs: double-tap an Output row to jump to its source
    /// session (Source carries the session name).</summary>
    private void OnOutputEntryDoubleTapped(object? sender, TappedEventArgs e)
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
        TerminalColorsMenu.Items.Clear();
        foreach (var (label, scheme) in new[]
        {
            ("自动（适应应用背景）", TerminalHub.Core.Terminal.TerminalColorScheme.Automatic),
            ("跟随界面", TerminalHub.Core.Terminal.TerminalColorScheme.FollowTheme),
            ("深色", TerminalHub.Core.Terminal.TerminalColorScheme.Dark),
            ("浅色", TerminalHub.Core.Terminal.TerminalColorScheme.Light)
        })
        {
            var item = new MenuItem { Header = label, ToggleType = MenuItemToggleType.Radio,
                IsChecked = Vm.ActiveSession?.Emulator.ColorScheme == scheme };
            item.Click += (_, _) =>
            {
                if (Vm.ActiveSession is not { } session) return;
                session.Emulator.ColorScheme = scheme;
                Vm.PersistSettings();
            };
            TerminalColorsMenu.Items.Add(item);
        }
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
        // Popped-out sessions leave the shelf entirely — this submenu is the
        // only in-window way to refocus a popout buried under other windows.
        if (PopoutSessionsMenu is not null)
        {
            PopoutSessionsMenu.Items.Clear();
            foreach (var win in Vm.Popouts)
            {
                if (win.Session is not { } session) continue;
                var item = new MenuItem { Header = $"聚焦 {session.Name}" };
                ToolTip.SetTip(item, session.WorkingDirectory);
                var target = win;
                item.Click += (_, _) => target.Activate();
                PopoutSessionsMenu.Items.Add(item);
            }
            if (PopoutSessionsMenu.Items.Count == 0)
                PopoutSessionsMenu.Items.Add(new MenuItem { Header = "无弹出窗口", IsEnabled = false });
        }
    }

    /// <summary>Per-card "⋯" menu on shelf cards (mockup): same session ops as
    /// the stage menu but applied to the card under the pointer, without
    /// switching the active session first.</summary>
    private void OnCardMenuOpening(object? sender, EventArgs e)
    {
        if (sender is not MenuFlyout flyout ||
            flyout.Target?.DataContext is not SessionCardViewModel card) return;
        flyout.Items.Clear();
        MenuItem Item(string header, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled };
            item.Click += (_, _) => action();
            return item;
        }
        flyout.Items.Add(Item("设为当前终端", () => Vm.ActivateCard(card)));
        flyout.Items.Add(Item("重命名", () => _ = RenameSessionAsync(card)));
        flyout.Items.Add(Item(card.Model.Pinned ? "取消置顶" : "置顶",
            () => Vm.SetPinned(card, !card.Model.Pinned)));
        flyout.Items.Add(Item("重启会话", () => _ = Vm.RestartSession(card),
            card.Model.Pty.ExitCode is not null));
        var moveTo = new MenuItem { Header = "移入分组" };
        foreach (var group in Vm.SessionGroups)
            moveTo.Items.Add(Item(group.Name, () => Vm.MoveCardToGroup(card, group.Id),
                group.Id != card.Model.GroupId));
        if (moveTo.Items.Count == 0)
            moveTo.Items.Add(new MenuItem { Header = "还没有分组", IsEnabled = false });
        flyout.Items.Add(moveTo);
        flyout.Items.Add(Item("移出分组", () => Vm.MoveCardToGroup(card, null),
            !string.IsNullOrEmpty(card.Model.GroupId)));
        flyout.Items.Add(Item("新建分组", async () =>
        {
            var name = await PromptTextAsync("新建分组", "分组名称", "");
            if (!string.IsNullOrWhiteSpace(name)) Vm.CreateGroup(name, card);
        }));
        flyout.Items.Add(new Separator());
        flyout.Items.Add(Item("关闭会话", () => Vm.CloseSessionCommand.Execute(card)));
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
