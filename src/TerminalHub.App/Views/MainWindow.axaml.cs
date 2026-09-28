using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TerminalHub.App.ViewModels;

namespace TerminalHub.App.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;

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
