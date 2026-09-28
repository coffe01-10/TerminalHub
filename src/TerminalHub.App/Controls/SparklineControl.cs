using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace TerminalHub.App.Controls;

/// <summary>Small polyline widget for CPU/Mem/Net history samples (0-100 normalized).</summary>
public sealed class SparklineControl : Control
{
    public static readonly StyledProperty<IEnumerable<double>?> ValuesProperty =
        AvaloniaProperty.Register<SparklineControl, IEnumerable<double>?>(nameof(Values));

    public static readonly StyledProperty<IBrush> StrokeProperty =
        AvaloniaProperty.Register<SparklineControl, IBrush>(nameof(Stroke), Brushes.DeepSkyBlue);

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<SparklineControl, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<double> MaxValueProperty =
        AvaloniaProperty.Register<SparklineControl, double>(nameof(MaxValue), 100.0);

    public IEnumerable<double>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public IBrush Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public double MaxValue
    {
        get => GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    static SparklineControl()
    {
        AffectsRender<SparklineControl>(ValuesProperty, StrokeProperty, FillProperty, MaxValueProperty);
    }

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);
        var values = Values?.ToArray();
        if (values is null || values.Length < 2 || Bounds.Width < 4) return;

        var max = Math.Max(MaxValue, values.Max());
        var geo = new StreamGeometry();
        var fillGeo = new StreamGeometry();
        var w = Bounds.Width;
        var h = Bounds.Height;
        var step = w / Math.Max(1, values.Length - 1);

        using (var g = geo.Open())
        using (var f = fillGeo.Open())
        {
            f.BeginFigure(new Point(0, h), true);
            for (var i = 0; i < values.Length; i++)
            {
                var x = i * step;
                var y = h - Math.Clamp(values[i] / max, 0, 1) * (h - 2) - 1;
                if (i == 0) { g.BeginFigure(new Point(x, y), false); f.LineTo(new Point(x, y)); }
                else { g.LineTo(new Point(x, y)); f.LineTo(new Point(x, y)); }
            }
            f.LineTo(new Point(w, h));
            f.EndFigure(true);
        }
        if (Fill is not null)
            ctx.DrawGeometry(Fill, null, fillGeo);
        ctx.DrawGeometry(null, new Pen(Stroke, 1.4), geo);
    }
}
