namespace TerminalHub.Core.Pty;

public static class ShellPathInput
{
    public static string Format(IEnumerable<string> paths, string shell)
    {
        var executable = Path.GetFileNameWithoutExtension(shell).ToLowerInvariant();
        return string.Join(" ", paths.Select(path => executable switch
        {
            "pwsh" or "powershell" => "'" + path.Replace("'", "''") + "'",
            "cmd" => "\"" + path + "\"",
            "wsl" => "'" + WslPath(path).Replace("'", "'\"'\"'") + "'",
            "bash" or "zsh" or "sh" or "fish" => "'" + path.Replace("'", "'\"'\"'") + "'",
            _ => "\"" + path.Replace("\"", "\\\"") + "\""
        }));
    }
    private static string WslPath(string path) => path.Length >= 3 && path[1] == ':'
        ? "/mnt/" + char.ToLowerInvariant(path[0]) + path[2..].Replace('\\', '/') : path.Replace('\\', '/');
}
