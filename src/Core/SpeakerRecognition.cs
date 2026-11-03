using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Locaptio.Core.Audio;

namespace Locaptio.Core;

public readonly record struct SpeakerMatch(int Id, double Confidence, bool Observed = false);
public readonly record struct SpeakerTurn(long Start, long End, int SpeakerId, double Confidence);

// Anonymous, session-local clustering. Never stores names, audio or voiceprints on disk.
public sealed class SpeakerClusterer(double threshold = .55, int maximum = 8)
{
    private readonly List<float[]> centers = [];
    public int Count => centers.Count;
    public SpeakerMatch Identify(float[] embedding)
    {
        double norm = Math.Sqrt(embedding.Sum(x => (double)x * x));
        if (embedding.Length == 0 || !double.IsFinite(norm) || norm < 1e-6) return default;
        var vector = embedding.Select(x => (float)(x / norm)).ToArray();
        if (centers.Count > 0 && centers[0].Length != vector.Length) return default;
        var scores = centers.Select((c, i) => (Id: i, Score: c.Zip(vector).Sum(p => (double)p.First * p.Second))).OrderByDescending(p => p.Score).ToArray();
        if (scores.Length > 0 && scores[0].Score >= threshold)
        {
            var best = scores[0];
            if (scores.Length > 1 && scores[1].Score >= threshold && best.Score - scores[1].Score < .04) return default;
            if (best.Score >= threshold + .08)
            {
                var c = centers[best.Id];
                for (int i = 0; i < c.Length; i++) c[i] = .95f * c[i] + .05f * vector[i];
                double n = Math.Sqrt(c.Sum(x => (double)x * x));
                for (int i = 0; i < c.Length; i++) c[i] /= (float)n;
            }
            return new(best.Id + 1, best.Score);
        }
        if (centers.Count >= maximum) return default;
        centers.Add(vector); return new(centers.Count, 1);
    }
}

public sealed class SpeakerTimeline
{
    private readonly object gate = new();
    private readonly List<SpeakerTurn> turns = [];
    public int Count { get { lock (gate) return turns.Count; } }
    public void Add(SpeakerTurn turn)
    {
        if (turn.End <= turn.Start) return;
        lock (gate)
        {
            turns.Add(turn);
            long cutoff = turn.End - 120 * Stopwatch.Frequency;
            turns.RemoveAll(t => t.End < cutoff);
            if (turns.Count > 256) turns.RemoveRange(0, turns.Count - 256);
        }
    }
    public SpeakerMatch Find(long start, long end)
    {
        if (end <= start) return default;
        lock (gate)
        {
            var overlap = turns.Select(t => (Turn: t, Length: Math.Max(0, Math.Min(t.End, end) - Math.Max(t.Start, start)))).Where(t => t.Length > 0).ToArray();
            double total = overlap.Sum(t => (double)t.Length);
            var best = overlap.Where(t => t.Turn.SpeakerId > 0).GroupBy(t => t.Turn.SpeakerId)
                .Select(g => (Id: g.Key, Weight: g.Sum(t => (double)t.Length), Confidence: g.Min(t => t.Turn.Confidence))).OrderByDescending(g => g.Weight).FirstOrDefault();
            return total > 0 ? total >= (end - start) * .35 && best.Weight / total >= .65 ? new(best.Id, best.Confidence, true) : new(0, 0, true) : default;
        }
    }
    public SpeakerMatch Find(CaptionUpdate caption)
    {
        if (caption.AudioThrough > caption.Started) return Find(caption.Started, caption.AudioThrough);
        // Windows exposes text without word/audio timestamps. Approximate the
        // first recognition update's audio interval, never the later LLM time.
        return Find(caption.Started - (long)(.9 * Stopwatch.Frequency), caption.Started + (long)(.3 * Stopwatch.Frequency));
    }
}

public interface ISpeakerEmbeddingBackend : IDisposable
{
    Task StartAsync(CancellationToken token);
    Task<float[]?> ExtractAsync(float[] samples, CancellationToken token);
}

public sealed class SpeakerEmbeddingBackend(string root) : ISpeakerEmbeddingBackend
{
    private Process? process;
    public async Task StartAsync(CancellationToken token)
    {
        string python = Path.Combine(root, ".venv-speakers", "Scripts", "python.exe");
        if (!File.Exists(python) || !File.Exists(Path.Combine(root, "models", "speakers", "embedding.onnx")) || !File.Exists(Path.Combine(root, "models", "speakers", "silero_vad.onnx")))
            throw new InvalidOperationException("scripts/setup-speakers.ps1で話者識別モデルを準備してください。");
        var start = new ProcessStartInfo(python) { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardInputEncoding = new UTF8Encoding(false) };
        start.ArgumentList.Add("-u"); start.ArgumentList.Add(Path.Combine(root, "inference", "speaker_worker.py"));
        start.ArgumentList.Add("--models"); start.ArgumentList.Add(Path.Combine(root, "models", "speakers"));
        process = Process.Start(start) ?? throw new InvalidOperationException("話者識別を開始できません。");
        process.ErrorDataReceived += (_, _) => { }; process.BeginErrorReadLine();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var registration = timeout.Token.Register(Kill);
        using var ready = await ReadAsync(timeout.Token);
        if (ready.RootElement.GetProperty("type").GetString() != "ready") throw new InvalidOperationException("話者識別モデルの読み込みに失敗しました。");
    }
    public async Task<float[]?> ExtractAsync(float[] samples, CancellationToken token)
    {
        if (samples.Length is < 12000 or > 32000) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var registration = timeout.Token.Register(Kill);
        var bytes = new byte[samples.Length * sizeof(float)]; Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        await process!.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { audio = Convert.ToBase64String(bytes) }, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }).AsMemory(), timeout.Token);
        await process.StandardInput.FlushAsync(timeout.Token);
        using var response = await ReadAsync(timeout.Token);
        if (response.RootElement.GetProperty("type").GetString() != "result") throw new InvalidOperationException("話者識別の推論に失敗しました: " + (response.RootElement.TryGetProperty("message", out var error) ? error.GetString() : "unknown error"));
        return response.RootElement.GetProperty("embedding").Deserialize<float[]>();
    }
    private async Task<JsonDocument> ReadAsync(CancellationToken token) => JsonDocument.Parse(await process!.StandardOutput.ReadLineAsync(token) ?? throw new InvalidOperationException("話者識別プロセスが終了しました。"));
    private void Kill() { try { if (process is { HasExited: false }) process.Kill(true); } catch (InvalidOperationException) { } }
    public void Dispose() { Kill(); process?.Dispose(); }
}

// Capture callbacks only enqueue bounded snapshots; model inference cannot block ASR.
public sealed class SpeakerRecognitionSession(ISpeakerEmbeddingBackend backend, double threshold = .55, int maximum = 8) : IDisposable
{
    private sealed record Job(float[] Samples, long Start, long End);
    private readonly Channel<Job> jobs = Channel.CreateBounded<Job>(new BoundedChannelOptions(2) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly List<float> pending = new(32000);
    private readonly object gate = new();
    private readonly SpeakerClusterer clusters = new(Math.Clamp(threshold, .3, .85), Math.Clamp(maximum, 2, 8));
    private int generation = -1, silence;
    private long started, lastSubmittedAt, lastThrough;
    private volatile bool accepting;
    public SpeakerTimeline Timeline { get; } = new();
    public event Action? Changed;
    public event Action<string>? Status;
    public void Submit(AudioFrame frame)
    {
        if (!accepting) return;
        lock (gate)
        {
            if (generation != frame.Generation) { pending.Clear(); silence = 0; generation = frame.Generation; }
            bool voice = frame.Samples.Any(v => Math.Abs(v) > .012f);
            if (pending.Count == 0 && !voice) return;
            if (pending.Count == 0) started = frame.Timestamp - (long)(frame.Samples.Length / 16000.0 * Stopwatch.Frequency);
            silence = voice ? 0 : silence + frame.Samples.Length;
            lastSubmittedAt = Stopwatch.GetTimestamp(); lastThrough = frame.Timestamp;
            pending.AddRange(frame.Samples);
            if (pending.Count < 32000 && silence < 8000) return;
            FlushPending();
        }
    }
    private void FlushPending()
    {
        if (pending.Count >= 12000) jobs.Writer.TryWrite(new(pending.Take(32000).ToArray(), started, lastThrough));
        pending.Clear(); silence = 0;
    }
    private async Task FlushIdleAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        while (await timer.WaitForNextTickAsync(token))
        {
            lock (gate)
                if (pending.Count > 0 && Stopwatch.GetElapsedTime(lastSubmittedAt).TotalMilliseconds >= 500) FlushPending();
        }
    }
    public async Task RunAsync(CancellationToken token)
    {
        using var idleStop = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task flushing = Task.CompletedTask;
        try
        {
            await backend.StartAsync(token); accepting = true;
            flushing = FlushIdleAsync(idleStop.Token);
            Status?.Invoke("話者識別: ON（ローカルCPU／最大8話者）");
            await foreach (var job in jobs.Reader.ReadAllAsync(token))
            {
                var embedding = await backend.ExtractAsync(job.Samples, token);
                token.ThrowIfCancellationRequested();
                var match = embedding is null ? default : clusters.Identify(embedding);
                Timeline.Add(new(job.Start, job.End, match.Id, match.Confidence)); Changed?.Invoke();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { Status?.Invoke("話者識別を停止しました。通常色で文字起こしを継続します: " + ex.Message); }
        finally
        {
            accepting = false; idleStop.Cancel();
            try { await flushing; } catch (OperationCanceledException) when (idleStop.IsCancellationRequested) { }
            backend.Dispose(); lock (gate) pending.Clear();
        }
    }
    public void Dispose() { accepting = false; backend.Dispose(); jobs.Writer.TryComplete(); lock (gate) pending.Clear(); }
}
