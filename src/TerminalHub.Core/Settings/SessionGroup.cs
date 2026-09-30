namespace TerminalHub.Core.Settings;

public sealed class SessionGroup : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    private string _name = "";
    public string Name
    {
        get => _name;
        set { _name = value ?? ""; PropertyChanged?.Invoke(this, new(nameof(Name))); }
    }
    private bool _collapsed;
    public bool Collapsed
    {
        get => _collapsed;
        set { _collapsed = value; PropertyChanged?.Invoke(this, new(nameof(Collapsed))); }
    }
}
