#!/usr/bin/env bash
# Terminal Hub — publish a self-contained Linux x64 binary.
# Output: artifacts/TerminalHub-linux-x64.tar.gz
# Pass MSBuild options such as -p:Version=0.4.0 to override the release version.
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
cp LICENSE packaging/QUICKSTART.linux.zh-CN.txt packaging/QUICKSTART.linux.en.txt artifacts/publish/linux-x64/
chmod +x artifacts/publish/linux-x64/TerminalHub
# Explicit modes also work when cross-publishing under Git Bash on NTFS.
archive="artifacts/TerminalHub-linux-x64.tar"
trap 'rm -f "$archive"' EXIT
# Windows' bsdtar can precede Git Bash's GNU tar on PATH and lacks --mode.
TAR=${TAR:-tar}
if [[ "$OSTYPE" == msys* && "$TAR" == tar ]]; then TAR=/usr/bin/tar; fi
"$TAR" -cf "$archive" --mode=0755 -C artifacts/publish/linux-x64 TerminalHub
"$TAR" -rf "$archive" --mode=0644 -C artifacts/publish/linux-x64 LICENSE QUICKSTART.linux.zh-CN.txt QUICKSTART.linux.en.txt docs/plugins/index.html docs/plugins/index.en.html docs/plugins/development-tutorial.html docs/plugins/development-tutorial.en.html
for plugin in WorkspaceNotes ScreenClips CommandWatch TerminalBroadcast Snippets ProjectNavigator GitWorkbench PortGuard TaskRunner; do
  "$TAR" -rf "$archive" --mode=0644 -C artifacts/publish/linux-x64 \
    "official-plugins/$plugin/TerminalHub.Official.$plugin.dll" \
    "official-plugins/$plugin/TerminalHub.Official.$plugin.deps.json" \
    "official-plugins/$plugin/plugin.json"
done
gzip -f "$archive"

echo "==> artifacts/TerminalHub-linux-x64.tar.gz"
