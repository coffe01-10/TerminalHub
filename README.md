<p align="center">
  <img src="docs/readme/logo.svg" width="520" alt="Terminal Hub: three terminal windows joined at one hub, next to a pixel wordmark">
</p>
<p align="center">
  <strong>English</strong> · <a href="README.zh-CN.md">简体中文</a>
</p>
<p align="center">
  <a href="src/TerminalHub.App/TerminalHub.App.csproj"><img src="docs/readme/runtime.svg" height="24" alt="Runtime: .NET 8"></a>
  <a href="src/TerminalHub.App"><img src="docs/readme/ui.svg" height="24" alt="UI: native Avalonia 11 interface"></a>
  <a href="scripts/run-linux.sh"><img src="docs/readme/platform.svg" height="24" alt="Platforms: Windows first, Linux supported"></a>
  <a href="LICENSE"><img src="docs/readme/license.svg" height="24" alt="License: MIT"></a>
</p>
<p align="center"><strong>Every terminal, one workspace.</strong></p>
<p align="center">
  <img src="docs/readme/typing.svg" width="470" alt="A terminal window typing the restore, build, and run commands, one character at a time">
</p>

Terminal Hub is a Windows-first, Linux-capable native multi-session terminal for developers who keep several projects, dev servers, and command-line tools open at once. Live thumbnails show what each session is doing before you switch, split view puts two sessions side by side, and file, log, process, and SSH panels unfold when you need them.

[Workspace](#the-workspace) · [Quick start](#quick-start) · [Keyboard shortcuts](#keyboard-shortcuts) · [Development](#development-and-checks) · [Documentation](#documentation-and-contributing)

> [!NOTE]
> Terminal Hub is in active development and there is no signed release build yet — the supported way to run it is from source below. Windows is the primary platform; Linux runs on a real PTY but sees less daily use, and macOS is not a verified platform. Restoring a workspace starts fresh shell processes: the layout comes back, running programs and their progress do not.

![Terminal Hub in the dark glass theme: live session thumbnails on the left, the focused terminal in the center, and the action dock at the bottom](docs/readme/workspace.png)

<p align="center"><sub>The workspace image is rendered from current source through Avalonia Headless + Skia, using a mock PTY and sample terminal output — it is not a capture of a real CLI session. <a href="docs/readme/README.md">Asset sources and how to regenerate them</a></sub></p>

## A place for every session

A dev server is running, logs need watching, and a spare shell handles one-off commands. Terminal Hub keeps those sessions in one workspace, so you can see their contents before deciding where to go.

| What you want to do | What the workspace provides |
| --- | --- |
| Find the task that is still running | Live terminal thumbnails in the sidebar; click to switch, drag to reorder |
| Work in two sessions at once | Left/right split, each pane labeled with its session name; click a pane to give it focus |
| Temporarily enlarge one task | Expand a thumbnail into the main area, or pop a session out into its own window |
| See what is behind a command | On-demand file, log, process, SSH, and output panels |
| Adjust the working environment | Four themes, monospace font and size settings, and three dock display modes |
| Come back to a familiar layout | Saved session order, names, directories, shells, split state, and the active session |

Workspace restore re-creates shell processes; the programs that were running, and their progress, are not restored with the layout.

<a id="the-workspace"></a>

## The workspace

### Sessions on the left, work in front

Dragged thumbnails follow the pointer while neighboring cards slide aside, then settle into their new slot. Switching sessions uses a macOS Dock-inspired expansion transition. Thumbnails only preview content — they never resize the terminal's PTY grid to fit themselves.

Click **Split** in the toolbar to place two sessions side by side. Click the pane you want to operate, then a thumbnail, to swap that pane's session. Moving between panes leaves the other side untouched while the sidebar tracks the active session.

![Split view in the light theme: two panes labeled with their session names, the focused pane marked by a highlighted border](docs/readme/split.png)

### Four themes, one set of habits

**Dark glass, black, white, and paper** cover the main window, terminal, thumbnails, menus, and settings. Collapsed session cards keep their own theme details: glass highlights, fine black outlines, soft white shadows, and stacked paper edges.

Settings use a grouped layout for theme, toolbar, output area, dock, fonts, and the shell new terminals start with. The dock appears near the bottom edge by default and can be pinned on or off.

![Grouped settings in the paper theme: colors, workspace toggles, dock, and terminal font share one warm palette](docs/readme/settings.png)

<details>
<summary>Show the black theme</summary>

![Black theme: dark terminal background, restrained card outlines, and a light blue active indicator](docs/readme/black.png)

</details>

### Terminal habits that carry over

Select with the mouse and copy with `Ctrl+Shift+C`; `Ctrl+C` stays with the running program. Chinese IME positioning, history scrolling, text search, and multi-line paste are supported. When an application enables the terminal mouse protocol, hold `Shift` to use the terminal's own selection and scrolling.

Tools like Claude Code and Codex CLI install and run inside the shell as usual. For verified keyboard protocols, mouse behavior, and the tested scope, see [CLI interaction compatibility](docs/cli-compatibility.md).

<a id="quick-start"></a>

## Quick start

### Windows

Requires **Windows 10 1809 or later / Windows 11**, Git, and the **.NET 8 SDK**. The default shell is PowerShell 7, so make sure `pwsh` is on PATH.

```powershell
git clone https://github.com/coffe01-10/TerminalHub.git
cd TerminalHub
dotnet restore TerminalHub.sln
dotnet run --project src/TerminalHub.App
```

Without `pwsh`, open Settings, change "New terminals use" to `cmd.exe`, then create a terminal. WSL, custom shells, and SSH connections require the corresponding programs to be installed locally.

### Linux

Requires the **.NET 8 SDK**, Git, a working X11 display, and font dependencies. On Debian / Ubuntu:

```bash
sudo apt-get install libx11-6 libxcb1 libfontconfig1 libice6 libsm6 fonts-noto-cjk
git clone https://github.com/coffe01-10/TerminalHub.git
cd TerminalHub
bash scripts/run-linux.sh
```

New sessions also default to `pwsh`; with only Bash installed, choose `bash` in Settings before creating a terminal. On a machine without a physical display, install Xvfb and run `bash scripts/run-linux.sh --headless`.

### First run

1. Click into a terminal and run `echo Hello Terminal Hub`; the output should appear in both the terminal and its thumbnail.
2. Press `Ctrl+Shift+N` for a new session and click thumbnails to switch; processes in the original session keep running.
3. Click **Split** and try typing on each side; the pane name and highlighted border show which session receives input.

The app runs as a single instance. After rebuilding, exit the old instance normally before starting the new build.

<a id="keyboard-shortcuts"></a>

## Keyboard shortcuts

| Action | Shortcut |
| --- | --- |
| New / close current session | `Ctrl+Shift+N` / `Ctrl+Shift+W` |
| Next / previous session | `Ctrl+Tab` / `Ctrl+Shift+Tab` |
| Toggle toolbar / output area | `Ctrl+Shift+B` / `Ctrl+Shift+J` |
| Copy selected text | `Ctrl+Shift+C` |
| Paste | `Ctrl+V`, `Ctrl+Shift+V`, or `Shift+Insert` |
| Zoom in / out / reset font size | `Ctrl+=` / `Ctrl+-` / `Ctrl+0` |
| Rename a session | `F2` with the session list focused, or double-click its title |

When the terminal has focus, `F2` goes to the program inside it. Closing a session ends its process.

<details>
<summary>Where are settings stored?</summary>

| Platform | Default settings file |
| --- | --- |
| Windows | `%APPDATA%\TerminalHub\settings.json` |
| Linux | `${XDG_CONFIG_HOME:-~/.config}/terminalhub/settings.json` |

Settings and workspace layout stay on the local machine. Changing the shell in Settings affects terminals created afterwards; existing sessions keep their original processes.

</details>

<a id="development-and-checks"></a>

## Development and checks

The project is **Avalonia 11 + .NET 8**. Windows sessions run on ConPTY, Linux on `forkpty`; VT parsing, the screen buffer, and session management live in a standalone core project.

```text
src/
├── TerminalHub.App     Native UI, terminal rendering, IME, and workspace
├── TerminalHub.Core    VT parsing, screen buffer, sessions, and settings
└── TerminalHub.Pty     Windows ConPTY, Linux PTY, and mock PTY
tests/
└── TerminalHub.Tests   Core logic, native layout, and interaction regressions
```

```powershell
dotnet build TerminalHub.sln -c Debug
dotnet test tests/TerminalHub.Tests/TerminalHub.Tests.csproj
```

<details>
<summary>Build a distributable package</summary>

The Windows x64 publish script needs PowerShell 7. Produce the app with the .NET runtime included:

```powershell
pwsh -File scripts/publish-windows.ps1 -SkipInstaller
```

Output goes to `artifacts/publish/win-x64/`. With Inno Setup 6 installed, drop `-SkipInstaller` to also build the installer under `artifacts/installer/`. The installer closes a running Terminal Hub — save your work and exit normally first.

Linux x64:

```bash
bash scripts/publish-linux.sh
```

Output goes to `artifacts/publish/linux-x64/`. Self-contained builds carry the .NET runtime, but Linux still needs the corresponding graphics system libraries.

</details>

<a id="documentation-and-contributing"></a>

## Documentation and contributing

- [Development guide](AGENTS.md): code entry points, background on known issues, and how to verify changes (Chinese).
- [CLI interaction compatibility](docs/cli-compatibility.md): paste, mouse, shortcuts, and the verified scope.
- [Linux debugging notes](docs/local-debugging.md): X11, Xvfb, and local debugging; historical UI descriptions defer to current source.
- [Product notes](docs/PRODUCT.md): design background and requirements history.

Issues and improvement suggestions are welcome at [GitHub Issues](https://github.com/coffe01-10/TerminalHub/issues). For display or input problems, include your OS, shell / CLI version, reproduction steps, and a screenshot; when contributing a fix, add or run the regressions that cover the specific behavior.

Current focus areas are Unicode graphemes and cell widths, cursor and IME positioning, and multi-session performance.

## License

[MIT](LICENSE) · Copyright © 2026 Jinhong Chen (coffe01-10)
