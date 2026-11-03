using System.Threading.Channels;
using Locaptio.Core.AI;

namespace Locaptio.Core;

// Windows recognition remains the immediate source of truth. Correction runs
// independently, with bounded/coalesced work and stale-result rejection.
public sealed class StreamingSpeechCorrection(ISpeechCorrectionBackend backend, string language)
{
    private sealed class Row(CaptionUpdate native)
    {
        public CaptionUpdate Native = native;
        public int OutputRevision = native.Revision - 1;
        public bool Pending, Corrected;
        public string ContextText = native.English;
    }
    private readonly object gate = new();
    private readonly SortedDictionary<long, Row> rows = new();
    private readonly Channel<bool> wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
    private bool enabled;
    private long generation;
    public event Action<CaptionUpdate>? Caption;
    public event Action<string>? Status;

    public void SetEnabled(bool value)
    {
        lock (gate)
        {
            if (enabled == value) return;
            enabled = value; generation++;
            foreach (var row in rows.Values) row.Pending = false;
            // Turning ON also handles the most recent finalized row, without
            // queuing an entire session's history or revising older saved rows.
            if (enabled && rows.Count > 0)
            {
                var latest = rows.Values.Last();
                latest.Pending = latest.Native.Final && !latest.Corrected && !string.IsNullOrWhiteSpace(latest.Native.English);
            }
        }
        wake.Writer.TryWrite(true);
    }

    public void Submit(CaptionUpdate native)
    {
        lock (gate)
        {
            if (!rows.TryGetValue(native.Id, out var row)) rows[native.Id] = row = new(native);
            else if (native.Revision < row.Native.Revision || native == row.Native) return;
            row.Native = native; row.ContextText = native.English; row.Corrected = false;
            row.Pending = enabled && native.Final && !string.IsNullOrWhiteSpace(native.English);
            Caption?.Invoke(native with { Revision = ++row.OutputRevision });
            while (rows.Count > 64) rows.Remove(rows.Keys.First());
            foreach (var old in rows.Values.Where(r => r.Pending).Reverse().Skip(4)) old.Pending = false;
        }
        wake.Writer.TryWrite(true);
    }

    private (CaptionUpdate Native, string[] Context, long Generation)? Next()
    {
        lock (gate)
        {
            if (!enabled) return null;
            var row = rows.Values.LastOrDefault(r => r.Pending);
            if (row is null) return null;
            row.Pending = false;
            return (row.Native, rows.Values.Where(r => r.Native.Id < row.Native.Id && !string.IsNullOrWhiteSpace(r.ContextText)).TakeLast(8).Select(r => r.ContextText).ToArray(), generation);
        }
    }

    public async Task RunAsync(CancellationToken token)
    {
        while (await wake.Reader.WaitToReadAsync(token))
        {
            while (wake.Reader.TryRead(out _)) { }
            while (Next() is { } job)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var result = await backend.CorrectAsync(job.Native.English, job.Context, language, token);
                    lock (gate)
                    {
                        if (!enabled || generation != job.Generation || !rows.TryGetValue(job.Native.Id, out var row) || row.Native != job.Native) continue;
                        if (string.IsNullOrWhiteSpace(result.Text) || result.Text == job.Native.English) { Status?.Invoke("AI確認済み：修正不要。文字起こしを継続します。"); continue; }
                        row.ContextText = result.Text; row.Corrected = true;
                        Caption?.Invoke(job.Native with { Revision = ++row.OutputRevision, English = result.Text, Japanese = null, SourceCorrected = true, TranslationRefined = false });
                        Status?.Invoke("AI文脈補正を反映しました。");
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    lock (gate)
                    {
                        if (enabled && generation == job.Generation) Status?.Invoke("AI補正を適用できません。原文表示を継続します: " + ex.Message);
                    }
                }
            }
        }
    }
}
