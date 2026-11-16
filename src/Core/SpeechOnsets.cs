using NAudio.Wave;

namespace Locaptio.Core;

/// <summary>
/// Corrects utterance start times from the recorded audio itself.
///
/// Windows Live Captions reports text when it appears on screen, which is later than the speech
/// by a delay that varies by PC and load. Whisper rows start at its voice detector, which can be
/// slightly late for soft speech. The recording knows where speech really begins: this finds the
/// speech onsets (silence followed by sound), estimates the common caption delay from how the
/// saved start times line up with them, and moves each start to its onset. Wording is unchanged.
/// </summary>
public static class SpeechOnsets
{
    public const double Frame = .02;
    public sealed record Result(RecordingDocument Document, double Delay, int Snapped, int Total, int Onsets);

    /// <summary>RMS level of each 20 ms of the file, mixed to mono. Reads the file once in small blocks.</summary>
    public static float[] Envelope(string audio, CancellationToken token = default)
    {
        using var reader = new AudioFileReader(audio);
        int channels = reader.WaveFormat.Channels, window = Math.Max(1, (int)(reader.WaveFormat.SampleRate * Frame)) * channels;
        var levels = new List<float>((int)(reader.TotalTime.TotalSeconds / Frame) + 8);
        var buffer = new float[window * 50]; double sum = 0; int filled = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            int read = reader.Read(buffer, 0, buffer.Length);
            if (read <= 0) break;
            for (int i = 0; i < read; i++)
            {
                sum += buffer[i] * (double)buffer[i];
                if (++filled == window) { levels.Add((float)Math.Sqrt(sum / window)); sum = 0; filled = 0; }
            }
        }
        if (filled > 0) levels.Add((float)Math.Sqrt(sum / filled));
        return levels.ToArray();
    }

    /// <summary>Times where at least 240 ms of quiet is followed by sound that lasts.</summary>
    public static List<double> Find(float[] levels)
    {
        var onsets = new List<double>();
        if (levels.Length < 20) return onsets;
        var sorted = levels.Where(l => l > 1e-6f).Order().ToArray();
        if (sorted.Length < 10) return onsets;
        float floor = sorted[sorted.Length / 10], loud = sorted[(int)(sorted.Length * .9)];
        // Between the noise floor and typical speech; an absolute minimum ignores near-digital silence.
        float threshold = Math.Max(Math.Max(floor * 3, floor + (loud - floor) * .12f), .0025f);
        int quiet = 12;
        for (int i = 0; i < levels.Length; i++)
        {
            if (levels[i] < threshold) { quiet++; continue; }
            if (quiet >= 12)
            {
                int voiced = 0;
                for (int k = i; k < Math.Min(levels.Length, i + 10); k++) if (levels[k] >= threshold) voiced++;
                // A click or a single loud frame is not the start of speech.
                if (voiced >= 5) onsets.Add(i * Frame);
            }
            quiet = 0;
        }
        return onsets;
    }

    public static Result Align(RecordingDocument document, IReadOnlyList<double> onsets)
    {
        var phrases = document.Phrases;
        if (phrases.Length == 0 || onsets.Count == 0) return new(document, 0, 0, phrases.Length, onsets.Count);
        // Caption text appears after speech; estimate how much earlier speech typically starts.
        // Recordings with exact recognizer times only get a small local correction.
        double delay = 0;
        if (document.ApproximateTiming)
        {
            double bestDelay = 0, bestScore = -1;
            for (int step = -20; step <= 80; step++)
            {
                double candidate = step * .05, score = 0;
                foreach (var phrase in phrases)
                {
                    double distance = Math.Abs(Nearest(onsets, phrase.Seconds - candidate) - (phrase.Seconds - candidate));
                    if (distance <= .25) score += 1 - distance / .5;
                }
                // Prefer the smallest correction among equally good ones.
                if (score > bestScore + 1e-9 || Math.Abs(score - bestScore) < 1e-9 && Math.Abs(candidate) < Math.Abs(bestDelay)) { bestScore = score; bestDelay = candidate; }
            }
            // Too few matches means no consistent delay (e.g. continuous music); keep the saved times.
            if (bestScore >= Math.Max(1.5, phrases.Length * .2)) delay = bestDelay;
        }
        double before = document.ApproximateTiming ? .8 : .35, after = document.ApproximateTiming ? .4 : .2;
        var updated = new RecordingPhrase[phrases.Length]; int snapped = 0; double floor = 0;
        for (int i = 0; i < phrases.Length; i++)
        {
            var phrase = phrases[i];
            double target = Math.Max(0, phrase.Seconds - delay), onset = Nearest(onsets, target), seconds = target;
            if (onset >= target - before && onset <= target + after && onset >= floor) { seconds = onset; snapped++; }
            seconds = Math.Max(seconds, floor);
            double shift = seconds - phrase.Seconds;
            updated[i] = phrase with
            {
                Seconds = seconds,
                EndSeconds = phrase.EndSeconds is double end ? Math.Max(seconds, end + shift) : null,
                Words = phrase.Words?.Select(w => w with { Start = Math.Max(seconds, w.Start + shift), End = Math.Max(seconds, w.End + shift) }).ToArray()
            };
            floor = seconds;
        }
        double Moved(double seconds)
        {
            int index = -1;
            for (int i = 0; i < phrases.Length && phrases[i].Seconds <= seconds + .0005; i++) index = i;
            return index < 0 ? Math.Max(0, seconds - delay) : Math.Max(0, seconds + updated[index].Seconds - phrases[index].Seconds);
        }
        return new(document with
        {
            Phrases = updated,
            Bookmarks = document.Bookmarks?.Select(b => b with { Seconds = Moved(b.Seconds) }).ToArray(),
            Summary = document.Summary?.Select(s => s with { Evidence = s.Evidence.Select(Moved).ToArray() }).ToArray()
        }, delay, snapped, phrases.Length, onsets.Count);
    }

    private static double Nearest(IReadOnlyList<double> sorted, double value)
    {
        int low = 0, high = sorted.Count - 1;
        while (low < high) { int middle = (low + high) / 2; if (sorted[middle] < value) low = middle + 1; else high = middle; }
        double best = sorted[low];
        if (low > 0 && Math.Abs(sorted[low - 1] - value) <= Math.Abs(best - value)) best = sorted[low - 1];
        return best;
    }
}
