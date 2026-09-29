using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

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
    private bool _dragging;
    public static readonly StyledProperty<double> SlotOffsetProperty =
        AvaloniaProperty.Register<StageCard, double>(nameof(SlotOffset));
    public double SlotOffset { get => GetValue(SlotOffsetProperty); set => SetValue(SlotOffsetProperty, value); }
    private readonly MatrixTransform _pose = new(Matrix.Identity);
    private readonly DoubleTransition _slotMotion = new()
    {
        Property = SlotOffsetProperty, Duration = TimeSpan.FromMilliseconds(240), Easing = new CubicEaseOut()
    };

    public bool IsActive { get => GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
    public int StageDistance { get => GetValue(StageDistanceProperty); set => SetValue(StageDistanceProperty, value); }
    public double Depth { get => GetValue(DepthProperty); set => SetValue(DepthProperty, value); }
    public double Tilt { get => GetValue(TiltProperty); set => SetValue(TiltProperty, value); }
    public double Lift { get => GetValue(LiftProperty); set => SetValue(LiftProperty, value); }

    public StageCard()
    {
        RenderTransformOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative);
        RenderTransform = _pose;
        Transitions = new Transitions
        {
            new DoubleTransition { Property = DepthProperty, Duration = TimeSpan.FromMilliseconds(520), Easing = new StageSpringEase() },
            new DoubleTransition { Property = TiltProperty, Duration = TimeSpan.FromMilliseconds(520), Easing = new StageSpringEase() },
            new DoubleTransition { Property = LiftProperty, Duration = TimeSpan.FromMilliseconds(520), Easing = new StageSpringEase() },
            new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(220) },
            new BrushTransition { Property = BackgroundProperty, Duration = TimeSpan.FromMilliseconds(220) },
            new BrushTransition { Property = BorderBrushProperty, Duration = TimeSpan.FromMilliseconds(220) },
            _slotMotion
        };
        UpdatePose();
        UpdateTransform();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsActiveProperty || change.Property == StageDistanceProperty)
            UpdatePose();
        if (change.Property == DepthProperty || change.Property == TiltProperty || change.Property == LiftProperty || change.Property == SlotOffsetProperty)
            UpdateTransform();
    }

    private void UpdatePose()
    {
        // Hover must not change hit-test geometry: the old tilted edge could
        // repeatedly enter/exit under a stationary pointer.
        Depth = _dragging ? 0 : IsActive ? .08 : 1;
        var tilt = ThemeManager.Current == "Paper" ? 1.2 : ThemeManager.Current == "Black" ? .35 : 1.8;
        Tilt = _dragging || IsActive ? 0 : -Math.Clamp(StageDistance, -2, 2) * tilt;
        Lift = _dragging ? 1.6 : _pressed ? -.3 : IsActive ? .6 : 0;
        Opacity = IsActive || _dragging ? 1 : .92;
        ZIndex = _dragging ? 30 : IsActive ? 10 : 0;
        InvalidateVisual();
    }

    private void UpdateTransform()
    {
        // Projective matrix: the far edge is smaller, not just rotated in the plane.
        _pose.Matrix = new Matrix(
            1 - .08 * Depth + .025 * Lift, Math.Tan(Tilt * Math.PI / 180), .00075 * Depth,
            0, 1 - .035 * Depth + .025 * Lift, 0,
            -8 * Depth + 5 * Lift, -2 * Lift + SlotOffset, 1);
    }

    public void SetSlotOffset(double offset, bool immediate = false)
    {
        if (immediate) Transitions?.Remove(_slotMotion);
        SlotOffset = offset;
        if (immediate) Transitions?.Add(_slotMotion);
    }

    public void SetDragging(bool dragging)
    {
        _dragging = dragging;
        _pressed = false;
        // Cards are nested in ListBoxItems: raise that sibling too, otherwise
        // the dragged preview disappears underneath the next item's terminal.
        if (this.FindAncestorOfType<ListBoxItem>() is { } container)
            container.ZIndex = dragging ? 30 : 0;
        UpdatePose();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ThemeManager.Changed += UpdatePose;
        UpdatePose();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ThemeManager.Changed -= UpdatePose;
        base.OnDetachedFromVisualTree(e);
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
