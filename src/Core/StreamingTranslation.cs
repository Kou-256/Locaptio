using System.Threading.Channels;
using System.Diagnostics;
using Locaptio.Core.AI;

namespace Locaptio.Core;

// Recognition never awaits translation. Pending work is coalesced per caption,
// while completed translations are merged into the current source revision.
public sealed class StreamingTranslation(ITranslationBackend fast, ITranslationRefinementBackend? quality = null, string glossary = "")
{
    private sealed class Row(CaptionUpdate source)
    {
        public CaptionUpdate Source = source;
        public string? Japanese;
        public string TranslatedSource = "";
        public bool Refined, FastPending, QualityPending;
        public long LastQualityStarted;
    }
    private readonly object gate = new();
    private readonly SortedDictionary<long, Row> rows = new();
    private readonly Channel<bool> fastWake = Signal(), qualityWake = Signal();
    private static Channel<bool> Signal() => Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
    public event Action<CaptionUpdate>? Caption;
    public event Action<string>? Status;
    public void Submit(CaptionUpdate source)
    {
        CaptionUpdate output;
        lock (gate)
        {
            bool created = !rows.TryGetValue(source.Id, out var row);
            if (created) rows[source.Id] = row = new(source);
            if (source.Revision < row!.Source.Revision) return;
            bool changed = created || source.English != row.Source.English;
            bool finalized = source.Final && !row.Source.Final;
            row.Source = source;
            if (changed && !Compatible(row.TranslatedSource, source.English)) { row.Japanese = null; row.TranslatedSource = ""; row.Refined = false; }
            if (string.IsNullOrWhiteSpace(source.English)) { row.Japanese = null; row.FastPending = row.QualityPending = false; }
            else
            {
                if (changed && !source.Final) row.QualityPending = false;
                row.FastPending |= row.Japanese is null || changed;
                row.QualityPending |= quality is not null && (source.Final || changed && source.English.Length >= 20 && source.English.Split(' ',StringSplitOptions.RemoveEmptyEntries).Length >= 4 && (row.LastQualityStarted == 0 || Stopwatch.GetElapsedTime(row.LastQualityStarted).TotalMilliseconds >= 600)) && (!row.Refined || changed || finalized);
                if (changed) row.Refined = false;
            }
            output = Merge(row);
            while (rows.Count > 64) rows.Remove(rows.Keys.First());
            Caption?.Invoke(output);
        } fastWake.Writer.TryWrite(true); qualityWake.Writer.TryWrite(true);
    }
    private static bool Compatible(string previous, string current)
    {
        if (current.StartsWith(previous, StringComparison.OrdinalIgnoreCase)) return true;
        int common = 0;
        while (common < Math.Min(previous.Length,current.Length) && char.ToUpperInvariant(previous[common]) == char.ToUpperInvariant(current[common])) common++;
        // Native ASR can revise a partially recognized final word. A close draft
        // remains useful until the translation of the new hypothesis is ready.
        return previous.Length >= 20 && common >= previous.Length * .85 && previous.Length - common <= 12;
    }
    private static CaptionUpdate Merge(Row row) => row.Source with { Japanese = row.Japanese, TranslationRefined = row.Refined };
    private (CaptionUpdate Source, string[] Context)? Next(bool refine)
    {
        lock (gate)
        {
            var pending = rows.Values.Where(r => refine ? r.QualityPending : r.FastPending).ToArray();
            var row = pending.FirstOrDefault(r => r.Source.Final) ?? pending.LastOrDefault();
            if (row is null) return null;
            if (refine) { row.QualityPending = false; row.LastQualityStarted = Stopwatch.GetTimestamp(); } else row.FastPending = false;
            return (row.Source, rows.Values.Where(r => r.Source.Id < row.Source.Id && !string.IsNullOrWhiteSpace(r.Source.English)).TakeLast(4).Select(r => r.Source.English).ToArray());
        }
    }
    public Task RunAsync(CancellationToken token) => Task.WhenAll(WorkAsync(false, token), quality is null ? Task.CompletedTask : WorkAsync(true, token));
    private async Task WorkAsync(bool refine, CancellationToken token)
    {
        var wake = refine ? qualityWake : fastWake;
        while (await wake.Reader.WaitToReadAsync(token))
        {
            while (wake.Reader.TryRead(out _)) { }
            while (Next(refine) is { } job)
            {
                try
                {
                    var result = refine ? await quality!.RefineAsync(job.Source.English, job.Context, glossary, token) : await fast.TranslateAsync(job.Source.English, token, job.Source.Final);
                    CaptionUpdate? output = null;
                    lock (gate)
                    {
                        if (rows.TryGetValue(job.Source.Id, out var row) && !string.IsNullOrWhiteSpace(result.Text))
                        {
                            bool matches = refine && job.Source.Final ? row.Source.English == job.Source.English : Compatible(job.Source.English, row.Source.English);
                            bool better = (refine || !row.Refined) && (row.Japanese is null || job.Source.English.Length >= row.TranslatedSource.Length);
                            if (matches && better)
                            {
                                row.Japanese = result.Text; row.TranslatedSource = job.Source.English; row.Refined = refine;
                                output = Merge(row) with { TranslationMs = result.Milliseconds };
                                Caption?.Invoke(output);
                            }
                        }
                    }

                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex) { Status?.Invoke((refine ? "文脈LLM翻訳" : "速報翻訳") + "に失敗しました。英文表示を継続します: " + ex.Message); }
            }
        }
    }
}
