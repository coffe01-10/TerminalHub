using CommunityToolkit.Mvvm.ComponentModel;
using TerminalHub.Core.Settings;
using TerminalHub.Core.Sessions;

namespace TerminalHub.App.ViewModels;

public partial class BroadcastTarget : ViewModelBase
{
    public SessionCardViewModel Card { get; }
    public string Name => Card.Name;
    [ObservableProperty] private bool _selected;
    public BroadcastTarget(SessionCardViewModel card) => Card = card;
}

public partial class OutputRuleViewModel : ViewModelBase
{
    public string Id { get; }
    [ObservableProperty] private string _name;
    [ObservableProperty] private string _pattern;
    [ObservableProperty] private bool _isRegex;
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private bool _notify;
    [ObservableProperty] private string _color;
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private int _matchCount;
    public OutputRuleViewModel(OutputRule rule)
    { Id = rule.Id; _name = rule.Name; _pattern = rule.Pattern; _isRegex = rule.IsRegex; _enabled = rule.Enabled; _notify = rule.Notify; _color = rule.Color; }
    public OutputRule Snapshot() => new() { Id = Id, Name = Name, Pattern = Pattern, IsRegex = IsRegex, Enabled = Enabled, Notify = Notify, Color = Color };
}

public sealed record OutputRuleResult(string RuleName, SessionSearchResult Result)
{
    public string SessionName => Result.SessionName;
    public string Text => Result.Text;
}

public partial class ProjectTaskViewModel : ViewModelBase
{
    public string Id { get; }
    public string WorkspaceId { get; }
    [ObservableProperty] private string _name;
    [ObservableProperty] private string _command;
    [ObservableProperty] private string _sessionName;
    [ObservableProperty] private string _status = "未运行";
    [ObservableProperty] private bool _running;
    public TerminalSessionModel? RunningSession { get; set; }
    public DateTimeOffset Started { get; set; }
    public ProjectTaskViewModel(ProjectTaskDefinition task)
    { Id = task.Id; WorkspaceId = task.WorkspaceId; _name = task.Name; _command = task.Command; _sessionName = task.SessionName; }
    public ProjectTaskDefinition Snapshot() => new() { Id = Id, WorkspaceId = WorkspaceId, Name = Name, Command = Command, SessionName = SessionName };
}
