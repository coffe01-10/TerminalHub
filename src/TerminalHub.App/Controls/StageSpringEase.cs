using Avalonia.Animation.Easings;

namespace TerminalHub.App.Controls;

/// <summary>A restrained, damped spring: fast lift, a small overshoot, then settle.</summary>
public sealed class StageSpringEase : Easing
{
    public override double Ease(double progress)
        => progress >= 1 ? 1 : 1 - Math.Exp(-8 * progress) *
            (Math.Cos(11 * progress) + 8d / 11 * Math.Sin(11 * progress));
}
