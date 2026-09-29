using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace TerminalHub.App.Controls;

public sealed class WorkspaceBackdrop : Control
{
    public WorkspaceBackdrop() => IsHitTestVisible = false;
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
    public override void Render(DrawingContext ctx)
    {
        ctx.DrawRectangle(ThemeManager.Brush("Canvas"), null, new Rect(Bounds.Size));
        if (ThemeManager.Current == "Paper")
        {
            var rule = new Pen(new SolidColorBrush(Color.Parse("#18756242")), 1);
            for (var y = 80d; y < Bounds.Height; y += 28)
                ctx.DrawLine(rule, new Point(0, y), new Point(Bounds.Width, y));
            ctx.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#30A9684C")), 1), new Point(24, 0), new Point(24, Bounds.Height));
        }
        else if (ThemeManager.Current == "White")
        {
            ctx.DrawEllipse(new SolidColorBrush(Color.Parse("#30FFFFFF")), null,
                new Point(Bounds.Width * .8, 0), Bounds.Width * .6, Bounds.Height * .9);
        }
        else if (ThemeManager.Current == "DarkGlass")
        {
            using var transform = ctx.PushTransform(Matrix.CreateScale(Bounds.Width / 1440, Bounds.Height / 900));
            ctx.DrawGeometry(new SolidColorBrush(Color.Parse("#243E59")), null,
                Geometry.Parse("M 340,-100 C 1250,-180 520,610 1560,680 L 1580,990 C 410,850 720,160 120,-60 Z"));
            ctx.DrawGeometry(new SolidColorBrush(Color.Parse("#243A54")), null,
                Geometry.Parse("M 1350,-120 C 760,190 1200,550 480,990 L 790,990 C 1430,560 930,150 1590,-60 Z"));
        }
    }
}
