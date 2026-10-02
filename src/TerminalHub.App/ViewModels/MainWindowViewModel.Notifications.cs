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
    private int _notificationSeq;

    /// <summary>Single-slot banner: newest notification replaces the old one and
    /// auto-hides after 8s (seq guards a stale timer from killing a newer banner).</summary>
    private void ShowNotification(TerminalSessionModel session, string text)
    {
        _notificationSession = session;
        NotificationText = text;
        NotificationVisible = true;
        var seq = ++_notificationSeq;
        Avalonia.Threading.DispatcherTimer.RunOnce(() =>
        {
            if (!_disposed && seq == _notificationSeq) NotificationVisible = false;
        }, TimeSpan.FromSeconds(8));
    }

    private void OnCommandCompleted(TerminalSessionModel session, ShellCommandState command)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || !_settings.NotifyCommandCompletion || !session.IsRunning) return;
            if (IsSplit ? session == LeftPane || session == RightPane : session == ActiveSession) return;
            ShowNotification(session,
                $"{session.Name} · {(command.ExitCode is null ? "命令结束" : command.ExitCode == 0 ? "命令完成" : $"命令失败（{command.ExitCode}）")} · {command.Duration.TotalSeconds:0.0}s");
        });
    }
    /// <summary>PTY BEL: a bell on a background pane/session surfaces the same
    /// banner as command completion (its「查看终端」jumps to the source) and
    /// flags the shelf card unread. A bell in the visible terminal is already
    /// on screen — no banner.</summary>
    private void OnSessionBell(TerminalSessionModel s)
    {
        if (_disposed) return;
        if (IsSplit ? s == LeftPane || s == RightPane : s == ActiveSession) return;
        ShowNotification(s, $"🔔 {s.Name} · 终端响铃");
        if (SessionCards.FirstOrDefault(c => ReferenceEquals(c.Model, s)) is { } card)
            card.HasUnreadOutput = true;
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
