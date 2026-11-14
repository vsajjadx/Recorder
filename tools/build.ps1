param([string]$Output = 'build\win-x64')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    if (-not (Test-Path -LiteralPath (Join-Path $root 'ffmpeg.exe'))) { throw 'Run tools\prepare-ffmpeg.ps1 first.' }
    dotnet publish Recorder.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $Output
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed; installation was not changed.' }
    Copy-Item -LiteralPath (Join-Path $root 'ffmpeg.exe') -Destination $Output -Force
    Copy-Item -LiteralPath (Join-Path $root 'THIRD_PARTY.md') -Destination $Output -Force
    Copy-Item -LiteralPath (Join-Path $root 'licenses') -Destination $Output -Recurse -Force
    Write-Output "Build ready: $((Resolve-Path $Output).Path)"
} finally { Pop-Location }
