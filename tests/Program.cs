using Recorder;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.CoreAudioApi;
using System.Diagnostics;
using System.Text.Json;

var folder = Path.GetFullPath(args.FirstOrDefault() ?? Path.Combine(AppContext.BaseDirectory, "recordings"));
Directory.CreateDirectory(folder);
var display = DisplayInfo.GetAll().First();
Console.WriteLine($"DISPLAY {display.Device} {display.Width}x{display.Height} {display.Rate} ({display.RefreshRate:F3} Hz)");
if (display.RefreshRate < 20 || display.RefreshRate > 600) throw new Exception("Invalid display refresh rate");
var profile = await RecordingSession.DetectProfileAsync(display);
Console.WriteLine("PROFILE " + profile.Label);
foreach (var test in new[] { "silent-system-audio", "intermittent-tone", "video-only", "compatibility" })
{
    var audio = test != "video-only";
    var selected = test == "compatibility" ? new CaptureProfile("libx264", false) : profile;
    Console.WriteLine("START " + test);
    await using var session = await RecordingSession.StartAsync(new(display, folder, audio, true), selected);
    if (test == "intermittent-tone")
    {
        await Task.Delay(2000);
        using var sound = new WasapiOut(AudioClientShareMode.Shared, 100);
        sound.Init(new SignalGenerator(48000, 2) { Frequency = 440, Gain = 0.035, Type = SignalGeneratorType.Sin }.ToWaveProvider());
        sound.Play();
        await Task.Delay(2000);
        sound.Stop();
        await Task.Delay(2000);
    }
    else await Task.Delay(3000);
    var elapsed = session.Elapsed;
    var output = await session.StopAsync();
    var result = await RecordingSession.RunAsync(["-hide_banner", "-loglevel", "error", "-i", output, "-f", "null", "-"], TimeSpan.FromSeconds(30));
    if (result.Code != 0) throw new Exception(result.Error);
    if (File.Exists(session.PartialPath) || new FileInfo(output).Length < 1000) throw new Exception("Finalization failed");
    Console.WriteLine($"PASS {test} | {elapsed.TotalSeconds:F2}s | {output}");
    if (Environment.GetEnvironmentVariable("FFPROBE_PATH") is { } probe)
    {
        var info = new ProcessStartInfo(probe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (var arg in new[] { "-v", "error", "-show_streams", "-show_format", "-of", "json", output }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var json = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new Exception("ffprobe failed");
        using var parsed = JsonDocument.Parse(json);
        var streams = parsed.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var video = streams.Single(s => s.GetProperty("codec_type").GetString() == "video");
        if (video.GetProperty("width").GetInt32() != display.Width || video.GetProperty("height").GetInt32() != display.Height) throw new Exception("Wrong resolution");
        var rate = video.GetProperty("avg_frame_rate").GetString()!.Split('/').Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        if (Math.Abs(rate[0] / rate[1] - display.RefreshRate) > display.RefreshRate * 0.01) throw new Exception("Wrong frame rate");
        var audioStreams = streams.Where(s => s.GetProperty("codec_type").GetString() == "audio").ToArray();
        if (audioStreams.Length != (audio ? 1 : 0)) throw new Exception("Wrong audio stream count");
        if (audio && (audioStreams[0].GetProperty("sample_rate").GetString() != "48000" || audioStreams[0].GetProperty("channels").GetInt32() != 2)) throw new Exception("Wrong audio format");
        Console.WriteLine($"METADATA OK {rate[0] / rate[1]:F3} FPS, audio={audio}");
    }
}
try
{
    await using var invalid = await RecordingSession.StartAsync(new(display with { Width = 0 }, folder, true, true), new("libx264", false));
    throw new Exception("Invalid screen size unexpectedly succeeded");
}
catch (IOException) { Console.WriteLine("PASS failed-start cleanup"); }
