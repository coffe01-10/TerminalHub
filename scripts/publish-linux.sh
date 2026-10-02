#!/usr/bin/env bash
# Terminal Hub — publish a self-contained Linux x64 binary.
# Output: artifacts/TerminalHub-linux-x64.tar.gz
# Pass MSBuild options such as -p:Version=0.3.3 to override the release version.
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
  -o artifacts/publish/linux-x64 "$@"

echo "==> artifacts/publish/linux-x64/TerminalHub"
ls -lh artifacts/publish/linux-x64/TerminalHub

# Package only the executable and distribution documents, never local settings.
cp LICENSE packaging/QUICKSTART.linux.zh-CN.txt artifacts/publish/linux-x64/
chmod +x artifacts/publish/linux-x64/TerminalHub
# Explicit modes also work when cross-publishing under Git Bash on NTFS.
archive="artifacts/TerminalHub-linux-x64.tar"
trap 'rm -f "$archive"' EXIT
tar -cf "$archive" --mode=0755 -C artifacts/publish/linux-x64 TerminalHub
tar -rf "$archive" --mode=0644 -C artifacts/publish/linux-x64 LICENSE QUICKSTART.linux.zh-CN.txt
gzip -f "$archive"

echo "==> artifacts/TerminalHub-linux-x64.tar.gz"
