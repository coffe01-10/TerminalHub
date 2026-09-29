using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TerminalHub.App.ViewModels;
using TerminalHub.Core.Sessions;

namespace TerminalHub.App.Views;

/// <summary>Standalone popout window hosting one detached session's terminal.
/// Closing it hands the session back to the main window (via the VM hook).</summary>
public partial class SessionWindow : Window
{
    /// <summary>Parameterless ctor for the XAML designer.</summary>
    public SessionWindow() => InitializeComponent();

    public SessionWindow(TerminalSessionModel session, double fontSize)
    {
        InitializeComponent();
        if (!OperatingSystem.IsWindows())
            TransparencyLevelHint = new[] { WindowTransparencyLevel.None };
        DataContext = new SessionWindowViewModel(session, fontSize);
        Opened += (_, _) => PopoutTerminal.Focus();
        // The emulator outlives the popout — drop the VM's event subscriptions
        // so each close doesn't leak a dead VM (and its queued UI posts).
        Closed += (_, _) => (DataContext as IDisposable)?.Dispose();
    }

    /// <summary>The session this window is showing (null only for the designer ctor).</summary>
    public TerminalSessionModel? Session => (DataContext as SessionWindowViewModel)?.Model;

    /// <summary>The embedded terminal view (tests poke at this).</summary>
    public TerminalHub.App.Controls.TerminalView Terminal => PopoutTerminal;

    private void OnReturnClick(object? sender, RoutedEventArgs e) => Close();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape) { Close(); e.Handled = true; }
    }
}
