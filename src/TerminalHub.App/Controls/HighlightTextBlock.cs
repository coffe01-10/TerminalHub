using Avalonia;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Controls;
using System.Text.RegularExpressions;

namespace TerminalHub.App.Controls;

/// <summary>
/// TextBlock that renders <see cref="LineText"/> and highlights every occurrence of
/// <see cref="Query"/> in bold yellow. Default: literal case-insensitive substring
/// (the bottom Search tab). With <see cref="UseRegex"/> on, <see cref="Query"/> is a
/// regular expression (IgnoreCase); an invalid or empty pattern renders plain text.
/// </summary>
public sealed class HighlightTextBlock : TextBlock
{
    public static readonly StyledProperty<string?> LineTextProperty =
        AvaloniaProperty.Register<HighlightTextBlock, string?>(nameof(LineText));

    public static readonly StyledProperty<string?> QueryProperty =
        AvaloniaProperty.Register<HighlightTextBlock, string?>(nameof(Query));

    public static readonly StyledProperty<bool> UseRegexProperty =
        AvaloniaProperty.Register<HighlightTextBlock, bool>(nameof(UseRegex));

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

    /// <summary>On: treat <see cref="Query"/> as a regex (IgnoreCase, 250ms timeout);
    /// invalid pattern or regex timeout → whole line plain, never throws.</summary>
    public bool UseRegex
    {
        get => GetValue(UseRegexProperty);
        set => SetValue(UseRegexProperty, value);
    }

    private static readonly IBrush MatchBrush = new SolidColorBrush(Color.Parse("#FDE047"));
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>One-entry memo of the last compiled pattern: every log row rebuilds on
    /// every filter change, and compiling the same pattern per row would dwarf the match.</summary>
    private static (string Pattern, Regex? Regex)? _regexMemo;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LineTextProperty || change.Property == QueryProperty
            || change.Property == UseRegexProperty)
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
        if (UseRegex)
        {
            var regex = TryCompileRegex(q);
            // null = broken pattern → the VM's filter matches nothing; render plain.
            if (regex is not null) AppendRegexHighlights(regex, text);
            else Inlines?.Add(new Run(text));
            return;
        }
        AppendLiteralHighlights(q, text);
    }

    private void AppendLiteralHighlights(string q, string text)
    {
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

    private void AppendRegexHighlights(Regex regex, string text)
    {
        var i = 0;
        try
        {
            foreach (Match m in regex.Matches(text))
            {
                if (m.Length == 0) continue; // zero-width match highlights nothing
                if (m.Index > i) Inlines?.Add(new Run(text[i..m.Index]));
                Inlines?.Add(new Run(m.Value)
                {
                    Foreground = MatchBrush,
                    FontWeight = FontWeight.Bold,
                });
                i = m.Index + m.Length;
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // catastrophic pattern → keep what was highlighted, rest renders plain
        }
        if (i < text.Length) Inlines?.Add(new Run(text[i..]));
    }

    private static Regex? TryCompileRegex(string pattern)
    {
        var memo = _regexMemo;
        if (memo is { } m && m.Pattern == pattern) return m.Regex;
        Regex? compiled;
        try
        {
            compiled = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (ArgumentException)
        {
            compiled = null;
        }
        _regexMemo = (pattern, compiled);
        return compiled;
    }
}
