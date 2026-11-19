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

    /// <summary>True when the text repeats a short unit four or more times in a row.</summary>
    public static bool HasLoop(string text) => !string.IsNullOrEmpty(text) && Loop().IsMatch(text);

    /// <summary>Keeps one copy of each looping unit ("はいはいはいはいはい" → "はい").</summary>
    public static string Collapse(string text) => string.IsNullOrEmpty(text) ? text : Loop().Replace(text, m => m.Groups[1].Value);

    /// <summary>
    /// An output is unusable when it loops or is far longer than its source (a looping decoder that stopped at the limit).
    /// </summary>
    public static bool IsRunaway(string source, string output)
    {
        if (string.IsNullOrEmpty(output)) return false;
        if (HasLoop(output) && !HasLoop(source)) return true;
        return output.Length > Math.Max(60, source.Length * 4);
    }

    /// <summary>
    /// The text to show for a first translation (no better one to fall back on): loops collapsed, and a still runaway
    /// result cut back to a reasonable length at a sentence end where possible.
    /// </summary>
    public static string Clean(string source, string output)
    {
        if (string.IsNullOrEmpty(output) || !IsRunaway(source, output)) return output;
        string text = Collapse(output);
        int limit = Math.Max(60, source.Length * 3);
        if (text.Length <= limit) return text;
        int end = text.LastIndexOfAny(['。', '．', '.', '!', '?', '！', '？'], limit - 1, limit);
        return end > 0 ? text[..(end + 1)] : text[..limit] + "…";
    }
}
