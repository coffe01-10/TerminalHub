using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using TerminalHub.App.ViewModels;
using TerminalHub.Core.Deploy;
using TerminalHub.Core.Settings;

namespace TerminalHub.App.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;

    /// <summary>Inner ScrollViewer of the Logs list; drives the follow-tail state machine.</summary>
    private ScrollViewer? _logsScroll;

    public MainWindow() : this(null)
    {
    }

    public MainWindow(SettingsStore? settingsStore)
    {
        InitializeComponent();
        // Acrylic is Windows-only eye candy; on compositor-less X11 it renders black.
        if (!OperatingSystem.IsWindows())
            TransparencyLevelHint = new[] { Avalonia.Controls.WindowTransparencyLevel.None };
        DataContext = new MainWindowViewModel(settingsStore: settingsStore);
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
        Opened += (_, _) =>
        {
            FitToScreen();
            _ = Vm.SpawnStartupSessionsAsync();
        };
        // The Logs tab may start hidden (IsVisible), so template/loaded race each other —
        // attach idempotently from whichever fires first.
        LogsList.TemplateApplied += (_, _) => AttachLogsScrollViewer();
        LogsList.Loaded += (_, _) => AttachLogsScrollViewer();
        Vm.Logs.PropertyChanged += OnLogsPropertyChanged;
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
        var wa = Screens.ScreenFromWindow(this)?.WorkingArea ?? Screens.Primary?.WorkingArea;
        if (wa is not { } s) return;
        var maxW = s.Width - 24;
        var maxH = s.Height - 24;
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

    /// <summary>Split panes: a press on a pane focuses it — its session becomes
    /// active so the middle input row, Output and Search follow it.</summary>
    private void OnLeftPanePressed(object? sender, PointerPressedEventArgs e) => Vm.FocusPane(0);
    private void OnRightPanePressed(object? sender, PointerPressedEventArgs e) => Vm.FocusPane(1);

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

    /// <summary>Window-level session shortcuts (tunneling, before TerminalView):
    /// Ctrl+W closes the active session; Ctrl+Tab / Ctrl+Shift+Tab cycle cards.</summary>
    private void OnSessionShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (e.Key == Key.W && !shift)
        {
            Vm.CloseActiveSessionCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Tab)
        {
            Vm.CycleSession(shift ? -1 : +1);
            e.Handled = true;
        }
    }

    private void OnCommandInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Vm.SubmitCommandInputCommand.Execute(null);
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
        }
    }

    private void OnAssistantInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Vm.Assistant.SubmitTaskCommand.Execute(null);
            e.Handled = true;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        Vm.Dispose(); // persists settings + kills PTYs
        base.OnClosing(e);
    }
}
