$ErrorActionPreference = 'Stop'
$dir = Split-Path -Parent $PSScriptRoot
$zip = Join-Path $env:TEMP ('recorder-ffmpeg-' + [guid]::NewGuid().ToString('N') + '.zip')
$url = 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip'
Invoke-WebRequest $url -OutFile $zip
$tmp = Join-Path $env:TEMP 'recorder-ffmpeg-extract'
if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
Expand-Archive $zip $tmp -Force
$bin = Get-ChildItem $tmp -Filter ffmpeg.exe -Recurse | Select-Object -First 1
Copy-Item $bin.FullName (Join-Path $dir 'ffmpeg.exe') -Force
Write-Host 'ffmpeg.exe prepared.'
