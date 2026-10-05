using TerminalHub.Core.Settings;

namespace TerminalHub.Core.Pty;

/// <summary>Session-only prompt integration; no profile files are changed.</summary>
public static class ShellIntegration
{
    public const string LegacyPowerShellArguments = "-NoExit -Command \"$global:TerminalHubOriginalPrompt = ${function:prompt}; function global:prompt { [Console]::Write([char]27 + ']9;9;' + $executionContext.SessionState.Path.CurrentFileSystemLocation.Path + [char]7); & $global:TerminalHubOriginalPrompt }\"";
    public static bool IsPowerShell(string shell) => Path.GetFileNameWithoutExtension(shell).ToLowerInvariant() is "pwsh" or "powershell";
    public const string PowerShellArguments = "-NoExit -Command \"" + PowerShellScript + "\"";
    public const string PreviousPowerShellArguments = "-NoExit -Command \"" + PowerShellPromptScript + "\"";
    // Console.Write emits OSC directory/command marks. Western Windows code pages
    // replace Chinese with '?' before ConPTY can translate the output to UTF-8.
    public const string PowerShellScript = "[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false); " +
        PowerShellPromptScript;
    private const string PowerShellPromptScript = "$global:TerminalHubOriginalPrompt = ${function:prompt}; " +
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

    public static bool IsBash(string shell) =>
        Path.GetFileNameWithoutExtension(shell).Equals("bash", StringComparison.OrdinalIgnoreCase);

    /// <summary>Tasks still emit completion when Windows PowerShell cannot load PSReadLine.
    /// The normal Enter hook already sets CommandActive; only its absence needs a C mark.</summary>
    public static string PrepareTaskCommand(string shell, string command) => IsPowerShell(shell)
        ? "if (-not $global:TerminalHubCommandActive) { $global:LASTEXITCODE = $null; $global:TerminalHubCommandActive = $true; " +
          "[Console]::Write([char]27 + ']133;C' + [char]7) }; " + command
        : command;

    /// <summary>Interactive bash reads this file instead of ~/.bashrc. The script
    /// chain-loads the user's rc files first, then installs the marks — so user
    /// aliases survive and our hooks cannot be overwritten by the profile.</summary>
    private const string BashRcScript = """
        # TerminalHub session integration — sourced via `bash --rcfile`.
        [ -f /etc/bash.bashrc ] && . /etc/bash.bashrc
        [ -f ~/.bashrc ] && . ~/.bashrc
        __terminalhub_prompt_command() {
            local __th_ec=$?
            printf '\e]133;D;%s\a' "$__th_ec"
            # Bare absolute path, not file://$PWD: '#' would parse as a URI
            # fragment, '?' as a query, '%' as a broken escape — dirs like
            # "C#proj" or "a%20b" reported a truncated cwd. The parser accepts
            # a bare path (TryParseOsc7) and treats it as local.
            printf '\e]7;%s\a' "$PWD"
            printf '\e]133;A\a'
            return "$__th_ec"
        }
        PROMPT_COMMAND="__terminalhub_prompt_command${PROMPT_COMMAND:+;$PROMPT_COMMAND}"
        PS1="${PS1}\[\e]133;B\a\]"
        # PS0 output bypasses readline: \[ \] would expand to literal \x01/\x02
        # bytes and paint two box glyphs before every command's output.
        PS0="${PS0}\e]133;C\a"
        """;

    /// <summary>Arguments that make an interactive bash emit OSC 133 command
    /// marks (A/B/C/D with exit codes) and OSC 7 cwd reports. Requires bash ≥ 5.0
    /// for PS0 (the C mark); older bash still gets cwd + prompt marks.
    /// The rc file lives next to the app settings — a user-owned path.</summary>
    public static string BashArguments => $"--rcfile \"{EnsureBashRcFile()}\"";

    /// <summary>True when <paramref name="arguments"/> is a bash `--rcfile`
    /// pointing at our integration script — i.e. args persisted by an earlier
    /// build (workspace restore). Those must be re-injected so the rc file is
    /// refreshed even if it was deleted meanwhile; a user's own rcfile with a
    /// different filename does not match.</summary>
    public static bool IsBashRcArguments(string? arguments)
        => !string.IsNullOrWhiteSpace(arguments)
           && arguments.Contains("--rcfile", StringComparison.OrdinalIgnoreCase)
           && arguments.Contains("bash-integration.sh");

    /// <summary>Writes (or refreshes) the bash rc file and returns its path.
    /// Write-temp-then-move so a concurrent session spawn never reads a torn file.</summary>
    internal static string EnsureBashRcFile()
    {
        var dir = Path.GetDirectoryName(SettingsStore.DefaultPath())!;
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "bash-integration.sh");
        try
        {
            if (File.Exists(path) && File.ReadAllText(path) == BashRcScript)
                return path;
        }
        catch (IOException) { /* unreadable → rewrite below */ }
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(tmp, BashRcScript);
        File.Move(tmp, path, overwrite: true);
        return path;
    }
}
