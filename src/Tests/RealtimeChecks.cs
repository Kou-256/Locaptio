using System.Collections.Concurrent;
using System.Diagnostics;
using Locaptio.Core;
using Locaptio.Core.AI;
using Locaptio.Core.Audio;
using NAudio.Wave;

static class RealtimeChecks
{
    public static async Task RunAsync(bool realtime)
    {
        string root = Directory.GetCurrentDirectory();
        using var prepared = new PreparedInference(root, true, "large-v3-turbo", "", "cuda", false, true, false, "", Console.WriteLine, realtime, previewModel: Environment.GetCommandLineArgs().Contains("--tiny-preview") ? "tiny" : "base");
        await Task.WhenAll(prepared.Ready, prepared.AccuracyReady).WaitAsync(TimeSpan.FromMinutes(3));
        var pipeline = new Pipeline(prepared.LiveSpeech, null, japaneseOnly: true, fast: true,
            input: new AudioInputSelection("application", ProcessId: Environment.ProcessId, ProcessStarted: Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks),
            accurateSpeech: prepared.Preview is null ? null : new ReadySpeechBackend(prepared.Speech, prepared.AccuracyReady));
        var updates = new ConcurrentQueue<(CaptionUpdate Caption, long At)>();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pipeline.Caption += c => { if (!string.IsNullOrWhiteSpace(c.English)) updates.Enqueue((c, Stopwatch.GetTimestamp())); };
        pipeline.Status += s => { if (s.StartsWith("Audio:")) ready.TrySetResult(); else Console.WriteLine(s); };
        using var stop = new CancellationTokenSource(); var running = pipeline.RunAsync(stop.Token);
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var reader = new WaveFileReader(Path.Combine(root, "artifacts", "test-ja-continuous.wav"));
            using var output = new WaveOutEvent(); output.Init(reader); long playback = Stopwatch.GetTimestamp(); output.Play();
            await Task.Delay(reader.TotalTime + TimeSpan.FromSeconds(6));
            var live = updates.Where(c => !c.Caption.SourceCorrected).ToArray();
            if (live.Length < 8) throw new Exception("Insufficient continuous live captions");
            double[] delays = live.Where(c => c.Caption.AudioThrough > 0).Select(c => (c.At - c.Caption.AudioThrough) * 1000.0 / Stopwatch.Frequency).Order().ToArray();
            double[] gaps = live.Zip(live.Skip(1), (a,b) => (b.At - a.At) * 1000.0 / Stopwatch.Frequency).Order().ToArray();
            double initial = (live[0].At - live[0].Caption.Started) * 1000.0 / Stopwatch.Frequency;
            double P(double[] data, double p) => data[Math.Min(data.Length - 1, (int)(data.Length * p))];
            var text = new TranscriptBuffer(); foreach (var update in updates) text.Update(update.Caption);
            bool precise = updates.Any(c => c.Caption.SourceCorrected);
            string report = $"mode={(realtime ? "two-stage" : "single-turbo")}; audio={reader.TotalTime.TotalSeconds:F2}s; live={prepared.LiveSpeech.Model}; accuracy={prepared.Speech.Model}; initial={initial:F0}ms; lagP50={P(delays,.5):F0}ms; lagP95={P(delays,.95):F0}ms; lagMax={delays.Max():F0}ms; updateGapP95={P(gaps,.95):F0}ms; updateGapMax={gaps.Max():F0}ms; liveUpdates={live.Length}; accurateUpdates={updates.Count(c=>c.Caption.SourceCorrected)}; droppedAudio={pipeline.DroppedAudio}";
            Console.WriteLine(report);
            File.WriteAllText(Path.Combine(root,"artifacts",realtime? prepared.LiveSpeech.Model == "tiny" ? "tiny-continuous.txt" : "realtime-continuous.txt":"baseline-continuous.txt"), report + "\n" + text.Export(true)
                + "\nTiming: playbackSec, id, revision, final, corrected, sttMs, audioLagMs\n"
                + string.Join("\n",updates.Select(c => $"{(c.At-playback)*1.0/Stopwatch.Frequency:F2}, {c.Caption.Id}, {c.Caption.Revision}, {c.Caption.Final}, {c.Caption.SourceCorrected}, {c.Caption.SttMs:F0}, {(c.At-c.Caption.AudioThrough)*1000.0/Stopwatch.Frequency:F0}")));
            if (realtime && (initial > 1800 || P(delays,.95) > 1800 || !precise || !text.Export(true).Contains("字幕"))) throw new Exception("Realtime latency or accuracy check failed: " + report);
        }
        finally { stop.Cancel(); try { await running; } catch (OperationCanceledException) { } }
    }
}
