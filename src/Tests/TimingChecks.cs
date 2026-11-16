using System.Diagnostics;
using Locaptio.Core;
using NAudio.Wave;

/// <summary>
/// Measures how far saved utterance starts are from where speech begins in the recording,
/// before and after correction. Synthetic speech runs everywhere; real speech uses the local
/// artifacts/test-*.wav files. No audio device is used.
/// </summary>
internal static class TimingChecks
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    /// <summary>Paused stretches of a video recording are removed from the transcript timeline.</summary>
    public static void Pause()
    {
        long t0 = Stopwatch.GetTimestamp(), f = Stopwatch.Frequency;
        var recording = new RecordingTranscript(Path.Combine(Path.GetTempPath(), "pause-check.mp4"), long.MaxValue, false);
        Check(!recording.Observe(new(1, 1, "開始前の発話", null, true, t0, 0, 0, "ja")), "Speech before the first frame was kept");
        recording.Started = t0;
        recording.Observe(new(2, 1, "最初", null, true, t0 + 2 * f, 0, 0, "ja"));
        recording.Pause(t0 + 5 * f);
        Check(recording.Paused && !recording.Observe(new(3, 1, "一時停止中", null, true, t0 + 6 * f, 0, 0, "ja")), "Speech while paused was kept");
        recording.Resume(t0 + 9 * f);
        recording.Observe(new(4, 1, "再開後", null, true, t0 + 10 * f, 0, 0, "ja"));
        recording.Pause(t0 + 12 * f); recording.Resume(t0 + 13 * f);
        var phrases = recording.Snapshot().Phrases;
        Check(phrases.Select(p => p.Seconds).SequenceEqual([2.0, 6.0]) && Math.Abs(recording.Offset(t0 + 15 * f) - 10) < 1e-9 && !recording.Paused,
            "Paused time not removed: " + string.Join(", ", phrases.Select(p => p.Seconds)) + " offset=" + recording.Offset(t0 + 15 * f));
        Console.WriteLine("PASS: video recording timeline excludes time before the first frame and paused stretches");
    }

    public static void Tracker()
    {
        // A Japanese row longer than 180 characters is split. The remainder must start when its own
        // first characters appeared, not when the split happened seconds later.
        var tracker = new WindowsCaptionTracker(true);
        long t0 = Stopwatch.GetTimestamp(), f = Stopwatch.Frequency;
        tracker.Observe("", t0).ToList();
        string sentence = "今日は会議の議事録を自動で保存する機能について説明します。";
        string text = ""; var starts = new Dictionary<long, long>();
        for (int i = 1; i <= 8; i++)
        {
            text += sentence; long now = t0 + i * 3 * f;
            foreach (var update in tracker.Observe(text, now)) starts.TryAdd(update.Id, update.Started);
        }
        var ordered = starts.OrderBy(s => s.Key).Select(s => (s.Value - t0) / (double)f).ToArray();
        Check(ordered.Length >= 2, "Long row was not split");
        // Each row's start is the 3-second tick on which its first sentence appeared.
        Check(ordered[0] == 3 && ordered[1] % 3 == 0 && ordered[1] < (t0 + 8 * 3 * f - t0) / (double)f, "Split row start is not when its text appeared: " + string.Join(",", ordered));
        int firstSentenceOfSecondRow = (int)Math.Round(ordered[1] / 3);
        Check(firstSentenceOfSecondRow is >= 5 and <= 7, "Split row start points to the wrong sentence: " + string.Join(",", ordered));
        // English rows split at sentence ends the same way.
        var english = new WindowsCaptionTracker(false); english.Observe("", t0).ToList();
        var englishStarts = new Dictionary<long, long>();
        string words = "";
        string[] parts = ["We will look at the new recording", " features today.", " Autosave keeps your work", " every five seconds."];
        for (int i = 0; i < parts.Length; i++)
        {
            words += parts[i];
            foreach (var update in english.Observe(words, t0 + (i + 1) * f)) englishStarts.TryAdd(update.Id, update.Started);
        }
        var englishTimes = englishStarts.OrderBy(s => s.Key).Select(s => (s.Value - t0) / (double)f).ToArray();
        Check(englishTimes.SequenceEqual([1.0, 3.0]), "English sentence start not taken from its text: " + string.Join(",", englishTimes));
        Console.WriteLine($"PASS: Windows caption split rows start when their text appeared ({string.Join(", ", ordered.Select(o => o.ToString("F0") + "s"))}; English {string.Join(", ", englishTimes)})");
    }

    /// <summary>Writes speech-like sound at known onsets and returns them.</summary>
    private static List<double> SyntheticSpeech(string path, int seed)
    {
        var random = new Random(seed); var onsets = new List<double>(); double cursor = 1.2;
        using var writer = new WaveFileWriter(path, new WaveFormat(16000, 16, 1));
        void Write(double seconds, Func<double, float> sample) { int n = (int)(seconds * 16000); for (int i = 0; i < n; i++) writer.WriteSample(sample(i / 16000.0)); }
        float Noise() => (float)((random.NextDouble() * 2 - 1) * .0015);
        Write(cursor, _ => Noise());
        for (int k = 0; k < 24; k++)
        {
            onsets.Add(cursor);
            double length = 1.2 + random.NextDouble() * 3, pitch = 110 + random.NextDouble() * 120;
            // Syllables: 4–6 Hz loudness, voiced harmonics, and one short in-utterance pause (< 240 ms).
            double pause = .4 + random.NextDouble() * (length - .8), rate = 4 + random.NextDouble() * 2;
            Write(length, t =>
            {
                if (t > pause && t < pause + .15) return Noise();
                double envelope = .35 + .65 * Math.Pow(Math.Abs(Math.Sin(Math.PI * rate * t)), .6);
                double rise = Math.Min(1, t / .03);
                return (float)(rise * envelope * .18 * (Math.Sin(2 * Math.PI * pitch * t) + .5 * Math.Sin(4 * Math.PI * pitch * t) + .25 * Math.Sin(6 * Math.PI * pitch * t))) + Noise();
            });
            double gap = .35 + random.NextDouble() * 1.4; Write(gap, _ => Noise()); cursor += length + gap;
        }
        return onsets;
    }

    private static (double Before, double After, int Within) Measure(string audio, List<double> truth, Func<double, double> estimate, bool approximate, string label)
    {
        var phrases = truth.Select((t, i) => new RecordingPhrase(i + 1, i + 1, Math.Max(0, estimate(t)), "発話" + (i + 1), null, 1)).ToArray();
        var document = new RecordingDocument(1, Path.GetFileName(audio), DateTimeOffset.Now, approximate, phrases, [new(phrases[3].Seconds + .1, "重要", 4)]);
        var clock = Stopwatch.StartNew();
        var onsets = SpeechOnsets.Find(SpeechOnsets.Envelope(audio));
        var result = SpeechOnsets.Align(document, onsets);
        double Error(RecordingPhrase[] list) => list.Zip(truth).Select(p => Math.Abs(p.First.Seconds - p.Second)).Average();
        double before = Error(phrases), after = Error(result.Document.Phrases);
        int within = result.Document.Phrases.Zip(truth).Count(p => Math.Abs(p.First.Seconds - p.Second) <= .1);
        Console.WriteLine($"  {label}: mean start error {before:F2}s → {after:F2}s; within 0.1s {within}/{truth.Count}; estimated delay {result.Delay:F2}s; onsets {onsets.Count}; {clock.ElapsedMilliseconds}ms");
        Check(result.Document.Phrases.Zip(result.Document.Phrases.Skip(1)).All(p => p.First.Seconds <= p.Second.Seconds), label + ": order broken");
        Check(Math.Abs(result.Document.Bookmarks![0].Seconds - (result.Document.Phrases[3].Seconds + .1)) < .01, label + ": bookmark did not move with its utterance");
        return (before, after, within);
    }

    public static void Synthetic()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Locaptio-timing-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string audio = Path.Combine(directory, "speech.wav"); var truth = SyntheticSpeech(audio, 7);
            var random = new Random(3);
            // Windows captions: text appears 1.4 s ± 0.35 s after speech; Locaptio saved appearance − 1.0 s.
            var windows = Measure(audio, truth, t => t + 1.4 + (random.NextDouble() - .5) * .7 - 1.0, true, "Windows captions (delay 1.4±0.35s)");
            Check(windows.Before > .3 && windows.After < .08 && windows.Within >= truth.Count * 9 / 10, "Windows caption timing not corrected");
            var slow = Measure(audio, truth, t => t + 2.6 + (random.NextDouble() - .5) * .4 - 1.0, true, "Windows captions on a busy PC (delay 2.6s)");
            Check(slow.After < .1 && slow.Within >= truth.Count * 8 / 10, "Large caption delay not corrected");
            var whisper = Measure(audio, truth, t => t + .06 + random.NextDouble() * .12, false, "Whisper voice detector (late 0.06–0.18s)");
            Check(whisper.After < .04 && whisper.After < whisper.Before, "Whisper timing not tightened");
            // Continuous sound without pauses gives no consistent delay: saved times must not move far.
            string music = Path.Combine(directory, "music.wav");
            using (var writer = new WaveFileWriter(music, new WaveFormat(16000, 16, 1))) for (int i = 0; i < 16000 * 40; i++) writer.WriteSample((float)(.2 * Math.Sin(i * 2 * Math.PI * 220 / 16000)));
            var none = SpeechOnsets.Align(new(1, "m.wav", DateTimeOffset.Now, true, [new(1, 1, 5, "a", null, 1), new(2, 2, 12, "b", null, 1)]), SpeechOnsets.Find(SpeechOnsets.Envelope(music)));
            Check(none.Delay == 0 && none.Document.Phrases[0].Seconds == 5 && none.Document.Phrases[1].Seconds == 12, "Continuous audio moved the saved times");
            Console.WriteLine("PASS: recorded-audio onset correction for Windows caption delay, busy-PC delay and Whisper detector; continuous audio left unchanged");
        }
        finally { Directory.Delete(directory, true); }
    }

    /// <summary>Real Japanese/English speech files joined with silence at known positions.</summary>
    public static void Real()
    {
        string root = Path.Combine(Directory.GetCurrentDirectory(), "artifacts");
        var sources = new[] { "test-ja.wav", "test-translation.wav", "test-ja-continuous.wav" }.Select(n => Path.Combine(root, n)).Where(File.Exists).ToArray();
        if (sources.Length == 0) throw new FileNotFoundException("artifacts/test-*.wav are required for --timing-real");
        string directory = Path.Combine(Path.GetTempPath(), "Locaptio-timing-real-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string audio = Path.Combine(directory, "meeting.wav"); var truth = new List<double>(); var random = new Random(11);
            using (var writer = new WaveFileWriter(audio, new WaveFormat(16000, 16, 1)))
            {
                double position = 0;
                void Silence(double seconds) { for (int i = 0; i < (int)(seconds * 16000); i++) writer.WriteSample((float)((random.NextDouble() * 2 - 1) * .001)); position += seconds; }
                Silence(1.5);
                for (int round = 0; round < 4; round++)
                    foreach (string source in sources)
                    {
                        using var reader = new AudioFileReader(source);
                        using var resampled = new MediaFoundationResampler(reader, WaveFormat.CreateIeeeFloatWaveFormat(16000, 1));
                        var samples = new List<float>(); var buffer = new byte[64000]; int read;
                        while ((read = resampled.Read(buffer, 0, buffer.Length)) > 0) for (int i = 0; i + 3 < read; i += 4) samples.Add(BitConverter.ToSingle(buffer, i));
                        // The utterance starts at the clip's first sound; leading silence of the clip is kept.
                        int first = samples.FindIndex(v => Math.Abs(v) > .02);
                        truth.Add(position + Math.Max(0, first) / 16000.0);
                        foreach (float v in samples) writer.WriteSample(v);
                        position += samples.Count / 16000.0; Silence(.8 + random.NextDouble() * 1.2);
                    }
            }
            Console.WriteLine($"Real speech: {truth.Count} utterances from {string.Join(", ", sources.Select(Path.GetFileName))}");
            var delays = new Random(5);
            var windows = Measure(audio, truth, t => t + 1.6 + (delays.NextDouble() - .5) * .8 - 1.0, true, "Windows captions (delay 1.6±0.4s)");
            var whisper = Measure(audio, truth, t => t + .05 + delays.NextDouble() * .15, false, "Whisper voice detector");
            Check(windows.After < .15 && windows.Within >= truth.Count * 3 / 4 && whisper.After <= whisper.Before, "Real speech timing not corrected");
            Console.WriteLine("PASS: real speech onset correction");
        }
        finally { Directory.Delete(directory, true); }
    }
}
