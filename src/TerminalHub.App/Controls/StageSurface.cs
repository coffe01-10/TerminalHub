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
        new DoubleTransition { Property = RevealProperty, Duration = TimeSpan.FromMilliseconds(540), Easing = new CubicEaseInOut() }
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
        var progress = Reveal;
        if (progress == 0) { _transform.Matrix = _from; return; }
        if (progress == 1) { _transform.Matrix = Matrix.Identity; return; }
        var remaining = 1 - progress;
        // Grow vertically first, then let the width catch up, like a Dock
        // restore. The entire terminal is transformed; its PTY grid stays fixed.
        var vertical = Math.Pow(remaining, 1.35);
        var flight = new Matrix(1 + (_from.M11 - 1) * remaining, _from.M12 * remaining, _from.M13 * remaining,
            _from.M21 * remaining, 1 + (_from.M22 - 1) * vertical, _from.M23 * remaining,
            _from.M31 * remaining, _from.M32 * vertical, 1 + (_from.M33 - 1) * remaining);
        // Briefly taper the edge nearest the shelf during the flight. This
        // gives the restore a Dock-like unfolding shape instead of a flat zoom.
        // The envelope is zero at both ends; interrupted flights retain their
        // complete projective pose rather than snapping back to a rectangle.
        var taper = progress > 0 && progress < 1 ? .18 * Math.Pow(Math.Sin(Math.PI * progress), 2) : 0;
        _transform.Matrix = Bounds.Width > 0 && taper > 0
            ? new Matrix(1 - taper, -Bounds.Height * taper / (2 * Bounds.Width), -taper / Bounds.Width,
                0, 1 - taper, 0, 0, Bounds.Height * taper / 2, 1) * flight
            : flight;
    }

    public void FinishActivation()
    {
        Transitions = null;
        Reveal = 1;
        UpdateTransform();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Transitions = null;
        Reveal = 1;
        base.OnDetachedFromVisualTree(e);
    }
}
