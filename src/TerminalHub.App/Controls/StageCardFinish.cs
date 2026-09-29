using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace TerminalHub.App.Controls;

/// <summary>Theme-specific shelf details, absent on the expanded session.</summary>
public sealed class StageCardFinish : Control
{
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ThemeManager.Changed += InvalidateVisual;
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ThemeManager.Changed -= InvalidateVisual;
        base.OnDetachedFromVisualTree(e);
    }
    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (ThemeManager.Current == "Paper")
        {
            var edge = new Pen(ThemeManager.Brush("Border"), 1);
            context.DrawLine(edge, new Point(5, height + 6), new Point(width - 5, height + 6));
            context.DrawLine(edge, new Point(2, height + 3), new Point(width - 2, height + 3));
        }
        if (width > 28 && height > 28)
        {
            if (ThemeManager.Current == "DarkGlass")
            {
                // A reflected rim belongs to the glass shelf, not the live window.
                using (context.PushOpacity(.38))
                    context.DrawLine(new Pen(ThemeManager.Brush("Accent"), 1), new Point(14, 1), new Point(width - 14, 1));
                using (context.PushOpacity(.12))
                    context.DrawLine(new Pen(ThemeManager.Brush("Accent"), 2), new Point(18, height + 4), new Point(width - 18, height + 4));
            }
            else if (ThemeManager.Current == "Black")
            {
                using (context.PushOpacity(.5))
                    context.DrawRectangle(null, new Pen(ThemeManager.Brush("Border"), 1), new Rect(3, 3, width - 6, height - 6), 5, 5);
            }
        }
    }

}
