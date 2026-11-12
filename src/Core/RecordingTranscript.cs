using System.Diagnostics;
using System.Text.Json;

namespace Locaptio.Core;

public sealed record RecordingPhrase(long Id, long Started, double Seconds, string Text, string? Translation, int Revision, string Language = "ja");
public sealed record RecordingDocument(int Version, string AudioFile, DateTimeOffset RecordedAt, bool ApproximateTiming, RecordingPhrase[] Phrases);

/// <summary>Only text and media offsets are persisted. No audio is held in memory.</summary>
public sealed class RecordingTranscript(string audioPath, long started, bool approximateTiming)
{
    private readonly Dictionary<(long, long), RecordingPhrase> phrases = new();
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
        if (string.IsNullOrWhiteSpace(update.English)) return phrases.Remove(key);
        double seconds = Math.Max(0, Stopwatch.GetElapsedTime(Started, update.Started).TotalSeconds - (approximateTiming ? 1.0 : 0));
        var phrase = new RecordingPhrase(update.Id, update.Started, seconds, update.English, update.Japanese, update.Revision, update.SourceLanguage);
        if (previous == phrase) return false;
        phrases[key] = phrase; return true;
    }
    public RecordingDocument Snapshot() => new(1, Path.GetFileName(AudioPath), RecordedAt, approximateTiming, phrases.Values.OrderBy(p => p.Seconds).ThenBy(p => p.Id).ToArray());
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
        if (document.Version != 1 || document.Phrases is null || document.Phrases.Any(p => !double.IsFinite(p.Seconds) || p.Seconds < 0 || p.Text is null)) throw new InvalidDataException("録音の文字起こし形式に対応していません。");
        // Never follow a path supplied by the sidecar; audio is selected by the user.
        return document with { Phrases = document.Phrases.OrderBy(p => p.Seconds).ThenBy(p => p.Id).ToArray() };
    }
}
