using System.Collections.Concurrent;
using TerminalHub.Core.Sessions;
using TerminalHub.Core.Settings;
using TerminalHub.Core.Terminal;
using TerminalHub.Extensibility;

namespace TerminalHub.App.ViewModels;

public partial class MainWindowViewModel : IWorkbenchHost
{
    public Dictionary<string, PluginSettings> PluginPreferences => _settings.Plugins;
    public Dictionary<string, ModuleSettings> ModulePreferences => _settings.Modules;
    public event Action<WorkbenchEvent>? WorkbenchChanged;
    private readonly ConcurrentDictionary<Guid, byte> _pluginDirtySessions = new();
    private IEnumerable<TerminalSessionModel> PluginSessions => _sessions.Snapshot.Concat(DetachedSessions).Distinct();
    IReadOnlyList<SessionInfo> IWorkbenchHost.Sessions => PluginSessions.Select(s => new SessionInfo(s.Id, s.Name,
        s.WorkingDirectory, s.Shell, _sessionWorkspaces.GetValueOrDefault(s.Id)?.Id ?? "", s.IsRunning, s.Detached)).ToArray();
    IReadOnlyList<WorkspaceInfo> IWorkbenchHost.Workspaces => ProjectWorkspaces.Select(w => new WorkspaceInfo(w.Id,w.Name,w == ActiveWorkspace,w.Count)).ToArray();
    Guid? IWorkbenchHost.ActiveSessionId => _sessions.Active?.Id;
    string IWorkbenchHost.ActiveWorkspaceId => ActiveWorkspace.Id;
    TerminalFrame? IWorkbenchHost.ReadFrame(Guid id, int offset)
    {
        var frame = PluginSessions.FirstOrDefault(s => s.Id == id)?.Emulator.Buffer.CaptureFrame(offset);
        // A plugin may edit the array it receives; never expose the renderer's cached frame.
        return frame is null ? null : frame with { Cells = frame.Cells.ToArray() };
    }
    async Task<Guid?> IWorkbenchHost.CreateSessionAsync(NewSessionRequest request)
        => (await CreateSessionAsync(request.Name, SessionTag.Dev, request.WorkingDirectory, shellCommand: request.Shell, arguments: request.Arguments))?.Id;
    void IWorkbenchHost.ActivateSession(Guid id)
    {
        if (PluginSessions.FirstOrDefault(s => s.Id == id) is not { } session) return;
        if (session.Detached) { _popouts.FirstOrDefault(w => w.Session == session)?.Activate(); return; }
        ActivateSearchSession(session);
    }
    async Task<string> IWorkbenchHost.CreateWorkspaceAsync(string name)
    { await NewProjectWorkspace(); ActiveWorkspace.Name = name; SaveSettingsInternal(); return ActiveWorkspace.Id; }
    void IWorkbenchHost.SwitchWorkspace(string id) => SwitchProjectWorkspace(ProjectWorkspaces.FirstOrDefault(w => w.Id == id));
    void IWorkbenchHost.MoveSession(Guid id, string workspaceId)
    {
        if (AllSessionCards.FirstOrDefault(c => c.Model.Id == id) is { } card && ProjectWorkspaces.FirstOrDefault(w => w.Id == workspaceId) is { } workspace)
            MoveSessionToWorkspace(card, workspace);
    }
    Task IWorkbenchHost.SplitAsync(bool vertical, Guid? sessionId, bool before)
        => sessionId is { } id && AllSessionCards.FirstOrDefault(c => c.Model.Id == id) is { } card
            ? DropSessionOnPaneAsync(card,FocusedPane,vertical ? before ? "Top" : "Bottom" : before ? "Left" : "Right")
            : SplitPaneAsync(FocusedPane,vertical);
    void IWorkbenchHost.SendInput(Guid id, string text, bool submit)
    {
        if (PluginSessions.FirstOrDefault(s => s.Id == id) is not { } session) return;
        session.Emulator.PasteText(text); if (submit) session.Emulator.SendText("\r");
    }
    public void SavePluginPreferences() => SaveSettingsInternal();
    private void NotifyWorkbench(WorkbenchEventKind kind, TerminalSessionModel? session = null, object? data = null)
        => WorkbenchChanged?.Invoke(new(kind, session?.Id, session is null ? ActiveWorkspace.Id : _sessionWorkspaces.GetValueOrDefault(session.Id)?.Id, data));
    public void FlushPluginOutput()
    {
        var ids = _pluginDirtySessions.Keys.Where(id => _pluginDirtySessions.TryRemove(id, out _)).ToArray();
        if (ids.Length > 0) NotifyWorkbench(WorkbenchEventKind.OutputBatch, data: ids);
    }
}
