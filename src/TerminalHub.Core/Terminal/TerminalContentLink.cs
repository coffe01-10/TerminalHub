using System.Text.RegularExpressions;

namespace TerminalHub.Core.Terminal;

public sealed record TerminalContentLink(string Target, bool IsUrl, int? Line = null, int? Column = null);

public static partial class TerminalContentLinks
{
    [GeneratedRegex(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();
    [GeneratedRegex("(?<path>(?:[A-Za-z]:[\\\\/]|/|\\.{0,2}[/\\\\]|(?:[\\w.-]+[/\\\\])+)[^\\r\\n<>\"|]*?\\.[A-Za-z0-9]{1,10}|[\\w.-]+\\.[A-Za-z0-9]{1,10})(?::(?<line>\\d+)(?::(?<column>\\d+))?|\\((?<line>\\d+)(?:,(?<column>\\d+))?\\))?")]
    private static partial Regex PathPattern();

    public static TerminalContentLink? Resolve(string text, int index, string cwd, bool remote, string? hyperlink = null)
    {
        if (hyperlink is not null && Uri.TryCreate(hyperlink, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme is "http" or "https") return new(uri.AbsoluteUri, true);
            if (!remote && uri.IsFile && (uri.Host.Length == 0 || uri.IsLoopback)) return new(uri.LocalPath, false);
        }
        foreach (Match match in UrlPattern().Matches(text))
            if (index >= match.Index && index < match.Index + match.Length)
                return new(match.Value.TrimEnd('.', ',', ';', ')', ']', '}'), true);
        if (remote) return null;
        foreach (Match match in PathPattern().Matches(text))
        {
            if (index < match.Index || index >= match.Index + match.Length) continue;
            var path = match.Groups["path"].Value.Trim();
            try
            {
                path = Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(path, cwd);
                if (!File.Exists(path) && !Directory.Exists(path)) return null;
                return new(path, false, Number("line"), Number("column"));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException) { return null; }
            int? Number(string group) => int.TryParse(match.Groups[group].Value, out var value) && value > 0 ? value : null;
        }
        return null;
    }
}
