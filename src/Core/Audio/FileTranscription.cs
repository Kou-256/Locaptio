using Locaptio.Core.AI;
using NAudio.Wave;

namespace Locaptio.Core.Audio;

/// <summary>Decodes incrementally: at most twelve seconds of mono PCM are retained.</summary>
public static class FileTranscription
{
    public static async Task RunAsync(string path, ISpeechRecognitionBackend speech, string language,
        Func<RecordingPhrase, Task> receive, Action<double> progress, CancellationToken token,
        ITranslationBackend? translation = null, ISpeechCorrectionBackend? correction = null, Func<string, string>? normalize = null)
    {
        using var reader = new AudioFileReader(path);
        using var pcm = new MediaFoundationResampler(reader, new WaveFormat(16000, 16, 1)) { ResamplerQuality = 60 };
        await RunPcmAsync(buffer => Task.FromResult(pcm.Read(buffer, 0, buffer.Length)), reader.TotalTime.TotalSeconds, speech, language, receive, progress, token, translation, correction, normalize);
    }

    /// <summary>
    /// Follows a 16 kHz mono 16-bit WAV that is still being recorded, so recognition keeps up with the
    /// recording and only the last moments remain when it stops. Times are from the start of the file.
    /// The file may be deleted by the recorder (MP3/M4A conversion) while it is read.
    /// </summary>
    /// <param name="complete">True once nothing more will be written to the file.</param>
    public static async Task RunGrowingAsync(string wav, Func<bool> complete, ISpeechRecognitionBackend speech, string language,
        Func<RecordingPhrase, Task> receive, CancellationToken token, Func<string, string>? normalize = null, Action<string>? trace = null)
    {
        FileStream? stream = null;
        while (stream is null)
        {
            bool done = complete();
            try { stream = new FileStream(wav, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); }
            catch (FileNotFoundException) when (!done) { await Task.Delay(250, token); }
        }
        using (stream)
        {
            long data;
            while ((data = DataOffset(stream)) < 0) { if (complete() && DataOffset(stream) < 0) return; await Task.Delay(250, token); }
            stream.Position = data;
            async Task<int> Read(byte[] buffer)
            {
                while (true)
                {
                    // Read the flag first: data flushed before completion is always read.
                    bool done = complete();
                    int count = stream.Read(buffer, 0, buffer.Length);
                    if (count % 2 == 1) { stream.Position--; count--; }
                    if (count > 0) return count;
                    if (done) return 0;
                    await Task.Delay(300, token);
                }
            }
            await RunPcmAsync(Read, null, speech, language, receive, _ => { }, token, normalize: normalize, trace: trace);
        }
    }
    /// <summary>Where the samples start, or -1 until the header has been written.</summary>
    private static long DataOffset(FileStream stream)
    {
        var header = new byte[256]; stream.Position = 0;
        int length = stream.Read(header, 0, header.Length);
        if (length < 12 || header[0] != 'R' || header[8] != 'W') return -1;
        for (int at = 12; at + 8 <= length;)
        {
            int size = BitConverter.ToInt32(header, at + 4);
            if (header[at] == 'd' && header[at + 1] == 'a' && header[at + 2] == 't' && header[at + 3] == 'a') return at + 8;
            if (size < 0) return -1;
            at += 8 + size + (size & 1);
        }
        return -1;
    }

    private static async Task RunPcmAsync(Func<byte[], Task<int>> read, double? totalSeconds, ISpeechRecognitionBackend speech, string language,
        Func<RecordingPhrase, Task> receive, Action<double> progress, CancellationToken token,
        ITranslationBackend? translation = null, ISpeechCorrectionBackend? correction = null, Func<string, string>? normalize = null, Action<string>? trace = null)
    {
        byte[] frame = new byte[3200]; List<float> segment = new(192000); Queue<string> context = new();
        long samples = 0, onset = 0, id = 0; int silence = 0;
        async Task Flush()
        {
            if (segment.Count == 0) return;
            double end = (onset + segment.Count - silence) / 16000.0;
            var audio = segment.ToArray();
            trace?.Invoke($"ready {onset / 16000.0:F1}-{end:F1}");
            var result = speech is ITimedSpeechRecognitionBackend timed ? await timed.RecognizeTimedAsync(audio, token) : await speech.RecognizeAsync(audio, token);
            segment.Clear(); silence = 0;
            string text = result.Text.Trim(); if (text.Length == 0) return;
            // The user's term dictionary is applied before AI correction and translation see the text.
            string recognized = text;
            if (normalize is not null) text = normalize(text);
            // The model can revert a registered spelling; the dictionary has the last word.
            if (correction is not null) { text = (await correction.CorrectAsync(text, context.ToArray(), language, token)).Text; if (normalize is not null) text = normalize(text); }
            string? translated = translation is null ? null : (await translation.TranslateAsync(text, token)).Text;
            var words = new List<RecordingWord>(); int cursor = 0;
            if (text == result.Text.Trim() && result.Words is not null)
                foreach (var word in result.Words)
                {
                    string tokenText = word.Text.Trim(); if (tokenText.Length == 0) continue;
                    int index = text.IndexOf(tokenText, cursor, StringComparison.Ordinal);
                    if (index < 0 || !double.IsFinite(word.Start) || !double.IsFinite(word.End) || word.Start < 0 || word.End < word.Start) { words.Clear(); break; }
                    words.Add(new(index, tokenText.Length, onset / 16000.0 + word.Start, onset / 16000.0 + word.End)); cursor = index + tokenText.Length;
                }
            await receive(new(++id, id, onset / 16000.0, text, translated, 1, language, Math.Max(onset / 16000.0, end), words.Count == 0 ? null : words.ToArray(), Original: text == recognized ? null : recognized));
            context.Enqueue(text); while (context.Count > 6) context.Dequeue();
        }
        while (true)
        {
            token.ThrowIfCancellationRequested();
            int count = await read(frame); if (count == 0) break;
            double energy = 0;
            for (int i = 0; i < count; i += 2) { float value = BitConverter.ToInt16(frame, i) / 32768f; energy += value * value; }
            bool voice = Math.Sqrt(energy / (count / 2)) > .003;
            // Recognition takes at most 12 s per request. A file still being written is read in uneven
            // pieces, so finish the segment before a piece would carry it past that limit.
            if (segment.Count > 0 && segment.Count + count / 2 > 192000) await Flush();
            if (voice && segment.Count == 0) onset = samples;
            if (voice || segment.Count > 0)
            {
                for (int i = 0; i < count; i += 2) segment.Add(BitConverter.ToInt16(frame, i) / 32768f);
                silence = voice ? 0 : silence + count / 2;
                if (segment.Count >= 192000 || silence >= 8000) await Flush();
            }
            samples += count / 2;
            if (totalSeconds is double total) progress(Math.Min(99, samples / 16000.0 / Math.Max(.001, total) * 100));
        }
        await Flush(); token.ThrowIfCancellationRequested(); progress(100);
    }
}
