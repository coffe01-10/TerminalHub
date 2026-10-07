# Plugin development and usage

**English** · [简体中文](README.md)

[Install](#install-official-plugins) · [Official plugins](#official-plugins) · [Development tutorial](development-tutorial.en.md) · [API reference](../plugin-sdk.en.md) · [Source](../../plugins) · [Offline manual](index.en.html)

Terminal Hub plugins are .NET 8 / Avalonia 11 libraries running inside the native application. They require Terminal Hub v0.4.0 or later; the current host API is **1**. Official and third-party plugins use the same public interfaces without depending on private application code. Some newer capabilities require the updated host described below, even though the API version remains 1.

The current plugin manager has **Developer guide ↗** and **Official plugins ↗** buttons. They open the bundled offline tutorial and manual in the application's language. Source and SDK links require internet access. Older v0.4.0 builds lack these buttons but support directory import.

## Install official plugins

Open **Plugins** in the main window to enter the marketplace. Find an official extension, select **Install**, then **Open**. All nine extension resources are bundled with the current source build and can be installed offline. Built-in tools are already available. See [Marketplace, placement, and startup settings](marketplace.en.md).

For source development, manual updates, or older import workflows, run from the repository root:

```powershell
pwsh -File scripts/build-official-plugins.ps1
```

This creates nine importable directories and individual ZIP files under `artifacts/official-plugins`: WorkspaceNotes, ScreenClips, CommandWatch, TerminalBroadcast, Snippets, ProjectNavigator, GitWorkbench, PortGuard, and TaskRunner. Extract a ZIP before importing it: the manager accepts directories, not ZIP files.

Project Navigator and Git Workbench need the updated host with project-directory capabilities. Earlier API 1 hosts must be upgraded to use them; the original five plugins remain compatible with the newer host.

Tools open in an independent window by default. The installed card's **Open location** dropdown can immediately move the page to the existing bottom toolbar or right sidebar. Bottom toolbar buttons open the page in the existing right panel, following the Monitor / SSH / Logs interaction. Settings also control startup and shortcut visibility. Project Navigator and Git Workbench have their own settings pages.

To import a directory:

1. Open **Plugins → Import plugin**.
2. Select a directory directly containing `plugin.json`, such as `artifacts/official-plugins/WorkspaceNotes`. Import enables it immediately.
3. Open the independent workspace tools window and select its module in the left navigation.
4. Use **Placement and startup** to set the location, startup behavior, shortcut, order, and workspace scope. Older builds call this module management.

You can also build one plugin with `dotnet build plugins/WorkspaceNotes/WorkspaceNotes.csproj -c Release` and import its `bin/Release/net8.0` directory.

To update, import a new build directory with the same ID. Disabling removes the plugin's UI and subscriptions without closing terminals. Re-enabling restores saved notes; command and clip lists restart empty. Removing a plugin deletes its installation and saved configuration, including its notes.

## Official plugins

### Workspace notes

ID: `official.workspace-notes` · [Source](../../plugins/WorkspaceNotes/WorkspaceNotesPlugin.cs)

Write handoff notes, to-dos, and investigation results. The page shows the active workspace; switching workspaces shows their separate notes. Pending edits are saved every second, and when you select **Save notes**, switch workspaces, or disable the plugin. Closing the tools window preserves editing state.

Notes are stored in the plugin's namespace in local user configuration. Changing the UI language leaves note content intact. There is no Markdown rendering, synchronization, or upload.

### Screen clips

ID: `official.screen-clips` · [Source](../../plugins/ScreenClips/ScreenClipsPlugin.cs)

Activate a terminal and select **Capture current screen**, or run the corresponding command from the command palette. Each clip records the session name, timestamp, and plain text. Select a clip to review, copy, or export it as UTF-8 `.txt`.

Capture reads the entire current screen buffer starting at its first row, not the history position being viewed. Physical rows retain line breaks; trailing spaces and final empty rows are removed. Chinese, combining characters, and emoji are preserved. API 1 has no soft-wrap metadata, so clips cannot reconstruct logical lines. They do not capture complete history, selection, colors, or ANSI sequences.

The latest 20 clips are kept only in memory. Clearing, disabling, or quitting discards them. Copy and export happen only when selected by the user. Review terminal text before sharing it because it may contain private project information.

### Command watch

ID: `official.command-watch` · [Source](../../plugins/CommandWatch/CommandWatchPlugin.cs)

See the latest observed command, duration, and exit code for each terminal running a build, test, or service. The default filter shows the current workspace; turn it off to include all workspaces. This preference is saved locally. Selecting a card activates its terminal, including the separate window of a popped-out session.

Command Watch uses shell-integration start and completion events. Without markers it reports that none have been received. Exit code 0 means completed; nonzero means failed; missing codes are explicitly unknown. It does not infer AI completion, backfill commands from before activation, send commands, or store output.

### Terminal broadcast and command snippets

IDs: `official.terminal-broadcast` and `official.snippets` · [Broadcast source](../../plugins/TerminalBroadcast/TerminalBroadcastPlugin.cs) · [Snippets source](../../plugins/Snippets/SnippetsPlugin.cs)

Terminal Broadcast sends input to the selected scope in the current workspace. Snippets saves reusable commands; selecting a snippet pastes it into the active session without pressing Enter. Settings are stored separately under each plugin ID.

### Project navigator

ID: `official.project-navigator` · [Source](../../plugins/ProjectNavigator/ProjectNavigatorPlugin.cs)

Browse local directories with breadcrumbs and history. Bookmarks support names, groups, ordering, search, and JSON import/export; recent folders are retained too. Browsing changes only the plugin's location. **Change terminal directory** sends a directory command; **New terminal here** preserves the existing session. For an unknown shell state, paste the directory command and press Enter yourself.

Bookmarks and recent folders are shared locally; browsing positions and project selections are tracked per workspace. Following the active terminal is enabled by default. SSH paths are not interpreted as local directories. **Open folder** uses the system file manager. See [Project and Git tools](project-git-workbench.en.md).

### Git workbench and GitHub

ID: `official.git-workbench` · [Source](../../plugins/GitWorkbench/GitWorkbenchPlugin.cs)

Use system Git to review changes and diffs, stage or unstage individual/all files, commit, commit and push, fetch/pull/push, create/switch branches, and inspect history. History defaults to 20 commits and is configurable. Select a remote and target branch on the branches page. Operation output retains the command, working directory, exit code, stdout, and stderr. Open conflicting files for editing. If commit succeeds but push fails, retry push separately.

The GitHub page uses system `gh` and its existing authentication to open the repository/current branch PR, list PRs and issues, inspect details and checks, create draft PRs, and create issue-linked branches. Login opens a one-time terminal. Local Git continues to work without GitHub authentication. See [Project and Git tools](project-git-workbench.en.md).

### Port board

ID: `official.port-guard` · [Source](../../plugins/PortGuard/PortGuardPlugin.cs)

Inspect local TCP listeners and UDP binds. TCP rows must be `LISTENING` / `LISTEN`; UDP rows must have peer `*:*`, excluding temporary UDP sessions. IPv6 ports are extracted using brackets. Windows reads `netstat -ano` and supplements process names with `tasklist`. Linux tries `ss -tulnp`, then `lsof -i -P -n`.

Windows output is decoded using the OEM code page, including code page 936 on Chinese Windows. Parsing uses column positions rather than translated headers. `ss` state is found by token, including `ss -o` output without Recv-Q/Send-Q. Filter by port, process name, or PID, or enter a port to find its owner.

Ending a process requires selecting **End** in the confirmation dialog. Closing the dialog, Cancel, and Esc cancel the action. Only integer PIDs in the snapshot are accepted; 0, 4, the current process, and other manually entered values are rejected. `taskkill /F` / `kill` ends the whole process, not one port; the dialog also explains that PIDs can be reused between the snapshot and the action. Diagnostics only show system command output in a new terminal.

### Task runner

ID: `official.task-runner` · [Source](../../plugins/TaskRunner/TaskRunnerPlugin.cs)

Read `package.json` scripts, Makefile targets, justfile recipes, and `.vscode/tasks.json` from a project. **Follow project / active terminal** defaults to on: the selected project directory takes priority over the active terminal's cwd. Workspace changes resolve the directory again; an empty workspace clears the previous project. Turn following off and choose a directory to pin it per workspace. The directory, paste mode, and follow preference are saved locally.

With **Paste into active session** selected, Run pastes the command without Enter. Otherwise it creates a one-time session using Windows `cmd /d /s /c` or `sh -c`. The command is passed without an extra outer pair of quotes because `cmd /s` strips outer quotes and can otherwise break commands such as `npm run "test app"`.

The plugin generates `npm run <name>`, `make <target>`, or `just <recipe>` rather than executing the file's script body directly. For tasks.json it accepts only `type=shell` with a string `command`, expands `${workspaceFolder}`, and resolves relative `options.cwd` against the project. Missing task files or runnable tasks are explained on the page.

## Develop your own plugin

Start with the [complete tutorial](development-tutorial.en.md) and [Command Draft sample](../../examples/plugins/CommandDraft/README.en.md). The [SDK reference](../plugin-sdk.en.md) describes the API. Official projects reference the public Extensibility SDK; [shared UI helpers](../../plugins/Shared/PluginUi.cs) are compiled into each DLL, so no additional shared helper DLL needs installation.

Start with a cached, lazily created tool page, a command, and configuration saved under the plugin ID. Use dynamic theme resources. Closing a tools window does not disable a plugin. Register subscriptions and timers through `context.Subscribe` / `Schedule` for cleanup on disable. Catch async button errors and call `ReportError`; check `Lifetime` after file selection and return to the Avalonia UI thread before UI work.

`Host.SendInput(id, text)` pastes by default; only explicit `submit: true` sends Enter. Viewing, copying, or switching modules should not execute commands. Relevant regressions cover workspace isolation, terminals surviving disable, configuration restoration, wide characters without duplication, and unknown command states remaining unknown.

## Build and distribute

Packaging copies plugin DLLs, dependency descriptions, manifests, and documentation without scanning user configuration. The nine official plugins need no separately installed third-party DLLs: the host provides the SDK, Core, and Avalonia. Git Workbench additionally needs system Git; GitHub features need `gh`. Port Board uses system netstat, ss, or lsof. Include additional dependencies and resources in a new plugin's directory and verify import from a clean directory.

The newer marketplace/project tools are delivered through source and local builds; inspect release notes for available attachments. See [Project/Git verification](project-git-workbench.en.md). The original three plugins' scenarios and untested environments are recorded in the [2026-10-03 acceptance report (Chinese)](acceptance-2026-10-03.md).

## Maintain the offline manual

The English offline manual is generated from this guide and uses the Chinese manual's shared stylesheet. Edit this Markdown first, then regenerate the checked-in HTML from the repository root:

```powershell
python -m pip install -r docs/plugins/requirements.txt
python -B docs/plugins/generate_manual.py
```

The Python dependency is only for documentation generation, not application builds or reading the manual. Tutorials keep their separate Markdown/HTML pairs.
