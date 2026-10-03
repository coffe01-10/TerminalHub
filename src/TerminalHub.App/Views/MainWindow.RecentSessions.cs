using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace TerminalHub.App.Views;

public partial class MainWindow
{
    private IInputElement? _recentPreviousFocus;
    private void InitializeRecentSwitcher()
    {
        Vm.RecentSwitcherHostRequested += () =>
        { if (WindowState == Avalonia.Controls.WindowState.Minimized) WindowState = Avalonia.Controls.WindowState.Normal; Show(); Activate(); };
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (Vm.RecentSwitcherOpen && !new Avalonia.Rect(RecentSwitcherPanel.Bounds.Size).Contains(e.GetPosition(RecentSwitcherPanel)))
                Vm.FinishRecentSwitcher(false);
        }, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, (_, e) =>
        { if (Vm.HandleRecentKeyUp(e)) e.Handled = true; }, RoutingStrategies.Tunnel);
        AddHandler(TextInputEvent, (_, e) =>
        { if (Vm.RecentSwitcherOpen) e.Handled = true; }, RoutingStrategies.Tunnel);
        Vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Vm.RecentSwitcherOpen) && Vm.RecentSwitcherOpen)
            {
                _recentPreviousFocus = FocusManager?.GetFocusedElement();
                Dispatcher.UIThread.Post(() => { if (Vm.RecentSwitcherOpen) RecentSessionList.Focus(); }, DispatcherPriority.Loaded);
            }
            if (e.PropertyName == nameof(Vm.SelectedRecent) && Vm.SelectedRecent is { } selected)
                Dispatcher.UIThread.Post(() => RecentSessionList.ScrollIntoView(selected));
        };
        Vm.RecentSwitcherFinished += popout =>
        {
            if (!popout) (ActiveTerminal() ?? _recentPreviousFocus)?.Focus();
            _recentPreviousFocus = null;
        };
        Deactivated += (_, _) => Vm.FinishRecentSwitcher(false);
    }
    private void OnRecentDoubleTapped(object? sender, TappedEventArgs e) => Vm.FinishRecentSwitcher(true);
}
