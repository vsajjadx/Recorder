using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Recorder;

// FFmpeg receives normalized PCM through a separate pipe. Its stdin remains free for graceful 'q'.
// A paced writer supplies silence even when WASAPI emits no packets, keeping the audio clock moving.
internal sealed class AudioPipe : IAsyncDisposable
{
    private readonly NamedPipeServerStream _pipe;
    private readonly WasapiLoopbackCapture _capture;
    private readonly BufferedWaveProvider _buffer;
    private readonly IWaveProvider _normalized;
    private readonly CancellationTokenSource _cancel = new();
    private Task? _writer;
    private Exception? _captureError;
    private bool _disposed;
    public string Name { get; } = "Recorder-" + Guid.NewGuid().ToString("N");
    public string DeviceFormat => _capture.WaveFormat.ToString();

    public AudioPipe()
    {
        _capture = new WasapiLoopbackCapture();
        _buffer = new BufferedWaveProvider(_capture.WaveFormat.AsStandardWaveFormat()) { BufferDuration = TimeSpan.FromSeconds(3), DiscardOnBufferOverflow = true, ReadFully = true };
        ISampleProvider samples = _buffer.ToSampleProvider();
        if (samples.WaveFormat.Channels == 1) samples = samples.ToStereo();
        else if (samples.WaveFormat.Channels > 2) samples = new StereoDownmix(samples);
        if (samples.WaveFormat.SampleRate != 48000) samples = new WdlResamplingSampleProvider(samples, 48000);
        _normalized = samples.ToWaveProvider16();
        _pipe = new NamedPipeServerStream(Name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 65536, 65536);
        _capture.DataAvailable += (_, e) => _buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
        _capture.RecordingStopped += (_, e) => _captureError = e.Exception;
    }

    public void Start() => _writer = Task.Run(WriteAsync);
    public Task Completion => _writer ?? Task.CompletedTask;
    private async Task WriteAsync()
    {
        var ct = _cancel.Token;
        await _pipe.WaitForConnectionAsync(ct);
        _capture.StartRecording();
        var clock = Stopwatch.StartNew();
        var bytes = new byte[1920]; // 10 ms, stereo signed 16-bit PCM at 48 kHz.
        long chunks = 0;
        while (!ct.IsCancellationRequested)
        {
            if (_captureError is not null) throw new IOException("The audio output was disconnected. Reconnect it and start a new recording.", _captureError);
            // Leave 30 ms of headroom for WASAPI scheduling jitter, without accumulating latency.
            var due = TimeSpan.FromMilliseconds(chunks * 10 + 30) - clock.Elapsed;
            if (due > TimeSpan.Zero) await Task.Delay(due, ct);
            var read = _normalized.Read(bytes, 0, bytes.Length);
            if (read < bytes.Length) Array.Clear(bytes, read, bytes.Length - read);
            await _pipe.WriteAsync(bytes, ct);
            chunks++;
        }
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _cancel.Cancel();
        _pipe.Dispose();
        if (_writer is not null) { try { await _writer; } catch { /* Completion/MonitorAsync reports recording failures. */ } }
        try { _capture.StopRecording(); }
        finally { _capture.Dispose(); _cancel.Dispose(); }
    }
}

internal sealed class StereoDownmix(ISampleProvider source) : ISampleProvider
{
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
    private float[] _input = [];
    public int Read(float[] buffer, int offset, int count)
    {
        var channels = source.WaveFormat.Channels;
        var frames = count / 2;
        if (_input.Length < frames * channels) _input = new float[frames * channels];
        var got = source.Read(_input, 0, frames * channels) / channels;
        for (var i = 0; i < got; i++)
        {
            var p = i * channels;
            float left = _input[p], right = _input[p + 1];
            // Windows standard 5.1/7.1 order: FL FR FC LFE BL BR [SL SR].
            if (channels >= 3) { left += _input[p + 2] * 0.707f; right += _input[p + 2] * 0.707f; }
            if (channels >= 6) { left += _input[p + 4] * 0.707f; right += _input[p + 5] * 0.707f; }
            if (channels >= 8) { left += _input[p + 6] * 0.707f; right += _input[p + 7] * 0.707f; }
            buffer[offset + i * 2] = Math.Clamp(left, -1, 1);
            buffer[offset + i * 2 + 1] = Math.Clamp(right, -1, 1);
        }
        return got * 2;
    }
}
