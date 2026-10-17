using System.Text;

namespace Locaptio.Core;

/// <summary>Session text in RAM, independent of the overlay's visible row limit.</summary>
public sealed class TranscriptBuffer
{
    private readonly SortedDictionary<long, CaptionUpdate> entries = new();
    public int Count => entries.Count;
    public void Clear() => entries.Clear();
    public void Update(CaptionUpdate update)
    {
        if (entries.TryGetValue(update.Id, out var previous) && previous.Revision > update.Revision) return;
        if (string.IsNullOrWhiteSpace(update.English)) { entries.Remove(update.Id); return; }
        entries[update.Id] = update;
    }
    public string Export(bool includeJapanese)
    {
        var result = new StringBuilder();
        foreach (var entry in entries.Values)
        {
            if (includeJapanese) result.Append(entry.SourceLanguage == "ja" ? "日本語: " : "English: ");
            result.AppendLine(entry.English);
            if (includeJapanese && !string.IsNullOrWhiteSpace(entry.Japanese)) result.AppendLine("日本語: " + entry.Japanese);
            result.AppendLine();
        }
        return result.ToString();
    }
}
