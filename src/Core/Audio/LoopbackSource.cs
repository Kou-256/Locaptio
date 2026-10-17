using System.Threading.Channels;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Locaptio.Core.Audio;

public sealed record AudioFrame(float[] Samples, long Timestamp, int Generation);

public sealed class LoopbackSource(AudioInputSelection? input = null) : IDisposable
{
    private readonly Channel<AudioFrame> frames = Channel.CreateBounded<AudioFrame>(new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
    private IWaveIn? capture;
    private readonly object gate = new();
    private string? deviceId;
    private bool broken;
    private int generation;
    private long dropped, systemSamples, microphoneSamples;
    public long SystemSamplesCaptured => Interlocked.Read(ref systemSamples);
    public long MicrophoneSamplesCaptured => Interlocked.Read(ref microphoneSamples);
    public long DroppedFrames => Interlocked.Read(ref dropped);
    public ChannelReader<AudioFrame> Frames => frames.Reader;
    public event Action<string>? Status;

    public async Task RunAsync(CancellationToken token)
    {
        try
        {
            if (input?.Kind == "mixed") { await RunMixedAsync(token); return; }
            using var devices = new MMDeviceEnumerator();
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var selection = input ?? new();
                    using var endpoint = selection.Kind == "application" ? null : string.IsNullOrEmpty(selection.DeviceId) ? devices.GetDefaultAudioEndpoint(selection.Kind == "microphone" ? DataFlow.Capture : DataFlow.Render, Role.Multimedia) : devices.GetDevice(selection.DeviceId);
                    string selectedId = endpoint?.ID ?? $"process:{selection.ProcessId}";
                    if (selectedId != deviceId || broken || capture is null)
                    {
                        Task? ready = null;
                        lock (gate)
                        {
                            CloseCapture();
                            generation++;
                            var localGeneration = generation;
                            capture = selection.Kind == "application" ? new ProcessLoopbackCapture(selection) : selection.Kind == "microphone" ? new WasapiCapture(endpoint!) : new WasapiLoopbackCapture(endpoint!);
                            var buffer = new BufferedWaveProvider(capture.WaveFormat) { ReadFully = false, BufferDuration = TimeSpan.FromSeconds(2) };
                            ISampleProvider samples = new MonoProvider(buffer.ToSampleProvider());
                            var resampler = new WdlResamplingSampleProvider(samples, 16000);
                            capture.DataAvailable += (_, e) =>
                            {
                                    try
                                    {
                                        buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
                                        var output = new float[320];
                                        int read;
                                        while ((read = resampler.Read(output, 0, output.Length)) > 0)
                                        {
                                            var frame = new AudioFrame(read == output.Length ? output : output[..read], System.Diagnostics.Stopwatch.GetTimestamp(), localGeneration);
                                            if (!frames.Writer.TryWrite(frame)) { Interlocked.Increment(ref dropped); generation++; localGeneration = generation; }
                                            output = new float[320];
                                        }
                                    }
                                    catch { broken = true; }
                            };
                            capture.RecordingStopped += (_, e) => { if (e.Exception is not null) { broken = true; Status?.Invoke("音声入力エラー: " + e.Exception.Message); } };
                            deviceId = selectedId;
                            broken = false;
                            capture.StartRecording();
                            if (capture is ProcessLoopbackCapture application) ready = application.Started;
                        }
                        if (ready is not null) await ready.WaitAsync(token);
                        Status?.Invoke("Audio: " + (endpoint?.FriendlyName ?? $"アプリ PID {selection.ProcessId}"));
                    }
                }
                catch (Exception ex) { broken = true; Status?.Invoke("音声デバイスを再接続中: " + ex.Message); }
                await Task.Delay(500, token);
            }
        }
        finally { Dispose(); frames.Writer.TryComplete(); }
    }

    private async Task RunMixedAsync(CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var system = new LoopbackSource(new("system"));
        using var microphone = new LoopbackSource(new("microphone"));
        system.Status += text => Status?.Invoke("PC音声: " + text);
        microphone.Status += text => Status?.Invoke("マイク: " + text);
        var mixer = new AudioMixBuffer();
        async Task PumpAsync(LoopbackSource source, int index)
        {
            int previousGeneration = -1;
            try
            {
                await foreach (var frame in source.Frames.ReadAllAsync(linked.Token))
                {
                    if (frame.Generation != previousGeneration) { mixer.Clear(index); previousGeneration = frame.Generation; }
                    mixer.Add(index, frame.Samples);
                    if (index == 0) Interlocked.Add(ref systemSamples, frame.Samples.Length);
                    else Interlocked.Add(ref microphoneSamples, frame.Samples.Length);
                }
            }
            catch { linked.Cancel(); throw; }
        }
        Task[] tasks = [system.RunAsync(linked.Token), microphone.RunAsync(linked.Token), PumpAsync(system, 0), PumpAsync(microphone, 1)];
        long outputDrops = 0, previousDrops = 0;
        try
        {
            Status?.Invoke("Audio: PC内音声＋マイク（同時入力・各50%でミックス）");
            // Small common jitter buffer; a silent/missing source never blocks the other.
            await Task.Delay(40, linked.Token);
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
            while (await timer.WaitForNextTickAsync(linked.Token))
            {
                foreach (var task in tasks) if (task.IsCompleted) await task;
                long lost = system.DroppedFrames + microphone.DroppedFrames + mixer.DroppedSamples / 320 + outputDrops;
                if (lost != previousDrops) { generation++; previousDrops = lost; }
                Interlocked.Exchange(ref dropped, lost);
                var mixed = new AudioFrame(mixer.Read(320), System.Diagnostics.Stopwatch.GetTimestamp(), generation);
                if (!frames.Writer.TryWrite(mixed)) outputDrops++;
            }
        }
        finally
        {
            linked.Cancel();
            try { await Task.WhenAll(tasks); } catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        }
    }

    private void CloseCapture() { var old = capture; capture = null; old?.StopRecording(); old?.Dispose(); }
    public void Dispose() { lock (gate) CloseCapture(); }

    private sealed class MonoProvider(ISampleProvider source) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
        private float[] input = [];
        public int Read(float[] buffer, int offset, int count)
        {
            int channels = source.WaveFormat.Channels;
            if (input.Length < count * channels) input = new float[count * channels];
            int read = source.Read(input, 0, count * channels) / channels;
            for (int i = 0; i < read; i++) { float sum = 0; for (int c = 0; c < channels; c++) sum += input[i * channels + c]; buffer[offset + i] = sum / channels; }
            return read;
        }
    }
}
