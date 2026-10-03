using TerminalHub.Core.Settings;

namespace TerminalHub.Core.Deploy;

/// <summary>
/// One real publish exit, persisted in <c>settings.json</c>.
/// <see cref="Outcome"/> is <c>success</c>, <c>fail</c>, or <c>cancelled</c>.
/// <see cref="ArtifactPath"/> is the last successful publish folder and is kept
/// when a later run fails or is cancelled, so the dock can still open it.
/// </summary>
public sealed class LastPublishResult
{
    private string _outcome = "";
    private string _repoRoot = "";
    private string _artifactPath = "";

    public string Outcome
    {
        get => _outcome;
        set => _outcome = value ?? "";
    }

    /// <summary>Process exit code. Cancel keeps whatever the PTY reported.</summary>
    public int ExitCode { get; set; }

    public DateTimeOffset FinishedAt { get; set; }

    public long DurationMs { get; set; }

    public string RepoRoot
    {
        get => _repoRoot;
        set => _repoRoot = value ?? "";
    }

    /// <summary>Publish directory from the last success. Empty until one is known.</summary>
    public string ArtifactPath
    {
        get => _artifactPath;
        set => _artifactPath = value ?? "";
    }
}

/// <summary>Record and format <see cref="LastPublishResult"/> for the Deploy dock.</summary>
public static class LastPublishResults
{
    public const string Success = "success";
    public const string Fail = "fail";
    public const string Cancelled = "cancelled";

    /// <summary>
    /// Writes the latest terminal outcome onto <paramref name="settings"/>.
    /// A non-empty <paramref name="artifactPath"/> is stored only for <see cref="Success"/>.
    /// Fail, cancel, and a success that located no directory keep the previous successful path.
    /// </summary>
    public static LastPublishResult Record(
        AppSettings settings,
        string? outcome,
        int exitCode,
        DateTimeOffset finishedAt,
        long durationMs,
        string? repoRoot,
        string? artifactPath)
    {
        var normalized = (outcome ?? "").Trim() switch
        {
            Success => Success,
            Cancelled => Cancelled,
            _ => Fail,
        };
        var previous = settings.LastPublishResult?.ArtifactPath ?? "";
        var path = normalized == Success && !string.IsNullOrWhiteSpace(artifactPath)
            ? artifactPath.Trim()
            : previous;

        var result = new LastPublishResult
        {
            Outcome = normalized,
            ExitCode = exitCode,
            FinishedAt = finishedAt,
            DurationMs = durationMs < 0 ? 0 : durationMs,
            RepoRoot = repoRoot?.Trim() ?? "",
            ArtifactPath = path,
        };
        settings.LastPublishResult = result;
        return result;
    }


    /// <summary>Drop the stored outcome so the dock badge and clear/open/copy gates reset.</summary>
    public static void Clear(AppSettings settings) =>
        settings.LastPublishResult = null;

    /// <summary>True when a known outcome is stored (badge would be non-empty when idle).</summary>
    public static bool HasRecord(LastPublishResult? result) => IsKnown(result);

    /// <summary>Short dock line. Empty when no real outcome has been recorded.</summary>
    public static string FormatBadge(LastPublishResult? result)
    {
        if (!IsKnown(result)) return "";
        var dur = FormatDuration(result!.DurationMs);
        return result.Outcome switch
        {
            Success => $"成功 · {dur}",
            Cancelled => $"已取消 · {dur}",
            _ => $"失败 exit {result.ExitCode} · {dur}",
        };
    }

    /// <summary>Tooltip detail: exit code and duration, or 「尚未打包」 when none.</summary>
    public static string FormatTooltip(LastPublishResult? result)
    {
        if (!IsKnown(result)) return "尚未打包";
        var label = result!.Outcome switch
        {
            Success => "成功",
            Cancelled => "已取消",
            _ => "失败",
        };
        var when = result.FinishedAt == default
            ? ""
            : " · " + result.FinishedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        return $"上次结果：{TerminalHub.Core.Localization.Localizer.Current.Translate(label)} · exit {result.ExitCode} · {FormatDuration(result.DurationMs)}{when}";
    }

    /// <summary>True when a last-success folder is stored and that directory still exists.</summary>
    public static bool CanOpen(LastPublishResult? result)
    {
        var path = result?.ArtifactPath;
        if (string.IsNullOrWhiteSpace(path)) return false;
        try { return Directory.Exists(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    public static string FormatDuration(long durationMs)
    {
        if (durationMs < 1000) return "<1s";
        var totalSeconds = durationMs / 1000;
        if (totalSeconds < 60) return $"{totalSeconds}s";
        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;
        return seconds == 0 ? $"{minutes}m" : $"{minutes}m{seconds}s";
    }

    /// <summary>
    /// Live packing elapsed for the dock. Zero shows <c>0s</c>; otherwise same wording as
    /// <see cref="FormatDuration"/> so idle badges and the running line stay consistent.
    /// </summary>
    public static string FormatLiveElapsed(long elapsedMs)
    {
        if (elapsedMs <= 0) return "0s";
        return FormatDuration(elapsedMs);
    }

    /// <summary>Status-line text while a publish is running, e.g. <c>打包中 · 8s</c>.</summary>
    public static string FormatLiveBadge(long elapsedMs) =>
        $"打包中 · {FormatLiveElapsed(elapsedMs)}";

    private static bool IsKnown(LastPublishResult? result) =>
        result is not null && result.Outcome is Success or Fail or Cancelled;
}
