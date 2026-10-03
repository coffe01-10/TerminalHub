namespace TerminalHub.Core.Settings;

public sealed class PluginSettings
{
    public bool Enabled { get; set; }
    public string Configuration { get; set; } = "";
}
public sealed class ModuleSettings
{
    public bool Visible { get; set; } = true;
    public int? Order { get; set; }
    // Missing workspace entries mean enabled. A disabled view does not stop the host task.
    public Dictionary<string, bool> Workspaces { get; set; } = [];
}
