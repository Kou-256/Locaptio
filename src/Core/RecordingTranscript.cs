using System.Diagnostics;
using System.Text.Json;

namespace Locaptio.Core;

public sealed record RecordingWord(int StartIndex, int Length, double Start, double End);
// Original keeps the recognized text from before AI correction or a manual edit.
public sealed record RecordingPhrase(long Id, long Started, double Seconds, string Text, string? Translation, int Revision, string Language = "ja", double? EndSeconds = null, RecordingWord[]? Words = null, string? Original = null, bool Edited = false);
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
        var phrase = new RecordingPhrase(update.Id, update.Started, seconds, update.English, update.Japanese, update.Revision, update.SourceLanguage, Original: previous?.Original, Edited: previous?.Edited ?? false);
        if (previous == phrase) return false;
        phrases[key] = phrase; return true;
    }
    public RecordingDocument Snapshot() => new(1, Path.GetFileName(AudioPath), RecordedAt, approximateTiming, phrases.Values.OrderBy(p => p.Seconds).ThenBy(p => p.Id).ToArray(),
        bookmarks.Count == 0 ? null : bookmarks.OrderBy(b => b.Seconds).ToArray());
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
            || p.Words is not null && p.Words.Any(w => w is null || w.StartIndex < 0 || w.Length < 1 || w.StartIndex > p.Text.Length - w.Length || !double.IsFinite(w.Start) || !double.IsFinite(w.End) || w.Start < p.Seconds || w.End < w.Start))) throw new InvalidDataException("録音の文字起こし形式に対応していません。");
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
