using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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

    public MainWindow(SettingsStore? settingsStore)
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel(settingsStore: settingsStore);
        Vm.PropertyChanged += OnStageSelectionChanged;
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
            RoutingStrategies.Bubble, handledEventsToo: true);
        SessionShelf.AddHandler(InputElement.PointerMovedEvent, OnShelfPointerMoved,
            RoutingStrategies.Bubble, handledEventsToo: true);
        SessionShelf.AddHandler(InputElement.PointerReleasedEvent, OnShelfPointerReleased,
            RoutingStrategies.Bubble, handledEventsToo: true);
        Opened += (_, _) =>
        {
            FitToScreen();
            _ = Vm.SpawnStartupSessionsAsync();
            _stageReady = true;
            UpdateDockMode();
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
        // Keep enough height for the full terminal grid; overflow remains scrollable.
        var visibleCards = Math.Clamp(Vm.SessionCards.Count, 1, 5);
        ThumbnailHeight = Math.Clamp((SessionShelf.Bounds.Height - 34 - (visibleCards - 1) * 16) / visibleCards, 208, 268);
        StageLayout.ColumnDefinitions[ShelfColumn].Width = new GridLength(Bounds.Width < 1250 ? 232 : 280);
        StageLayout.ColumnDefinitions[InspectorGutterColumn].Width = new GridLength(Vm.InspectorVisible ? 12 : 0);
        StageLayout.ColumnDefinitions[InspectorColumn].Width = new GridLength(Vm.InspectorVisible ? (Bounds.Width < 1250 ? 300 : 326) : 0);
        Dispatcher.UIThread.Post(() =>
        {
            if (_stageReady && IsVisible && Vm.ActiveCard is { } active) SessionShelf.ScrollIntoView(active);
        }, DispatcherPriority.Loaded);
    }

    private void OnStageSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.InspectorVisible)) UpdateStageLayout();
        if (e.PropertyName == nameof(MainWindowViewModel.DockVisibilityMode)) UpdateDockMode();
        if (!_stageReady || e.PropertyName != nameof(MainWindowViewModel.ActiveSession)) return;
        var generation = ++_selectionGeneration;
        // SyncActive finishes binding the new card before we locate its visual.
        Dispatcher.UIThread.Post(() =>
        {
            if (!_stageReady || generation != _selectionGeneration || !IsVisible) return;
            if (Vm.ActiveCard is { } active) SessionShelf.ScrollIntoView(active);
            // Apply a shelf scroll before reading the card's visible origin.
            SessionShelf.UpdateLayout();
            var card = SessionShelf.GetVisualDescendants().OfType<StageCard>()
                .FirstOrDefault(c => ReferenceEquals(c.DataContext, Vm.ActiveCard));
            // Measure in the shared parent: StageWindow itself is transformed
            // during a flight, so translating into it would distort the origin.
            if (card is not null && Vm.ActiveSession is not null)
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
        var inBottomZone = point.Y >= Bounds.Height - 54 &&
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
            ActiveTerminal()?.RevealLine(hit.Line);
    }

    // Session-card drag reorder — a small threshold keeps click→activate intact;
    // the move fires when the pointer actually travels onto another card.
    private SessionCardViewModel? _dragCard;
    private Point _dragOrigin;
    private bool _cardDragging;

    private void OnShelfPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragCard = null;
        _cardDragging = false;
        if (!e.GetCurrentPoint(SessionShelf).Properties.IsLeftButtonPressed) return;
        _dragCard = (e.Source as Control)?.FindAncestorOfType<StageCard>(includeSelf: true)
            ?.DataContext as SessionCardViewModel;
        _dragOrigin = e.GetPosition(SessionShelf);
    }

    private void OnShelfPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragCard is null) return;
        var pos = e.GetPosition(SessionShelf);
        if (!_cardDragging && Math.Abs(pos.Y - _dragOrigin.Y) + Math.Abs(pos.X - _dragOrigin.X) < 12)
            return;
        _cardDragging = true;
        var target = (SessionShelf.InputHitTest(pos) as Visual)
            ?.FindAncestorOfType<StageCard>(includeSelf: true)
            ?.DataContext as SessionCardViewModel;
        Vm.MoveSessionCard(_dragCard, target);
    }

    private void OnShelfPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragCard = null;
        _cardDragging = false;
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
        // Grok uses F2 for settings; terminal-focused function keys belong to the CLI.
        if (e.Key == Key.F2 && e.KeyModifiers == KeyModifiers.None
            && e.Source is not (TextBox or TerminalView)) { OnRenameActive(sender, e); return; }
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (e.Key == Key.Tab)
        {
            Vm.CycleSession(shift ? -1 : +1);
            e.Handled = true;
        }
        // Font zoom lives on Ctrl+non-letter keys — the bare Ctrl+A..Z control
        // bytes below keep flowing to the shell untouched.
        else if (e.Key is Key.OemPlus or Key.Add) { Vm.AdjustFontSize(+1); e.Handled = true; }
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

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _stageReady = false;
        Vm.PropertyChanged -= OnStageSelectionChanged;
        ++_selectionGeneration;
        _dockHideTimer.Stop();
        Vm.Dispose(); // persists settings + kills PTYs
        base.OnClosing(e);
    }
}
