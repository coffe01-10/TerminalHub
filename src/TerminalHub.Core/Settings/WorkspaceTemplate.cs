namespace TerminalHub.Core.Settings;

public sealed class WorkspaceTemplate : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    private string _name = "";
    public string Name
    {
        get => _name;
        set { _name = value; PropertyChanged?.Invoke(this, new(nameof(Name))); }
    }
    public WorkspaceState Layout { get; set; } = new();
    public DateTimeOffset LastUsed { get; set; }
}
