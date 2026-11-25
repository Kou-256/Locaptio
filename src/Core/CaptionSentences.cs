using System.Text.RegularExpressions;

namespace Locaptio.Core;

/// <summary>
/// Splits a caption into its sentences for the caption window's "one sentence per bubble" view. A translation is
/// split the same way and paired sentence by sentence when the counts match; otherwise it stays with the last one.
/// </summary>
public static partial class CaptionSentences
{
    // After 。！？!? or a period followed by a space (not "3.5" or "e.g.x").
    [GeneratedRegex(@"(?<=[。！？!?])\s*|(?<=\.)\s+")]
    private static partial Regex Boundary();

    // "p.m." "e.g." "Mr." … end with a period without ending the sentence.
    [GeneratedRegex(@"(?:\b(?:[A-Za-z]\.){2,}|\b(?:Mr|Mrs|Ms|Dr|Prof|St|vs|etc|No|Inc|Ltd|Jr|Sr)\.)$", RegexOptions.IgnoreCase)]
    private static partial Regex Abbreviation();

    public static string[] Split(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var parts = new List<string>();
        foreach (string piece in Boundary().Split(text.Trim()).Select(s => s.Trim()).Where(s => s.Length > 0))
        {
            if (parts.Count > 0 && Abbreviation().IsMatch(parts[^1])) parts[^1] += " " + piece;
            else parts.Add(piece);
        }
        return parts.ToArray();
    }

    public static IEnumerable<CaptionUpdate> Expand(CaptionUpdate caption)
    {
        var sources = Split(caption.English);
        if (sources.Length <= 1) { yield return caption; yield break; }
        var translations = Split(caption.Japanese);
        bool paired = translations.Length == sources.Length;
        for (int i = 0; i < sources.Length; i++)
        {
            string? translation = paired ? translations[i] : i == sources.Length - 1 ? caption.Japanese : null;
            yield return caption with { English = sources[i], Japanese = translation };
        }
    }
}
