using System.Collections.Concurrent;
using System.Diagnostics;
using Locaptio.Core;
using Locaptio.Core.AI;

internal static class StreamingTranslationChecks
{
    internal static async Task RunAsync()
    {
        var backend = new Fake(); var streaming = new StreamingTranslation(backend, backend, "subject=被写体");
        var updates = new ConcurrentQueue<CaptionUpdate>(); streaming.Caption += updates.Enqueue;
        using var stop = new CancellationTokenSource(); var running = streaming.RunAsync(stop.Token);
        try
        {
            long started = Stopwatch.GetTimestamp(); var source = new CaptionUpdate(1, 1, "We", null, false, started, 0, 0);
            var submit = Stopwatch.StartNew();
            for (int i = 1; i <= 16; i++)
            {
                source = source with { Revision = i, English = "We " + string.Join(" ", Enumerable.Repeat("record", i)) };
                streaming.Submit(source); await Task.Delay(40);
            }
            if (!updates.Any(c => c.Japanese is not null && !c.Final) || updates.Where(c => !c.Final).Select(c=>c.Revision).Distinct().Count() < 16) throw new Exception("Translation blocked recognition or discarded every interim translation");
            streaming.Submit(source with { Revision = 17, Final = true });
            await Until(() => updates.Any(c => c.Id == 1 && c.TranslationRefined));
            int at = updates.Count;
            streaming.Submit(new(2, 1, "Your subject suddenly gets loud.", null, true, started, 0, 0));
            await Until(() => updates.Any(c => c.Id == 2 && c.TranslationRefined));
            if (!backend.ContextSeen || !backend.GlossarySeen) throw new Exception("LLM did not receive previous utterance and glossary");
            var rowOne = updates.Where(c=>c.Id==1).ToArray(); int refined = Array.FindIndex(rowOne,c=>c.Final && c.TranslationRefined);
            if (rowOne.Skip(refined).Any(c=>!c.TranslationRefined)) throw new Exception("Fast translation overwrote refined translation");
            streaming.Submit(new(3, 1, "Old words", null, false, started, 0, 0));
            await Task.Delay(30); streaming.Submit(new(3, 2, "Corrected words", null, false, started, 0, 0));
            await Until(()=>updates.Any(c=>c.Id==3 && c.Japanese is not null));
            if (updates.Any(c=>c.Id==3 && c.English=="Corrected words" && c.Japanese=="訳:Old words")) throw new Exception("Stale translation survived a source rewrite");
            var stable = new CaptionUpdate(4,1,"This complete sentence stays unchanged",null,false,started,0,0);
            streaming.Submit(stable); await Until(()=>updates.Any(c=>c.Id==4&&c.TranslationRefined));
            streaming.Submit(stable with {Revision=2,Final=true});
            await Until(()=>backend.StableCalls>=2);
            Console.WriteLine("PASS: continuous interim translations, nonblocking source updates, stale-result rejection, context/glossary delivery, refined translation priority");
        }
        finally { stop.Cancel(); try { await running; } catch (OperationCanceledException) { } }
    }
    private static async Task Until(Func<bool> predicate)
    { for(int i=0;i<100&&!predicate();i++) await Task.Delay(30); if(!predicate()) throw new Exception("Translation did not finish"); }
    private sealed class Fake : ITranslationBackend, ITranslationRefinementBackend
    {
        internal bool ContextSeen, GlossarySeen;
        internal int StableCalls;
        public async Task<InferenceResult> TranslateAsync(string source, CancellationToken token, bool final=true) { await Task.Delay(160,token); return new("訳:"+source,160); }
        public Task<InferenceResult> RefineAsync(string source,CancellationToken token) => RefineAsync(source,[],"",token);
        public async Task<InferenceResult> RefineAsync(string source,string[] context,string glossary,CancellationToken token)
        { if(source.StartsWith("This complete sentence")) Interlocked.Increment(ref StableCalls); ContextSeen |= context.Any(c=>c.StartsWith("We ")); GlossarySeen |= glossary.Contains("被写体"); await Task.Delay(450,token); return new("文脈訳:"+source,450); }
    }
}
