#!/usr/bin/env bash
# Terminal Hub — run on Linux (real forkpty PTYs, Avalonia X11).
# Usage:  scripts/run-linux.sh [--mock] [--headless]
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO"

DOTNET=${DOTNET:-dotnet}
if ! command -v "$DOTNET" >/dev/null 2>&1 && [ -x "$HOME/.dotnet/dotnet" ]; then
  DOTNET="$HOME/.dotnet/dotnet"
fi
command -v "$DOTNET" >/dev/null || { echo "dotnet SDK not found — see README"; exit 1; }

# --- dependency check (Debian/Ubuntu names) --------------------------------
MISSING=()
for lib in libX11.so.6 libxcb.so.1 libfontconfig.so.1 libICE.so.6 libSM.so.6; do
  found=0
  for d in /usr/lib /usr/lib64 /usr/lib/x86_64-linux-gnu /lib/x86_64-linux-gnu; do
    [ -e "$d/$lib" ] && found=1 && break
  done
  [ "$found" = 0 ] && MISSING+=("$lib")
done
if [ "${#MISSING[@]}" -gt 0 ]; then
  cat >&2 <<EOF
Missing X11 libraries: ${MISSING[*]}
Debian/Ubuntu:  sudo apt-get install -y libx11-6 libxcb1 libfontconfig1 libice6 libsm6 fonts-noto-cjk
Fedora:         sudo dnf install libX11 libxcb fontconfig libICE libSM google-noto-sans-cjk-fonts
EOF
  exit 1
fi

ARGS=()
HEADLESS=0
for a in "$@"; do
  case "$a" in
    --mock) ARGS+=("--mock");;
    --headless) HEADLESS=1;;
    *) ARGS+=("$a");;
  esac
done

if [ "$HEADLESS" = 1 ]; then
  # No display? Run under Xvfb — still a real GUI app (real forkpty, real /proc data).
  command -v xvfb-run >/dev/null || { echo "xvfb-run not found: apt-get install xvfb"; exit 1; }
  exec xvfb-run -a -s "-screen 0 1440x900x24" "$DOTNET" run --project src/TerminalHub.App -- "${ARGS[@]}"
fi

if [ -z "${DISPLAY:-}" ]; then
  echo "DISPLAY not set. Options:" >&2
  echo "  - start a desktop session, or" >&2
  echo "  - scripts/run-linux.sh --headless   (Xvfb virtual display)" >&2
  exit 1
fi

exec "$DOTNET" run --project src/TerminalHub.App -- "${ARGS[@]}"
