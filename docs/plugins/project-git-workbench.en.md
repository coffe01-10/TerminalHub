# Project Navigator and Git Workbench

**English** · [简体中文](project-git-workbench.md) · [Plugin guide](README.en.md)

This guide describes the **2026-10-05** local development delivery. Both plugins use the updated API 1 host and default to an independent workspace tools window. They can also add an entry to the original Monitor / SSH / Logs / Deploy bottom toolbar or a tab to the original right panel; a bottom entry opens the page on the right.

## Installation

Open Plugins in the main toolbar, install Project Navigator and Git Workbench, then select Open. Resources ship with the current build and support offline installation. See [Marketplace and settings](marketplace.en.md).

For development, run `pwsh -File scripts/build-official-plugins.ps1`, then import `artifacts/official-plugins/ProjectNavigator` and `GitWorkbench`. The updated app is required: older v0.4.0 and earlier v0.4.1 builds do not expose the new directory interface.

Import, disable, and re-enable do not close existing terminals. A new build does not replace a running old instance.

## Project Navigator

Press Enter in the path field or select Go. The directory list shows folder names with full paths in tooltips; double-click to enter. Use breadcrumbs, parent, back, and forward to browse. Back/forward history is isolated per workspace. Choose folder uses the system picker; Open folder uses the system file manager.

On Bookmarks and recent, enter a name/group and bookmark the current directory. Select an existing bookmark, edit its name/group, and update it. Move up/down changes ordering. Search matches names, groups, and paths. JSON import merges by path; export contains only bookmark names, paths, and groups, not terminal or SSH settings.

Bookmarks and recent folders are shared locally across workspaces. Browsing position and project selection are workspace-specific. Closing, disabling, and re-enabling preserve bookmarks; removing the plugin deletes its configuration.

Follow active terminal defaults to on. Manual browsing never sends input. Change terminal directory targets an explicitly selected active local session, then waits for the shell's actual cwd report. Use New terminal here when a command is running, the session is an AI session/in the alternate screen, or shell integration is unavailable. Unsupported automatic shell navigation can paste a directory command for you to review and execute. SSH paths retain remote meaning; file-manager actions operate on local directories.

## Git Workbench

Install system Git and use existing author, SSH, or credential-helper settings. The plugin does not save passwords/tokens. Select a directory manually or follow Project Navigator and the active local terminal. With following off, a pinned directory is saved per workspace.

- Changes distinguishes untracked, unstaged, staged, and conflicting files. A file with both staged and unstaged changes appears twice, each with its corresponding diff.
- Stage/unstage individual or all files. A commit includes only staged content, listed in the commit area.
- Branches supports create/switch, remote/target selection, and fetch/pull/push. Initialize a repository in the selected local directory if needed.
- If Commit and push commits successfully but push fails, the commit remains and its consumed message is cleared. Fix remote/authentication and retry Push.
- Open conflicting files with the default application. After resolving and staging, continue merge/rebase in the terminal.
- History shows 20 commits by default, with a configurable limit. Select a commit to inspect it.
- Operation output retains 30 operations by default: command, directory, output, and exit code. Cancel ends only the plugin's Git/gh process. Refresh afterward to see any changes already made.

Status uses [Git porcelain v1 with NUL separators](https://git-scm.com/docs/git-status) so Chinese names, spaces, and rename arrows do not become ambiguous display escapes. Untracked text previews show up to the first 128K characters; binary files show an explanation.

## GitHub

System `gh` is required. GitHub login opens a one-time terminal running `gh auth login`; the CLI handles login. Restarting the app does not replay that command. Missing tools, authentication, or network access show a reason while local Git remains available.

Open the repository or current branch PR, list PRs/issues, read their bodies, and open their web pages. Checks use [gh pr checks](https://cli.github.com/manual/gh_pr_checks), preserving passed, failed, and pending states. Exit codes indicating failed/pending checks do not become list-read errors.

Push the current branch before creating a draft PR, then enter its title, body, and base branch. Creation specifies the head explicitly, avoiding an implicit push or fork. To create a branch from an issue, select the issue and supply the branch name/base; the plugin creates an associated branch and checks it out. See [gh pr create](https://cli.github.com/manual/gh_pr_create) and [gh issue develop](https://cli.github.com/manual/gh_issue_develop).

## Recorded verification scope

The original targeted tests used disposable directories, removing Git's read-only objects on cleanup. Real Git covered Chinese/special filenames, unstaging an initial repository, staged/unstaged diffs, renames, worktrees, detached HEAD, local bare-remote push/fetch/pull, rejection, and conflict.

Imported DLLs were exercised with Avalonia Headless for bookmarks, ordering, search, import/export, workspace directory/history isolation, following/remote separation, commits, and retry after failure. Fourteen targeted tests and 21 including older plugin compatibility passed; after history isolation was corrected, seven UI regressions passed again.

Real Windows ConPTY checked directory changes containing Chinese, spaces, brackets, and single quotes, plus commits and pushes to a local bare remote. Minimum-window English UI, dark and Paper themes passed Headless rendering checks.

The same day's marketplace/docking work initially passed 61 regressions; after placement was corrected to the existing toolbar/panel, 63 regressions and the final eight marketplace checks passed. See [Marketplace verification](marketplace.en.md#recorded-verification).

GitHub responses and command arguments used replay verification: no test PR, issue branch, or test push was made to the user's live GitHub repository. Manual system-picker/file-manager interaction, live GitHub login/writes, and physical Linux testing were not performed. These are recorded limits rather than claims of success or permanent blockers.
