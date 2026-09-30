namespace TerminalHub.Core.Pty;

/// <summary>Session-only prompt integration; no profile files are changed.</summary>
public static class ShellIntegration
{
    public const string LegacyPowerShellArguments = "-NoExit -Command \"$global:TerminalHubOriginalPrompt = ${function:prompt}; function global:prompt { [Console]::Write([char]27 + ']9;9;' + $executionContext.SessionState.Path.CurrentFileSystemLocation.Path + [char]7); & $global:TerminalHubOriginalPrompt }\"";
    public static bool IsPowerShell(string shell) => Path.GetFileNameWithoutExtension(shell).ToLowerInvariant() is "pwsh" or "powershell";
    public const string PowerShellArguments = "-NoExit -Command \"" + PowerShellScript + "\"";
    public const string PowerShellScript = "$global:TerminalHubOriginalPrompt = ${function:prompt}; " +
        "function global:prompt { $thSuccess = $?; " +
        "if ($global:TerminalHubCommandActive) { $thCode = if ($null -ne $global:LASTEXITCODE) { $global:LASTEXITCODE } elseif ($thSuccess) { 0 } else { 1 }; " +
        "[Console]::Write([char]27 + ']133;D;' + $thCode + [char]7); $global:TerminalHubCommandActive = $false }; " +
        "[Console]::Write([char]27 + ']9;9;' + $executionContext.SessionState.Path.CurrentFileSystemLocation.Path + [char]7); " +
        "& $global:TerminalHubOriginalPrompt }; " +
        "if (Get-Module -ListAvailable PSReadLine) { Import-Module PSReadLine; " +
        "Set-PSReadLineKeyHandler -Key Enter -ScriptBlock { $global:LASTEXITCODE = $null; $global:TerminalHubCommandActive = $true; " +
        "$thLine = ''; $thCursor = 0; [Microsoft.PowerShell.PSConsoleReadLine]::GetBufferState([ref]$thLine, [ref]$thCursor); " +
        "$thLine = $thLine.Replace([char]7, ' ').Replace([char]27, ' '); " +
        "[Console]::Write([char]27 + ']133;E;' + $thLine + [char]7); " +
        "[Console]::Write([char]27 + ']133;C' + [char]7); [Microsoft.PowerShell.PSConsoleReadLine]::AcceptLine() } }";
}
