using System.Text.RegularExpressions;
using TerminalHub.Core.Settings;

namespace TerminalHub.Core.Terminal;

public readonly record struct RuleMatch(int Index, int Length);

public sealed class OutputRuleMatcher
{
    private readonly Regex? _regex;
    public OutputRule Rule { get; }
    public string Error { get; private set; } = "";

    public OutputRuleMatcher(OutputRule rule)
    {
        Rule = rule;
        if (rule.IsRegex && rule.Pattern.Length > 0)
            try { _regex = new Regex(rule.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)); }
            catch (ArgumentException ex) { Error = ex.Message; }
    }

    public IReadOnlyList<RuleMatch> Find(string text)
    {
        if (!Rule.Enabled || Rule.Pattern.Length == 0 || Error.Length > 0) return [];
        var hits = new List<RuleMatch>();
        if (_regex is not null)
        {
            try
            {
                foreach (Match match in _regex.Matches(text))
                    if (match.Length > 0) hits.Add(new(match.Index, match.Length));
            }
            catch (RegexMatchTimeoutException) { Error = "规则匹配超时，请简化正则表达式。"; }
        }
        else
        {
            var start = 0;
            while (start < text.Length)
            {
                var hit = text.IndexOf(Rule.Pattern, start, StringComparison.OrdinalIgnoreCase);
                if (hit < 0) break;
                hits.Add(new(hit, Rule.Pattern.Length));
                start = hit + Rule.Pattern.Length;
            }
        }
        return hits;
    }
}
