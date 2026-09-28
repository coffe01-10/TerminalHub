# Terminal Hub / 终端控制中心

Windows-first **Terminal Control Center** — a glassmorphic multi-session terminal host with process monitor, system widgets, bottom action dock, and an AI assistant side panel.

> Platform: **Windows 10/11** (first-class). Built with **Avalonia 11** (`net8.0`) so the full app compiles and runs on both Windows and Linux; the Windows build ships ConPTY for real shells, Linux uses a real `forkpty` PTY (with a `--mock` fallback).

## Product vision

Match the design mockups in [`docs/design/`](docs/design/):

- Left: live session thumbnails (with optional 开发/测试/部署 tags)
- Center: main terminal viewport + Output / Debug / Problems / Search panel
- Right: Processes / Files / Logs / SSH + CPU · Mem · Disk · Net widgets **or** AI assistant checklist
- Bottom: New Session · Monitor · SSH · Logs · Deploy · Settings dock

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

## Build / test on Linux

```bash
dotnet restore
dotnet build
dotnet test
dotnet run --project src/TerminalHub.App           # runs with real Linux PTY
dotnet run --project src/TerminalHub.App -- --mock # force mock PTY
```

Linux runs the identical UI with a real `forkpty` shell; ConPTY code paths are
Windows-only and compile-checked via the shared `IPtySession` abstraction.

## License

MIT — see [LICENSE](LICENSE).
