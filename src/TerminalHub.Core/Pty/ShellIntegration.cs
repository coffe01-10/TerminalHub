namespace TerminalHub.Core.Pty;

/// <summary>Session-only prompt integration; no profile files are changed.</summary>
public static class ShellIntegration
{
    public const string PowerShellArguments = "-NoExit -Command \"$global:TerminalHubOriginalPrompt = ${function:prompt}; function global:prompt { [Console]::Write([char]27 + ']9;9;' + $executionContext.SessionState.Path.CurrentFileSystemLocation.Path + [char]7); & $global:TerminalHubOriginalPrompt }\"";
}
