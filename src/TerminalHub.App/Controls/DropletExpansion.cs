using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace TerminalHub.App.Controls;

/// <summary>Panel motion retains its current position and velocity when reversed.</summary>
public sealed class DropletExpansion : AvaloniaObject
{
    public static readonly AttachedProperty<bool> IsOpenProperty =
        AvaloniaProperty.RegisterAttached<DropletExpansion, Control, bool>("IsOpen");
    public static bool GetIsOpen(Control control) => control.GetValue(IsOpenProperty);
    public static void SetIsOpen(Control control, bool value) => control.SetValue(IsOpenProperty, value);
    public static readonly AttachedProperty<bool> AnimatePopupProperty =
        AvaloniaProperty.RegisterAttached<DropletExpansion, Control, bool>("AnimatePopup");
    public static bool GetAnimatePopup(Control control) => control.GetValue(AnimatePopupProperty);
    public static void SetAnimatePopup(Control control, bool value) => control.SetValue(AnimatePopupProperty, value);
    private static readonly ConditionalWeakTable<Control, Motion> Motions = new();
    static DropletExpansion()
    {
        IsOpenProperty.Changed.AddClassHandler<Control>((control, _) =>
            Motions.GetValue(control, c => new Motion(c)).SetOpen(GetIsOpen(control)));
        AnimatePopupProperty.Changed.AddClassHandler<Control>((control, change) =>
        {
            if (change.NewValue is true)
                control.AttachedToVisualTree += (_, _) => Motions.GetValue(control, c => new Motion(c)).SetOpen(true);
        });
    }

    public static void SetOrigin(Control panel, Control? trigger)
    {
        if (trigger is null || panel.Parent is not Visual parent) { panel.RenderTransformOrigin = RelativePoint.Center; return; }
        Dispatcher.UIThread.Post(() =>
        {
            if (panel.Bounds.Width <= 0 || panel.Bounds.Height <= 0) return;
            if (trigger.TranslatePoint(new Point(trigger.Bounds.Width / 2, trigger.Bounds.Height / 2), parent) is not { } p) return;
            panel.RenderTransformOrigin = new RelativePoint(
                Math.Clamp((p.X - panel.Bounds.X) / panel.Bounds.Width, 0, 1),
                Math.Clamp((p.Y - panel.Bounds.Y) / panel.Bounds.Height, 0, 1), RelativeUnit.Relative);
        }, DispatcherPriority.Loaded);
    }

    private sealed class Motion
    {
        private readonly Control _panel;
        private readonly ScaleTransform _scale = new(.94, .82);
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
        private double _progress, _velocity, _target;
        private long _last;
        public Motion(Control panel)
        {
            _panel = panel;
            panel.RenderTransform = _scale;
            panel.RenderTransformOrigin = panel is MenuFlyoutPresenter ? new RelativePoint(0, 0, RelativeUnit.Relative) : RelativePoint.Center;
            _timer.Tick += Tick;
            panel.DetachedFromVisualTree += (_, _) =>
            {
                _timer.Stop();
                _progress = _velocity = 0;
                _scale.ScaleX = .94; _scale.ScaleY = .82;
            };
        }
        public void SetOpen(bool open)
        {
            _target = open ? 1 : 0;
            // Closing panels relinquish input at once, even during their painted exit.
            _panel.IsHitTestVisible = open;
            _panel.Opacity = Math.Clamp(_progress, 0, 1);
            if (open) _panel.IsVisible = true;
            if (!_timer.IsEnabled) { _last = Stopwatch.GetTimestamp(); _timer.Start(); }
        }
        private void Tick(object? sender, EventArgs e)
        {
            var now = Stopwatch.GetTimestamp();
            var dt = Math.Clamp(Stopwatch.GetElapsedTime(_last, now).TotalSeconds, .001, .032);
            _last = now;
            _velocity += ((_target - _progress) * 230 - _velocity * 28) * dt;
            _progress += _velocity * dt;
            _scale.ScaleX = .94 + .06 * _progress;
            _scale.ScaleY = .82 + .18 * _progress;
            _panel.Opacity = Math.Clamp(_progress, 0, 1);
            if (Math.Abs(_target - _progress) + Math.Abs(_velocity) < .002)
            {
                _progress = _target; _velocity = 0; _timer.Stop();
                _scale.ScaleX = .94 + .06 * _target; _scale.ScaleY = .82 + .18 * _target;
                _panel.Opacity = _target;
                if (_target == 0) _panel.IsVisible = false;
            }
        }
    }
}
