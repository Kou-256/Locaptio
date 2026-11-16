using System.Text.Json;

namespace Locaptio.Core;

public sealed record ComparisonRow(double Seconds, string Before, string After)
{
    public bool Changed => Before != After;
}
/// <summary>An earlier transcript of the same audio, kept so results can be compared and switched back.</summary>
public sealed record TranscriptVersion(string Label, DateTimeOffset SavedAt, RecordingDocument Document);

/// <summary>Lines up two transcripts of the same audio by time, since their utterance boundaries differ.</summary>
public static class TranscriptComparison
{
    public static List<ComparisonRow> Align(IReadOnlyList<RecordingPhrase> before, IReadOnlyList<RecordingPhrase> after)
    {
        static double End(IReadOnlyList<RecordingPhrase> list, int index)
        {
            var phrase = list[index];
            double end = phrase.EndSeconds ?? (index + 1 < list.Count ? list[index + 1].Seconds : phrase.Seconds + Math.Max(1, phrase.Text.Length * .2));
            return Math.Max(end, phrase.Seconds + .2);
        }
        var rows = new List<ComparisonRow>(); int a = 0, b = 0;
        while (a < before.Count || b < after.Count)
        {
            // Start a group at the earliest remaining utterance and absorb everything overlapping it.
            double start = Math.Min(a < before.Count ? before[a].Seconds : double.MaxValue, b < after.Count ? after[b].Seconds : double.MaxValue), end = start;
            var left = new List<string>(); var right = new List<string>();
            for (bool grew = true; grew;)
            {
                grew = false;
                while (a < before.Count && (left.Count + right.Count == 0 ? before[a].Seconds <= start : before[a].Seconds < end - .3)) { left.Add(before[a].Text); end = Math.Max(end, End(before, a++)); grew = true; }
                while (b < after.Count && (left.Count + right.Count == 0 ? after[b].Seconds <= start : after[b].Seconds < end - .3)) { right.Add(after[b].Text); end = Math.Max(end, End(after, b++)); grew = true; }
            }
            rows.Add(new(start, string.Join(" ", left), string.Join(" ", right)));
        }
        return rows;
    }

    /// <summary>0–1 share of characters the two texts have in common, ignoring spacing and punctuation.</summary>
    public static double Similarity(string before, string after)
    {
        static string Letters(string text) => new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).Take(4000).ToArray());
        string x = Letters(before), y = Letters(after);
        if (x.Length == 0 && y.Length == 0) return 1;
        if (x.Length == 0 || y.Length == 0) return 0;
        var previous = new int[y.Length + 1]; var current = new int[y.Length + 1];
        for (int i = 1; i <= x.Length; i++)
        {
            for (int j = 1; j <= y.Length; j++) current[j] = x[i - 1] == y[j - 1] ? previous[j - 1] + 1 : Math.Max(previous[j], current[j - 1]);
            (previous, current) = (current, previous);
        }
        return 2.0 * previous[y.Length] / (x.Length + y.Length);
    }

    public static async Task<List<TranscriptVersion>> LoadVersionsAsync(string audio)
    {
        string path = RecordingFiles.Versions(audio);
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 256 * 1024 * 1024) return [];
            var versions = JsonSerializer.Deserialize<List<TranscriptVersion>>(await File.ReadAllTextAsync(path)) ?? [];
            return versions.Where(v => v is { Document.Phrases: not null } && v.Document.Phrases.All(p => p is not null && p.Text is not null && double.IsFinite(p.Seconds))).ToList();
        }
        catch (Exception ex) when (ex is JsonException or IOException) { return []; }
    }
    /// <summary>Appends a version; only the most recent ten are retained.</summary>
    public static async Task SaveVersionAsync(string audio, string label, RecordingDocument document)
    {
        var versions = await LoadVersionsAsync(audio);
        versions.Add(new(label, DateTimeOffset.Now, document));
        string path = RecordingFiles.Versions(audio), temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(versions.TakeLast(10)));
        File.Move(temporary, path, true);
    }
}
