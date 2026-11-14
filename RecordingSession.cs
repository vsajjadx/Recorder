using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Recorder;

public sealed record RecordingOptions(DisplayInfo Display, string Folder, bool SystemAudio, bool Cursor);
public sealed record CaptureProfile(string Encoder, bool DesktopDuplication)
{
    public string Label => $"{(Encoder == "h264_nvenc" ? "NVIDIA GPU" : Encoder == "h264_qsv" ? "Intel GPU" : Encoder == "h264_amf" ? "AMD GPU" : "CPU")} · {(DesktopDuplication ? "Desktop Duplication" : "Windows capture")}";
}

public sealed class RecordingSession : IAsyncDisposable
{
    public static string Ffmpeg => Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
    private readonly Process _process;
    private readonly AudioPipe? _audio;
    private readonly StreamWriter _log;
    private readonly Task _errors, _progress;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Stopwatch _clock = new();
    private Task<string>? _stopTask;
    private bool _disposed;
    public string OutputPath { get; }
    public string PartialPath { get; }
    public string LogPath { get; }
    public CaptureProfile Profile { get; }
    public TimeSpan Elapsed => _clock.Elapsed;
    public double ActualFps { get; private set; }
    public double RequestedFps { get; }
    public Task Completion { get; }
    private long _frames;

    private RecordingSession(RecordingOptions options, CaptureProfile profile)
    {
        Profile = profile;
        RequestedFps = options.Display.RefreshRate;
        Directory.CreateDirectory(options.Folder);
        var stem = "Recording_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss_fff");
        OutputPath = Path.Combine(options.Folder, stem + ".mp4");
        PartialPath = Path.Combine(options.Folder, stem + ".partial.mp4");
        Directory.CreateDirectory(Path.Combine(Settings.DataFolder, "logs"));
        LogPath = Path.Combine(Settings.DataFolder, "logs", stem + ".log");
        _log = new StreamWriter(LogPath) { AutoFlush = true };
        try
        {
            _audio = options.SystemAudio ? new AudioPipe() : null;
            var args = new List<string> { "-hide_banner", "-loglevel", "warning", "-stats_period", "0.25", "-progress", "pipe:1", "-n" };
            var d = options.Display;
            if (profile.DesktopDuplication)
                args.AddRange(["-thread_queue_size", "256", "-f", "lavfi", "-i", $"ddagrab=output_idx=0:framerate={d.Rate}:draw_mouse={(options.Cursor ? 1 : 0)}" + (profile.Encoder == "h264_nvenc" ? "" : ",hwdownload,format=bgra")]);
            else
                args.AddRange(["-thread_queue_size", "256", "-f", "gdigrab", "-framerate", d.Rate, "-offset_x", d.X.ToString(), "-offset_y", d.Y.ToString(), "-video_size", $"{d.Width}x{d.Height}", "-draw_mouse", options.Cursor ? "1" : "0", "-i", "desktop"]);
            if (_audio is not null)
                args.AddRange(["-thread_queue_size", "1024", "-f", "s16le", "-ar", "48000", "-ac", "2", "-probesize", "32", "-analyzeduration", "0", "-i", @"\\.\pipe\" + _audio.Name]);
            args.AddRange(["-map", "0:v:0"]);
            if (_audio is not null) args.AddRange(["-map", "1:a:0", "-c:a", "aac", "-b:a", "192k"]);
            args.AddRange(EncoderArguments(profile.Encoder));
            // Hardware frames are already full-size BGRA. Software input needs even dimensions for H.264 4:2:0.
            if (profile.Encoder != "h264_nvenc" || !profile.DesktopDuplication)
                args.AddRange(["-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2", "-pix_fmt", "yuv420p"]);
            args.AddRange(["-r", d.Rate, "-fps_mode", "cfr", "-g", Math.Max(1, (int)Math.Ceiling(d.RefreshRate * 2)).ToString(), "-video_track_timescale", d.RateNumerator.ToString(), "-movflags", "+frag_keyframe+empty_moov+default_base_moof", "-f", "mp4", PartialPath]);
            _log.WriteLine($"Display: {d.Device} {d.Width}x{d.Height} @ {d.Rate}; {profile.Label}");
            _log.WriteLine("Audio input: " + (_audio?.DeviceFormat ?? "off"));
            _process = NewProcess(args);
            _process.Start();
            _audio?.Start();
            _errors = DrainErrorsAsync();
            _progress = ReadProgressAsync();
            Completion = MonitorAsync();
        }
        catch { _audio?.DisposeAsync().AsTask().GetAwaiter().GetResult(); _log.Dispose(); throw; }
    }

    public static async Task<RecordingSession> StartAsync(RecordingOptions options, CaptureProfile profile)
    {
        if (!File.Exists(Ffmpeg)) throw new FileNotFoundException("The recording engine is missing. Reinstall Recorder.", Ffmpeg);
        var session = new RecordingSession(options, profile);
        try
        {
            await session._ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            if (File.Exists(session.PartialPath) && new FileInfo(session.PartialPath).Length == 0) File.Delete(session.PartialPath);
            throw;
        }
    }
    private async Task ReadProgressAsync()
    {
        while (await _process.StandardOutput.ReadLineAsync() is { } line)
        {
            if (line.StartsWith("frame=") && long.TryParse(line[6..], out var frame))
            {
                _frames = frame;
                if (frame > 0 && !_clock.IsRunning) { _clock.Start(); _ready.TrySetResult(); }
            }
            if (line.StartsWith("fps=") && double.TryParse(line[4..], CultureInfo.InvariantCulture, out var fps)) ActualFps = fps;
        }
    }
    private async Task DrainErrorsAsync()
    {
        while (await _process.StandardError.ReadLineAsync() is { } line) await _log.WriteLineAsync(line);
    }
    private async Task MonitorAsync()
    {
        var exit = _process.WaitForExitAsync();
        Exception? audioError = null;
        if (_audio is not null)
        {
            var first = await Task.WhenAny(exit, _audio.Completion);
            if (first == _audio.Completion && !exit.IsCompleted)
            {
                try { await _audio.Completion; }
                catch (Exception e) { audioError = e; _ready.TrySetException(e); await _process.StandardInput.WriteLineAsync("q"); await _process.StandardInput.FlushAsync(); }
            }
        }
        await exit;
        await Task.WhenAll(_errors, _progress);
        _ready.TrySetException(new IOException("The recording engine could not start. See " + LogPath));
        if (audioError is not null) throw audioError;
    }
    public Task<string> StopAsync() => _stopTask ??= FinishAsync();
    private async Task<string> FinishAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                await _process.StandardInput.WriteLineAsync("q");
                await _process.StandardInput.FlushAsync();
            }
            try { await Completion.WaitAsync(TimeSpan.FromSeconds(25)); }
            catch (TimeoutException)
            {
                _process.Kill(true);
                await _process.WaitForExitAsync();
                throw new IOException("Saving took too long. The recoverable recording is at " + PartialPath);
            }
            _clock.Stop();
            if (_audio is not null) await _audio.DisposeAsync();
            if (_process.ExitCode != 0 || _frames == 0) throw new IOException("Recording stopped unexpectedly. Details: " + LogPath + "\nRecoverable file: " + PartialPath);
            // Keep a fragmented, recoverable MP4 until the final seekable MP4 has closed successfully.
            var result = await RunAsync(["-hide_banner", "-loglevel", "error", "-n", "-i", PartialPath, "-map", "0", "-c", "copy", "-movflags", "+faststart", OutputPath], TimeSpan.FromMinutes(5));
            if (result.Code != 0) throw new IOException("Could not finalize MP4. Your recording is still at " + PartialPath + "\n" + result.Error);
            File.Delete(PartialPath);
            return OutputPath;
        }
        finally { await DisposeAsync(); }
    }
    public static string[] EncoderArguments(string encoder) => encoder switch
    {
        "h264_nvenc" => ["-c:v", encoder, "-preset", "p4", "-rc", "vbr", "-cq", "18", "-b:v", "0"],
        "h264_qsv" => ["-c:v", encoder, "-preset", "veryfast", "-global_quality", "18"],
        "h264_amf" => ["-c:v", encoder, "-quality", "balanced", "-rc", "cqp", "-qp_i", "18", "-qp_p", "20"],
        _ => ["-c:v", "libx264", "-preset", "ultrafast", "-crf", "18"]
    };
    public static async Task<CaptureProfile> DetectProfileAsync(DisplayInfo display)
    {
        string encoder = "libx264";
        foreach (var candidate in new[] { "h264_nvenc", "h264_qsv", "h264_amf" })
        {
            var args = new List<string> { "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "color=size=640x360:rate=30", "-frames:v", "1" };
            args.AddRange(EncoderArguments(candidate)); args.AddRange(["-f", "null", "-"]);
            var probe = await RunAsync(args, TimeSpan.FromSeconds(5));
            if (probe.Code == 0) { encoder = candidate; break; }
        }
        // DXGI output indexes differ across adapters. Use the unambiguous GDI coordinates for multi-display setups.
        var dda = display.Primary && DisplayInfo.GetAll().Count == 1;
        if (dda)
        {
            var probe = await RunAsync(["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "ddagrab=framerate=30,hwdownload,format=bgra", "-frames:v", "1", "-f", "null", "-"], TimeSpan.FromSeconds(5));
            dda = probe.Code == 0;
        }
        return new(encoder, dda);
    }
    internal static Process NewProcess(IEnumerable<string> args)
    {
        var info = new ProcessStartInfo(Ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        return new Process { StartInfo = info };
    }
    public static async Task<(int Code, string Error)> RunAsync(IEnumerable<string> args, TimeSpan timeout)
    {
        using var process = NewProcess(args);
        process.Start();
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(timeout); }
        catch (TimeoutException) { process.Kill(true); await process.WaitForExitAsync(); return (-1, "Timed out"); }
        await output;
        return (process.ExitCode, await error);
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_process.HasExited) { _process.Kill(true); await _process.WaitForExitAsync(); }
        if (_audio is not null) { try { await _audio.DisposeAsync(); } catch { } }
        try { await Completion; } catch { }
        _log.Dispose();
        _process.Dispose();
    }
}
