using System.Diagnostics;
using System.Text.Json;

namespace Locaptio.Core;

public sealed record RecordingWord(int StartIndex, int Length, double Start, double End);
// Original keeps the recognized text from before AI correction or a manual edit.
/// <param name="AiCorrected">The text was changed by AI correction (the mark and the text before it are shown).</param>
/// <param name="TranslationRefined">The translation was finished by the local AI.</param>
public sealed record RecordingPhrase(long Id, long Started, double Seconds, string Text, string? Translation, int Revision, string Language = "ja", double? EndSeconds = null, RecordingWord[]? Words = null, string? Original = null, bool Edited = false, double? Match = null,
    bool AiCorrected = false, bool TranslationRefined = false);
public sealed record RecordingBookmark(double Seconds, string Label, long PhraseId = 0, string? Text = null);
/// <summary>Evidence is stored as phrase onsets so it survives the row IDs of a later session.</summary>
public sealed record RecordingSummaryItem(string Section, string Text, double[] Evidence);
// Optional members are appended so files written by older versions still load.
public sealed record RecordingDocument(int Version, string AudioFile, DateTimeOffset RecordedAt, bool ApproximateTiming, RecordingPhrase[] Phrases, RecordingBookmark[]? Bookmarks = null, RecordingSummaryItem[]? Summary = null, string? Model = null);

/// <summary>Only text and media offsets are persisted. No audio is held in memory.</summary>
public sealed class RecordingTranscript(string audioPath, long started, bool approximateTiming)
{
    private readonly Dictionary<(long, long), RecordingPhrase> phrases = new();
    private readonly List<RecordingBookmark> bookmarks = new();
    // Paused stretches of a video recording are not in the file, so they are not in its timeline.
    private readonly List<(long Start, long End)> pauses = new();
    public bool Approximate => approximateTiming;
    public bool Paused => pauses.Count > 0 && pauses[^1].End == long.MaxValue;
    public void Pause(long timestamp) { if (!Paused) pauses.Add((timestamp, long.MaxValue)); }
    public void Resume(long timestamp) { if (Paused) pauses[^1] = (pauses[^1].Start, Math.Max(pauses[^1].Start, timestamp)); }
    private double PausedBefore(long timestamp) => pauses.Where(p => p.Start < timestamp).Sum(p => Stopwatch.GetElapsedTime(p.Start, Math.Min(p.End, timestamp)).TotalSeconds);
    private bool InPause(long timestamp) => pauses.Any(p => timestamp >= p.Start && timestamp < p.End);
    /// <summary>Media offset of "now" on the same clock as the phrases.</summary>
    public double Offset(long timestamp) => Math.Max(0, Stopwatch.GetElapsedTime(Started, timestamp).TotalSeconds - PausedBefore(timestamp));
    public RecordingBookmark AddBookmark(string label, long timestamp, long phraseId = 0, string? text = null)
    {
        var bookmark = new RecordingBookmark(Offset(timestamp), label, phraseId, text); bookmarks.Add(bookmark); return bookmark;
    }
    public bool RemoveBookmark(RecordingBookmark bookmark) => bookmarks.Remove(bookmark);
    /// <summary>Removes the bookmark with this label on this utterance; the screen may show it on another clock.</summary>
    public bool RemoveBookmark(string label, long phraseId)
    {
        int index = bookmarks.FindLastIndex(b => b.Label == label && b.PhraseId == phraseId);
        if (index < 0) return false;
        bookmarks.RemoveAt(index); return true;
    }
    public void Annotate(long id, long started, string? original, bool edited)
    {
        if (phrases.TryGetValue((id, started), out var phrase)) phrases[(id, started)] = phrase with { Original = original, Edited = edited };
    }
    public string AudioPath { get; } = Path.GetFullPath(audioPath);
    public DateTimeOffset RecordedAt { get; } = DateTimeOffset.Now;
    public long Started { get; set; } = started;
    public static string Sidecar(string audio) => audio + ".locaptio.json";
    public bool Observe(CaptionUpdate update)
    {
        var key = (update.Id, update.Started);
        if (phrases.TryGetValue(key, out var previous) && previous.Revision > update.Revision) return false;
        // Captions already in progress when recording began have no reliable onset.
        if (update.Started < Started) return false;
        // Speech while a video recording is paused is not in the file.
        if (InPause(update.Started)) return false;
        if (string.IsNullOrWhiteSpace(update.English)) return phrases.Remove(key);
        double seconds = Math.Max(0, Offset(update.Started) - (approximateTiming ? 1.0 : 0));
        var phrase = new RecordingPhrase(update.Id, update.Started, seconds, update.English, update.Japanese, update.Revision, update.SourceLanguage, Original: previous?.Original, Edited: previous?.Edited ?? false,
            AiCorrected: update.AiCorrected || (previous?.AiCorrected ?? false), TranslationRefined: update.TranslationRefined || (previous?.TranslationRefined ?? false));
        if (update.Appeared is { } times && times.Length == update.English.Length) appeared[key] = times; else appeared.Remove(key);
        if (previous == phrase) return false;
        phrases[key] = phrase; return true;
    }
    private readonly Dictionary<(long, long), long[]> appeared = new();
    public RecordingDocument Snapshot() => new(1, Path.GetFileName(AudioPath), RecordedAt, approximateTiming,
        phrases.Select(p => appeared.TryGetValue(p.Key, out var times) ? p.Value with { Words = AppearanceWords(p.Value, times) } : p.Value).OrderBy(p => p.Seconds).ThenBy(p => p.Id).ToArray(),
        bookmarks.Count == 0 ? null : bookmarks.OrderBy(b => b.Seconds).ToArray());
    /// <summary>
    /// Word times from when each word appeared in the captions. A word is shown after it is spoken, by
    /// about the same delay as the start of its utterance, so each word ends where it appeared minus that
    /// delay and starts where the previous one ended. Words that appeared together share their span.
    /// The caption delay itself is measured from the audio when recording stops (SpeechOnsets).
    /// </summary>
    private RecordingWord[]? AppearanceWords(RecordingPhrase phrase, long[] times)
    {
        var tokens = PlaybackHighlight.Tokens(phrase.Text);
        if (tokens.Count == 0) return null;
        double lag = approximateTiming ? 1.0 : 0;
        var ends = tokens.Select(t => Math.Max(phrase.Seconds, Offset(times[t.Index + t.Length - 1]) - lag)).ToArray();
        for (int i = 1; i < ends.Length; i++) ends[i] = Math.Max(ends[i], ends[i - 1]);
        var words = new List<RecordingWord>(tokens.Count); double cursor = phrase.Seconds;
        for (int i = 0; i < tokens.Count;)
        {
            // Words that appeared together share one span. A burst with no time of its own (the first
            // words, shown as the utterance began) shares the span of the words that appeared next.
            int j = i;
            while (j + 1 < tokens.Count && (ends[j + 1] - ends[j] < .001 || ends[j] - cursor < .05)) j++;
            double end = Math.Max(ends[j], cursor + .25 * (j - i + 1) * (ends[j] - cursor < .05 ? 1 : 0));
            int letters = 0; for (int k = i; k <= j; k++) letters += tokens[k].Length;
            double from = cursor;
            for (int k = i, done = 0; k <= j; k++)
            {
                done += tokens[k].Length; double to = cursor + (end - cursor) * done / letters;
                words.Add(new(tokens[k].Index, tokens[k].Length, from, to)); from = to;
            }
            cursor = end; i = j + 1;
        }
        return words.ToArray();
    }
    public static RecordingPhrase[] Search(RecordingDocument document, string query) => string.IsNullOrWhiteSpace(query) ? []
        : document.Phrases.Where(p => p.Text.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase) || (p.Translation?.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase) ?? false)).ToArray();
    public static RecordingPhrase? At(RecordingDocument document, double seconds)
    {
        int low = 0, high = document.Phrases.Length - 1, found = -1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            if (document.Phrases[middle].Seconds <= seconds) { found = middle; low = middle + 1; }
            else high = middle - 1;
        }
        return found < 0 ? null : document.Phrases[found];
    }
    public static async Task SaveAsync(string audio, RecordingDocument document)
    {
        string target = Sidecar(audio), temporary = target + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(document));
        File.Move(temporary, target, true);
    }
    public static async Task<RecordingDocument> LoadAsync(string audio)
    {
        var info = new FileInfo(Sidecar(audio));
        if (info.Length > 64 * 1024 * 1024) throw new InvalidDataException("文字起こしデータが大きすぎます。");
        var document = JsonSerializer.Deserialize<RecordingDocument>(await File.ReadAllTextAsync(info.FullName)) ?? throw new InvalidDataException("録音の文字起こしを読み込めません。");
        if (document.Version != 1 || document.Phrases is null || document.Phrases.Any(p => p is null || !double.IsFinite(p.Seconds) || p.Seconds < 0 || p.Text is null || p.EndSeconds is double end && (!double.IsFinite(end) || end < p.Seconds)
            || p.Words is not null && p.Words.Any(w => w is null || w.StartIndex < 0 || w.Length < 1 || w.StartIndex > p.Text.Length - w.Length || !double.IsFinite(w.Start) || !double.IsFinite(w.End) || w.Start < p.Seconds || w.End < w.Start)
            || p.Match is double match && !(match >= 0 && match <= 1))) throw new InvalidDataException("録音の文字起こし形式に対応していません。");
        // Never follow a path supplied by the sidecar; audio is selected by the user.
        // Bookmarks and summaries are optional conveniences: drop malformed entries instead of rejecting the recording.
        return document with
        {
            Phrases = document.Phrases.OrderBy(p => p.Seconds).ThenBy(p => p.Id).ToArray(),
            Bookmarks = document.Bookmarks?.Where(b => b is not null && double.IsFinite(b.Seconds) && b.Seconds >= 0 && !string.IsNullOrWhiteSpace(b.Label)).OrderBy(b => b.Seconds).ToArray(),
            Summary = document.Summary?.Where(i => i is not null && !string.IsNullOrWhiteSpace(i.Section) && !string.IsNullOrWhiteSpace(i.Text))
                .Select(i => i with { Evidence = (i.Evidence ?? []).Where(e => double.IsFinite(e) && e >= 0).ToArray() }).ToArray()
        };
    }
}
