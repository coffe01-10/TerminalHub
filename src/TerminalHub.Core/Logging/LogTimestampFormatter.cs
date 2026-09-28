namespace TerminalHub.Core.Logging;

/// <summary>
/// Logs panel timestamp labels. Absolute = local clock <c>HH:mm:ss</c>.
/// Relative = "ago from now" short Chinese-friendly labels, falling back to
/// absolute for rows older than yesterday (or on any formatting failure).
/// </summary>
public static class LogTimestampFormatter
{
    /// <summary>Format <paramref name="time"/> for the list. When
    /// <paramref name="relative"/> is false → absolute <c>HH:mm:ss</c>.
    /// When true → ago-from-<paramref name="now"/> (defaults to
    /// <see cref="DateTime.Now"/>): <c>刚刚</c> / <c>12s</c> / <c>3m</c> /
    /// <c>1h</c> / <c>昨天 HH:mm</c>; older or weird → absolute.</summary>
    public static string Format(DateTime time, bool relative, DateTime? now = null)
    {
        if (!relative) return FormatAbsolute(time);
        try
        {
            var clock = now ?? DateTime.Now;
            var delta = clock - time;
            if (delta < TimeSpan.Zero) delta = TimeSpan.Zero; // future / clock skew → 刚刚

            if (delta < TimeSpan.FromSeconds(2)) return "刚刚";
            if (delta < TimeSpan.FromMinutes(1)) return $"{(int)delta.TotalSeconds}s";
            if (delta < TimeSpan.FromHours(1)) return $"{(int)delta.TotalMinutes}m";
            if (delta < TimeSpan.FromHours(24)) return $"{(int)delta.TotalHours}h";
            if (time.Date == clock.Date.AddDays(-1)) return $"昨天 {time:HH:mm}";
            return FormatAbsolute(time);
        }
        catch
        {
            return FormatAbsolute(time);
        }
    }

    /// <summary>Absolute clock label used by export and absolute display mode.</summary>
    public static string FormatAbsolute(DateTime time)
    {
        try { return time.ToString("HH:mm:ss"); }
        catch { return "--:--:--"; }
    }
}
