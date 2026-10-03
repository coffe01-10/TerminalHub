using System.Globalization;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace TerminalHub.Extensibility;

/// <summary>Monochrome path SVG icons inherit the host's current accent/foreground.</summary>
public sealed class SvgIcon : Control
{
    public static readonly StyledProperty<IBrush?> ForegroundProperty = AvaloniaProperty.Register<SvgIcon,IBrush?>(nameof(Foreground),Brushes.Gray);
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty,value); }
    private readonly Geometry[] _paths;
    private readonly Rect _viewBox;
    static SvgIcon() => AffectsRender<SvgIcon>(ForegroundProperty);
    public SvgIcon(string svg)
    {
        var root = XDocument.Parse(svg).Root ?? throw new InvalidDataException("SVG is empty");
        var box = ((string?)root.Attribute("viewBox") ?? "0 0 24 24").Split([' ', ','],StringSplitOptions.RemoveEmptyEntries)
            .Select(v => double.Parse(v,CultureInfo.InvariantCulture)).ToArray();
        _viewBox = new(box[0],box[1],box[2],box[3]);
        _paths = root.Descendants().Where(e => e.Name.LocalName == "path" && e.Attribute("d") is not null)
            .Select(e => Geometry.Parse((string)e.Attribute("d")!)).ToArray();
        Width = Height = 18;
    }
    public static SvgIcon FromFile(string path) => new(File.ReadAllText(path));
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var scale = Math.Min(Bounds.Width / _viewBox.Width,Bounds.Height / _viewBox.Height);
        using (context.PushTransform(Matrix.CreateTranslation(-_viewBox.X,-_viewBox.Y) * Matrix.CreateScale(scale,scale)))
            foreach (var path in _paths) context.DrawGeometry(Foreground,null,path);
    }
}
