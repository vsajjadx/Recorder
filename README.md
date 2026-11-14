# Recorder

Lightweight Windows desktop recorder built with .NET 8 WPF. It detects the primary display resolution and refresh rate, then captures the desktop at those values into H.264 MP4.

## Build

Run `tools\prepare-ffmpeg.ps1` once, then `dotnet publish -c Release -r win-x64 --self-contained true`.

The app uses FFmpeg's native `gdigrab` input. Audio capture is intentionally not included in this minimal build.
