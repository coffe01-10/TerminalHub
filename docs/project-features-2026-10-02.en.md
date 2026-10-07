# Workspace tools guide

**English** · [简体中文](project-features-2026-10-02.md) · [Documentation](README.md)

This guide records the October 2 workspace tabs, output rules, input broadcast, project tasks, SSH remote files, and terminal recording/playback delivery in the native Avalonia app. For later plugin placement and startup controls, see the [marketplace guide](plugins/marketplace.en.md).

## Start the updated app

Save active work and exit the old instance normally, then run from the repository root:

```powershell
dotnet run --project src/TerminalHub.App
```

The app is single-instance. If an older instance is still running, this command activates its window. Updating source/build output does not update an installed or running copy.

## Workspace tabs

Workspaces appear in the main window's second toolbar row. Create a workspace with New workspace; click tabs to switch, drag horizontally to reorder (with edge scrolling), and use the context menu to rename. Order is saved. Esc or releasing outside the tab bar cancels reordering. Tab numbers count sessions, and the active tab uses the theme accent.

Each workspace retains session order, active session, split layout, and pinned/collapsed sidebar preference. Switching preserves background processes. A tab's × closes its workspace and terminals. Restart restores layout with new processes, not the old processes' memory. Search, task views, and output notifications can take you to the owning workspace.

## Workspace tools

Open Workspace tools at the top right. Left navigation selects the five pages; the main area separates lists, editing, and results. Empty states provide a create action. Save changes stores rules/tasks; closing the window also saves them. Layout works with all four themes, scrolling forms and wrapping actions in small windows.

| Page | Usage |
| --- | --- |
| Output rules | Add a keyword/regex and color, optionally enabling notifications. Matching screen text is highlighted; new output increments hit counts. Scan current-workspace screens/history, then double-click a result to locate it. Up to 200 locations are displayed; counts include all matches. Rules apply to all workspaces. |
| Input broadcast | Select at least two running terminals and enable broadcast. Typing, pasting, and dragged paths in that workspace go to the selected targets, which remain visible in the main window. Stop, Esc, or changing workspace ends broadcast. Each target receives its own bracketed-paste encoding. Project-task execution bypasses broadcast. |
| Project tasks | Add a name/command and target PowerShell/Bash session. Run/rerun executes there; Stop sends Ctrl+C and waits for actual completion. Status shows elapsed time/exit code; View terminal returns to the session. Definitions are saved per workspace. |
| Remote files | Choose a host saved in SSH, open a remote directory, double-click to enter, or go to the parent. Upload/download show byte progress and support cancellation. Open an SSH terminal at the remote directory. |
| Recording/playback | Start recording the active terminal at a chosen location, then stop to save `.threc`. Open a recording in an independent read-only player with pause, 0.5 / 1 / 2 / 4× speed, and timeline seeking. |

Tasks use OSC 133 completion and real shell exit codes. When Windows PowerShell cannot load PSReadLine, the task supplies a start marker and the existing prompt supplies completion; system execution policy is unchanged. Custom shell arguments that bypass integration may not produce task-completion events.

## Remote authentication and transfer

Remote files use the local `ssh` SFTP subsystem with existing OpenSSH configuration, keys, and ssh-agent. Complete initial host confirmation in an ordinary SSH terminal first. File transfer uses noninteractive authentication; the file panel cannot accept passwords. It does not read/copy key contents or change host-confirmation policy.

Downloads write a local temporary file before replacing the selected destination; uploads write a remote temporary file and rename it on completion. Without the OpenSSH overwrite-rename extension, replacing an existing remote file can be rejected while preserving the original. Failure/cancellation attempts cleanup. If a disconnected connection prevents confirmation, the UI reports the remote temporary path.

Recordings contain the initial screen, subsequent raw output, timing, and terminal size changes; pre-recording scrollback is excluded. They may contain sensitive displayed text. You choose the location and nothing is uploaded automatically.

## Recorded verification scope

Regressions covered workspace process retention and split isolation; background search navigation; saved layouts with fresh processes; sidebar rebuilds preserving layouts; late-arriving sessions being saved; broadcast targets and bracketed paste; rule matching/navigation; actual task completion; player pause/seeking; Chinese/emoji; starting mid-UTF-8 sequence; end-of-line state, colors, resize events, alternate-screen exit, and full recording save on close.

The October 2 SFTP tests used an in-memory protocol peer to exercise binary packets, Chinese/space/quote-containing names, transfers, progress, cancellation, original-file preservation, and temporary cleanup. Native Headless pages were inspected in four themes and temporary captures removed.

Real Windows PowerShell and PowerShell 7 ConPTY tasks returned exit code 7, with actual output/completion in recordings. That round did not connect to a real SSH server or test physical Linux. Later real SFTP verification is recorded separately in the [October 4 delivery record (Chinese)](round-2026-10-04.md); protocol simulation is not described as a real remote connection.

Related regression command:

```powershell
dotnet test tests/TerminalHub.Tests/TerminalHub.Tests.csproj --filter 'FullyQualifiedName~ProjectFeaturesTests|FullyQualifiedName~SftpTests|FullyQualifiedName~RealPowerShell_TaskCompletion'
```
