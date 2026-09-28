using System.Text.RegularExpressions;

namespace TerminalHub.Core.Logging;

/// <summary>
/// Heuristic line classification for the Output/Problems panels:
/// error → Problems badge, warn → warn level, else info.
/// </summary>
public static partial class LineClassifier
{
    [GeneratedRegex(
        @"\berror\b|\berr:|exception|fatal|failed|failure|command not found|permission denied|no such file|exit[\s_]*(code|status)?[\s_:=]*[1-9]|exited? with (code )?[1-9]|错误|失败|无法",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ErrorPattern();

    [GeneratedRegex(
        @"\bwarn(ing)?\b|deprecated|deprecat|警告|注意",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex WarnPattern();

    public static string Classify(string line)
    {
        if (ErrorPattern().IsMatch(line)) return "error";
        if (WarnPattern().IsMatch(line)) return "warn";
        return "info";
    }
}
