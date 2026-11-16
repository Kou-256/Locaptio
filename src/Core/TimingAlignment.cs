using System.Text;
using System.Text.RegularExpressions;

namespace Locaptio.Core;

/// <summary>
/// Adds word times to an existing transcript. The audio is recognized again with word
/// timestamps (the reference); each saved utterance is then matched against the reference
/// characters spoken around its approximate time. The saved wording is never changed: only
/// its onset, end and per-word times are taken from the matching reference characters.
/// </summary>
public static class TimingAlignment
{
    private readonly record struct Timed(char Value, double Start, double End);
    public sealed record Result(RecordingDocument Document, int Aligned, int Total);

    private static char Fold(char c)
    {
        if (!char.IsLetterOrDigit(c)) return '\0';
        // Full-width forms and case must not prevent a match; katakana and hiragana are the same sound.
        string folded = c.ToString().Normalize(NormalizationForm.FormKC);
        char value = char.ToLowerInvariant(folded.Length > 0 ? folded[0] : c);
        return value is >= 'ァ' and <= 'ヶ' ? (char)(value - 0x60) : value;
    }
    private static List<Timed> Reference(IEnumerable<RecordingPhrase> phrases)
    {
        var result = new List<Timed>();
        foreach (var phrase in phrases.OrderBy(p => p.Seconds))
        {
            double end = phrase.EndSeconds is double known && known > phrase.Seconds ? known : phrase.Seconds + Math.Max(.5, phrase.Text.Length * .2);
            for (int i = 0; i < phrase.Text.Length; i++)
            {
                char value = Fold(phrase.Text[i]); if (value == '\0') continue;
                var word = phrase.Words?.FirstOrDefault(w => i >= w.StartIndex && i < w.StartIndex + w.Length);
                // Inside a word, and for phrases without word times, characters share the span evenly.
                double from = word?.Start ?? phrase.Seconds, to = word?.End ?? end;
                int offset = word is null ? i : i - word.StartIndex, length = word?.Length ?? phrase.Text.Length;
                double start = from + (to - from) * offset / length;
                if (result.Count > 0 && start < result[^1].Start) start = result[^1].Start;
                result.Add(new(value, start, Math.Max(start, from + (to - from) * (offset + 1) / length)));
            }
        }
        return result;
    }

    /// <param name="slack">How far, in seconds, a saved onset may be from the truth. Use more for Windows captions.</param>
    public static Result Apply(RecordingDocument document, IReadOnlyList<RecordingPhrase> reference, double slack = 4)
    {
        var timed = Reference(reference);
        var phrases = document.Phrases; var updated = new RecordingPhrase[phrases.Length];
        var shifts = new double?[phrases.Length]; int aligned = 0; double floor = 0;
        for (int i = 0; i < phrases.Length; i++)
        {
            var phrase = phrases[i]; updated[i] = phrase;
            double windowEnd = (i + 1 < phrases.Length ? phrases[i + 1].Seconds : phrase.EndSeconds ?? phrase.Seconds + 30) + slack;
            if (phrase.EndSeconds is double ended) windowEnd = Math.Max(windowEnd, ended + slack);
            int first = LowerBound(timed, Math.Max(floor - .5, phrase.Seconds - slack)), last = LowerBound(timed, windowEnd);
            // Bound the work for one utterance; a window this long cannot be matched reliably anyway.
            if (last - first > 3000) last = first + 3000;
            var letters = new List<(char Value, int Index)>();
            for (int k = 0; k < phrase.Text.Length; k++) { char value = Fold(phrase.Text[k]); if (value != '\0') letters.Add((value, k)); }
            if (letters.Count == 0 || last <= first) continue;
            var pairs = Local(letters, timed, first, last);
            if (pairs.Count < Math.Max(2, (letters.Count + 1) / 2)) continue;
            double start = timed[pairs[0].Reference].Start, end = timed[pairs[^1].Reference].End;
            if (start < floor - .5) continue;
            start = Math.Max(start, floor);
            updated[i] = phrase with { Seconds = start, EndSeconds = Math.Max(end, start + .1), Words = Words(phrase.Text, letters, pairs, timed, start, Math.Max(end, start + .1)) };
            shifts[i] = start - phrase.Seconds; floor = start; aligned++;
        }
        // An utterance that could not be matched moves with its nearest matched neighbour:
        // Windows captions are late by a similar amount throughout a recording.
        for (int i = 0; i < phrases.Length; i++)
        {
            if (shifts[i] is not null) continue;
            double? shift = null;
            for (int distance = 1; distance < phrases.Length && shift is null; distance++)
                shift = (i - distance >= 0 ? shifts[i - distance] : null) ?? (i + distance < phrases.Length ? shifts[i + distance] : null);
            if (shift is not double moved || aligned == 0) continue;
            var phrase = phrases[i]; double seconds = Math.Max(0, phrase.Seconds + moved);
            updated[i] = phrase with { Seconds = seconds, EndSeconds = phrase.EndSeconds is double e ? Math.Max(seconds, e + moved) : null,
                Words = phrase.Words?.Select(w => w with { Start = Math.Max(seconds, w.Start + moved), End = Math.Max(seconds, w.End + moved) }).ToArray() };
        }
        // Keep the reading order: an unmatched utterance must stay between its neighbours.
        for (int i = 1; i < updated.Length; i++)
            if (updated[i].Seconds < updated[i - 1].Seconds)
            {
                double seconds = updated[i - 1].Seconds;
                updated[i] = updated[i] with { Seconds = seconds, EndSeconds = updated[i].EndSeconds is double e ? Math.Max(e, seconds) : null,
                    Words = updated[i].Words?.Select(w => w with { Start = Math.Max(seconds, w.Start), End = Math.Max(seconds, w.End) }).ToArray() };
            }
        double Moved(double seconds)
        {
            // Bookmarks and summary evidence follow the utterance they were attached to.
            int index = -1;
            for (int i = 0; i < phrases.Length && phrases[i].Seconds <= seconds + .0005; i++) index = i;
            return index < 0 ? seconds : Math.Max(0, seconds + updated[index].Seconds - phrases[index].Seconds);
        }
        return new(document with
        {
            Phrases = updated, ApproximateTiming = document.ApproximateTiming && aligned < phrases.Length,
            Bookmarks = document.Bookmarks?.Select(b => b with { Seconds = Moved(b.Seconds) }).ToArray(),
            Summary = document.Summary?.Select(s => s with { Evidence = s.Evidence.Select(Moved).ToArray() }).ToArray()
        }, aligned, phrases.Length);
    }
    private static int LowerBound(List<Timed> timed, double seconds)
    {
        int low = 0, high = timed.Count;
        while (low < high) { int middle = (low + high) / 2; if (timed[middle].Start < seconds) low = middle + 1; else high = middle; }
        return low;
    }
    /// <summary>
    /// Local alignment: finds the one stretch of the reference that reads like the utterance.
    /// A plain longest-common-subsequence would also pair common particles far from that stretch.
    /// </summary>
    private static List<(int Letter, int Reference)> Local(List<(char Value, int Index)> letters, List<Timed> timed, int first, int last)
    {
        int rows = letters.Count, columns = last - first;
        var score = new short[rows + 1, columns + 1];
        int best = 0, bestRow = 0, bestColumn = 0;
        for (int i = 1; i <= rows; i++)
            for (int j = 1; j <= columns; j++)
            {
                int value = Math.Max(0, Math.Max(score[i - 1, j - 1] + (letters[i - 1].Value == timed[first + j - 1].Value ? 2 : -1), Math.Max(score[i - 1, j], score[i, j - 1]) - 1));
                score[i, j] = (short)Math.Min(value, short.MaxValue);
                if (value > best) { best = value; bestRow = i; bestColumn = j; }
            }
        var pairs = new List<(int, int)>();
        for (int i = bestRow, j = bestColumn; i > 0 && j > 0 && score[i, j] > 0;)
        {
            bool same = letters[i - 1].Value == timed[first + j - 1].Value;
            if (score[i, j] == score[i - 1, j - 1] + (same ? 2 : -1)) { if (same) pairs.Add((i - 1, first + j - 1)); i--; j--; }
            else if (score[i, j] == score[i - 1, j] - 1) i--;
            else j--;
        }
        pairs.Reverse(); return pairs;
    }
    private static RecordingWord[]? Words(string text, List<(char Value, int Index)> letters, List<(int Letter, int Reference)> pairs, List<Timed> timed, double start, double end)
    {
        var matched = pairs.ToDictionary(p => letters[p.Letter].Index, p => timed[p.Reference]);
        var tokens = PlaybackHighlight.Tokens(text);
        var times = new (double Start, double End)?[tokens.Count];
        for (int t = 0; t < tokens.Count; t++)
        {
            double from = double.MaxValue, to = double.MinValue;
            for (int k = tokens[t].Index; k < tokens[t].Index + tokens[t].Length; k++)
                if (matched.TryGetValue(k, out var reference)) { from = Math.Min(from, reference.Start); to = Math.Max(to, reference.End); }
            if (from <= to) times[t] = (from, to);
        }
        var words = new List<RecordingWord>(); double cursor = start;
        for (int t = 0; t < tokens.Count; t++)
        {
            // Unmatched words (punctuation, or a wording difference) fill the gap up to the next matched word.
            double next = end;
            for (int k = t + 1; k < tokens.Count; k++) if (times[k] is { } later) { next = later.Start; break; }
            double from = Math.Clamp(times[t]?.Start ?? cursor, cursor, end), to = Math.Clamp(times[t]?.End ?? next, from, end);
            if (to > from) words.Add(new(tokens[t].Index, tokens[t].Length, from, to));
            cursor = Math.Max(cursor, to);
        }
        return words.Count == 0 ? null : words.ToArray();
    }
}
