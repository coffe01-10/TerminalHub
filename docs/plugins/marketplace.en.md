# Plugin marketplace and docked tools

**English** · [简体中文](marketplace.md) · [Plugin guide](README.en.md)

This guide describes the local development build dated **2026-10-05**. Open **Plugins** in the main toolbar to enter the marketplace. Docked tools use the existing floating bottom toolbar and right panel.

## Choose a tool

The marketplace includes three categories:

- **Built-in tools:** output rules, input broadcast, project tasks, remote files, and recording/playback. Included with the app; open, disable, or move them without downloading anything.
- **Official extensions:** Project Navigator, Git Workbench, Workspace Notes, Screen Clips, Command Watch, Terminal Broadcast, Command Snippets, Port Board, and Task Runner. Cards show installed/not installed. Resources are copied to the user plugin directory and enabled only when you select Install.
- **Custom plugins:** DLL or declarative script plugins installed through Import plugin directory, listed among installed plugins.

Search by name, feature, or plugin ID. Filter all, installed, not installed, built-in, or official tools. Disabling preserves configuration; uninstalling deletes the installation and saved plugin configuration. All nine official resources ship with the current build for offline installation. There is currently no remote community catalog or upload service.

## Placement and startup

Installed cards expose **Open location**. Choosing the bottom toolbar or right sidebar adds an entry immediately without restarting. The default is an independent window, so installing alone does not add a bottom icon or sidebar tab. Use Settings or the placement/startup page to adjust:

| Setting | Behavior |
| --- | --- |
| Independent window | Workspace tools window with left module navigation |
| Bottom toolbar | Adds an icon alongside Monitor / SSH / Logs / Deploy; opens the page in the right panel |
| Right sidebar | Adds a tab alongside Processes / Files / Logs / SSH / Commands / AI |
| Open manually | Keeps the tool closed at app startup |
| Open at startup | Opens it on every app launch |
| Restore last open state | Opens it if it was still open when the app exited; explicitly closing it prevents restoration |
| Show shortcut entry | Shows/hides its bottom icon or sidebar tab; hidden tools remain accessible through the marketplace |

Visibility, workspace scope, and ordering continue to apply. Changing placement moves the existing cached page, preserving notes, input, and selection without restarting or closing terminals. **↗** moves a sidebar page to an independent window; **×** returns to the previous built-in page. Sidebar tabs wrap when needed, and the bottom toolbar scrolls horizontally.

Bottom entries and sidebar tabs share the original right content area. Restoration selects the last plugin in that area. Switching to a built-in tab or explicitly closing the sidebar clears the plugin's open state, but retains its cached page. The independent window tracks its selected tool separately. Opening a page does not trigger Git commits, pushes, or GitHub writes; those use their own action buttons.

## Project Navigator settings

Set whether to follow the active terminal, the default directory, hidden-directory visibility, full-path display, recent-history limit (1–50), and compact layout. With following off, the default directory is used on first open or when a workspace has no browsing history; an empty/nonexistent default falls back to the user directory. Hidden directories include dot-prefixed names and the system Hidden attribute.

Folder names are concise by default, with full paths in tooltips. Text settings save immediately so switching pages or disabling the plugin cannot drop the last edit.

## Git Workbench settings

Set project/terminal following, default remote, PR/issue base branch, history limit (1–200), operation-output limit (1–100), refresh after terminal commands, diff wrapping, and compact layout.

The default remote is a preference when no remote or upstream is selected. The default base branch fills forms for newly opened repositories. History limits apply on refresh; output limits and diff wrapping apply immediately. Changes and diffs stack vertically in a narrow sidebar and appear side by side in wider panels.

Git and gh use system installations and existing authentication. Installing the plugin does not install these tools or save tokens.

## Development and packaging

`dotnet build src/TerminalHub.App/TerminalHub.App.csproj` builds all nine official plugins and copies each DLL, dependency description, and manifest to `official-plugins` in the app output. `dotnet publish` carries those resources too. Windows portable/installer and Linux packaging use the same resource set without collecting user configuration.

Third-party pages registered with `ExtensionSurface.WorkspaceTools` receive the host's placement/startup settings. Register `ExtensionSurface.Settings` for a plugin-specific settings page. Existing API 1 plugins remain supported; older settings without the new fields default to independent-window placement and manual startup.

## Recorded verification

The 2026-10-05 placement correction passed 63 relevant regressions with no failures or skips: marketplace, Project/Git UI, original official plugins, round-two plugins, themes, and layouts. After the final shortcut adjustment, all eight marketplace regressions passed again.

- Install, search, filter, disable, and uninstall against real bundled resources while terminals remain running.
- Move the same notes control between independent, bottom, and sidebar locations without losing notes or sessions.
- Save settings immediately and restore them after disable/re-enable; exercise directory/history limits, base branch, and wrapping.
- Create/close real main windows to check manual startup, startup opening, and restoration of the last page.
- Use the existing `ActionDock` and `InspectorTabs`; original Monitor / SSH / Logs buttons still switch their built-in pages.
- Scroll ten toolbar entries; show a temporary tab for a hidden plugin opened from the marketplace and hide it again on close.
- Render the minimum English marketplace window in dark and Paper themes with Avalonia Headless to verify visible tabs.

These checks caught stale layout references during cross-TopLevel page migration, restoration to an earlier page, and multiple manifests published to one path. Later checks caught duplicate attachment caused by replacing original ListBoxItem containers and original tabs being squeezed offscreen. The six original containers are retained and extra tabs wrap within the available width.

The earlier placement-correction Windows portable build contained seven plugins (21 resource files) at `artifacts/plugin-market-placement/TerminalHub-windows-x64-preview.zip`, with its executable in the adjacent `app` directory. This records that intermediate build; the current catalog has nine plugins. Individual builds are under `artifacts/official-plugins`. Previous previews were retained rather than overwriting a running version.

System-picker/file-manager interaction, installed-package use, physical Linux testing, and live GitHub authentication/writes were not performed in that round. Headless rendering is not a substitute for those checks. The user's running instance was not replaced and no release was uploaded as part of the round.

### If new entries are missing

The commonly used local executable is `app/TerminalHub.exe`. An October 5 follow-up found this path still had an old build; updating a different preview directory does not update it. The common local output was subsequently synchronized. Exit the old instance normally before launching the new build; otherwise single-instance handling activates the old window.

Open Plugins, find the installed Git Workbench card, and change Open location from Independent window to Bottom toolbar or Right sidebar. Install Project Navigator first if needed. The eight marketplace regressions verified that card changes immediately add the original-toolbar icon or sidebar tab without changing user placement preferences or ending terminals.
