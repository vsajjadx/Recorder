param([string]$Destination = 'C:\Recorder')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root 'build\win-x64'
$target = [IO.Path]::GetFullPath($Destination)
if ($target.TrimEnd('\') -eq [IO.Path]::GetPathRoot($target)) { throw 'Choose an application folder, not a drive root.' }
foreach ($name in @('Recorder.exe','ffmpeg.exe','THIRD_PARTY.md')) {
    if (-not (Test-Path -LiteralPath (Join-Path $source $name))) { throw "Missing build file: $name. Run tools\build.ps1." }
}
$processes = @(Get-CimInstance Win32_Process)
$apps = @($processes | Where-Object { $_.Name -eq 'Recorder.exe' -and $_.ExecutablePath -eq (Join-Path $target 'Recorder.exe') })
foreach ($app in $apps) {
    if ($processes | Where-Object { $_.Name -eq 'ffmpeg.exe' -and $_.ParentProcessId -eq $app.ProcessId }) { throw 'A recording is active. Stop and save it before installing.' }
    $running = Get-Process -Id $app.ProcessId -ErrorAction SilentlyContinue
    if ($running) { [void]$running.CloseMainWindow(); if (-not $running.WaitForExit(5000)) { throw 'Close Recorder before installing.' } }
}
New-Item -ItemType Directory -Path $target -Force | Out-Null
if (Test-Path -LiteralPath (Join-Path $target 'Recorder.exe')) {
    $backup = Join-Path $root ('build\backups\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $target 'Recorder.exe') -Destination $backup
}
foreach ($name in @('Recorder.exe','ffmpeg.exe','THIRD_PARTY.md')) { Copy-Item -LiteralPath (Join-Path $source $name) -Destination $target -Force }
Copy-Item -LiteralPath (Join-Path $source 'licenses') -Destination $target -Recurse -Force
$shell = New-Object -ComObject WScript.Shell
$menu = Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs'
New-Item -ItemType Directory -Path $menu -Force | Out-Null
foreach ($link in @((Join-Path $menu 'Recorder.lnk'), (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Recorder.lnk'))) {
    $shortcut = $shell.CreateShortcut($link)
    $shortcut.TargetPath = Join-Path $target 'Recorder.exe'
    $shortcut.WorkingDirectory = $target
    $shortcut.Description = 'Recorder - screen and system audio'
    $shortcut.Save()
}
$expected = (Get-FileHash -LiteralPath (Join-Path $source 'Recorder.exe') -Algorithm SHA256).Hash
$actual = (Get-FileHash -LiteralPath (Join-Path $target 'Recorder.exe') -Algorithm SHA256).Hash
if ($expected -ne $actual) { throw 'Installed executable checksum mismatch.' }
Write-Output "Installed Recorder in $target (SHA256 $actual)"
