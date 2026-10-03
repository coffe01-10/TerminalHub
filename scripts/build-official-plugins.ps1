#requires -Version 7.0
<# .SYNOPSIS Builds official API 1 plugins into importable folders and ZIPs. #>
param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$destinationRoot = Join-Path $repo 'artifacts/official-plugins'
New-Item -ItemType Directory -Path $destinationRoot -Force | Out-Null
foreach ($name in @('WorkspaceNotes', 'ScreenClips', 'CommandWatch')) {
    dotnet build (Join-Path $repo "plugins/$name/$name.csproj") -c $Configuration --disable-build-servers -m:1 -p:UseSharedCompilation=false -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw "$name build failed" }
    $source = Join-Path $repo "plugins/$name/bin/$Configuration/net8.0"
    $destination = Join-Path $destinationRoot $name
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    $payload = @("TerminalHub.Official.$name.dll", "TerminalHub.Official.$name.deps.json", 'plugin.json')
    foreach ($file in $payload) { Copy-Item -LiteralPath (Join-Path $source $file) -Destination $destination -Force }
    $usage = "Terminal Hub / $name / API 1`n`n请打开 index.html 阅读使用说明，或 development-tutorial.html 阅读开发教程。`nEnglish developer tutorial: development-tutorial.en.html`n在插件管理器导入本目录（ZIP 请先解压），再从独立工作区工具窗口选择模块。`n源码：https://github.com/coffe01-10/TerminalHub`n"
    [System.IO.File]::WriteAllText((Join-Path $destination 'USAGE.txt'), $usage)
    Copy-Item -LiteralPath (Join-Path $repo 'docs/plugins/index.html') -Destination $destination -Force
    Copy-Item -LiteralPath (Join-Path $repo 'docs/plugins/development-tutorial.html') -Destination $destination -Force
    Copy-Item -LiteralPath (Join-Path $repo 'docs/plugins/development-tutorial.en.html') -Destination $destination -Force
    Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $destination -Force
    $archive = Join-Path $destinationRoot "TerminalHub-$name-1.0.0.zip"
    # Explicit payload: never include settings or SSH records added beside the package.
    $files = $payload + @('USAGE.txt', 'index.html', 'development-tutorial.html', 'development-tutorial.en.html', 'LICENSE')
    Compress-Archive -LiteralPath ($files | ForEach-Object { Join-Path $destination $_ }) -DestinationPath $archive -Force
    Write-Host "$name -> $destination"
}
