# Recorder 1.1

A compact, dark Windows recorder for your screen and system audio. No account or background service.

## Use

Choose a screen and save folder, then click **Start recording**. Click **Stop & save** to finish the MP4. **Ctrl+Shift+R** starts/stops from other applications while Recorder is open. Closing the window during a recording saves it before exiting.

- Native display dimensions and fractional refresh rate (for example 59.94 or 74.973 Hz).
- Desktop Duplication capture on single-monitor systems; explicit GDI screen coordinates for multi-monitor setups and compatibility fallback.
- Automatically probes NVIDIA, Intel and AMD H.264 encoders; CPU fallback when hardware encoding is unavailable.
- WASAPI loopback records the default speakers/headphones, with native-format conversion to stereo 48 kHz AAC. Silence keeps its place on the timeline. The microphone is not captured.
- Visible start/stop states, elapsed time, achieved encoder FPS, audio/cursor switches, folder picker and persistent preferences.
- Fragmented MP4 while recording; finalizes into a seekable MP4 on stop. If recording/finalization fails, any recoverable `.partial.mp4` is kept beside the output and the error identifies it.

Capture is high-quality, lossy **8-bit SDR H.264**, not lossless/HDR mastering. Matching the requested FPS does not guarantee unique frames under heavy GPU/CPU load. The UI reports a slow encoder. Audio follows the output device selected at the beginning; reconnecting/changing devices requires a new recording. Protected video may not be capturable. Resolution changes stop/save the current session.

## Build and install

Windows 10/11 x64 and .NET 8 SDK or newer:

```powershell
.\tools\prepare-ffmpeg.ps1
.\tools\build.ps1
.\tools\install.ps1
```

The build script creates a compressed, self-contained executable plus `ffmpeg.exe` and third-party notices in `build\win-x64`. Installation defaults to `C:\Recorder` and creates user Desktop/Start Menu shortcuts. Previous app executables are backed up under `build\backups`; recordings and preferences are preserved. Installation refuses to replace an active recording.

Preferences: `%LOCALAPPDATA%\Recorder\settings.json`. Per-session diagnostics: `%LOCALAPPDATA%\Recorder\logs`.

## Real recording checks

```powershell
dotnet build tests\Recorder.Integration.csproj -c Release
Copy-Item ffmpeg.exe tests\bin\Release\net8.0-windows\
# Optional: provide a local ffprobe to assert stream metadata as well.
$env:FFPROBE_PATH = 'C:\path\to\ffprobe.exe'
dotnet tests\bin\Release\net8.0-windows\Recorder.Integration.dll build\test-recordings
```

These tests capture the real desktop, record silence and a short quiet test tone through the default audio output, exercise video-only and CPU/GDI fallback, and decode the completed files. Run on an unlocked Windows desktop. Test videos remain local under `build`, which Git ignores.

See [THIRD_PARTY.md](THIRD_PARTY.md) for dependency and FFmpeg source/license information.
