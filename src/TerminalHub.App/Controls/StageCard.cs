using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace TerminalHub.App.Controls;

/// <summary>A live session thumbnail that folds back into the stage shelf.</summary>
public sealed class StageCard : Border
{
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<StageCard, bool>(nameof(IsActive));
    public static readonly StyledProperty<int> StageDistanceProperty =
        AvaloniaProperty.Register<StageCard, int>(nameof(StageDistance));
    public static readonly StyledProperty<double> DepthProperty =
        AvaloniaProperty.Register<StageCard, double>(nameof(Depth), 1);
    public static readonly StyledProperty<double> TiltProperty =
        AvaloniaProperty.Register<StageCard, double>(nameof(Tilt), 4);
    public static readonly StyledProperty<double> LiftProperty =
        AvaloniaProperty.Register<StageCard, double>(nameof(Lift));
    private bool _pressed;

    public bool IsActive { get => GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
    public int StageDistance { get => GetValue(StageDistanceProperty); set => SetValue(StageDistanceProperty, value); }
    public double Depth { get => GetValue(DepthProperty); set => SetValue(DepthProperty, value); }
    public double Tilt { get => GetValue(TiltProperty); set => SetValue(TiltProperty, value); }
    public double Lift { get => GetValue(LiftProperty); set => SetValue(LiftProperty, value); }

    public StageCard()
    {
        RenderTransformOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative);
        Transitions = new Transitions
        {
            new DoubleTransition { Property = DepthProperty, Duration = TimeSpan.FromMilliseconds(520), Easing = new StageSpringEase() },
            new DoubleTransition { Property = TiltProperty, Duration = TimeSpan.FromMilliseconds(520), Easing = new StageSpringEase() },
            new DoubleTransition { Property = LiftProperty, Duration = TimeSpan.FromMilliseconds(520), Easing = new StageSpringEase() },
            new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(220) },
            new BrushTransition { Property = BackgroundProperty, Duration = TimeSpan.FromMilliseconds(220) },
            new BrushTransition { Property = BorderBrushProperty, Duration = TimeSpan.FromMilliseconds(220) }
        };
        UpdatePose();
        UpdateTransform();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsActiveProperty || change.Property == StageDistanceProperty || change.Property == IsPointerOverProperty)
            UpdatePose();
        if (change.Property == DepthProperty || change.Property == TiltProperty || change.Property == LiftProperty)
            UpdateTransform();
    }

    private void UpdatePose()
    {
        Depth = IsActive ? .08 : IsPointerOver ? .3 : 1;
        Tilt = IsActive ? -1 : IsPointerOver ? -Math.Sign(StageDistance) : -Math.Clamp(StageDistance, -2, 2) * 2.8;
        Lift = _pressed ? -.5 : IsActive ? 1 : IsPointerOver ? .55 : 0;
        Opacity = IsActive || IsPointerOver ? 1 : .82;
        ZIndex = IsActive || IsPointerOver ? 10 : 0;
    }

    private void UpdateTransform()
    {
        // Projective matrix: the far edge is smaller, not just rotated in the plane.
        RenderTransform = new MatrixTransform(new Matrix(
            1 - .08 * Depth + .025 * Lift, Math.Tan(Tilt * Math.PI / 180), .00075 * Depth,
            0, 1 - .035 * Depth + .025 * Lift, 0,
            -8 * Depth + 5 * Lift, -2 * Lift, 1));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _pressed = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed;
        UpdatePose();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _pressed = false;
        UpdatePose();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _pressed = false;
        UpdatePose();
    }
}
