using System.Globalization;
using System.Text.RegularExpressions;
namespace Locaptio.Core;

public static class PlaybackHighlight
{
    public static (int Start, int Length) Range(RecordingPhrase phrase, string text, double seconds, double end, string mode)
    {
        if (text.Length == 0 || phrase.EndSeconds is double finished && seconds >= finished) return (0, 0);
        if (mode == "phrase") return (0, text.Length);
        // Translations do not share source word alignment. Old recordings and
        // Windows captions also use an explicitly labelled time estimate.
        if (text == phrase.Text && phrase.Words is { Length: > 0 } words)
        {
            var word = words.FirstOrDefault(w => seconds >= w.Start && seconds < w.End);
            if (word is null) return (0, 0);
            if (word.StartIndex < 0 || word.Length <= 0 || word.StartIndex + word.Length > text.Length) return (0, 0);
            if (mode == "word") return (word.StartIndex, word.Length);
            return Character(text, word.StartIndex, word.Length, (seconds - word.Start) / Math.Max(.001, word.End - word.Start));
        }
        double ratio = Math.Clamp((seconds - phrase.Seconds) / Math.Max(.2, end - phrase.Seconds), 0, .999999);
        if (mode == "character") return Character(text, 0, text.Length, ratio);
        // Japanese boundaries are heuristic without model-provided word times.
        var matches = Regex.Matches(text, @"[\p{IsHiragana}]+|[\p{IsKatakana}ー]+|[\p{IsCJKUnifiedIdeographs}]+|[\p{L}\p{N}]+|[^\s]", RegexOptions.CultureInvariant);
        if (matches.Count == 0) return (0, 0);
        var match = matches[Math.Min(matches.Count - 1, (int)(ratio * matches.Count))]; return (match.Index, match.Length);
    }
    private static (int, int) Character(string text, int start, int length, double ratio)
    {
        int[] indexes = StringInfo.ParseCombiningCharacters(text.Substring(start, length));
        if (indexes.Length == 0) return (0, 0);
        int index = Math.Clamp((int)(Math.Clamp(ratio, 0, .999999) * indexes.Length), 0, indexes.Length - 1);
        return (start + indexes[index], (index + 1 < indexes.Length ? indexes[index + 1] : length) - indexes[index]);
    }
}
