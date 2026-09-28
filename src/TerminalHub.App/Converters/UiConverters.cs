using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using TerminalHub.Core.AI;

namespace TerminalHub.App.ViewModels;

/// <summary>[info]/[warn]/[error] → accent color.</summary>
public sealed class LogLevelConverter : IValueConverter
{
    public static readonly LogLevelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => (value as string)?.ToLowerInvariant() switch
        {
            "warn" or "warning" => new SolidColorBrush(Color.Parse("#FBBF24")),
            "error" or "err" => new SolidColorBrush(Color.Parse("#F87171")),
            "debug" => new SolidColorBrush(Color.Parse("#64748B")),
            _ => new SolidColorBrush(Color.Parse("#38BDF8")),
        };

    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>int equality for tab/visibility — handles string parameters.</summary>
public sealed class IntEqualConverter : IValueConverter
{
    public static readonly IntEqualConverter Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var v = value is int i ? i : -1;
        var p = parameter is int pi ? pi : int.TryParse(parameter?.ToString(), out var pj) ? pj : -2;
        return v == p;
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Byte count → "512 MB" style.</summary>
public sealed class BytesConverter : IValueConverter
{
    public static readonly BytesConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var bytes = value switch
        {
            double d => d,
            long l => l,
            int i => i,
            _ => 0d,
        };
        if (bytes >= 1L << 30) return $"{bytes / (1L << 30):0.0} GB";
        if (bytes >= 1L << 20) return $"{bytes / (1L << 20):0} MB";
        if (bytes >= 1L << 10) return $"{bytes / (1L << 10):0} KB";
        return $"{bytes:0} B";
    }

    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>IsDirectory → dir names get a blue tint, files stay near-white.</summary>
public sealed class DirNameConverter : IValueConverter
{
    public static readonly DirNameConverter Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true
            ? new SolidColorBrush(Color.Parse("#7DD3FC"))
            : new SolidColorBrush(Color.Parse("#E2E8F0"));
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Non-empty string → true (status line visibility).</summary>
public sealed class StringNotEmptyConverter : IValueConverter
{
    public static readonly StringNotEmptyConverter Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrEmpty(value as string);
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>bool → accent brush when true, muted gray when false (e.g. split-toggle state).</summary>
public sealed class BoolBrushConverter : IValueConverter
{
    public static readonly BoolBrushConverter Instance = new();
    private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#38BDF8"));
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#94A3B8"));
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Accent : Muted;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Checklist state → ring stroke color.</summary>
public sealed class ChecklistRingConverter : IValueConverter
{
    public static readonly ChecklistRingConverter Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is ChecklistState s
            ? new SolidColorBrush(Color.Parse(s switch
            {
                ChecklistState.Done => "#34D399",
                ChecklistState.Active => "#38BDF8",
                _ => "#64748B",
            }))
            : Brushes.Gray;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Checklist state → fill (done=solid green, active=translucent, pending=empty).</summary>
public sealed class ChecklistFillConverter : IValueConverter
{
    public static readonly ChecklistFillConverter Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is ChecklistState s
            ? new SolidColorBrush(Color.Parse(s switch
            {
                ChecklistState.Done => "#34D399",
                ChecklistState.Active => "#3338BDF8",
                _ => "#00000000",
            }))
            : Brushes.Transparent;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Checklist state → text color (done=dimmed green, active=bright, pending=gray).</summary>
public sealed class ChecklistTextConverter : IValueConverter
{
    public static readonly ChecklistTextConverter Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is ChecklistState s
            ? new SolidColorBrush(Color.Parse(s switch
            {
                ChecklistState.Done => "#34D399",
                ChecklistState.Active => "#E2E8F0",
                _ => "#64748B",
            }))
            : Brushes.Gray;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}
