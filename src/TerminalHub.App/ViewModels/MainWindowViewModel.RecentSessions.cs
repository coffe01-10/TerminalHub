using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using TerminalHub.Core.Sessions;
using TerminalHub.Core.Settings;

namespace TerminalHub.App.ViewModels;

public sealed record RecentSessionEntry(TerminalSessionModel Session, string Workspace)
{
    public string Name => Session.Name;
    public TerminalHub.Core.Terminal.TerminalEmulator Emulator => Session.Emulator;
}

public partial class MainWindowViewModel
{
    private readonly List<TerminalSessionModel> _recentSessions = [];
    public ObservableCollection<RecentSessionEntry> RecentCandidates { get; } = [];
    [ObservableProperty] private bool _recentSwitcherOpen;
    [ObservableProperty] private RecentSessionEntry? _selectedRecent;
    private KeyModifiers _recentModifiers;
    private TerminalSessionModel? _recentOrigin;
    public event Action<bool>? RecentSwitcherFinished;
    public event Action? RecentSwitcherHostRequested;

    private void RememberRecentSession(TerminalSessionModel? session)
    {
        if (session is null || RecentSwitcherOpen) return;
        _recentSessions.Remove(session); _recentSessions.Insert(0, session);
    }

    private bool MatchesRecent(KeyEventArgs e, out SessionShortcutViewModel? shortcut)
    {
        shortcut = SessionShortcuts.FirstOrDefault(s => s.Binding.Action == SessionShortcutAction.Recent
            && s.Error.Length == 0 && s.ParsedGesture is not null);
        return shortcut?.ParsedGesture is { } gesture && gesture.Matches(new KeyEventArgs
        { Key = e.Key, KeyModifiers = e.KeyModifiers & ~KeyModifiers.Shift });
    }

    public bool HandleRecentKeyDown(KeyEventArgs e, TerminalSessionModel? origin = null)
    {
        if (!RecentSwitcherOpen && e.Source is TerminalHub.App.Controls.ShortcutEditor) return false;
        var matches = MatchesRecent(e, out var shortcut);
        if (!RecentSwitcherOpen)
        {
            if (!matches) return false;
            var live = _sessions.Sessions.Concat(DetachedSessions).ToHashSet();
            _recentSessions.RemoveAll(s => !live.Contains(s));
            RecentCandidates.Clear();
            foreach (var session in _recentSessions.Concat(live).Distinct())
                RecentCandidates.Add(new(session, _sessionWorkspaces.GetValueOrDefault(session.Id)?.Name ?? ""));
            if (RecentCandidates.Count == 0) return true;
            _recentModifiers = shortcut!.ParsedGesture!.KeyModifiers & ~KeyModifiers.Shift;
            _recentOrigin = origin ?? ActiveSession;
            SelectedRecent = RecentCandidates.FirstOrDefault(c => c.Session == _recentOrigin) ?? RecentCandidates[0];
            RecentSwitcherOpen = true;
            if (origin?.Detached == true) RecentSwitcherHostRequested?.Invoke();
        }
        if (e.Key == Key.Escape) { FinishRecentSwitcher(false); return true; }
        if (e.Key == Key.Enter) { FinishRecentSwitcher(true); return true; }
        if (matches || e.Key is Key.Up or Key.Down)
        {
            var direction = e.Key == Key.Up || (e.KeyModifiers & KeyModifiers.Shift) != 0 ? -1 : 1;
            var index = SelectedRecent is null ? 0 : RecentCandidates.IndexOf(SelectedRecent);
            SelectedRecent = RecentCandidates[(index + direction + RecentCandidates.Count) % RecentCandidates.Count];
        }
        // The overlay owns every key until confirmation, so no preview input reaches a PTY.
        return true;
    }

    public bool HandleRecentKeyUp(KeyEventArgs e)
    {
        if (!RecentSwitcherOpen) return false;
        if ((e.KeyModifiers & _recentModifiers) != _recentModifiers) FinishRecentSwitcher(true);
        return true;
    }

    public void FinishRecentSwitcher(bool confirm)
    {
        if (!RecentSwitcherOpen) return;
        var target = confirm ? SelectedRecent?.Session : _recentOrigin;
        RecentSwitcherOpen = false;
        if (target is not null)
        {
            if (_popouts.FirstOrDefault(w => w.Session == target) is { } window)
            {
                if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                window.Show(); window.Activate(); window.Terminal.Focus(); RememberRecentSession(target);
            }
            else if (confirm && _sessions.Sessions.Contains(target)) { ActivateSearchSession(target); SyncActive(); }
        }
        RecentCandidates.Clear(); SelectedRecent = null;
        _recentOrigin = null;
        RecentSwitcherFinished?.Invoke(target?.Detached == true);
    }

    private void RemoveRecentSession(TerminalSessionModel session)
    {
        _recentSessions.Remove(session);
        var candidate = RecentCandidates.FirstOrDefault(c => c.Session == session);
        if (candidate is null) return;
        var index = RecentCandidates.IndexOf(candidate);
        RecentCandidates.Remove(candidate);
        if (RecentCandidates.Count == 0) FinishRecentSwitcher(false);
        else if (SelectedRecent == candidate) SelectedRecent = RecentCandidates[Math.Min(index, RecentCandidates.Count - 1)];
    }
}
