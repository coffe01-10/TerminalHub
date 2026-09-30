namespace TerminalHub.Core.Settings;

public static class LaunchRequest
{
    public static bool TryGetDirectory(string[] args, out string path, out string? error)
    {
        path = "";
        error = null;
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("--cwd", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    error = "请在 --cwd 后面写目录。";
                    return false;
                }
                path = args[i + 1];
                return true;
            }
            const string prefix = "--cwd=";
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                path = arg[prefix.Length..];
                if (path.Length == 0)
                {
                    error = "请在 --cwd 后面写目录。";
                    return false;
                }
                return true;
            }
        }
        return false;
    }

    /// <summary>Null when the directory exists. Otherwise a message safe to show the user.</summary>
    public static string? CheckDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "没有提供目录。";
        try
        {
            var full = Path.GetFullPath(path);
            if (!Directory.Exists(full)) return "目录不存在：" + path;
            return null;
        }
        catch (Exception)
        {
            return "无法识别目录：" + path;
        }
    }
}
