namespace TerminalHub.Core.Terminal;

/// <summary>A line in the scrollback that survives trimming and reflow.
/// Line numbers are rebased when history is dropped or the primary screen rewraps.</summary>
public sealed class BufferAnchor
{
    public int Line { get; set; }
    public int Column { get; set; }
    public long RemovedBaseline { get; set; }
    public bool Alive { get; set; } = true;
    /// <summary>True when the anchor was taken on the alternate screen.
    /// The same line number on the other screen is different text.</summary>
    public bool Alternate { get; set; }
    internal ScreenReflow.Position? Mapped { get; set; }
}
