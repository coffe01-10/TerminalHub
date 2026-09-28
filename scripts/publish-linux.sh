#!/usr/bin/env bash
# Terminal Hub — publish a self-contained Linux x64 binary.
# Output: artifacts/publish/linux-x64/TerminalHub
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO"

DOTNET=${DOTNET:-dotnet}
if ! command -v "$DOTNET" >/dev/null 2>&1 && [ -x "$HOME/.dotnet/dotnet" ]; then
  DOTNET="$HOME/.dotnet/dotnet"
fi
command -v "$DOTNET" >/dev/null || { echo "dotnet SDK not found — see README"; exit 1; }

"$DOTNET" publish src/TerminalHub.App/TerminalHub.App.csproj \
  -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o artifacts/publish/linux-x64

echo "==> artifacts/publish/linux-x64/TerminalHub"
ls -lh artifacts/publish/linux-x64/TerminalHub
