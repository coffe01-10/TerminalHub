using TerminalHub.Extensibility;

namespace TerminalHub.App.Plugins;

/// <summary>Host-side <see cref="IWorkbenchPlugin"/> for declarative manifests:
/// a plugin.json with commands and no entry assembly. Each declared command
/// registers like a compiled plugin's command; running it spawns a terminal
/// session with the expanded command line so output stays visible.
/// </summary>
internal sealed class ManifestPlugin(PluginManifest manifest, string directory) : IWorkbenchPlugin
{
    public void Initialize(IPluginContext context)
    {
        foreach (var command in manifest.Commands)
        {
            var declared = command;
            context.RegisterCommand(new PluginCommand(declared.Id,
                string.IsNullOrWhiteSpace(declared.Title) ? declared.Id : declared.Title,
                () => RunAsync(declared, context), declared.Gesture, declared.Description));
        }
    }

    internal static string Expand(string run, string cwd, string session, string workspace, string directory)
        => run.Replace("{cwd}", cwd, StringComparison.OrdinalIgnoreCase)
            .Replace("{session}", session, StringComparison.OrdinalIgnoreCase)
            .Replace("{workspace}", workspace, StringComparison.OrdinalIgnoreCase)
            .Replace("{dir}", directory, StringComparison.OrdinalIgnoreCase);

    private async Task RunAsync(ManifestCommand command, IPluginContext context)
    {
        var host = context.Host;
        var active = host.Sessions.FirstOrDefault(s => s.Id == host.ActiveSessionId);
        var workspace = host.Workspaces.FirstOrDefault(w => w.Active)?.Name ?? "";
        var cwd = active?.WorkingDirectory ?? "";
        var run = Expand(command.Run, cwd, active?.Name ?? "", workspace, directory);
        if (string.IsNullOrWhiteSpace(run)) return;
        var (shell, arguments) = OperatingSystem.IsWindows()
            ? ("cmd.exe", "/d /s /c \"" + run + "\"")
            : ("sh", "-c '" + run.Replace("'", "'\"'\"'") + "'");
        var title = string.IsNullOrWhiteSpace(command.Title) ? command.Id : command.Title;
        await host.CreateSessionAsync(new NewSessionRequest(title, cwd, shell, arguments));
    }

    public void Deactivate() { }
}
