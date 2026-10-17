using System.Diagnostics;
using NAudio.Wave;

namespace Locaptio.Core.Audio;

/// <summary>Explicit opt-in recording, independent of model availability.</summary>
public sealed class LoopbackRecorder(AudioInputSelection? input = null)
{
    public event Action<string>? Status;
    public long DroppedFrames { get; private set; }
    public async Task RunAsync(string path, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var source = new LoopbackSource(input);
        source.Status += message => Status?.Invoke(message);
        using var writer = new WaveFileWriter(path, new WaveFormat(16000, 16, 1));
        var started = Stopwatch.GetTimestamp();
        long written = 0;
        long lastFlush = started;
        var silence = new byte[3200];
        var pcm = new byte[3200];
        void WritePcm(float[] samples)
        {
            // Convert a complete packet and write once, rather than per sample.
            if (pcm.Length < samples.Length * 2) pcm = new byte[samples.Length * 2];
            for (int i = 0; i < samples.Length; i++)
            {
                short value = (short)Math.Clamp(samples[i] * 32768f, short.MinValue, short.MaxValue);
                pcm[i * 2] = (byte)value; pcm[i * 2 + 1] = (byte)(value >> 8);
            }
            writer.Write(pcm, 0, samples.Length * 2);
        }
        void PadTo(long position)
        {
            while (written < position)
            {
                int count = (int)Math.Min(position - written, silence.Length / 2);
                writer.Write(silence, 0, count * 2); written += count;
            }
        }
        void Drain()
        {
            List<AudioFrame> batch = [];
            int sampleCount = 0;
            while (source.Frames.TryRead(out var frame)) { batch.Add(frame); sampleCount += frame.Samples.Length; }
            if (batch.Count == 0) return;
            // Preserve gaps where WASAPI emits no packets (silence/device reconnect).
            long end = (long)(Stopwatch.GetElapsedTime(started, batch[^1].Timestamp).TotalSeconds * 16000);
            PadTo(Math.Max(0, end - sampleCount));
            foreach (var frame in batch) { WritePcm(frame.Samples); written += frame.Samples.Length; }
            DroppedFrames = source.DroppedFrames;
            if (writer.Length > 3_900_000_000L) throw new IOException("WAVのサイズ上限に近づいたため録音を終了しました。別ファイルで録音を再開してください。");
        }
        var capture = source.RunAsync(linked.Token);
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
            while (await timer.WaitForNextTickAsync(token))
            {
                if (capture.IsCompleted) await capture;
                Drain();
                // Flush periodically so an interrupted recording retains recent data.
                if (Stopwatch.GetElapsedTime(lastFlush).TotalSeconds >= 1)
                {
                    PadTo(Math.Max(0, (long)((Stopwatch.GetElapsedTime(started).TotalSeconds - 0.25) * 16000)));
                    if (writer.Length > 3_900_000_000L) throw new IOException("WAVのサイズ上限に近づいたため録音を終了しました。");
                    writer.Flush(); lastFlush = Stopwatch.GetTimestamp();
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            linked.Cancel();
            try { await capture; } catch (OperationCanceledException) { }
            Drain();
            PadTo((long)(Stopwatch.GetElapsedTime(started).TotalSeconds * 16000));
        }
    }
}
