using System.Text.RegularExpressions;

namespace TerminalHub.Core.Logging;

/// <summary>
/// Heuristic line classification for the Output/Problems panels:
/// error → Problems badge, warn → warn level, else info.
/// </summary>
public static partial class LineClassifier
{
    [GeneratedRegex(
        @"(?<!\b0\s)(?<!\bno\s)\berrors?\b(?!\s*[:=]\s*0)|\berr:|exception|fatal|(?<!\b0\s)(?<!\bno\s)\bfailed\b|\bfailure\b(?!\s*[:=]\s*0)|command not found|permission denied|no such file|exit[\s_]*(code|status)?[\s_:=]*[1-9]|exited? with (code )?[1-9]|(?<!0\s*个?\s*)错误(?!\s*[:：]\s*0)|失败|无法",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ErrorPattern();

    [GeneratedRegex(
        @"\bwarn(ing)?\b(?!\s*[:=]\s*0)|deprecated|deprecat|(?<!0\s*个?\s*)警告(?!\s*[:：]\s*0)|注意",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex WarnPattern();

    public static string Classify(string line)
    {
        if (ErrorPattern().IsMatch(line)) return "error";
        if (WarnPattern().IsMatch(line)) return "warn";
        return "info";
    }
}
