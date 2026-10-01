using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;

namespace TerminalHub.App.Controls;

/// <summary>A bottom-anchored capsule grows into the tool dock without resizing its content.</summary>
public sealed class DropletDock : Decorator
{
    public static readonly StyledProperty<IBrush?> BackgroundProperty = Border.BackgroundProperty.AddOwner<DropletDock>();
    public static readonly StyledProperty<IBrush?> BorderBrushProperty = Border.BorderBrushProperty.AddOwner<DropletDock>();
    public IBrush? Background { get => GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }
    public IBrush? BorderBrush { get => GetValue(BorderBrushProperty); set => SetValue(BorderBrushProperty, value); }
    public static readonly StyledProperty<double> RevealProperty =
        AvaloniaProperty.Register<DropletDock, double>(nameof(Reveal));
    public double Reveal { get => GetValue(RevealProperty); set => SetValue(RevealProperty, value); }
    private readonly TranslateTransform _motion = new();
    static DropletDock() => AffectsRender<DropletDock>(BackgroundProperty, BorderBrushProperty);
    public DropletDock()
    {
        Transitions = new Transitions { new DoubleTransition { Property = RevealProperty,
            Duration = TimeSpan.FromMilliseconds(420), Easing = new StageSpringEase() } };
        IsHitTestVisible = false;
    }
    public Rect SurfaceBounds
    {
        get
        {
            var p = Math.Clamp(Reveal, 0, 1);
            var w = Math.Min(82, Bounds.Width);
            var h = Math.Min(30, Bounds.Height);
            w += (Bounds.Width - w) * Math.Pow(p, 1.25);
            h += (Bounds.Height - h) * (1 - Math.Pow(1 - p, 1.7));
            return new Rect((Bounds.Width - w) / 2, Bounds.Height - h, w, h);
        }
    }
    private double Radius => 15 + 9 * Math.Sin(Math.PI * Math.Clamp(Reveal, 0, 1));
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ChildProperty && Child is { } child) child.RenderTransform = _motion;
        if (change.Property == RevealProperty || change.Property == BoundsProperty || change.Property == ChildProperty)
        {
            var p = Math.Clamp(Reveal, 0, 1);
            Clip = new RectangleGeometry(SurfaceBounds, Radius, Radius);
            if (Child is { } content) content.Opacity = Math.Clamp((p - .25) / .65, 0, 1);
            _motion.Y = 12 * (1 - p);
            IsHitTestVisible = p > .65;
            InvalidateVisual();
        }
    }
    public override void Render(DrawingContext context)
    {
        var pen = BorderBrush is { } brush ? new Pen(brush, 1) : null;
        context.DrawRectangle(Background, pen, SurfaceBounds.Deflate(.5), Radius, Radius);
    }
}
