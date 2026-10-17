using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Locaptio.Core.AI;
using Locaptio.Core.Audio;

namespace Locaptio.Core;

public sealed record CaptionUpdate(long Id, int Revision, string English, string? Japanese, bool Final, long Started, double SttMs, double TranslationMs, string SourceLanguage = "en", bool TranslationRefined = false, bool SourceCorrected = false, long AudioThrough = 0);
internal sealed record SpeechJob(long Id, int Revision, float[] Samples, bool Final, long Started, long AudioThrough);
internal sealed record TranslationJob(CaptionUpdate Caption);

public sealed class Pipeline(ISpeechRecognitionBackend speech, ITranslationBackend? translation, bool japaneseOnly = false, ITranslationRefinementBackend? refiner = null, AudioInputSelection? input = null, bool fast = false, ISpeechCorrectionBackend? corrector = null, ISpeechRecognitionBackend? accurateSpeech = null, string translationGlossary = "")
{
    private readonly Channel<SpeechJob> finalSpeechQueue = Channel.CreateBounded<SpeechJob>(16);
    private readonly Channel<bool> speechWake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
    private SpeechJob? pendingSpeech;
    private readonly Channel<TranslationJob> translationQueue = Channel.CreateBounded<TranslationJob>(16);
    private readonly Channel<CaptionUpdate> refinementQueue = Channel.CreateBounded<CaptionUpdate>(4);
    private readonly ConcurrentDictionary<long, int> revisions = new();
    private readonly Channel<(CaptionUpdate Caption, string[] Context)> correctionQueue = Channel.CreateBounded<(CaptionUpdate, string[])>(4);
    private readonly Channel<(CaptionUpdate Caption, float[] Samples)> accuracyQueue = Channel.CreateBounded<(CaptionUpdate, float[])>(2);
    private readonly ConcurrentDictionary<long, string> context = new();
    public event Action<CaptionUpdate>? Caption;
    public event Action<string>? Status;
    public long DroppedAudio { get; private set; }
    private LoopbackSource? audioSource;
    public int BufferedAudioMs => audioSource?.Frames.CanCount == true ? audioSource.Frames.Count * 20 : 0;

    public async Task RunAsync(CancellationToken token)
    {
        using var source = new LoopbackSource(input);
        audioSource = source;
        source.Status += message => Status?.Invoke(message);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
        async Task Guard(Func<Task> operation)
        {
            try { await operation(); }
            catch { linked.Cancel(); throw; }
        }
        var live = Guard(() => SpeechAsync(linked.Token));
        var accurate = Guard(() => AccurateAsync(linked.Token));
        var correction = Guard(() => CorrectAsync(linked.Token));
        async Task CompleteTranslationsAsync()
        {
            try { await Task.WhenAll(live, accurate, correction); }
            finally { translationQueue.Writer.TryComplete(); }
        }
        try { await Task.WhenAll(Guard(() => source.RunAsync(linked.Token)), Guard(() => SegmentAsync(source, linked.Token)), live, accurate, correction, Guard(CompleteTranslationsAsync), Guard(() => TranslateAsync(linked.Token)), Guard(() => RefineAsync(linked.Token))); }
        finally { audioSource = null; }
    }

    private async Task SegmentAsync(LoopbackSource source, CancellationToken token)
    {
        List<float> samples = new(192000);
        long id = 0, started = 0, lastVoice = 0, audioThrough = 0;
        int revision = 0, lastSnapshot = 0, generation = -1, silentSamples = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
        void Submit(bool final)
        {
            if (samples.Count < 3200) { if (final) samples.Clear(); return; }
            var job = new SpeechJob(id, ++revision, samples.ToArray(), final, started, audioThrough);
            if (final)
            {
                if (!finalSpeechQueue.Writer.TryWrite(job)) Status?.Invoke("認識負荷が高いため、最新の速報を優先しています。");
                var pending = pendingSpeech;
                if (pending?.Id == id) Interlocked.CompareExchange(ref pendingSpeech, null, pending);
            }
            else Interlocked.Exchange(ref pendingSpeech, job);
            speechWake.Writer.TryWrite(true);
            lastSnapshot = samples.Count;
            if (final) { samples.Clear(); revision = 0; lastSnapshot = 0; silentSamples = 0; }
        }
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                while (source.Frames.TryRead(out var frame))
                {
                    audioThrough = frame.Timestamp;
                    if (frame.Generation != generation) { samples.Clear(); revision = 0; lastSnapshot = 0; generation = frame.Generation; }
                    double rms = Math.Sqrt(frame.Samples.Sum(v => (double)v * v) / frame.Samples.Length);
                    bool voice = rms > 0.008;
                    if (voice)
                    {
                        if (samples.Count == 0) { id++; started = frame.Timestamp; }
                        lastVoice = frame.Timestamp; silentSamples = 0;
                    }
                    else silentSamples += frame.Samples.Length;
                    if (samples.Count == 0 && !voice) continue;
                    samples.AddRange(frame.Samples);
                    if (samples.Count >= (fast ? 96000 : 192000) || silentSamples >= 8000) Submit(true);
                    else if (lastSnapshot == 0 && samples.Count >= (fast ? 6400 : 24000) || lastSnapshot > 0 && samples.Count - lastSnapshot >= (fast ? accurateSpeech is null ? 8000 : 4000 : 24000)) Submit(false);
                }
                if (samples.Count > 0 && Stopwatch.GetElapsedTime(lastVoice).TotalMilliseconds >= 500) Submit(true);
                DroppedAudio = source.DroppedFrames;
            }
        }
        finally { finalSpeechQueue.Writer.TryComplete(); speechWake.Writer.TryComplete(); }
    }

    private async IAsyncEnumerable<SpeechJob> ReadSpeechJobsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        await foreach (var _ in speechWake.Reader.ReadAllAsync(token))
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var job = finalSpeechQueue.Reader.TryRead(out var final) ? final : Interlocked.Exchange(ref pendingSpeech, null);
                if (job is null) break;
                yield return job;
            }
        }
    }

    private async Task SpeechAsync(CancellationToken token)
    {
        Dictionary<long, int> processed = new();
        try
        {
            await foreach (var job in ReadSpeechJobsAsync(token))
            {
                if (processed.TryGetValue(job.Id, out int done) && done >= job.Revision) continue;
                processed[job.Id] = job.Revision;
                foreach (var old in processed.Keys.Where(id => id < job.Id - 64).ToArray()) processed.Remove(old);
                try
                {
                    var result = await speech.RecognizeAsync(job.Samples, token, job.Final);
                    if (string.IsNullOrWhiteSpace(result.Text))
                    {
                        if (job.Final) { revisions.TryRemove(job.Id, out _); Caption?.Invoke(new(job.Id, job.Revision, "", "", true, job.Started, result.Milliseconds, 0)); }
                        continue;
                    }
                    var caption = new CaptionUpdate(job.Id, job.Revision, result.Text, null, job.Final, job.Started, result.Milliseconds, 0, japaneseOnly ? "ja" : "en", AudioThrough: job.AudioThrough);
                    revisions[job.Id] = job.Revision;
                    Caption?.Invoke(caption);
                    if (job.Final)
                    {
                        if (accurateSpeech is null || !accuracyQueue.Writer.TryWrite((caption, job.Samples))) QueueCorrection(caption);
                    }
                    if (japaneseOnly) continue;
                    if (!translationQueue.Writer.TryWrite(new(caption)) && job.Final) Status?.Invoke("翻訳が混雑しています。速報の原文表示を優先します。");
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { Status?.Invoke("STT失敗: " + ex.Message); }
            }
        }
        finally { accuracyQueue.Writer.TryComplete(); if (accurateSpeech is null) correctionQueue.Writer.TryComplete(); }
    }
    private void QueueCorrection(CaptionUpdate caption)
    {
        if (corrector is not null && !correctionQueue.Writer.TryWrite((caption, context.Where(c => c.Key < caption.Id).OrderBy(c => c.Key).Select(c => c.Value).TakeLast(8).ToArray()))) Status?.Invoke("AI補正が混雑しているため、この発話は認識結果を保持します。");
        context[caption.Id] = caption.English;
        foreach (var old in context.Keys.Order().SkipLast(32)) context.TryRemove(old, out _);
    }
    private async Task AccurateAsync(CancellationToken token)
    {
        try
        {
            await foreach (var job in accuracyQueue.Reader.ReadAllAsync(token))
            {
                var caption = job.Caption;
                try
                {
                    var result = await accurateSpeech!.RecognizeAsync(job.Samples, token, true);
                    if (string.IsNullOrWhiteSpace(result.Text))
                    {
                        var empty = caption with { Revision = caption.Revision + 1, English = "", Japanese = "", SourceCorrected = true };
                        revisions[empty.Id] = empty.Revision; Caption?.Invoke(empty); continue;
                    }
                    if (!string.IsNullOrWhiteSpace(result.Text) && result.Text != caption.English)
                    {
                        caption = caption with { Revision = caption.Revision + 1, English = result.Text, Japanese = null, SttMs = result.Milliseconds, SourceCorrected = true };
                        revisions[caption.Id] = caption.Revision;
                        Caption?.Invoke(caption);
                        if (!japaneseOnly) translationQueue.Writer.TryWrite(new(caption));
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { Status?.Invoke("高精度認識に失敗。速報を保持します: " + ex.Message); }
                QueueCorrection(caption);
            }
        }
        finally { if (accurateSpeech is not null) correctionQueue.Writer.TryComplete(); }
    }
    private async Task TranslateAsync(CancellationToken token)
    {
        try
        {
        await foreach (var job in translationQueue.Reader.ReadAllAsync(token))
        {
            var caption = job.Caption;
            if (revisions.TryGetValue(caption.Id, out int revision) && revision > caption.Revision) continue;
            try
            {
                var result = await translation!.TranslateAsync(caption.English, token, caption.Final);
                if (revisions.TryGetValue(caption.Id, out revision) && revision > caption.Revision) continue;
                var translated = caption with { Japanese = result.Text, TranslationMs = result.Milliseconds };
                Caption?.Invoke(translated);
                if (caption.Final && refiner is not null && !refinementQueue.Writer.TryWrite(translated)) Status?.Invoke("LLM翻訳が混雑しているため、この発話は標準訳を保持します。");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Status?.Invoke("翻訳失敗: " + ex.Message);
                if (!revisions.TryGetValue(caption.Id, out revision) || revision <= caption.Revision) Caption?.Invoke(caption);
            }
        }
        }
        finally { refinementQueue.Writer.TryComplete(); }
    }
    private async Task RefineAsync(CancellationToken token)
    {
        bool failed = false;
        await foreach (var caption in refinementQueue.Reader.ReadAllAsync(token))
        {
            if (failed || refiner is null) continue;
            try
            {
                var result = await refiner.RefineAsync(caption.English, context.Where(c => c.Key < caption.Id).OrderBy(c => c.Key).Select(c => c.Value).TakeLast(4).ToArray(), translationGlossary, token);
                if (revisions.TryGetValue(caption.Id, out int latest) && latest > caption.Revision) continue;
                Caption?.Invoke(caption with { Japanese = result.Text, TranslationMs = result.Milliseconds, TranslationRefined = true });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed = true;
                Status?.Invoke("LLM翻訳を停止し、標準訳を継続します: " + ex.Message);
            }
        }
    }
    private async Task CorrectAsync(CancellationToken token)
    {
        try
        {
            await foreach (var job in correctionQueue.Reader.ReadAllAsync(token))
            {
                if (corrector is null) continue;
                try
                {
                    Status?.Invoke("AI補正中（直前の最大8発話を参照）…");
                    var previous = context.Where(c => c.Key < job.Caption.Id).OrderBy(c => c.Key).Select(c => c.Value).TakeLast(8).ToArray();
                    var result = await corrector.CorrectAsync(job.Caption.English, previous.Length > 0 ? previous : job.Context, japaneseOnly ? "ja" : "en", token);
                    if (string.IsNullOrWhiteSpace(result.Text) || result.Text == job.Caption.English) { Status?.Invoke("AI確認済み：修正不要。文字起こしを継続します。"); continue; }
                    var corrected = job.Caption with { Revision = job.Caption.Revision + 1, English = result.Text, Japanese = null, SourceCorrected = true, TranslationRefined = false };
                    revisions[corrected.Id] = corrected.Revision;
                    if (context.ContainsKey(corrected.Id)) context[corrected.Id] = corrected.English;
                    Status?.Invoke("AI文脈補正を反映しました。");
                    Caption?.Invoke(corrected);
                    if (!japaneseOnly) await translationQueue.Writer.WriteAsync(new(corrected), token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Status?.Invoke("この発話のAI補正に失敗。原文を保持し、次の発話で再試行します: " + ex.Message);
                }
            }
        }
        finally { /* RunAsync completes translations after every producer has stopped. */ }
    }
}
