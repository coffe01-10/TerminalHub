namespace TerminalHub.Core.Settings;

public sealed class ProjectWorkspaceState
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "工作区";
    public WorkspaceState Layout { get; set; } = new();
    public bool ShelfAutoHide { get; set; }
    public double ShelfWidth { get; set; }
}

public sealed class OutputRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新规则";
    public string Pattern { get; set; } = "";
    public bool IsRegex { get; set; }
    public bool Enabled { get; set; } = true;
    public bool Notify { get; set; }
    public string Color { get; set; } = "#FBBF24";
}

public sealed class ProjectTaskDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string WorkspaceId { get; set; } = "";
    public string Name { get; set; } = "新任务";
    public string Command { get; set; } = "";
    public string SessionName { get; set; } = "";
}
