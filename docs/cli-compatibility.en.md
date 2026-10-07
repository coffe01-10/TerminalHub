# CLI interaction compatibility

**English** · [简体中文](cli-compatibility.md) · [Documentation](README.md)

Focus: Claude Code, Grok Build, and Codex CLI. The former local Codex assistant panel was removed; running `codex` inside a terminal is unaffected. The dated sections below preserve the original verification history; an untested item in an earlier round is not necessarily a current limitation.

## Controls

- `Ctrl+V`, `Ctrl+Shift+V`, `Shift+Insert`: paste text. When the application enables bracketed paste, multiline text uses one pair of paste boundaries, preserving Chinese and line breaks.
- Right-click pastes in an ordinary shell; applications using mouse reporting, such as Grok, receive the mouse event instead.
- Grok clicks, drags, hover, and wheel events use the terminal mouse protocol. Hold `Shift` for local selection/history scrolling. `Ctrl+Shift+C` copies selection; `Ctrl+C` remains interrupt.
- `Alt+Enter` sends the alternate combination for multiline input. `Shift+Enter` / `Ctrl+Enter` send CSI-u only after Kitty keyboard-protocol negotiation. Without negotiation, Shift+Enter retains ordinary Enter behavior.
- Modified arrow keys retain Ctrl/Shift/Alt. F1–F12, Ctrl+Backspace, Alt+B/F, and Ctrl+backslash are supported.
- With terminal focus, F2 belongs to the CLI (Grok settings). With session-list focus, F2 renames a session; double-clicking its title is another entry.

## September 29, 2026 verification

- **Avalonia Headless:** Chinese multiline paste through the real clipboard entry to PTY, mouse click/drag/hover/wheel, Shift local selection, modified keys, and session focus switching.
- **Grok Build 1.0.41 / Windows ConPTY:** captured `CSI ?1003;1006h` and bracketed-paste negotiation. Clicking No, quit on the trust screen through mouse coordinates exited normally. No directory trust or model request was accepted, so in-session response blocks were not verified.
- **Claude Code 2.1.281:** startup captured bracketed paste and keyboard-protocol queries, stopping at directory trust. Existing VT replay regressions covered Chinese editing cursors separately.
- **Codex CLI 0.149.1:** startup captured bracketed paste; the environment showed login, so post-login conversational input was not verified.
- **Animation:** expansion followed actual thumbnail bounds and rapid switches continued from the current pose. Headless checked unchanged geometry and PTY dimensions; physical desktop smoothness still required manual use.

References: [Grok terminal support](https://github.com/xai-org/grok-build/blob/main/crates/codegen/xai-grok-pager/docs/user-guide/21-terminal-support.md), [Grok shortcuts](https://docs.x.ai/build/keyboard-shortcuts).

## September 30: Chinese editing and resize

Claude Code was launched through real Windows ConPTY in an already trusted local project directory. No new trust was accepted and no model request was submitted. `WindowsCliEditingTests` typed `ab中文cd` and observed the inverse editing cursor moving from column 10 through three left presses to 文 at column 6, then right to c at column 8. Resizing from 100 to 60 columns kept column 8; Home/End returned to 2/10. Chinese on a second bracketed-paste line retained its independent editing position. Each step read real output frames and checked TerminalView's IME anchor.

Headless regressions also covered composition Enter not being sent to the shell, clearing composition on session switch, and querying updated IME coordinates before Render after font/cell-size changes.

Enable the real CLI regression manually (it explicitly skips if variables are absent):

```powershell
$env:TERMINALHUB_CLAUDE_PATH = (Get-Command claude.exe).Source
$env:TERMINALHUB_CLI_CWD = 'an already trusted project directory'
dotnet test tests/TerminalHub.Tests/TerminalHub.Tests.csproj --filter FullyQualifiedName~WindowsCliEditingTests
```

Actual system candidate windows, desktop scaling, and multiple displays were not manually checked in that round. Grok/Codex editing after login was not checked. ConPTY and Headless results do not replace those experiences.

## September 30: v0.4 scope

Workspace changes did not repeat real Claude, Codex, or Grok editing. New Headless cases checked two terminal views at font sizes 13/20 using the same cells; existing cases retained pre-render IME position checks. Real PowerShell with updated OSC 133 Enter handling reported zero/nonzero exit codes. System candidate windows, multiple displays, and post-login Codex/Grok remained untested in that round.

## October 1: Linux and Windows parity

Linux gained PTY behavior and regressions matching Windows:

- **Bash integration:** interactive Bash injects `ShellIntegration.BashArguments`, using `--rcfile <settings>/bash-integration.sh`. The script loads `/etc/bash.bashrc` and `~/.bashrc` before installing hooks without editing user configuration. PROMPT_COMMAND emits `OSC 133;D;<exit code>`, `OSC 7;<absolute path>` (a plain cwd, retaining `#`, `?`, `%`), and `133;A`; PS1 ends with `133;B`, PS0 emits `133;C`. Previously Linux only polled cwd through `/proc` and lacked completion/exit-code events.
- **PowerShell:** selecting pwsh on Linux injects OSC 133/9;9 integration too.
- **Environment:** on non-Windows systems, `PtyEnvironment` adds `LANG=C.UTF-8` only when neither `LANG` nor `LC_ALL` exists, avoiding POSIX/C locale fallback for UTF-8/CJK tools.
- **Parity tests:** LinuxStreamingTests covers streaming before exit, COLORTERM/custom environment, a failing subscriber not blocking later output, exit codes, and resize via `stty size`, alongside WindowsStreamingTests. LinuxCommandCompletionTests covers OSC 133 exit codes 0→7→0→1, OSC 7 directory tracking, and CommandJournal. PtyEnvironmentTests covers environment merging.
- **Real CLIs:** LocalClaudeFact/LocalGrokFact work on Windows and Linux, selecting ConPTY/forkpty. Set `TERMINALHUB_CLAUDE_PATH` / `TERMINALHUB_GROK_PATH` and an already trusted `TERMINALHUB_CLI_CWD`; otherwise they explicitly skip. Standard-handle replacement applies only on Windows.

Custom zsh/sh sessions remain available without OSC command-completion integration. A subsequent October 1 fix ends a Linux shell's process tree on session close. Real PTY regressions include a background process ignoring HUP so closing a tab does not leave it behind.

## October 1: UI and functionality follow-up

Windows/Linux checks used the same remote-main revision `0a34ce0`:

- Linux first launch defaults to Bash while honoring an explicitly saved shell. Startup settings hide unavailable cmd.exe/WSL options; examples/fonts use platform-appropriate text.
- Linux folder-context integration uses Thunar custom actions, changing only Terminal Hub entries and retaining others. See [Xfce's custom-action documentation](https://docs.xfce.org/xfce/thunar/custom-actions).
- Fixed empty PTY arguments, shell filenames with spaces, quoted backslashes, and file links truncated by dots in parent directories.
- Checked DarkGlass / Black / White / Paper at 1440×900 and 1100×680. The Linux self-contained app launched in an isolated X11 display and its native window was captured.
- Linux recorded 519 passes/3 skips; after supplying CLI paths, two Claude Chinese editing/resize/multiline and Grok-color cases passed without model requests. Windows recorded 518 passes; a sandbox-denied registry test passed separately with permission, with three configuration-based skips.

These checks excluded actual system candidate windows, Wayland, multiple displays, and manual Thunar menu clicks. SSH command/configuration regressions did not establish an additional real server connection. Existing user settings and the installed Windows copy were unchanged.

An initial isolated-X11 clipboard attempt returned empty text from GTK through shortcuts/context paste while normal keys worked. The connection was interrupted before diagnosis. The follow-up below resolved that uncertainty; the initial attempt itself was not a pass.

### October 1 clipboard follow-up

An ICCCM-compliant CLIPBOARD owner exposing UTF8_STRING/TARGETS was used on isolated Xvfb:

- External → terminal: Ctrl+Shift+V and context paste wrote Chinese text to Bash, with an observed TARGETS→UTF8_STRING handshake.
- Terminal → external: drag selection followed by Ctrl+Shift+C made the selected text readable by an external X11 client.

The earlier empty result was an environment issue: without a CLIPBOARD owner, or with only PRIMARY set, paste reading CLIPBOARD returns empty under ICCCM semantics. No product-code change was required.

For later Windows IME/CLI checks and their limits, see the [v0.4.1 acceptance record (Chinese)](acceptance-v0.4.1-2026-10-04.md). These English pages translate the recorded evidence; they do not represent new platform or CLI acceptance runs.
