using System.Text.RegularExpressions;

namespace Locaptio.Core;

/// <summary>
/// Translation and language models sometimes fall into a loop and repeat a few words until the length limit
/// ("キラキラしたキラキラした…"). This finds such loops: a unit of 2–12 characters repeated four or more times in a
/// row, or an output far longer than what was said.
/// </summary>
public static partial class RepetitionGuard
{
    [GeneratedRegex(@"(.{2,12}?)(?:\s*\1){3,}", RegexOptions.Singleline)]
    private static partial Regex Loop();
    // The unit must contain a letter: digit groups ("1,000,000,000,000") and punctuation runs are not loops.
    private static bool Wordy(Match m) => m.Groups[1].Value.Any(char.IsLetter);

    /// <summary>True when the text repeats a short unit four or more times in a row.</summary>
    public static bool HasLoop(string text) => !string.IsNullOrEmpty(text) && Loop().Matches(text).Any(Wordy);

    /// <summary>Keeps one copy of each looping unit ("はいはいはいはいはい" → "はい").</summary>
    public static string Collapse(string text) => string.IsNullOrEmpty(text) ? text : Loop().Replace(text, m => Wordy(m) ? m.Groups[1].Value : m.Value);

    /// <summary>
    /// An output is unusable when it loops or is far longer than its source (a looping decoder that stopped at the limit).
    /// </summary>
    public static bool IsRunaway(string source, string output)
    {
        if (string.IsNullOrEmpty(output)) return false;
        if (HasLoop(output) && !HasLoop(source)) return true;
        return output.Length > Limit(source, output, 4);
    }
    /// <summary>
    /// The longest plausible output: Japanese to an alphabetic language naturally takes several times more characters
    /// (about 4× for English, more for Italian/Spanish), so the ratio is widened in that direction.
    /// </summary>
    private static int Limit(string source, string output, double ratio)
    {
        static bool Cjk(string text) => text.Count(c => c is >= '぀' and <= 'ヿ' or >= '一' and <= '鿿') * 2 > text.Length;
        if (Cjk(source) && !Cjk(output)) ratio *= 2.5;
        return (int)Math.Max(60, source.Length * ratio);
    }

    /// <summary>
    /// The text to show for a first translation (no better one to fall back on): loops collapsed, and a still runaway
    /// result cut back to a reasonable length at a sentence end where possible.
    /// </summary>
    public static string Clean(string source, string output)
    {
        if (string.IsNullOrEmpty(output) || !IsRunaway(source, output)) return output;
        string text = Collapse(output);
        int limit = Limit(source, output, 3);
        if (text.Length <= limit) return text;
        int end = text.LastIndexOfAny(['。', '．', '.', '!', '?', '！', '？'], limit - 1, limit);
        return end > 0 ? text[..(end + 1)] : text[..limit] + "…";
    }
}
