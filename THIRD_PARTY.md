# Third-party components

Recorder uses NAudio.Wasapi and NAudio.Core 2.2.1 (MIT):
Copyright 2020 Mark Heath. The full notice is in `licenses/NAudio-MIT.txt`.
https://github.com/naudio/NAudio/tree/v2.2.1
https://github.com/naudio/NAudio/blob/v2.2.1/license.txt

The portable Windows package includes a separate, unmodified FFmpeg executable.
The installed FFmpeg 9.0.1 essentials binary identifies its builder as Gyan.
FFmpeg build configuration and license: `ffmpeg.exe -buildconf` and `ffmpeg.exe -L`.
This build includes GPL components, including libx264, and reports GPL version 3 or later.

FFmpeg project and source: https://ffmpeg.org/ and https://git.ffmpeg.org/ffmpeg.git
Windows binary provider and build/source information: https://www.gyan.dev/ffmpeg/builds/
GPL v3 text: https://www.gnu.org/licenses/gpl-3.0.txt
The full license text is included in `licenses/FFmpeg-GPLv3.txt`.

FFmpeg is invoked as a separate process; it is not statically linked into Recorder.
No ownership of the third-party components is claimed.
