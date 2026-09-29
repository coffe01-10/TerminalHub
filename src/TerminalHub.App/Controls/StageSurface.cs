using Avalonia;
using Avalonia.Animation;
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
        new DoubleTransition { Property = RevealProperty, Duration = TimeSpan.FromMilliseconds(560), Easing = new StageSpringEase() }
    };
    private double _originY;

    public double Reveal { get => GetValue(RevealProperty); set => SetValue(RevealProperty, value); }

    public void ActivateFrom(double originY)
    {
        if (this.GetVisualRoot() is null) return;
        // Rapid clicks continue the existing flight rather than jumping back to its first frame.
        if (Math.Abs(Reveal - 1) > .0001) return;
        _originY = Math.Clamp(originY, -120, 120);
        Transitions = null;
        Reveal = 0;
        Transitions = _motion;
        Reveal = 1;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != RevealProperty) return;
        var remaining = 1 - Reveal;
        RenderTransform = new MatrixTransform(new Matrix(1 - .085 * remaining, 0, 0,
            1 - .065 * remaining, -58 * remaining, _originY * remaining));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Transitions = null;
        Reveal = 1;
        base.OnDetachedFromVisualTree(e);
    }
}
