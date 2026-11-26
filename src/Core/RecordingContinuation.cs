using NAudio.MediaFoundation;
using NAudio.Wave;

namespace Locaptio.Core;

/// <summary>
/// Recording the continuation of a saved recording (for example after Locaptio was closed unexpectedly): the new part
/// is recorded beside it and then appended, so the recording stays one file in its own folder.
/// </summary>
public static class RecordingContinuation
{
    /// <summary>Where the new part is recorded before it is appended (same folder; removed afterwards).</summary>
    public static string PartPath(string recording, string extension) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(recording))!, Path.GetFileNameWithoutExtension(recording) + ".continue" + extension);

    /// <summary>
    /// The first document followed by the second, whose times move by <paramref name="offset"/> seconds (the length of the
    /// first recording). IDs of the second are kept when they do not collide, otherwise moved after the first's.
    /// Summaries stay those of the first recording.
    /// </summary>
    public static RecordingDocument Append(RecordingDocument first, RecordingDocument second, double offset)
    {
        var used = first.Phrases.Select(p => p.Id).ToHashSet();
        long next = used.Count == 0 ? 1 : used.Max() + 1;
        var ids = new Dictionary<long, long>();
        foreach (var phrase in second.Phrases)
        {
            long id = phrase.Id;
            if (!used.Add(id)) { while (!used.Add(next)) next++; id = next; }
            ids[phrase.Id] = id;
        }
        var moved = second.Phrases.Select(p => p with
        {
            Id = ids[p.Id], Seconds = p.Seconds + offset, EndSeconds = p.EndSeconds + offset,
            Words = p.Words?.Select(w => w with { Start = w.Start + offset, End = w.End + offset }).ToArray()
        });
        var bookmarks = (first.Bookmarks ?? []).Concat((second.Bookmarks ?? []).Select(b => b with { Seconds = b.Seconds + offset, PhraseId = ids.TryGetValue(b.PhraseId, out long id) ? id : b.PhraseId })).ToArray();
        return first with
        {
            ApproximateTiming = first.ApproximateTiming || second.ApproximateTiming,
            Phrases = first.Phrases.Concat(moved).OrderBy(p => p.Seconds).ThenBy(p => p.Id).ToArray(),
            Bookmarks = bookmarks.Length == 0 ? null : bookmarks.OrderBy(b => b.Seconds).ToArray()
        };
    }

    /// <summary>
    /// Appends the audio of <paramref name="addition"/> (a WAV recording) to <paramref name="recording"/> (WAV, MP3 or M4A)
    /// in place, and returns the length of the original recording in seconds. The result keeps the recording's format.
    /// </summary>
    public static double AppendAudio(string recording, string addition)
    {
        string extension = Path.GetExtension(recording).ToLowerInvariant();
        string folder = Path.GetDirectoryName(Path.GetFullPath(recording))!;
        string joined = Path.Combine(folder, Path.GetFileNameWithoutExtension(recording) + ".joining.wav");
        MediaFoundationApi.Startup();
        double offset;
        using (var firstReader = Open(recording))
        {
            // Locaptio records 16 kHz mono; an imported or converted recording keeps its own rate and channels.
            var format = new WaveFormat(firstReader.WaveFormat.SampleRate, 16, Math.Clamp(firstReader.WaveFormat.Channels, 1, 2));
            using var writer = new WaveFileWriter(joined, format);
            using (var first = new MediaFoundationResampler(firstReader, format)) Copy(first, writer);
            offset = writer.Length / (double)format.AverageBytesPerSecond;
            using var secondReader = new WaveFileReader(addition);
            using var second = new MediaFoundationResampler(secondReader, format);
            Copy(second, writer);
        }
        try
        {
            if (extension == ".wav") { Replace(joined, recording); return offset; }
            string encoded = Path.Combine(folder, Path.GetFileNameWithoutExtension(recording) + ".joining" + extension);
            using (var reader = new WaveFileReader(joined))
            using (var resampler = new MediaFoundationResampler(reader, new WaveFormat(44100, 16, reader.WaveFormat.Channels)))
            {
                if (extension == ".mp3") MediaFoundationEncoder.EncodeToMp3(resampler, encoded, 96000);
                else MediaFoundationEncoder.EncodeToAac(resampler, encoded, 64000);
            }
            Replace(encoded, recording);
            return offset;
        }
        finally { try { if (File.Exists(joined)) File.Delete(joined); } catch (IOException) { } }
    }

    /// <summary>
    /// Replaces <paramref name="target"/> with <paramref name="source"/>. A player, a virus scanner or the thumbnail
    /// cache can hold the target for a moment after it was closed, so this tries again for a few seconds.
    /// </summary>
    public static void Replace(string source, string target)
    {
        for (int attempt = 1; ; attempt++)
        {
            try { File.Move(source, target, true); return; }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < 20) { GC.Collect(); GC.WaitForPendingFinalizers(); Thread.Sleep(250); }
        }
    }

    private static WaveStream Open(string path) => Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase) ? new WaveFileReader(path) : new MediaFoundationReader(path);
    private static void Copy(IWaveProvider source, WaveFileWriter writer)
    {
        var buffer = new byte[source.WaveFormat.AverageBytesPerSecond];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0) writer.Write(buffer, 0, read);
    }
}
