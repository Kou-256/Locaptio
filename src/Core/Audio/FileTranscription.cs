using Locaptio.Core.AI;
using NAudio.Wave;

namespace Locaptio.Core.Audio;

/// <summary>Decodes incrementally: at most twelve seconds of mono PCM are retained.</summary>
public static class FileTranscription
{
    public static async Task RunAsync(string path, ISpeechRecognitionBackend speech, string language,
        Func<RecordingPhrase, Task> receive, Action<double> progress, CancellationToken token,
        ITranslationBackend? translation = null, ISpeechCorrectionBackend? correction = null)
    {
        using var reader = new AudioFileReader(path);
        using var pcm = new MediaFoundationResampler(reader, new WaveFormat(16000, 16, 1)) { ResamplerQuality = 60 };
        byte[] frame = new byte[3200]; List<float> segment = new(192000); Queue<string> context = new();
        long samples = 0, onset = 0, id = 0; int silence = 0;
        async Task Flush()
        {
            if (segment.Count == 0) return;
            var result = await speech.RecognizeAsync(segment.ToArray(), token); segment.Clear(); silence = 0;
            string text = result.Text.Trim(); if (text.Length == 0) return;
            if (correction is not null) text = (await correction.CorrectAsync(text, context.ToArray(), language, token)).Text;
            string? translated = translation is null ? null : (await translation.TranslateAsync(text, token)).Text;
            await receive(new(++id, id, onset / 16000.0, text, translated, 1, language));
            context.Enqueue(text); while (context.Count > 6) context.Dequeue();
        }
        while (true)
        {
            token.ThrowIfCancellationRequested();
            int count = pcm.Read(frame, 0, frame.Length); if (count == 0) break;
            double energy = 0;
            for (int i = 0; i < count; i += 2) { float value = BitConverter.ToInt16(frame, i) / 32768f; energy += value * value; }
            bool voice = Math.Sqrt(energy / (count / 2)) > .003;
            if (voice && segment.Count == 0) onset = samples;
            if (voice || segment.Count > 0)
            {
                for (int i = 0; i < count; i += 2) segment.Add(BitConverter.ToInt16(frame, i) / 32768f);
                silence = voice ? 0 : silence + count / 2;
                if (segment.Count >= 192000 || silence >= 8000) await Flush();
            }
            samples += count / 2;
            progress(Math.Min(99, samples / 16000.0 / Math.Max(.001, reader.TotalTime.TotalSeconds) * 100));
        }
        await Flush(); token.ThrowIfCancellationRequested(); progress(100);
    }
}
