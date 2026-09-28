using Avalonia;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Controls;

namespace TerminalHub.App.Controls;

/// <summary>
/// TextBlock that renders <see cref="LineText"/> and highlights every
/// case-insensitive occurrence of <see cref="Query"/> in bold yellow.
/// </summary>
public sealed class HighlightTextBlock : TextBlock
{
    public static readonly StyledProperty<string?> LineTextProperty =
        AvaloniaProperty.Register<HighlightTextBlock, string?>(nameof(LineText));

    public static readonly StyledProperty<string?> QueryProperty =
        AvaloniaProperty.Register<HighlightTextBlock, string?>(nameof(Query));

    public string? LineText
    {
        get => GetValue(LineTextProperty);
        set => SetValue(LineTextProperty, value);
    }

    public string? Query
    {
        get => GetValue(QueryProperty);
        set => SetValue(QueryProperty, value);
    }

    private static readonly IBrush MatchBrush = new SolidColorBrush(Color.Parse("#FDE047"));

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LineTextProperty || change.Property == QueryProperty)
            Rebuild();
    }

    private void Rebuild()
    {
        Inlines?.Clear();
        var text = LineText ?? "";
        var q = Query ?? "";
        if (q.Length == 0 || text.Length == 0)
        {
            Inlines?.Add(new Run(text));
            return;
        }
        var i = 0;
        while (i < text.Length)
        {
            var k = text.IndexOf(q, i, System.StringComparison.OrdinalIgnoreCase);
            if (k < 0) { Inlines?.Add(new Run(text[i..])); break; }
            if (k > i) Inlines?.Add(new Run(text[i..k]));
            Inlines?.Add(new Run(text.Substring(k, q.Length))
            {
                Foreground = MatchBrush,
                FontWeight = FontWeight.Bold,
            });
            i = k + q.Length;
        }
    }
}
