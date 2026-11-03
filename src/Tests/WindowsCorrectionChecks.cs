using System.Collections.Concurrent;
using System.Threading.Channels;
using Locaptio.Core;
using Locaptio.Core.AI;

internal static class WindowsCorrectionChecks
{
    private static CaptionUpdate Source(long id, int revision, string text, bool final = true) => new(id, revision, text, null, final, 1, 0, 0, "ja");
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static async Task Until(Func<bool> condition) { for (int i = 0; i < 100 && !condition(); i++) await Task.Delay(10); Check(condition(), "Correction result did not arrive"); }

    public static async Task RunAsync()
    {
        var fake = new Controlled(); var updates = new ConcurrentQueue<CaptionUpdate>();
        var correction = new StreamingSpeechCorrection(fake, "ja"); correction.Caption += updates.Enqueue;
        using var stop = new CancellationTokenSource(); var running = correction.RunAsync(stop.Token);
        try
        {
            correction.Submit(Source(1, 1, "今日は音正認識を説明します。"));
            Check(updates.Count == 1 && fake.Calls == 0, "OFF invoked AI or delayed the native text");
            correction.SetEnabled(true);
            var first = await fake.Next();
            correction.Submit(Source(1, 2, "今日は音声認識を説明します。"));
            Check(updates.Last().English == "今日は音声認識を説明します。", "Recognition waited for correction");
            correction.SetEnabled(false); correction.SetEnabled(true);
            first.Result.SetResult(new("古い補正結果", 1));
            var current = await fake.Next();
            Check(!updates.Any(c => c.English == "古い補正結果"), "An OFF/ON toggle accepted a stale correction");
            Check(current.Source == "今日は音声認識を説明します。" && current.Language == "ja", "Japanese mode or latest revision was lost");
            current.Result.SetResult(new("今日は音声認識について説明します。", 1));
            await Until(() => updates.Any(c => c.SourceCorrected));
            var corrected = updates.Last();
            Check(corrected.Id == 1 && corrected.English == "今日は音声認識について説明します。", "Correction appended a separate caption");
            correction.Submit(Source(1, 3, "今日は音声認識について詳しく説明します。"));
            Check(updates.Last().Revision > corrected.Revision && !updates.Last().SourceCorrected, "A later native revision could not replace the correction");
            var revision = await fake.Next(); revision.Result.SetResult(new(revision.Source, 1));
            correction.Submit(Source(2, 1, "次に高速化の方法を説明します。"));
            var second = await fake.Next();
            Check(second.Context.Contains("今日は音声認識について詳しく説明します。"), "Previous utterances were not supplied as context");
            correction.SetEnabled(false); second.Result.SetResult(new("OFF後の結果", 1));
            correction.Submit(Source(3, 1, "原文を保持します。"));
            stop.Cancel(); try { await running; } catch (OperationCanceledException) { }
            Check(!updates.Any(c => c.English == "OFF後の結果"), "OFF published an in-flight result");
        }
        finally { stop.Cancel(); try { await running; } catch (OperationCanceledException) { } }
        await CheckFailureAsync();
        await CheckBoundedAsync();
        await CheckEnglishTranslationAsync();
        Console.WriteLine("PASS: Windows correction OFF/ON, immediate raw text, live toggles, stale-result rejection, stable IDs/revisions, context, bounded backlog, cancellation, error fallback and corrected-source translation");
    }

    private static async Task CheckFailureAsync()
    {
        var backend = new Controlled(); var updates = new ConcurrentQueue<CaptionUpdate>(); var messages = new ConcurrentQueue<string>();
        var flow = new StreamingSpeechCorrection(backend, "ja"); flow.Caption += updates.Enqueue; flow.Status += messages.Enqueue; flow.SetEnabled(true);
        using var stop = new CancellationTokenSource(); var running = flow.RunAsync(stop.Token);
        try
        {
            flow.Submit(Source(1, 1, "モデルがなくても原文を表示。"));
            (await backend.Next()).Result.SetException(new InvalidOperationException("LLM model missing"));
            await Until(() => messages.Any(m => m.Contains("原文表示を継続")));
            Check(updates.Count == 1 && !updates.Last().SourceCorrected, "Initialization failure erased native text");
            flow.Submit(Source(2, 1, "続きの発話。")); var next = await backend.Next();
            stop.Cancel(); try { await running.WaitAsync(TimeSpan.FromSeconds(1)); } catch (OperationCanceledException) { }
            Check(updates.Last().English == "続きの発話。", "Failed correction blocked later recognition");
        }
        finally { stop.Cancel(); try { await running; } catch (OperationCanceledException) { } }
    }

    private static async Task CheckBoundedAsync()
    {
        var backend = new Controlled(); var flow = new StreamingSpeechCorrection(backend, "ja"); flow.SetEnabled(true);
        using var stop = new CancellationTokenSource(); var running = flow.RunAsync(stop.Token);
        try
        {
            flow.Submit(Source(1, 1, "最初")); var first = await backend.Next();
            for (int i = 2; i <= 10000; i++) flow.Submit(Source(i, 1, $"発話{i}"));
            first.Result.SetResult(new("古い行", 1));
            for (int i = 0; i < 4; i++)
            {
                var job = await backend.Next(); Check(job.Context.Length <= 8 && (job.Source.StartsWith("発話999") || job.Source == "発話10000"), "Backlog/context was not bounded");
                job.Result.SetResult(new(job.Source, 1));
            }
            await Task.Delay(30); Check(backend.Calls == 5, "Old pending work was not coalesced/dropped");
        }
        finally { stop.Cancel(); try { await running; } catch (OperationCanceledException) { } }
    }

    private static async Task CheckEnglishTranslationAsync()
    {
        var backend = new Controlled(); var flow = new StreamingSpeechCorrection(backend, "en"); flow.SetEnabled(true);
        var translated = new StreamingTranslation(new Translation()); var updates = new ConcurrentQueue<CaptionUpdate>();
        flow.Caption += translated.Submit; translated.Caption += updates.Enqueue;
        using var stop = new CancellationTokenSource(); var correcting = flow.RunAsync(stop.Token); var translating = translated.RunAsync(stop.Token);
        try
        {
            flow.Submit(Source(1, 1, "We discuss there cameras.") with { SourceLanguage = "en" });
            var job = await backend.Next(); Check(job.Language == "en", "English mode corrected as Japanese");
            await Until(() => updates.Any(c => c.Japanese == "訳:We discuss there cameras."));
            job.Result.SetResult(new("We discuss their cameras.", 1));
            await Until(() => updates.Any(c => c.SourceCorrected && c.Japanese == "訳:We discuss their cameras."));
        }
        finally { stop.Cancel(); try { await Task.WhenAll(correcting, translating); } catch (OperationCanceledException) { } }
    }

    private sealed record Request(string Source, string[] Context, string Language)
    {
        public TaskCompletionSource<InferenceResult> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class Controlled : ISpeechCorrectionBackend
    {
        private readonly Channel<Request> jobs = Channel.CreateUnbounded<Request>();
        public int Calls;
        public async Task<InferenceResult> CorrectAsync(string source, string[] context, string language, CancellationToken token)
        {
            Interlocked.Increment(ref Calls); var job = new Request(source, context, language); jobs.Writer.TryWrite(job);
            return await job.Result.Task.WaitAsync(token);
        }
        public async Task<Request> Next() => await jobs.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
    }
    private sealed class Translation : ITranslationBackend
    {
        public Task<InferenceResult> TranslateAsync(string text, CancellationToken token, bool final = true) => Task.FromResult(new InferenceResult("訳:" + text, 1));
    }
}
