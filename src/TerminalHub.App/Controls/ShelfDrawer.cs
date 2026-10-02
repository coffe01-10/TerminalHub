using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;

namespace TerminalHub.App.Controls;

/// <summary>Slides the shelf without changing the terminal's layout during a hover reveal.</summary>
public sealed class ShelfDrawer : Border
{
    public static readonly StyledProperty<double> RevealProperty =
        AvaloniaProperty.Register<ShelfDrawer, double>(nameof(Reveal), 1);
    public double Reveal { get => GetValue(RevealProperty); set => SetValue(RevealProperty, value); }
    private readonly TranslateTransform _slide = new();
    private bool _revealed = true;

    public ShelfDrawer()
    {
        RenderTransform = _slide;
        Transitions = new Transitions
        {
            new DoubleTransition { Property = RevealProperty, Duration = TimeSpan.FromMilliseconds(220), Easing = new CubicEaseOut() }
        };
    }

    public void SetRevealed(bool revealed, bool immediate = false)
    {
        _revealed = revealed;
        if (revealed) IsVisible = true;
        // A closing drawer must immediately stop intercepting terminal input.
        IsHitTestVisible = revealed;
        var transitions = Transitions;
        if (immediate) Transitions = null;
        Reveal = revealed ? 1 : 0;
        if (immediate) Transitions = transitions;
        UpdateSlide();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == RevealProperty || change.Property == BoundsProperty) UpdateSlide();
    }

    private void UpdateSlide()
    {
        var p = Math.Clamp(Reveal, 0, 1);
        _slide.X = -(Bounds.Width + 18) * (1 - p);
        Opacity = p;
        if (!_revealed && p <= .001) IsVisible = false;
    }
}
