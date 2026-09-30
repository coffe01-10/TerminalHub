using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHub.Core.Sessions;
using TerminalHub.Core.Terminal;

namespace TerminalHub.App.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty] private string _notificationText = "";
    [ObservableProperty] private bool _notificationVisible;
    private TerminalSessionModel? _notificationSession;
    private void OnCommandCompleted(TerminalSessionModel session, ShellCommandState command)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || !_settings.NotifyCommandCompletion || !session.IsRunning) return;
            if (IsSplit ? session == LeftPane || session == RightPane : session == ActiveSession) return;
            _notificationSession = session;
            NotificationText = $"{session.Name} · {(command.ExitCode is null ? "命令结束" : command.ExitCode == 0 ? "命令完成" : $"命令失败（{command.ExitCode}）")} · {command.Duration.TotalSeconds:0.0}s";
            // A single card is updated rather than stacking every completion.
            NotificationVisible = true;
        });
    }
    [RelayCommand] private void DismissNotification() => NotificationVisible = false;
    [RelayCommand] private void ShowNotificationSession()
    {
        if (_notificationSession is { Detached: true } detached)
            _popouts.FirstOrDefault(p => p.Session == detached)?.Activate();
        else if (_notificationSession is { } session)
            ActiveCard = SessionCards.FirstOrDefault(c => c.Model == session);
        NotificationVisible = false;
    }
}
