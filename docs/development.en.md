# Terminal Hub development guide

**English** · [简体中文](../AGENTS.md) · [Documentation](README.md)

This guide describes current code entry points and the known terminal/plugin pitfalls recorded in the repository's [AGENTS.md](../AGENTS.md). That file remains the working-instruction source for agents. Dated development notes explain history, not permission to implement an entire roadmap or proof of current behavior.

## Project and working conventions

Terminal Hub is a Windows-first native multisession terminal using Avalonia 11 and .NET 8. Windows uses ConPTY; Linux uses a real PTY. Existing features include terminal rendering, previews, splits/pop-out windows, themes, Files/Logs/Processes/SSH panels, and packaging.

- Check `git status --short` and relevant code before changes; preserve unrelated work.
- Keep fixes focused. Commits, pushes, releases, and merges follow the current task's authorization.
- Remove temporary scripts, probes, captures, and configurations after use; retain user files and formal regression fixtures.
- Use the built-in browser for web checks unless requested otherwise. Native issues require native runs or Avalonia Headless; a browser does not validate the terminal.
- Add checks only for an observed bug or a specific named risk. Do not add unrelated gates/scans or broad defensive refactors.
- Report missing tools, credentials, or environments honestly and finish work that does not depend on them. Never label unperformed verification as passed.
- Never commit/upload local settings or SSH records. Package program payloads while excluding `settings.json`, `settings-*.json`, and `.ssh`; preserve user configuration.

## Code entry points

Paths are relative to the repository root.

| Area | Start here |
| --- | --- |
| Text, keyboard, cursor, IME, scrolling | `src/TerminalHub.App/Controls/TerminalView.cs` |
| Default/reversed colors and themes | `src/TerminalHub.App/Controls/TerminalPalette.cs`, `ThemeManager.cs` |
| VT parsing, screen/history buffers, cells | `src/TerminalHub.Core/Terminal/VtParser.cs`, `ScreenBuffer.cs`, `TerminalCell.cs` |
| Stable frames and emulator/PTY connection | `src/TerminalHub.Core/Terminal/TerminalFrame.cs`, `TerminalEmulator.cs` |
| Platform terminal processes | `src/TerminalHub.Pty/ConPtySession.cs`, `LinuxPtySession.cs` |
| Environment and shell integration | `src/TerminalHub.Core/Pty/PtyEnvironment.cs`, `ShellIntegration.cs` |
| Session lifecycle | `src/TerminalHub.Core/Sessions/SessionManager.cs`, `TerminalSessionModel.cs` |
| Main window/layout | `src/TerminalHub.App/Views/MainWindow.axaml` and partials; `src/TerminalHub.App/ViewModels/MainWindowViewModel` partials |
| Session shelf/previews/animation | `src/TerminalHub.App/Controls/StageSurface.cs`, `StageCard.cs`, `StagePreview.cs` |
| Settings/startup | `src/TerminalHub.Core/Settings/AppSettings.cs`, `SettingsStore.cs` |
| Cursor/IME regressions | `tests/TerminalHub.Tests/TerminalImeTests.cs` |

## Known terminal pitfalls

### Claude's editing cursor can differ from its VT cursor

Real Claude Code 2.1.281 ConPTY output showed a hidden VT cursor remaining in a status row or at the end of partial redraws. Its editing cursor is a single inverse character between SGR 7 and SGR 27. At line end it may be an inverse space; Chinese spans the main and continuation cell. For `ab中文cd`, the captured editing column went from 10 through three left presses to 文 at 6, then right to c at 8, using zero-based columns.

Prefer a visible VT cursor. When hidden, recognize a single inverse character inside the observed input region bounded by horizontal separators. Skip wide-character continuation cells and do not mistake a multi-character inverse selection for a cursor. Only use input-end/fallback positions when there is no clear marker. This is compatibility with observed TUI output, not a universal protocol: capture new styles before broadening recognition.

### Resolve colors before inversion

`TerminalColor.Default` depends on its original foreground/background role. Resolve both colors first, then swap the actual colors. Swapping Default markers first, or skipping a default-background fill, can make an inverse block cursor disappear in both light/dark themes. Draw the inverse background even when both original colors are Default; do not add a second synthetic cursor when the application already supplies one.

### Font width and terminal cell width differ

Fallback CJK glyphs need not be exactly two cells wide. Drawing a mixed line at natural font width while placing the IME by cells accumulates gaps. ASCII runs can remain grouped, but non-ASCII placement advances by terminal cells. Text, cursor, IME, and selection must share the same coordinate convention.

Cells store a complete cluster through `Char` plus `Tail`; shared `GraphemeWidth` handles surrogate pairs, combining characters, ZWJ emoji, and flag pairs. This is not a claim of complete Unicode grapheme compliance. Changes must consider parsing, buffering, drawing, and position mapping together.

### IME coordinates can be queried before Render

Composition start or PTY output may trigger an immediate `CursorRectangle` query. Compute from the current stable frame rather than only the previous Render cache. Composition text/candidate windows use the same anchor. Keys consumed by IME must not be sent again to the shell: selecting a candidate with Enter must not also execute a command. Send Shift+Enter CSI-u only after keyboard-protocol negotiation.

### Views share a session

Previews display content without resizing the active PTY to thumbnail dimensions. Main views compare the emulator's real size rather than an old resize cache because another view may have changed it. Read stable `TerminalFrame` snapshots rather than active buffers while PTY threads write. Pop-out/layout changes transfer ownership without closing, restarting, or killing the session.

### ConPTY tests can differ from GUI runs

Redirected test-runner handles can leave child tools incorrectly attached to a console. Claude once appeared noninteractive and required stdin/prompt. Refer to `Program.cs` startup handling and `WindowsStreamingTests.cs`. A probe changing process standard handles restores them in `finally` and uses the ProcessWide collection.

For input capture, type reproduction text/keys without submitting model requests. Do not automatically accept new directory-trust prompts; use an already trusted reproduction directory or report the missing real-tool check. Write VT query responses after releasing the buffer lock: a blocked PTY write inside it can stall drawing.

## Layout, tools, and plugins

Nested layout uses `PaneLayout.cs`, `PaneNode.cs`, `MainWindowViewModel.PaneTree.cs`, and `MainWindow.PaneTree.cs`. Four legacy pane properties remain for settings/fixed-layout compatibility; the tree is the complete structure.

Public plugin contracts live in `src/TerminalHub.Extensibility`; loading/lifecycle is in `App/Plugins/PluginManager.cs`, host operations in `MainWindowViewModel.PluginHost.cs`, and UI extensions in `MainWindow.Plugins.cs`. Independent tools windows remain the default and preserve cached pages on close/reopen. Disabling cleans only that plugin's registrations, not host sessions.

The original `ActionDock` floating toolbar and `InspectorTabs` right panel are the docking targets. Do not create a second bottom bar/panel. `MainWindow.WorkbenchDock.cs` reuses cached controls; `PluginSettings.cs` stores location/startup/shortcut preferences. Before moving a control between windows, detach its old container and drain the old window's layout queue so it does not retain a stale TopLevel.

The marketplace is in `PluginManagerWindow.Market.cs` and its catalog in `OfficialPluginCatalog.cs`. Five built-in tools and nine opt-in official extensions share the manager. Build/publish carries `official-plugins`; startup does not auto-install them. UI helpers in `plugins/Shared` are source-linked into each DLL. See the [plugin SDK](plugin-sdk.en.md), [marketplace](plugins/marketplace.en.md), and [Project/Git guide](plugins/project-git-workbench.en.md).

Specific pitfalls:

- Workspace Notes observes Text property changes synchronously. Queued TextChanged can arrive after a workspace switch and lose the departing note.
- Screen Clips API 1 lacks soft-wrap metadata: capture physical rows, skip hidden/continuation cells, and do not claim logical-line reconstruction.
- Command Watch uses shell events, not output guessing.
- Project directory changes wait for the shell's reported cwd. Git operations run in plugin-owned child processes.
- PortGuard decodes Windows netstat/tasklist/taskkill with its OEM-aware OemProcess, not the fixed UTF-8 PluginProcess. Chinese headers require positional parsing. End is the only confirmation action; X/Cancel/Esc cancel.
- TaskRunner passes commands without extra outer quotes for cmd /s. It resolves following per workspace, retains pinned directories per workspace, and clears an empty workspace instead of retaining another project's path.

## Useful verification

Verify the changed scenario rather than citing a test count. Cursor/IME work should replay real VT output and inspect cell coordinates/rendering, not only input sent to PTY. Add relevant regressions such as Chinese movement crossing a wrong cell, invisible inverse spaces, or extra copy line breaks. Pure prose does not need behavior tests.

Performance comparisons use the same machine, session count, and workload. Distinguish build success, simulation/replay, real CLI operation, and actual system candidate windows. After related checks pass, continue delivery without repeating full suites unless new evidence warrants it.

```powershell
dotnet build TerminalHub.sln -c Debug
dotnet run --project src/TerminalHub.App
dotnet test tests/TerminalHub.Tests/TerminalHub.Tests.csproj --filter FullyQualifiedName~TerminalImeTests
```

Only broaden when changes also touch input, buffers, layouts, or themes:

```powershell
dotnet test tests/TerminalHub.Tests/TerminalHub.Tests.csproj --filter 'FullyQualifiedName~TerminalImeTests|FullyQualifiedName~TerminalInputTests|FullyQualifiedName~TerminalCoreTests|FullyQualifiedName~TerminalStreamingTests|FullyQualifiedName~SplitPaneTests|FullyQualifiedName~ThemeWorkspaceTests'
```

Use `--no-restore` after dependencies are available. Marketplace/project regressions include PluginMarketplaceTests, ProjectGitPluginTests, OfficialPluginTests, PluginRound2Tests, PortGuardPluginTests, and TaskRunnerPluginTests.

The app is single-instance. A newly built process may exit after activating the user's old instance; do not misdiagnose this or terminate the user's terminals. Explain what changed, which checks ran, and what remains unverified. Source/build success does not update the installed or currently running app.
