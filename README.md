# Terminal Hub / 终端控制中心

Windows-first **Terminal Control Center** — a glassmorphic multi-session terminal host with process monitor, system widgets, bottom action dock, and an AI assistant side panel.

> Platform: **Windows 10/11** (first-class). Built with **Avalonia 11** (`net8.0`) so the full app compiles and runs on both Windows and Linux; the Windows build ships ConPTY for real shells, Linux uses a real `forkpty` PTY (with a `--mock` fallback).

## Product vision

Match the design mockups in [`docs/design/`](docs/design/):

- Left: live session thumbnails (with optional 开发/测试/部署 tags)
- Center: main terminal viewport + Output / Debug / Problems / Search panel
- Right: Processes / Files / Logs / SSH + CPU · Mem · Disk · Net widgets **or** AI assistant checklist
- Bottom: New Session · Monitor · SSH · Logs · Deploy · Settings dock

The main workspace uses a Stage Manager-style session shelf: live thumbnails tilt
in perspective around the active session, flatten on hover, and animate when
selected. The foreground terminal expands with a damped spring without restarting
its PTY. Output and the inspector start collapsed; use the title-bar controls or
`Ctrl+Shift+J` / `Ctrl+Shift+B` to toggle them — bare Ctrl+letter chords stay
reserved for the shell (^B tmux prefix, ^W readline delete-word, ^J newline …).
`Ctrl+Shift+W` closes the active session, `Ctrl+Shift+N` opens a new one.
Input goes directly into the terminal.
The centered dock reveals near the bottom edge by default. Settings offers
auto-hide, always visible (with reserved space), and hidden modes. These choices
are remembered. Smaller windows use a narrower, scrollable shelf. Click a thumbnail or use
`Ctrl+Tab` / `Ctrl+Shift+Tab` to switch sessions.

Current native renders: [1440 × 900](docs/screenshots/stage-1440.png) ·
[1100 × 680](docs/screenshots/stage-1100.png).

The focused layout/input tests use temporary settings and remove them on exit:

```powershell
dotnet test --filter FullyQualifiedName~StageLayoutTests
```

Full requirements: [`docs/PRODUCT.md`](docs/PRODUCT.md)  
Layout regions: [`docs/design/DESIGN.md`](docs/design/DESIGN.md)

## Tech stack

- **UI:** Avalonia 11 (`net8.0`), Fluent theme + custom dark glass resources
- **PTY:** `IPtySession` abstraction in `src/TerminalHub.Core`
  - Windows: `ConPtySession` (CreatePseudoConsole) — `src/TerminalHub.Pty`
  - Linux/macOS: `LinuxPtySession` (`forkpty`) — real shells for dev/test
  - `MockPtySession` for CI/`--mock`
- **Logic/tests:** `TerminalHub.Core` + `TerminalHub.Pty` are fully testable on Linux

## Build & run on Windows

```powershell
# Prerequisites: .NET 8 SDK, Windows 10/11
dotnet restore
dotnet build -c Release
dotnet run --project src/TerminalHub.App
```

Package (Inno Setup script under `packaging/`):

```powershell
scripts\publish-windows.ps1   # publishes win-x64 + builds installer
```

## Build / run / test on Linux

Fully runnable on Linux — real shells via `forkpty`, real `/proc` monitoring,
identical Avalonia UI:

```bash
# deps (Debian/Ubuntu): dotnet-sdk-8.0 libx11-6 libxcb1 libfontconfig1
#                       libice6 libsm6 fonts-noto-cjk
scripts/run-linux.sh              # open GUI on $DISPLAY
scripts/run-linux.sh --headless   # run under Xvfb (no physical display)
scripts/run-linux.sh --mock       # in-memory mock PTY

dotnet test                       # unit + headless-rendered UI frames
scripts/publish-linux.sh          # self-contained linux-x64 binary
```

See [`docs/local-debugging.md`](docs/local-debugging.md) for the full
guide (dependency matrix, Xvfb/xdotool automation, screenshot capture) and
[`docs/screenshots/`](docs/screenshots/) for verified live runs.

Linux runs the identical UI with a real `forkpty` shell; ConPTY code paths are
Windows-only and compile-checked via the shared `IPtySession` abstraction.

## License

MIT — see [LICENSE](LICENSE).
