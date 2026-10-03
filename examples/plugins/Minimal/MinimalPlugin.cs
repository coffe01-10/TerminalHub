using Avalonia.Controls;
using TerminalHub.Extensibility;
public sealed class MinimalPlugin : IWorkbenchPlugin
{
    public void Initialize(IPluginContext context)
    {
        context.RegisterView(new("hello", "Hello plugin"), () => new TextBlock { Text = "Hello Terminal Hub", Margin = new(16) });
        context.RegisterCommand(new("new-terminal", "Plugin: new terminal", async () => { await context.Host.CreateSessionAsync(new()); }));
    }
    public void Deactivate() { }
}
