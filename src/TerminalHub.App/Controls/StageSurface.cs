using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace TerminalHub.App.Controls;

/// <summary>Animates the whole foreground surface, without relaying out its terminal.</summary>
public sealed class StageSurface : Border
{
    public static readonly StyledProperty<double> RevealProperty =
        AvaloniaProperty.Register<StageSurface, double>(nameof(Reveal), 1);
    private readonly Transitions _motion = new()
    {
        new DoubleTransition { Property = RevealProperty, Duration = TimeSpan.FromMilliseconds(460), Easing = new CubicEaseOut() }
    };
    private readonly MatrixTransform _transform = new(Matrix.Identity);
    private Matrix _from = Matrix.Identity;

    public StageSurface()
    {
        RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
        RenderTransform = _transform;
    }

    public double Reveal { get => GetValue(RevealProperty); set => SetValue(RevealProperty, value); }

    public void ActivateFrom(Rect thumbnail)
    {
        if (this.GetVisualRoot() is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        // Continue from the currently painted pose when another session is picked
        // mid-flight. A fresh flight maps the full surface onto the actual card.
        _from = Math.Abs(Reveal - 1) > .0001 ? _transform.Matrix :
            new Matrix(thumbnail.Width / Bounds.Width, 0, 0, thumbnail.Height / Bounds.Height,
                thumbnail.X, thumbnail.Y);
        Transitions = null;
        Reveal = 0;
        UpdateTransform();
        Transitions = _motion;
        Reveal = 1;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != RevealProperty) return;
        UpdateTransform();
    }

    private void UpdateTransform()
    {
        var remaining = 1 - Reveal;
        _transform.Matrix = new Matrix(1 + (_from.M11 - 1) * remaining, 0, 0,
            1 + (_from.M22 - 1) * remaining, _from.M31 * remaining, _from.M32 * remaining);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Transitions = null;
        Reveal = 1;
        base.OnDetachedFromVisualTree(e);
    }
}
