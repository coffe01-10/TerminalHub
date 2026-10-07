<p align="center">
  <picture>
    <source media="(max-width: 600px)" srcset="docs/readme/hero-en-compact.svg">
    <img src="docs/readme/hero-en.svg" width="1200" alt="Terminal Hub: Let AI code. Stay in control. A native terminal workbench for AI CLIs.">
  </picture>
</p>

<p align="center"><strong>English</strong> / <a href="README.zh-CN.md">简体中文</a></p>
<p align="center"><strong>AI sessions, development servers, and tests. One workspace.</strong></p>

<p align="center">
  <a href="src/TerminalHub.App/TerminalHub.App.csproj"><img src="docs/readme/runtime.svg" height="24" alt=".NET 8"></a>
  <a href="src/TerminalHub.App"><img src="docs/readme/ui.svg" height="24" alt="Avalonia 11"></a>
  <a href="scripts/run-linux.sh"><img src="docs/readme/platform.svg" height="24" alt="Windows / Linux"></a>
  <a href="LICENSE"><img src="docs/readme/license.svg" height="24" alt="MIT License"></a>
</p>

<p align="center">
  <a href="#demo">Watch the film</a> &nbsp; · &nbsp;
  <a href="#quick-start">Get started</a> &nbsp; · &nbsp;
  <a href="#plugins">Plugins</a> &nbsp; · &nbsp;
  <a href="https://github.com/coffe01-10/TerminalHub/releases">Downloads</a>
</p>

Claude Code is editing code. Codex is working on another task. Your development server and tests are still running. As the windows pile up, you need to see what is happening and step in when it matters.

**Terminal Hub brings those independent terminals into a native workbench organized by project.** Find a session in its live preview, split terminals to compare output, and keep useful tools nearby. Keep your attention on the work.

<a id="demo"></a>

## Your workbench, in 66 seconds

https://github.com/user-attachments/assets/5c55badb-4a9a-4e93-874c-4f8be8a4e3d7

<p align="center"><sub>v0.4.1 launch film · From scattered windows to one workbench · Animated feature and interaction showcase.</sub></p>

<a id="features"></a>

## See progress in the background

The session shelf shows live terminal previews. New output indicators and exit codes help you notice changes. Give AI sessions, services, tests, and SSH connections their own place, then click a preview to pick up where you left off.

Organize projects into workspaces and name, group, or pin sessions. Background processes keep running as you switch projects. History, search, and logs help you find important output again.

## Split anywhere. Keep processes running.

Let AI edit code while you watch server logs and test results. Split horizontally, vertically, or inside an existing pane; drag the borders to adjust proportions. Open a session in its own window and bring it back when you are done.

Splitting, popping out, and moving between workspaces reuse the same session without restarting its shell. On the next launch, the saved layout returns with fresh shell processes.

## Chinese input follows the editing cursor

Move through `ab中文cd` and keep typing: composition text and the IME candidate window follow the actual insertion point. Terminal Hub handles wide characters and inverse cursors used by AI CLIs, keeping mixed text, selection, and input aligned.

Select and copy text, search history, paste multiple lines, and adjust the font size from the terminal. See [CLI interaction compatibility](docs/cli-compatibility.en.md) for the verified scope of Claude Code, Codex CLI, and other tools.

<a id="themes"></a>

## Find your theme

Dark Glass, Black, White, and Paper: four distinct palettes and textures.

<table>
  <tr>
    <td width="50%"><a href="docs/readme/theme-glass.svg"><img src="docs/readme/theme-glass.svg" width="580" alt="Dark Glass: depth and light"></a><p align="center"><strong>Dark Glass</strong></p></td>
    <td width="50%"><a href="docs/readme/theme-black.svg"><img src="docs/readme/theme-black.svg" width="580" alt="Black: quiet focus"></a><p align="center"><strong>Black</strong></p></td>
  </tr>
  <tr>
    <td width="50%"><a href="docs/readme/theme-white.svg"><img src="docs/readme/theme-white.svg" width="580" alt="White: room to breathe"></a><p align="center"><strong>White</strong></p></td>
    <td width="50%"><a href="docs/readme/theme-paper.svg"><img src="docs/readme/theme-paper.svg" width="580" alt="Paper: a warmer workspace"></a><p align="center"><strong>Paper</strong></p></td>
  </tr>
</table>

<p align="center"><sub>SVG theme palette illustrations.</sub></p>

<a id="plugins"></a>

## Nine official plugins. One open SDK.

Bring project folders, Git, tasks, and terminal notes into the workbench. Open the plugin marketplace from the toolbar and install bundled extensions as needed. Place their pages in an independent window, the existing bottom toolbar, or the right sidebar.

| Plugin | What it does |
| --- | --- |
| Project navigator | Browse and bookmark folders; open terminals in a selected directory |
| Git workbench | Review diffs, commit and push; manage branches, PRs, and issues |
| Task runner | Run tasks from `package.json`, Makefile, justfile, and `tasks.json` |
| Terminal broadcast | Choose target sessions and send input to them together |
| Command watch | Track commands, duration, and exit codes through shell integration |
| Port board | Inspect listening ports and their processes; end a process after confirmation |
| Command snippets | Save and search commands; paste them into a selected session |
| Workspace notes | Keep separate notes per workspace, saved as you switch |
| Screen clips | Capture terminal screen snippets to review and copy later |

The current source build bundles all nine extensions; see each release's notes for its package contents. See the [marketplace guide](docs/plugins/marketplace.en.md) for installation and placement. Build your own .NET / Avalonia tool pages or register commands with a script manifest, with hot reload during development. Start with the [plugin SDK](docs/plugin-sdk.en.md) or [step-by-step tutorial](docs/plugins/development-tutorial.en.md).

## Native terminals. Real work.

**Avalonia 11 + .NET 8, with Windows ConPTY and Linux PTY.** Claude Code, Codex CLI, and everyday shells run in their own real processes. Install and sign in to AI CLIs separately; authentication, model selection, and permissions follow each tool's settings.

The film's **71% lower CPU use and 76% fewer memory allocations** come from a v0.4.1 before-and-after comparison on the same machine with 10 sessions continuously producing output. These figures describe that measured workload. See the [performance record](docs/performance.en.md) for the data and other workloads.

<a id="quick-start"></a>

## Get started

### Download a release

Visit [Releases](https://github.com/coffe01-10/TerminalHub/releases) and choose the package for your platform. Available archives, installers, and plugin SDKs vary by release. Application packages include the .NET runtime; see the corresponding notes for dependencies, included plugins, and changes.

### Run from source

Requires Git and the **.NET 8 SDK**. Windows uses ConPTY and needs Windows 10 1809 or newer; Linux uses a real PTY and needs a working X11 display.

**Windows**

```powershell
git clone https://github.com/coffe01-10/TerminalHub.git
cd TerminalHub
dotnet restore TerminalHub.sln
dotnet run --project src/TerminalHub.App
```

The default shell is PowerShell 7. If `pwsh` is unavailable, select Windows PowerShell or Command Prompt at startup.

**Linux (Debian / Ubuntu)**

```bash
sudo apt-get install libx11-6 libxcb1 libfontconfig1 libice6 libsm6 fonts-noto-cjk
git clone https://github.com/coffe01-10/TerminalHub.git
cd TerminalHub
bash scripts/run-linux.sh
```

Linux defaults to Bash on first launch. You can also select an installed PowerShell or a custom shell. See [Linux debugging notes](docs/local-debugging.md) for other environments.

### Start your first project

Enter your project directory and run your installed AI CLIs and project commands in separate sessions, for example:

```text
claude
codex
```

First run `echo Hello Terminal Hub` to check that your shell works, then arrange AI sessions, services, and tests around your work. Switching sessions preserves processes; closing a session ends its process. The next launch restores the workspace layout with fresh shell processes.

The app runs as a single instance. After rebuilding, exit the old instance normally before starting the new build.

## Everyday shortcuts

| Action | Default keys |
| --- | --- |
| New / close current session | `Ctrl+Shift+N` / `Ctrl+Shift+W` |
| Next / previous session | `Ctrl+Tab` / `Ctrl+Shift+Tab` |
| Copy selected text / interrupt a program | `Ctrl+Shift+C` / `Ctrl+C` |
| Paste | `Ctrl+V`, `Ctrl+Shift+V`, or `Shift+Insert` |
| Zoom in / out / reset font size | `Ctrl+=` / `Ctrl+-` / `Ctrl+0` |

Session-switching keys are configurable. Hold `Shift` for local selection and history scrolling when an application enables terminal mouse reporting.

<a id="docs"></a>

## Development and documentation

Built with **Avalonia 11 + .NET 8**, with separate layers for the native UI, terminal parsing, screen buffers, session management, and platform PTYs.

```powershell
dotnet build TerminalHub.sln -c Debug
dotnet test tests/TerminalHub.Tests/TerminalHub.Tests.csproj
```

- [Documentation index](docs/README.md): English/Chinese usage, plugin, and development guides.
- [Development guide](docs/development.en.md): code entry points, known pitfalls, and relevant checks.
- [CLI interaction compatibility](docs/cli-compatibility.en.md): tools, protocols, and real CLI verification.
- [Workspace tools](docs/project-features-2026-10-02.en.md): usage details for tasks, output rules, broadcast, remote files, and recording.
- [Plugin development tutorial](docs/plugins/development-tutorial.en.md) ([中文](docs/plugins/development-tutorial.md)): build a complete Command Draft plugin from scratch, with a runnable sample, configuration, events and UI extensions.
- [Plugin guide and official plugins](docs/plugins/README.en.md): install and use all nine official extensions; see the [SDK reference](docs/plugin-sdk.en.md) for API details.
- [Planning record (Chinese)](docs/TODO.md): historical status and upcoming development.
- [README assets](docs/readme/README.en.md): SVG sources and generation.

Suggestions are welcome in [Issues](https://github.com/coffe01-10/TerminalHub/issues). For input or rendering problems, include your OS, shell / CLI version, and reproduction steps.

[MIT License](LICENSE) · Copyright © 2026 Jinhong Chen (coffe01-10)
