using System.Diagnostics;
using Locaptio.Core;
using Locaptio.Core.Audio;
using NAudio.Wave;

internal static class SpeakerChecks
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static long At(double seconds) => (long)(seconds * Stopwatch.Frequency);
    public static async Task RunAsync(bool realModels)
    {
        var groups = new SpeakerClusterer(.55, 2);
        Check(groups.Identify([1, 0, 0]).Id == 1 && groups.Identify([.99f, .01f, 0]).Id == 1, "Same voice changed identity");
        Check(groups.Identify([0, 1, 0]).Id == 2 && groups.Identify([0, 0, 1]).Id == 0, "Speaker capacity was not bounded");
        Check(groups.Identify([float.NaN, 0, 0]).Id == 0 && groups.Identify([0, 0, 0]).Id == 0, "Invalid voice vector accepted");
        Check(groups.Identify([1, 1, 0]).Id == 0, "Ambiguous voices should remain unassigned");
        var timeline = new SpeakerTimeline();
        timeline.Add(new(At(1), At(3), 1, .9)); timeline.Add(new(At(3), At(5), 2, .9));
        Check(timeline.Find(At(1), At(2)).Id == 1 && timeline.Find(At(4), At(5)).Id == 2, "Timestamp alignment failed");
        Check(timeline.Find(At(2), At(4)) is { Id: 0, Observed: true }, "Mixed-speaker row was confidently mislabeled");
        Check(!timeline.Find(At(20), At(21)).Observed, "Missing audio was assigned a speaker");
        var windows = new CaptionUpdate(1, 1, "native text", null, true, At(2), 0, 0);
        Check(timeline.Find(windows).Id == 1 && timeline.Find(windows with { SourceCorrected = true, Revision = 2 }).Id == 1, "Correction shifted Windows audio alignment");
        for (int i = 6; i < 20000; i++) timeline.Add(new(At(i), At(i + 1), 1, .9));
        Check(timeline.Count <= 121, "Speaker timeline grew with recording duration");
        var transcript = new TranscriptBuffer(); transcript.Update(windows with { SpeakerId = 2 });
        Check(transcript.Export(false).Contains("[話者2] native text"), "Saved text omitted speaker number");
        await CheckBoundedAsync(); await CheckIdleFlushAsync(); await CheckFailureAsync();
        if (realModels) await CheckModelsAsync();
        Console.WriteLine("PASS: anonymous voice clustering, ambiguous/invalid voices, speaker limit, timestamp alignment, corrections, bounded audio/history, cancellation, model failure and transcript labels");
    }
    private static async Task CheckBoundedAsync()
    {
        var backend = new Controlled(); using var session = new SpeakerRecognitionSession(backend);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Status += _ => ready.TrySetResult();
        using var stop = new CancellationTokenSource(); var running = session.RunAsync(stop.Token);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(2));
        long timestamp = 0;
        void Feed(int frames)
        {
            for (int i = 0; i < frames; i++) { timestamp += At(.02); session.Submit(new(Enumerable.Repeat(.1f, 320).ToArray(), timestamp, 1)); }
        }
        Feed(100); await backend.First.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var clock = Stopwatch.StartNew(); Feed(10000);
        Check(clock.Elapsed.TotalSeconds < 2, "Speaker inference blocked audio capture");
        backend.Release.TrySetResult();
        for (int i = 0; i < 100 && backend.Calls < 3; i++) await Task.Delay(10);
        Check(backend.Calls == 3 && session.Timeline.Count <= 3, "Unbounded speaker backlog");
        stop.Cancel(); await running.WaitAsync(TimeSpan.FromSeconds(1)); Check(backend.Disposed, "Speaker backend survived Stop");
    }
    private static async Task CheckFailureAsync()
    {
        using var session = new SpeakerRecognitionSession(new Failed());
        string status = ""; session.Status += s => status = s;
        await session.RunAsync(CancellationToken.None);
        Check(status.Contains("通常色") && status.Contains("missing"), "Missing models did not fall back safely");
    }
    private static async Task CheckIdleFlushAsync()
    {
        var backend = new Controlled(); using var session = new SpeakerRecognitionSession(backend);
        using var stop = new CancellationTokenSource(); var running = session.RunAsync(stop.Token);
        // Some WASAPI endpoints stop producing frames entirely on silence.
        // A short last utterance must not wait for the next audio packet.
        for (int i = 0; i < 50; i++) session.Submit(new(Enumerable.Repeat(.1f, 320).ToArray(), At(i * .02), 0));
        await backend.First.Task.WaitAsync(TimeSpan.FromSeconds(2));
        stop.Cancel(); await running.WaitAsync(TimeSpan.FromSeconds(1));
        Check(session.Timeline.Count == 0, "Canceled pending inference emitted late speaker results");
    }
    private static async Task CheckModelsAsync()
    {
        using var backend = new SpeakerEmbeddingBackend(Directory.GetCurrentDirectory());
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30)); await backend.StartAsync(stop.Token);
        Check(await backend.ExtractAsync(new float[32000], stop.Token) is null, "Silence was identified as a speaker");
        var clusters = new SpeakerClusterer(); int[] expected = [1, 1, 2, 2]; int index = 0;
        var clock = Stopwatch.StartNew();
        foreach (var name in new[] { "fangjun-sr-1.wav", "fangjun-sr-2.wav", "leijun-sr-1.wav", "leijun-sr-2.wav" })
        {
            using var wave = new WaveFileReader(Path.Combine("artifacts", "speaker-fixtures", name));
            var bytes = new byte[64000]; int n = wave.Read(bytes, 0, bytes.Length);
            Check(wave.WaveFormat.SampleRate == 16000 && wave.WaveFormat.Channels == 1 && wave.WaveFormat.BitsPerSample == 16, "Unexpected official fixture format");
            var samples = new float[n / 2]; for (int i = 0; i < samples.Length; i++) samples[i] = BitConverter.ToInt16(bytes, i * 2) / 32768f;
            var vector = await backend.ExtractAsync(samples, stop.Token);
            Check(vector is { Length: > 0 } && clusters.Identify(vector).Id == expected[index++], "Real voices were not grouped correctly: " + name);
        }
        Console.WriteLine($"PASS: Silero silence rejection and CAMPPlus real-model IPC: 4 official two-speaker recordings grouped 1,1,2,2; mean job {clock.Elapsed.TotalMilliseconds / 4:F0}ms");
    }
    private sealed class Controlled : ISpeakerEmbeddingBackend
    {
        public TaskCompletionSource First = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls; public bool Disposed;
        public Task StartAsync(CancellationToken token) => Task.CompletedTask;
        public async Task<float[]?> ExtractAsync(float[] samples, CancellationToken token) { Interlocked.Increment(ref Calls); First.TrySetResult(); await Release.Task.WaitAsync(token); return [1, 0]; }
        public void Dispose() => Disposed = true;
    }
    private sealed class Failed : ISpeakerEmbeddingBackend
    {
        public Task StartAsync(CancellationToken token) => Task.FromException(new InvalidOperationException("missing model"));
        public Task<float[]?> ExtractAsync(float[] samples, CancellationToken token) => throw new Exception("Should not infer");
        public void Dispose() { }
    }
}
