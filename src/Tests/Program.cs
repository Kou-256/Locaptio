using Locaptio.Core.AI;
using System.Diagnostics;
using System.Text;
using Locaptio.Core;
using Locaptio.Core.Audio;
using NAudio.Wave;

if (args.Contains("--mixed-input")) { await MixedInputChecks.RunAsync(); return; }
if (args.Contains("--streaming-translation")) { await StreamingTranslationChecks.RunAsync(); return; }
if (args.Contains("--translation-quality")) { await TranslationQualityChecks.RunAsync(); return; }
if (args.Contains("--windows-captions")) { WindowsCaptionChecks.Run(); return; }
if (args.Contains("--features")) { await FeatureChecks.RunAsync(); return; }
if (args.Contains("--correction-recovery")) { await FeatureChecks.CorrectionRecoveryAsync(); return; }
if (args.Contains("--realtime-isolation")) { await FeatureChecks.RealtimeIsolationAsync(); return; }
if (args.Contains("--continuous-live")) { await RealtimeChecks.RunAsync(!args.Contains("--baseline")); return; }
if (args.Contains("--cuda-live-latency"))
{
    using var prepared = new PreparedInference(Directory.GetCurrentDirectory(), true, "large-v3-turbo", "", "cuda", false, true, false, "", Console.WriteLine);
    await prepared.Ready.WaitAsync(TimeSpan.FromMinutes(3));
    var pipeline = new Pipeline(prepared.Speech, null, japaneseOnly: true, fast: true,
        input: new AudioInputSelection("application", ProcessId: Environment.ProcessId, ProcessStarted: Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks));
    var firstCaption = new TaskCompletionSource<CaptionUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
    var finalCaptions = new System.Collections.Concurrent.ConcurrentQueue<CaptionUpdate>();
    var audioReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    pipeline.Caption += c => { if (!string.IsNullOrWhiteSpace(c.English)) firstCaption.TrySetResult(c); if (c.Final) finalCaptions.Enqueue(c); };
    pipeline.Status += s => { if (s.StartsWith("Audio:")) audioReady.TrySetResult(); };
    using var stop = new CancellationTokenSource(); var running = pipeline.RunAsync(stop.Token);
    try
    {
        await audioReady.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var reader = new WaveFileReader(Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "test-ja.wav"));
        using var output = new WaveOutEvent(); output.Init(reader); output.Play();
        var caption = await firstCaption.Task.WaitAsync(TimeSpan.FromSeconds(15));
        double latency = Stopwatch.GetElapsedTime(caption.Started).TotalMilliseconds;
        await Task.Delay(reader.TotalTime + TimeSpan.FromSeconds(1));
        for (int i = 0; i < 30 && !finalCaptions.Any(c => c.English.Contains("日本語")); i++) await Task.Delay(100);
        string finals = string.Join(" / ", finalCaptions.Select(c => c.English));
        string report = $"PASS: actual WASAPI Japanese CUDA large-v3-turbo; onsetToCaption={latency:F0}ms; STT={caption.SttMs:F0}ms; partial={!caption.Final}; interim={caption.English}; final={finals}";
        if (caption.Final || latency > 2500) throw new Exception("First caption too slow: " + report);
        if (!finals.Contains("日本語") || !finals.Contains("文字起こし")) throw new Exception("Final Japanese quality regression: " + report);
        Console.WriteLine(report); File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "cuda-live-latency-verification.txt"), report);
    }
    finally { stop.Cancel(); try { await running; } catch (OperationCanceledException) { } }
    return;
}
if (args.Contains("--cuda-prepared"))
{
    var clock = Stopwatch.StartNew();
    using var prepared = new PreparedInference(Directory.GetCurrentDirectory(), true, "large-v3-turbo", "", "cuda", false, true, false, "", Console.WriteLine);
    await prepared.Ready.WaitAsync(TimeSpan.FromMinutes(3));
    double cold = clock.Elapsed.TotalMilliseconds;
    int? pid = prepared.Speech.ProcessId;
    using var reader = new AudioFileReader(Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "test-ja.wav"));
    var samples = new List<float>(); float[] buffer = new float[4096]; int count;
    while ((count = reader.Read(buffer, 0, buffer.Length)) > 0) samples.AddRange(buffer.Take(count));
    prepared.Speech.ResetSpeechContext();
    var partial = await prepared.Speech.RecognizeAsync(samples.ToArray(), CancellationToken.None, false);
    var final = await prepared.Speech.RecognizeAsync(samples.ToArray(), CancellationToken.None, true);
    clock.Restart(); await prepared.Ready; double reuse = clock.Elapsed.TotalMilliseconds;
    if (!prepared.IsUsable || prepared.Speech.ProcessId != pid || prepared.Speech.Model != "large-v3-turbo" || prepared.Speech.Backend != "CUDA GPU" || !final.Text.Contains("日本語")) throw new Exception("CUDA preparation/model/reuse failed");
    string report = $"PASS: CUDA large-v3-turbo, coldReady={cold:F0}ms, reusedReady={reuse:F3}ms, sameWorker=True\nPartial {partial.Milliseconds:F0}ms: {partial.Text}\nFinal {final.Milliseconds:F0}ms: {final.Text}";
    Console.WriteLine(report); File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "cuda-prepared-verification.txt"), report); return;
}
if (args.Contains("--llm-correction"))
{
    using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(3));
    using var llm = new LocalLlmBackend(); await llm.StartAsync(Directory.GetCurrentDirectory(), "", false, stop.Token);
    foreach (var sample in new[] { ("ja", "明日の会議は午後3時からです よろしくおねがいします", "会議の予定について確認します。"), ("en", "We need to improov the performence of this system.", "We are discussing software performance."), ("ja", "文字を越しの精度を上げたいです。", "Whisperを使った日本語の文字起こしアプリについて話しています。") })
    {
        var result = await llm.CorrectAsync(sample.Item2, [sample.Item3], sample.Item1, stop.Token);
        if (string.IsNullOrWhiteSpace(result.Text) || sample.Item2.Contains('3') && !result.Text.Contains('3')) throw new Exception("Correction changed number or was empty");
        if (sample.Item2.Contains("文字を越し") && !result.Text.Contains("文字起こし")) throw new Exception("Japanese contextual homophone correction failed: " + result.Text);
        Console.WriteLine($"PASS: local {sample.Item1} correction, {result.Milliseconds:F0}ms: {result.Text}");
    }
    return;
}
if (args.Contains("--fast-speech") || args.Contains("--npu-speech") || args.Contains("--cuda-speech"))
{
    using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(30)); using var speech = new LocalBackend();
    speech.Status += Console.WriteLine;
    await speech.StartAsync(Directory.GetCurrentDirectory(), "speech", false, stop.Token, speechLanguage: "ja", model: "base", device: args.Contains("--npu-speech") ? "npu" : args.Contains("--cuda-speech") ? "cuda" : "gpu", fast: true);
    using var reader = new AudioFileReader(Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "test-ja.wav"));
    var samples = new List<float>(); float[] buffer = new float[4096]; int count;
    while ((count = reader.Read(buffer, 0, buffer.Length)) > 0) samples.AddRange(buffer.Take(count));
    var result = await speech.RecognizeAsync(samples.ToArray(), stop.Token);
    if (!result.Text.Contains("日本語")) throw new Exception("Fast Japanese fixture failed: " + result.Text);
    if (args.Contains("--cuda-speech") && speech.Backend != "CUDA GPU") throw new Exception("Explicit CUDA did not run on CUDA");
    Console.WriteLine($"PASS: fast Japanese {speech.Backend}/{speech.Model}, {result.Milliseconds:F0}ms: {result.Text}"); return;
}

if (args.Contains("--modes"))
{
    async Task Exercise(bool japanese)
    {
        var speech = new FixtureSpeech(japanese ? "これは日本語のテストです。" : "This is a test.");
        var translation = new FixtureTranslation();
        var refiner = new FailingRefiner();
        var updates = new System.Collections.Concurrent.ConcurrentQueue<CaptionUpdate>();
        var statuses = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var pipeline = new Pipeline(speech, translation, japanese, japanese ? null : refiner);
        pipeline.Caption += updates.Enqueue; pipeline.Status += statuses.Enqueue;
        using var stop = new CancellationTokenSource();
        var running = Task.Run(() => pipeline.RunAsync(stop.Token));
        try
        {
            await Task.Delay(600);
            using var output = new WaveOutEvent();
            output.Init(new NAudio.Wave.SampleProviders.SignalGenerator(48000, 2) { Frequency = 440, Gain = 0.05 });
            output.Play(); await Task.Delay(1000); output.Stop();
            for (int i = 0; i < 160 && !(japanese ? updates.Any(c => c.Final) : statuses.Any(s => s.Contains("LLM翻訳を停止"))); i++) await Task.Delay(100);
            if (!updates.Any(c => c.Final)) throw new Exception("Final speech result missing");
            if (japanese)
            {
                if (translation.Calls != 0 || updates.Any(c => c.SourceLanguage != "ja" && !string.IsNullOrWhiteSpace(c.English))) throw new Exception("Japanese mode invoked English translation");
                var text = new TranscriptBuffer(); foreach (var update in updates) text.Update(update);
                if (!text.Export(true).Contains("日本語: ") || text.Export(true).Contains("English:")) throw new Exception("Japanese export is mislabeled");
            }
            else if (refiner.Calls == 0 || !updates.Any(c => c.Japanese == "標準の訳" && c.Final) || !statuses.Any(s => s.Contains("LLM翻訳を停止"))) throw new Exception("LLM failure did not preserve standard translation");
        }
        finally { stop.Cancel(); try { await running; } catch (OperationCanceledException) { } }
    }
    await Exercise(true); Console.WriteLine("PASS: Japanese mode bypasses translation; final results and correctly labeled export");
    await Exercise(false); Console.WriteLine("PASS: LLM failure preserves standard final translation and pipeline continues");
    return;
}

if (args.Contains("--storage"))
{
    var text = new TranscriptBuffer();
    for (int i = 1; i <= 12; i++) text.Update(new(i, 1, $"Sentence {i}.", $"日本語{i}", true, 0, 0, 0));
    text.Update(new(1, 2, "Revised sentence.", null, false, 0, 0, 0));
    text.Update(new(1, 1, "Old sentence.", "古い訳", true, 0, 0, 0));
    string english = text.Export(false), bilingual = text.Export(true);
    if (text.Count != 12 || !english.Contains("Sentence 12.") || !english.Contains("Revised sentence.") || english.Contains("日本語") || bilingual.Contains("古い訳") || !bilingual.Contains("日本語12")) throw new Exception("Transcript snapshot/history failed");
    string folder = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "storage-test");
    Directory.CreateDirectory(folder);
    string txt = Path.Combine(folder, "transcript.txt");
    await File.WriteAllTextAsync(txt, bilingual, new UTF8Encoding(true));
    if (await File.ReadAllTextAsync(txt) != bilingual) throw new Exception("UTF-8 roundtrip failed");
    text.Clear();
    if (text.Count != 0 || text.Export(true) != "") throw new Exception("Clear failed");
    Console.WriteLine("PASS: full transcript, revisions, English/bilingual UTF-8 export, Clear");

    string wav = Path.Combine(folder, "recording.wav");
    var recorder = new LoopbackRecorder();
    using var cancellation = new CancellationTokenSource();
    var recording = Task.Run(() => recorder.RunAsync(wav, cancellation.Token));
    await Task.Delay(1000);
    using (var output = new WaveOutEvent())
    {
        output.Init(new NAudio.Wave.SampleProviders.SignalGenerator(48000, 2) { Frequency = 440, Gain = 0.05 });
        output.Play(); await Task.Delay(1500); output.Stop();
    }
    await Task.Delay(1500); cancellation.Cancel(); await recording;
    using var reader = new WaveFileReader(wav);
    if (reader.WaveFormat.SampleRate != 16000 || reader.WaveFormat.Channels != 1 || reader.WaveFormat.BitsPerSample != 16 || reader.TotalTime.TotalSeconds < 3.8 || reader.TotalTime.TotalSeconds > 6) throw new Exception("Invalid recorded WAV/timeline");
    byte[] bytes = new byte[reader.Length]; reader.ReadExactly(bytes);
    bool audio = false;
    for (int i = 0; i + 1 < bytes.Length; i += 2) if (Math.Abs((int)BitConverter.ToInt16(bytes, i)) > 32) { audio = true; break; }
    if (!audio) throw new Exception("Loopback recording contains no test audio");
    Console.WriteLine($"PASS: real WASAPI recording, WAV header/PCM, silence timeline, cancellation finalization; {reader.TotalTime.TotalSeconds:F2}s; dropped={recorder.DroppedFrames}");
    // Recording must not interfere with subsequent translation/session starts.
    using var secondStop = new CancellationTokenSource(200);
    await recorder.RunAsync(Path.Combine(folder, "recording-second.wav"), secondStop.Token);
    using var second = new WaveFileReader(Path.Combine(folder, "recording-second.wav"));
    if (second.TotalTime.TotalSeconds < 0.1) throw new Exception("Second recording failed");
    Console.WriteLine("PASS: recording restart and silent WAV finalization without models");
    return;
}

string root = Directory.GetCurrentDirectory();
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
using var backend = new LocalBackend();
await backend.StartAsync(root, "translation", true, timeout.Token);
var first = await backend.TranslateAsync("This is a test.", timeout.Token);
if (first.Text != "これはテストです。") throw new Exception("Basic translation failed: " + first.Text);
Console.WriteLine("PASS: offline CPU translation, UTF-8 request, Japanese model path");

int id = backend.ProcessId ?? throw new Exception("Worker not running");
using (var worker = Process.GetProcessById(id)) { worker.Kill(true); await worker.WaitForExitAsync(timeout.Token); }
var recovered = await backend.TranslateAsync("This is a test.", timeout.Token);
if (recovered.Text != first.Text || backend.ProcessId == id) throw new Exception("Worker recovery failed");
Console.WriteLine("PASS: native worker termination followed by CPU restart and replay");

using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel();
    try { await backend.TranslateAsync("This is a test.", cancelled.Token); throw new Exception("Cancellation ignored"); }
    catch (OperationCanceledException) { Console.WriteLine("PASS: cancelled request"); }
}

var start = new ProcessStartInfo(Path.Combine(root, ".venv", "Scripts", "python.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = new UTF8Encoding(false) };
foreach (var arg in new[] { Path.Combine(root, "inference", "worker.py"), "--kind", "speech", "--models", Path.Combine(root, "artifacts", "missing-models") }) start.ArgumentList.Add(arg);
using (var worker = Process.Start(start)!)
{
    var errorDrain = worker.StandardError.ReadToEndAsync(timeout.Token);
    string output = await worker.StandardOutput.ReadToEndAsync(timeout.Token);
    await worker.WaitForExitAsync(timeout.Token); await errorDrain;
    if (worker.ExitCode != 2 || !output.Contains("必要なモデルがインストールされていません")) throw new Exception("Missing models were not handled");
    Console.WriteLine("PASS: missing models reported without download");
}
