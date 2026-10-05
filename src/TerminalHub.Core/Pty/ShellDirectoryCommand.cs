namespace TerminalHub.Core.Pty;

public static class ShellDirectoryCommand
{
    public static string Build(string path, string shell)
    {
        var quoted = ShellPathInput.Format([path], shell);
        return Path.GetFileNameWithoutExtension(shell).ToLowerInvariant() switch
        {
            "pwsh" or "powershell" => "Set-Location -LiteralPath " + quoted,
            "cmd" => "cd /d " + quoted,
            _ => "cd -- " + quoted
        };
    }
}
