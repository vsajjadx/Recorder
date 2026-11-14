param([switch]$Force)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'ffmpeg.exe'
if ((Test-Path -LiteralPath $exe) -and -not $Force) {
    & $exe -version | Select-Object -First 1
    if ($LASTEXITCODE -ne 0) { throw 'Existing FFmpeg is invalid. Use -Force to download again.' }
    return
}
$download = Join-Path $root ('build\dependencies\' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $download -Force | Out-Null
$archive = Join-Path $download 'ffmpeg.zip'
curl.exe --fail --location --connect-timeout 20 --max-time 180 --output $archive https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip
if ($LASTEXITCODE -ne 0) { throw 'FFmpeg download failed. The existing executable has not been changed.' }
Expand-Archive -LiteralPath $archive -DestinationPath $download
$binary = Get-ChildItem -LiteralPath $download -Filter ffmpeg.exe -Recurse | Select-Object -First 1
if (-not $binary) { throw 'The archive does not contain ffmpeg.exe.' }
& $binary.FullName -version | Select-Object -First 1
if ($LASTEXITCODE -ne 0) { throw 'The downloaded FFmpeg could not be executed.' }
Copy-Item -LiteralPath $binary.FullName -Destination $exe -Force
Write-Output "FFmpeg ready: $exe"
