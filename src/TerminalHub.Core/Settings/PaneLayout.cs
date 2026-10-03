namespace TerminalHub.Core.Settings;

/// <summary>A branch divides space; a leaf refers to the saved session list.</summary>
public sealed class PaneLayout
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int SessionIndex { get; set; } = -1;
    public bool Vertical { get; set; }
    public double Ratio { get; set; } = .5;
    public PaneLayout? First { get; set; }
    public PaneLayout? Second { get; set; }
}
