#requires -Version 7.0
<#
.SYNOPSIS
  Publish a portable Windows x64 preview ZIP and optionally an Inno Setup installer.

.USAGE
  scripts\publish-windows.ps1 [-SkipInstaller]

  Prerequisites: .NET 8 SDK. For the installer step: Inno Setup 6 (iscc on PATH,
  or set $Iscc to the full path of ISCC.exe).
#>
param(
  [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$out  = Join-Path $repo 'app'

Write-Host "==> dotnet publish (win-x64, self-contained single file)" -ForegroundColor Cyan
dotnet publish "$repo\src\TerminalHub.App\TerminalHub.App.csproj" `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o $out

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
Write-Host "==> Published to $out" -ForegroundColor Green

# A portable preview is useful even without Inno Setup installed.
Copy-Item -LiteralPath (Join-Path $repo 'packaging\QUICKSTART.zh-CN.txt') -Destination $out
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $out
$previewZip = Join-Path $repo 'artifacts\TerminalHub-windows-x64-preview.zip'
# Package the program payload explicitly; personal settings and SSH records
# placed beside a portable executable must never enter a release archive.
$portableNames = @('TerminalHub.exe', 'TerminalHub.Core.pdb', 'TerminalHub.Pty.pdb',
  'TerminalHub.pdb', 'LICENSE', 'QUICKSTART.zh-CN.txt', 'CHANGES-2026-10-01.md', 'PERFORMANCE-2026-10-01.md')
$portableStream = [System.IO.File]::Open($previewZip, [System.IO.FileMode]::Create)
$portableArchive = [System.IO.Compression.ZipArchive]::new($portableStream, [System.IO.Compression.ZipArchiveMode]::Create)
try {
  foreach ($portableName in $portableNames) {
    $portablePath = Join-Path $out $portableName
    if (Test-Path -LiteralPath $portablePath) {
      [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($portableArchive, $portablePath, "app/$portableName") | Out-Null
    }
  }
} finally { $portableArchive.Dispose(); $portableStream.Dispose() }
Write-Host "==> Portable preview: $previewZip" -ForegroundColor Green

if ($SkipInstaller) { exit 0 }

$iscc = if ($env:ISCC) { $env:ISCC } else { (Get-Command iscc.exe -ErrorAction SilentlyContinue)?.Source }
if (-not $iscc) {
  $candidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
  )
  $iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $iscc) {
  Write-Warning "Inno Setup (iscc.exe) not found — skipping installer build."
  Write-Warning "Install Inno Setup 6 and re-run, or run: iscc packaging\TerminalHub.iss"
  exit 0
}

Write-Host "==> Building installer via $iscc" -ForegroundColor Cyan
& $iscc "$repo\packaging\TerminalHub.iss"
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }
Write-Host "==> Installer written to artifacts\installer\" -ForegroundColor Green
