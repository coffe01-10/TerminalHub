# Terminal Hub / 终端控制中心

Windows-first **Terminal Control Center** — a glassmorphic multi-session terminal host with process monitor, system widgets, bottom action dock, and an AI assistant side panel.

> Platform: **Windows 10/11** (first-class). This repo is developed on Linux CI boxes with Windows TFMs / Avalonia fallback; run and package on Windows.

## Product vision

Match the design mockups in [`docs/design/`](docs/design/):

- Left: live session thumbnails (with optional 开发/测试/部署 tags)
- Center: main terminal viewport + Output / Debug / Problems / Search panel
- Right: Processes / Files / Logs / SSH + CPU · Mem · Disk · Net widgets **or** AI assistant checklist
- Bottom: New Session · Monitor · SSH · Logs · Deploy · Settings dock

Full requirements: [`docs/PRODUCT.md`](docs/PRODUCT.md)  
Layout regions: [`docs/design/DESIGN.md`](docs/design/DESIGN.md)

## Tech stack

Preferred (in order):

1. **WinUI 3** (Windows App SDK) + ConPTY
2. **WPF** (.NET 8 `net8.0-windows`) + ConPTY
3. **Avalonia 11** (`net8.0`) if WinUI/WPF cannot iterate on Linux — still ship as a Windows app with a Windows packaging path

PTY is abstracted behind an interface (`IPtySession`); Windows uses ConPTY, Linux uses a mock/stub for non-UI tests.

## Build on Windows

```powershell
# Prerequisites: .NET 8 SDK, Windows 10/11
dotnet restore
dotnet build -c Release
dotnet run --project src/TerminalHub.App
```

Installer (after scripts land):

```powershell
# Inno Setup or NSIS — see scripts/installer/
# or MSIX packaging under packaging/
```

## Build / test on Linux (this box)

```bash
dotnet restore
dotnet build   # may skip windows-only TFMs via conditional; non-UI tests should pass
dotnet test
```

Windows-only ConPTY and WinUI/WPF UI require a Windows machine or CI runner.

## License

MIT — see [LICENSE](LICENSE).
