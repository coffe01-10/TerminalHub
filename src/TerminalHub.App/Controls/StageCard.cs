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
    public static readonly StyledProperty<double> CenterDistanceProperty =
        AvaloniaProperty.Register<StageCard, double>(nameof(CenterDistance));
    public static readonly StyledProperty<bool> WheelModeProperty =
        AvaloniaProperty.Register<StageCard, bool>(nameof(WheelMode));
    public static readonly StyledProperty<double> DepthProperty =
        AvaloniaProperty.Register<StageCard, double>(nameof(Depth), 1);
    public static readonly StyledProperty<double> TiltProperty =
        AvaloniaProperty.Register<StageCard, double>(nameof(Tilt), 4);
    public static readonly StyledProperty<double> LiftProperty =
        AvaloniaProperty.Register<StageCard, double>(nameof(Lift));
    private bool _pressed;
    private bool _dragging;
    private bool _hovered;
    public static readonly StyledProperty<double> SlotOffsetProperty =
        AvaloniaProperty.Register<StageCard, double>(nameof(SlotOffset));
    public double SlotOffset { get => GetValue(SlotOffsetProperty); set => SetValue(SlotOffsetProperty, value); }
    private readonly MatrixTransform _pose = new(Matrix.Identity);
    private readonly TranslateTransform _slotPose = new();
    private readonly DoubleTransition _slotMotion = new()
    {
        Property = SlotOffsetProperty, Duration = TimeSpan.FromMilliseconds(240), Easing = new CubicEaseOut()
    };

    public bool IsActive { get => GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
    public int StageDistance { get => GetValue(StageDistanceProperty); set => SetValue(StageDistanceProperty, value); }
    /// <summary>Distance from the shelf viewport center, in card slots.</summary>
    public double CenterDistance { get => GetValue(CenterDistanceProperty); set => SetValue(CenterDistanceProperty, value); }
    public bool WheelMode { get => GetValue(WheelModeProperty); set => SetValue(WheelModeProperty, value); }
    public double Depth { get => GetValue(DepthProperty); set => SetValue(DepthProperty, value); }
    public double Tilt { get => GetValue(TiltProperty); set => SetValue(TiltProperty, value); }
    public double Lift { get => GetValue(LiftProperty); set => SetValue(LiftProperty, value); }

    public StageCard()
    {
        Background = Brushes.Transparent;
        RenderTransform = _slotPose;
        Transitions = new Transitions
        {
            new DoubleTransition { Property = DepthProperty, Duration = TimeSpan.FromMilliseconds(300), Easing = new StageSpringEase() },
            new DoubleTransition { Property = TiltProperty, Duration = TimeSpan.FromMilliseconds(300), Easing = new StageSpringEase() },
            new DoubleTransition { Property = LiftProperty, Duration = TimeSpan.FromMilliseconds(300), Easing = new StageSpringEase() },
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
        if (change.Property == ChildProperty && Child is { } body)
        {
            body.RenderTransformOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative);
            body.RenderTransform = _pose;
        }
        if (change.Property == IsActiveProperty || change.Property == StageDistanceProperty
            || change.Property == CenterDistanceProperty || change.Property == WheelModeProperty)
            UpdatePose();
        if (change.Property == DepthProperty || change.Property == TiltProperty || change.Property == LiftProperty || change.Property == SlotOffsetProperty)
            UpdateTransform();
    }

    private void UpdatePose()
    {
        // Hover must not change hit-test geometry: the old tilted edge could
        // repeatedly enter/exit under a stationary pointer.
        Depth = _dragging ? 0 : IsActive ? .08 : _hovered ? .4 : 1;
        var tilt = ThemeManager.Current == "Paper" ? 1.2 : ThemeManager.Current == "Black" ? .35 : 1.8;
        Tilt = _dragging || IsActive ? 0 : -Math.Clamp(StageDistance, -2, 2) * tilt * (_hovered ? .35 : 1);
        Lift = _dragging ? 1.6 : _pressed ? -.3 : _hovered ? IsActive ? .9 : 1.2 : IsActive ? .6 : 0;
        Opacity = IsActive || _dragging || _hovered ? 1
            : WheelMode ? Math.Clamp(1 - Math.Abs(CenterDistance) * .3, .22, .88) : .92;
        ZIndex = _dragging ? 30 : _hovered ? 20 : IsActive ? 10 : 0;
        if (this.FindAncestorOfType<ListBoxItem>() is { } container)
            container.ZIndex = ZIndex;
        InvalidateVisual();
    }

    private void UpdateTransform()
    {
        // Projective matrix: the far edge is smaller, not just rotated in the plane.
        _pose.Matrix = new Matrix(
            1 - .08 * Depth + .025 * Lift, Math.Tan(Tilt * Math.PI / 180), .00075 * Depth,
            0, 1 - .035 * Depth + .025 * Lift, 0,
            -8 * Depth + 5 * Lift, -2 * Lift, 1);
        _slotPose.Y = SlotOffset;
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
        _hovered = _pressed = _dragging = false;
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
        _hovered = _pressed = false;
        UpdatePose();
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _hovered = true;
        UpdatePose();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _pressed = false;
        UpdatePose();
    }
}
