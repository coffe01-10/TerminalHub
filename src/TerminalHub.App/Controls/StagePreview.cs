namespace TerminalHub.App.Controls;

/// <summary>Full-grid live mirror, sharing the main renderer without changing PTY size or focus.</summary>
public sealed class StagePreview : TerminalView
{
    public StagePreview()
    {
        IsPreview = true;
        Focusable = false;
        IsHitTestVisible = false;
    }
}
