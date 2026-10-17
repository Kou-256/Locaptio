using System.Diagnostics;
using Locaptio.Core;
using Locaptio.Core.AI;
using Locaptio.Core.Audio;
using NAudio.Wave;

static class FeatureChecks
{
    public static async Task RunAsync()
    {
        using (var microphone = new LoopbackSource(new("microphone")))
        {
            long samples = 0; using var stop = new CancellationTokenSource(900);
            var running = microphone.RunAsync(stop.Token);
            try { await foreach (var frame in microphone.Frames.ReadAllAsync(stop.Token)) samples += frame.Samples.Length; }
            catch (OperationCanceledException) { }
            try { await running; } catch (OperationCanceledException) { }
            if (samples == 0) throw new Exception("Microphone did not produce audio frames");
            Console.WriteLine($"PASS: microphone capture and cancellation, samples={samples}");
        }
        using var self = Process.GetCurrentProcess();
        var selection = new AudioInputSelection("application", ProcessId: self.Id, ProcessStarted: self.StartTime.ToUniversalTime().Ticks);
        using (var capture = new ProcessLoopbackCapture(selection))
        {
            double peak = 0; Exception? failure = null; long bytes = 0;
            capture.DataAvailable += (_, e) => { bytes += e.BytesRecorded; for (int i = 0; i + 1 < e.BytesRecorded; i += 2) peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(e.Buffer, i)) / 32768d); };
            capture.RecordingStopped += (_, e) => failure = e.Exception;
            capture.StartRecording(); await capture.Started.WaitAsync(TimeSpan.FromSeconds(10)); await Task.Delay(100);
            await ToneAsync(1100); capture.StopRecording();
            if (failure is not null) throw failure;
            if (peak < .005 || bytes == 0) throw new Exception("Application loopback did not capture selected process");
            Console.WriteLine($"PASS: selected application WASAPI audio, bytes={bytes}, peak={peak:F3}");
        }
        // Another process's tone must be excluded from this process's capture.
        using (var capture = new ProcessLoopbackCapture(selection))
        {
            double peak = 0; Exception? failure = null;
            capture.DataAvailable += (_, e) => { for (int i = 0; i + 1 < e.BytesRecorded; i += 2) peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(e.Buffer, i)) / 32768d); };
            capture.RecordingStopped += (_, e) => failure = e.Exception;
            capture.StartRecording(); await capture.Started.WaitAsync(TimeSpan.FromSeconds(10));
            // Application loopback includes descendants, so use an unrelated existing process
            // for the negative control instead of this test process.
            using var unrelated = Process.GetProcessesByName("explorer").First();
            using var isolated = new ProcessLoopbackCapture(new("application", ProcessId: unrelated.Id, ProcessStarted: unrelated.StartTime.ToUniversalTime().Ticks));
            double excludedPeak = 0;
            isolated.DataAvailable += (_, e) => { for (int i = 0; i + 1 < e.BytesRecorded; i += 2) excludedPeak = Math.Max(excludedPeak, Math.Abs((int)BitConverter.ToInt16(e.Buffer, i)) / 32768d); };
            isolated.StartRecording(); await isolated.Started.WaitAsync(TimeSpan.FromSeconds(10)); await ToneAsync(1000); isolated.StopRecording(); capture.StopRecording();
            if (failure is not null) throw failure;
            if (excludedPeak > .001 || peak < .005) throw new Exception($"Application isolation failed: included={peak}, excluded={excludedPeak}");
            Console.WriteLine("PASS: application isolation excludes another process's audio");
        }
        string folder = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "feature-tests"); Directory.CreateDirectory(folder);
        foreach (string format in new[] { "wav", "mp3", "m4a" })
        {
            string path = Path.Combine(folder, "recording." + format);
            using var stop = new CancellationTokenSource();
            var recording = new AudioRecording(selection).RunAsync(path, stop.Token);
            await Task.Delay(500); await ToneAsync(1200); stop.Cancel(); await recording;
            using var reader = new AudioFileReader(path);
            float[] samples = new float[(int)reader.Length / 4]; int count = reader.Read(samples, 0, samples.Length);
            if (reader.TotalTime.TotalSeconds < 1 || count == 0 || samples.Take(count).Max(v => Math.Abs(v)) < .005 || File.Exists(path + ".recording.wav")) throw new Exception("Invalid recording: " + path);
            Console.WriteLine($"PASS: {format} encoding and decoding, {reader.TotalTime.TotalSeconds:F2}s, checkpoint cleaned");
        }
        foreach (bool japanese in new[] { true, false })
        {
            var correction = new Correction(); var updates = new System.Collections.Concurrent.ConcurrentQueue<CaptionUpdate>();
            var pipeline = new Pipeline(new FixtureSpeech("raw"), japanese ? null : new SlowTranslation(), japanese, input: selection, fast: true, corrector: correction);
            pipeline.Caption += updates.Enqueue;
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            pipeline.Status += message => Console.WriteLine("Pipeline: " + message);
            pipeline.Status += message => { if (message.StartsWith("Audio:")) ready.TrySetResult(); };
            using var stop = new CancellationTokenSource(); var running = pipeline.RunAsync(stop.Token);
            try
            {
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(10)); await ToneAsync(1800);
                for (int i = 0; i < 80 && !updates.Any(c => c.SourceCorrected && (japanese || c.Japanese is not null)); i++) await Task.Delay(100);
                await Task.Delay(900); await ToneAsync(1800);
                for (int i = 0; i < 100 && !(correction.Contexts.Count > 1 && updates.Count(c => c.SourceCorrected && (japanese || c.Japanese is not null)) > 1); i++) await Task.Delay(100);
                var text = new TranscriptBuffer(); foreach (var update in updates) text.Update(update);
                if (!updates.Any(c => c.SourceCorrected) || text.Export(true).Contains("［AI補正］") || !text.Export(true).Contains("corrected") || correction.Languages.Any(l => l != (japanese ? "ja" : "en")) || !correction.Contexts.Any(c => c.Contains("corrected"))) throw new Exception($"Corrected context/export failed: corrections={correction.Contexts.Count}, updates={updates.Count}, transcript={text.Export(true)}");
                foreach (long id in updates.Select(c => c.Id).Distinct())
                {
                    var latest = updates.Last(c => c.Id == id);
                    if (!latest.SourceCorrected || latest.English != "corrected") throw new Exception("Stale translation overwrote correction");
                }
                Console.WriteLine($"PASS: {(japanese ? "Japanese" : "English")} immediate source then context correction; stale translation rejected and export has no AI marker");
            }
            finally { stop.Cancel(); try { await running.WaitAsync(TimeSpan.FromSeconds(5)); } catch (OperationCanceledException) { } }
        }
    }
    public static async Task CorrectionRecoveryAsync()
    {
        var backend = new RecoveringCorrection();
        var pipeline = new Pipeline(new FixtureSpeech("raw"), null, true, corrector: backend);
        var updates = new System.Collections.Concurrent.ConcurrentQueue<CaptionUpdate>();
        var statuses = new System.Collections.Concurrent.ConcurrentQueue<string>();
        pipeline.Caption += updates.Enqueue; pipeline.Status += statuses.Enqueue;
        using var stop = new CancellationTokenSource();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pipeline.Status += message => { if (message.StartsWith("Audio:")) ready.TrySetResult(); };
        var running = pipeline.RunAsync(stop.Token);
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            for (int utterance = 1; utterance <= 3; utterance++)
            {
                await ToneAsync(1500);
                for (int i = 0; i < 70 && backend.Calls < utterance; i++) await Task.Delay(100);
                await Task.Delay(1000);
            }
            if (backend.Calls < 3 || !updates.Any(c => c.SourceCorrected) || !backend.CorrectedContext || !statuses.Any(s => s.Contains("次の発話で再試行"))) throw new Exception("Correction recovery or corrected context failed");
            Console.WriteLine("PASS: a rejected correction preserves original; later utterances recover and use corrected context");
        }
        finally { stop.Cancel(); try { await running.WaitAsync(TimeSpan.FromSeconds(5)); } catch (OperationCanceledException) { } }
    }
    private sealed class RecoveringCorrection : ISpeechCorrectionBackend
    {
        public int Calls;
        public bool CorrectedContext;
        public Task<InferenceResult> CorrectAsync(string text, string[] context, string language, CancellationToken token)
        {
            Calls++;
            if (Calls == 1) throw new InvalidOperationException("Fixture rejected a number-changing correction");
            CorrectedContext |= context.Contains("corrected");
            return Task.FromResult(new InferenceResult("corrected", 1));
        }
    }
    public static async Task RealtimeIsolationAsync()
    {
        var precise = new SlowPreciseSpeech();
        using var self = Process.GetCurrentProcess();
        var pipeline = new Pipeline(new FixtureSpeech("live"), null, japaneseOnly: true, fast: true,
            input: new AudioInputSelection("application", ProcessId: self.Id, ProcessStarted: self.StartTime.ToUniversalTime().Ticks), accurateSpeech: precise);
        var updates = new System.Collections.Concurrent.ConcurrentQueue<(CaptionUpdate Caption, long At)>();
        pipeline.Caption += c => updates.Enqueue((c, Stopwatch.GetTimestamp()));
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pipeline.Status += s => { if (s.StartsWith("Audio:")) ready.TrySetResult(); };
        using var stop = new CancellationTokenSource(); var running = pipeline.RunAsync(stop.Token);
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10)); await ToneAsync(11500); await Task.Delay(700);
            var during = updates.Where(c => !c.Caption.SourceCorrected && c.At > precise.Started && c.At < precise.Finished).ToArray();
            if (precise.Finished == 0 || during.Length < 6 || !updates.Any(c => c.Caption.SourceCorrected)) throw new Exception("Slow precision decoding blocked live captions");
            double maxLag = during.Max(c => (c.At-c.Caption.AudioThrough)*1000.0/Stopwatch.Frequency);
            if (maxLag > 700) throw new Exception("Live caption queue accumulated stale audio");
            Console.WriteLine($"PASS: 4-second precision request ran independently; {during.Length} live updates continued; max lag={maxLag:F0}ms");
        }
        finally { stop.Cancel(); try { await running.WaitAsync(TimeSpan.FromSeconds(5)); } catch (OperationCanceledException) { } }
    }
    private sealed class SlowPreciseSpeech : ISpeechRecognitionBackend
    {
        public long Started, Finished;
        public async Task<InferenceResult> RecognizeAsync(float[] samples, CancellationToken token, bool final = true)
        {
            if (Started == 0) Started = Stopwatch.GetTimestamp();
            await Task.Delay(4000, token);
            if (Finished == 0) Finished = Stopwatch.GetTimestamp();
            return new("precise",4000);
        }
    }
    private static async Task ToneAsync(int ms)
    {
        using var output = new WaveOutEvent(); output.Init(new NAudio.Wave.SampleProviders.SignalGenerator(48000, 2) { Frequency = 440, Gain = .05 });
        output.Play(); await Task.Delay(ms); output.Stop();
    }
    private sealed class Correction : ISpeechCorrectionBackend
    {
        public readonly List<string[]> Contexts = []; public readonly List<string> Languages = [];
        public async Task<InferenceResult> CorrectAsync(string text, string[] context, string language, CancellationToken token) { Contexts.Add(context); Languages.Add(language); await Task.Delay(60, token); return new("corrected", 60); }
    }
    private sealed class SlowTranslation : ITranslationBackend
    {
        public async Task<InferenceResult> TranslateAsync(string text, CancellationToken token, bool final = true) { await Task.Delay(200, token); return new(text == "corrected" ? "補正した訳" : "古い訳", 200); }
    }
}
