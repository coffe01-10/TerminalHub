using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace TerminalHub.App.Controls;

/// <summary>Animate the painted button surface; the outer hit target and command remain immediate.</summary>
public sealed class DropletMotion : AvaloniaObject
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<DropletMotion, Button, bool>("Enabled");
    public static bool GetEnabled(Button button) => button.GetValue(EnabledProperty);
    public static void SetEnabled(Button button, bool value) => button.SetValue(EnabledProperty, value);
    private static readonly ConditionalWeakTable<Button, ButtonMotion> Motions = new();
    static DropletMotion() => EnabledProperty.Changed.AddClassHandler<Button>((button, change) =>
    {
        if (change.NewValue is true) Motions.GetValue(button, b => new ButtonMotion(b));
        else if (Motions.TryGetValue(button, out var motion)) { motion.Dispose(); Motions.Remove(button); }
    });

    private sealed class ButtonMotion : IDisposable
    {
        private readonly Button _button;
        private readonly ScaleTransform _scale = new(1, 1);
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
        private Control? _surface;
        private double _x = 1, _y = 1, _vx, _vy;
        private long _last;
        public ButtonMotion(Button button)
        {
            _button = button;
            button.TemplateApplied += TemplateApplied;
            button.PropertyChanged += Changed;
            button.DetachedFromVisualTree += Detached;
            _timer.Tick += Tick;
        }
        private void TemplateApplied(object? sender, Avalonia.Controls.Primitives.TemplateAppliedEventArgs e)
        {
            _surface = e.NameScope.Find<ContentPresenter>("PART_ContentPresenter");
            if (_surface is null) return;
            _surface.RenderTransformOrigin = RelativePoint.Center;
            _surface.RenderTransform = _scale;
        }
        private void Changed(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property != Button.IsPressedProperty && e.Property != InputElement.IsPointerOverProperty
                && e.Property != InputElement.IsEffectivelyEnabledProperty) return;
            if (_surface is null || !_button.IsEffectivelyVisible) return;
            if (!_timer.IsEnabled) { _last = Stopwatch.GetTimestamp(); _timer.Start(); }
        }
        private void Tick(object? sender, EventArgs e)
        {
            var now = Stopwatch.GetTimestamp();
            var dt = Math.Clamp(Stopwatch.GetElapsedTime(_last, now).TotalSeconds, .001, .032);
            _last = now;
            var pressed = _button.IsEffectivelyEnabled && _button.IsPressed;
            var hover = _button.IsEffectivelyEnabled && _button.IsPointerOver;
            var tx = pressed ? 1.04 : hover ? 1.012 : 1;
            var ty = pressed ? .94 : hover ? 1.012 : 1;
            Advance(ref _x, ref _vx, tx, dt);
            Advance(ref _y, ref _vy, ty, dt);
            _scale.ScaleX = _x; _scale.ScaleY = _y;
            if (Math.Abs(_x - tx) + Math.Abs(_y - ty) + Math.Abs(_vx) + Math.Abs(_vy) < .001)
            { _scale.ScaleX = _x = tx; _scale.ScaleY = _y = ty; _vx = _vy = 0; _timer.Stop(); }
        }
        private static void Advance(ref double value, ref double velocity, double target, double dt)
        {
            velocity += ((target - value) * 340 - velocity * 27) * dt;
            value += velocity * dt;
        }
        private void Detached(object? sender, VisualTreeAttachmentEventArgs e)
        { _timer.Stop(); _x = _y = _scale.ScaleX = _scale.ScaleY = 1; _vx = _vy = 0; }
        public void Dispose()
        {
            _timer.Stop();
            _button.TemplateApplied -= TemplateApplied;
            _button.PropertyChanged -= Changed;
            _button.DetachedFromVisualTree -= Detached;
            if (_surface?.RenderTransform == _scale) _surface.RenderTransform = null;
        }
    }
}
