using System.Collections;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia;
using Avalonia.Media;
using TerminalHub.App.Controls;
using TerminalHub.Core.Logging;

namespace TerminalHub.App.ViewModels;

/// <summary>[info]/[warn]/[error] → accent color.</summary>
public sealed class LogLevelConverter : IValueConverter
{
    public static readonly LogLevelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => (value as string)?.ToLowerInvariant() switch
        {
            "warn" or "warning" => ThemeManager.Brush("Warm"),
            "error" or "err" => ThemeManager.Brush("Bad"),
            "debug" => ThemeManager.Brush("Faint"),
            _ => ThemeManager.Brush("Accent"),
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
            ? ThemeManager.Brush("Accent")
            : ThemeManager.Brush("Ink");
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
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? ThemeManager.Brush("Accent") : ThemeManager.Brush("Muted");
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>bool wrap → TextWrapping.Wrap (true) or NoWrap (false). Logs message lines.</summary>
public sealed class BoolToTextWrappingConverter : IValueConverter
{
    public static readonly BoolToTextWrappingConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? TextWrapping.Wrap : TextWrapping.NoWrap;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is TextWrapping.Wrap;
}

/// <summary>Logs.CompactDensity → list FontSize: on ≈8.5, off (default) 9.5.</summary>
public sealed class BoolToLogsFontSizeConverter : IValueConverter
{
    public static readonly BoolToLogsFontSizeConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? 8.5 : 9.5;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double d && d < 9.0;
}

/// <summary>Logs.CompactDensity → ListBoxItem Padding: on ≈2,0 · off (default) 4,1.</summary>
public sealed class BoolToLogsItemPaddingConverter : IValueConverter
{
    public static readonly BoolToLogsItemPaddingConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? new Thickness(2, 0) : new Thickness(4, 1);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Thickness t && t.Top < 1;
}

/// <summary>Logs list timestamp: values[0]=DateTime Time, values[1]=bool UseRelativeTimestamps → label.</summary>
public sealed class LogTimestampConverter : IMultiValueConverter
{
    public static readonly LogTimestampConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var time = values.Count > 0 && values[0] is DateTime dt ? dt : default;
        var relative = values.Count > 1 && values[1] is true;
        return LogTimestampFormatter.Format(time, relative);
    }
}
