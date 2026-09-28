using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using TerminalHub.App.ViewModels;

namespace TerminalHub.App.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;

    /// <summary>Inner ScrollViewer of the Logs list; drives the follow-tail state machine.</summary>
    private ScrollViewer? _logsScroll;

    public MainWindow()
    {
        InitializeComponent();
        // Acrylic is Windows-only eye candy; on compositor-less X11 it renders black.
        if (!OperatingSystem.IsWindows())
            TransparencyLevelHint = new[] { Avalonia.Controls.WindowTransparencyLevel.None };
        DataContext = new MainWindowViewModel();
        // Button marks pointer events handled before instance handlers, so listen with handledEventsToo.
        // Press runs before the release click, which is what DockSelectCommand executes.
        DeployDockButton.AddHandler(InputElement.PointerPressedEvent, OnDeployPointerPressed,
            RoutingStrategies.Bubble, handledEventsToo: true);
        DeployDockButton.AddHandler(InputElement.PointerReleasedEvent, OnDeployPointerReleased,
            RoutingStrategies.Bubble, handledEventsToo: true);
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

    /// <summary>「⬇ 跟随」clicked (FollowTail went true) — jump to the newest line.</summary>
    private void OnLogsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LogsViewModel.FollowTail) && Vm.Logs.FollowTail)
            _logsScroll?.ScrollToEnd();
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
