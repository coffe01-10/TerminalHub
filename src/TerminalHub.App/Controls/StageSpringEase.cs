using Avalonia.Animation.Easings;

namespace TerminalHub.App.Controls;

/// <summary>Critically damped motion: quick response without overshoot at slot boundaries.</summary>
public sealed class StageSpringEase : Easing
{
    public override double Ease(double progress)
        => (1 - Math.Exp(-9 * progress) * (1 + 9 * progress)) / (1 - 10 * Math.Exp(-9));
}
