# 本机调试 — Running & debugging on this Linux box

The app is fully runnable on Linux: real shells via `forkpty`, real system
data via `/proc` + BCL APIs, same Avalonia UI that ships to Windows.

## Quick start

```bash
scripts/run-linux.sh            # opens the GUI on $DISPLAY
scripts/run-linux.sh --headless # no display? runs under Xvfb (still a real GUI)
scripts/run-linux.sh --mock     # force in-memory mock PTY (CI)
```

## Dependencies (Debian/Ubuntu)

```bash
sudo apt-get install -y dotnet-sdk-8.0 \
  libx11-6 libxcb1 libfontconfig1 libice6 libsm6 fonts-noto-cjk
```

`run-linux.sh` checks for the X11 libraries and prints the matching
`apt`/`dnf` line when something is missing.

Fedora equivalents: `libX11 libxcb fontconfig libICE libSM google-noto-sans-cjk-fonts`.

## What runs where

| Piece | Linux | Windows |
|---|---|---|
| `IPtySession` | `LinuxPtySession` (libc `forkpty`) | `ConPtySession` (CreatePseudoConsole) |
| CPU/Mem | `/proc/stat`, `/proc/meminfo` | `GetSystemTimes`, `GlobalMemoryStatusEx` |
| Disk/Net | `DriveInfo`, `NetworkInterface` | same BCL APIs |
| Terminal render | `TerminalView` (Skia) | same |
| AI panel | `MockAiAssistant` | same (`IAiAssistant` hook) |

## Headless verification (no physical display)

The repo's `UiSmokeTests` use `Avalonia.Headless` + Skia to render real frames:

```bash
dotnet test --filter "FullyQualifiedName~UiSmokeTests"
# frames land in tests/TerminalHub.Tests/bin/Debug/net8.0/ui-snapshots/
```

## Driving a live instance under Xvfb (screenshots, input)

```bash
xvfb-run -a -s "-screen 0 1440x900x24" dotnet run --project src/TerminalHub.App &

# find the window, click into the terminal, type, screenshot:
WID=$(xdotool search --name "Terminal Hub" | head -1)
xdotool windowactivate "$WID"
xdotool mousemove --window "$WID" 500 300 click 1
xdotool type --delay 30 'echo hello'; xdotool key Return
xwd -root -silent | convert xwd:- shot.png     # needs x11-apps + imagemagick
```

Verified on this box (see `docs/screenshots/`): three forkpty `bash`
sessions, `echo HELLO_FROM_LINUX_$((40+2))` → `HELLO_FROM_LINUX_42` typed via
real X events, live thumbnails, real process table (Xvfb/TerminalHub/node/…),
real CPU/Mem/Disk/Net widgets.

## CI / pure-headless smoke

```bash
dotnet test            # 36+ tests incl. real forkpty roundtrip + UI renders
dotnet run --project src/TerminalHub.App -- --mock   # no PTY at all
```
