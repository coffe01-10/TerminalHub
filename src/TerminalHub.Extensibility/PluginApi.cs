using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Controls.Primitives;
using TerminalHub.Core.Terminal;

namespace TerminalHub.Extensibility;

public interface IWorkbenchPlugin
{
    void Initialize(IPluginContext context);
    void Deactivate();
}

public sealed record PluginManifest
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Version { get; init; } = "1.0.0";
    /// <summary>Assembly file relative to the plugin directory.</summary>
    public string Entry { get; init; } = "";
    public string EntryType { get; init; } = "";
    public int HostApi { get; init; } = 1;
    /// <summary>Declarative script-plugin commands. A manifest without
    /// <see cref="Entry"/> but with commands is enabled by the built-in
    /// manifest plugin — no assembly is loaded.</summary>
    public List<ManifestCommand> Commands { get; init; } = [];
}

/// <summary>One click-runnable command a declarative plugin contributes.
/// <see cref="Run"/> expands placeholders at execution time: {cwd} the active
/// session's directory, {session} its name, {workspace} the active workspace,
/// {dir} the plugin directory. The expanded line runs inside a new terminal
/// session (cmd /d /s /c on Windows, sh -c elsewhere) so output stays visible.</summary>
public sealed record ManifestCommand
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Run { get; init; } = "";
    public string? Description { get; init; }
    public string? Gesture { get; init; }
}

public enum ExtensionSurface { Toolbar, WorkspaceTabs, Sidebar, StatusBar, Menu, SidePanel, BottomPanel, WorkspaceTools, Settings, ToolWindow }
public sealed record ModuleDefinition(string Id, string Title, ExtensionSurface Surface = ExtensionSurface.WorkspaceTools,
    int Order = 100, bool Replace = false, string? IconSvg = null);
public sealed record PluginCommand(string Id, string Title, Func<Task> Execute, string? Gesture = null, string? Description = null,
    bool ShowInMenu = true);
public sealed record SessionInfo(Guid Id, string Name, string WorkingDirectory, string Shell, string WorkspaceId, bool Running, bool Detached);
public sealed record WorkspaceInfo(string Id, string Name, bool Active, int Sessions);
public sealed record NewSessionRequest(string? Name = null, string WorkingDirectory = "", string? Shell = null, string? Arguments = null);
public enum WorkbenchEventKind { SessionCreated, SessionClosed, ActiveSessionChanged, WorkspaceChanged, CommandStarted, CommandCompleted, OutputBatch, LanguageChanged, WorkspaceRemoved }
public sealed record WorkbenchEvent(WorkbenchEventKind Kind, Guid? SessionId = null, string? WorkspaceId = null, object? Data = null);

/// <summary>All operations run on the UI thread. The host retains ownership of terminal processes.</summary>
public interface IWorkbenchHost
{
    IReadOnlyList<SessionInfo> Sessions { get; }
    IReadOnlyList<WorkspaceInfo> Workspaces { get; }
    Guid? ActiveSessionId { get; }
    string ActiveWorkspaceId { get; }
    TerminalFrame? ReadFrame(Guid sessionId, int historyOffset = 0);
    Task<Guid?> CreateSessionAsync(NewSessionRequest request);
    void ActivateSession(Guid sessionId);
    Task<string> CreateWorkspaceAsync(string name);
    void SwitchWorkspace(string workspaceId);
    void MoveSession(Guid sessionId, string workspaceId);
    Task SplitAsync(bool vertical, Guid? sessionId = null, bool before = false);
    void RemoveFocusedPane();
    void SendInput(Guid sessionId, string text, bool submit = false);
}

/// <summary>Registrations are scoped to this activation and removed on disable.</summary>
public interface IPluginContext
{
    string PluginId { get; }
    string Directory { get; }
    IWorkbenchHost Host { get; }
    CancellationToken Lifetime { get; }
    string Language { get; }
    string Text(string key, string fallback);
    T? ReadConfiguration<T>();
    void SaveConfiguration<T>(T value);
    IDisposable RegisterView(ModuleDefinition module, Func<Control> create);
    IDisposable RegisterCommand(PluginCommand command);
    IDisposable RegisterStyles(IStyle styles);
    IDisposable RegisterResources(IResourceProvider resources);
    IDisposable RegisterLocalization(string defaultLanguage, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> resources);
    IDisposable Subscribe(Action<WorkbenchEvent> callback);
    IDisposable Schedule(TimeSpan interval, Action callback);
    IDisposable Track(IDisposable resource);
    void ReportError(Exception exception);
}
