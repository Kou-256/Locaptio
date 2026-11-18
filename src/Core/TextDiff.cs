using System.Text;
using System.Text.RegularExpressions;

namespace Locaptio.Core;

/// <summary>Character-level difference between two short texts (an utterance before and after a change).</summary>
public static class TextDiff
{
    public enum Kind { Same, Removed, Added }
    public readonly record struct Segment(Kind Kind, string Text);

    public static List<Segment> Segments(string before, string after)
    {
        var result = new List<Segment>();
        int n = before.Length, m = after.Length;
        if ((long)n * m > 400_000) { Add(result, Kind.Removed, before); Add(result, Kind.Added, after); return result; }
        var lcs = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                lcs[i, j] = before[i] == after[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
        int a = 0, b = 0;
        while (a < n || b < m)
        {
            if (a < n && b < m && before[a] == after[b]) { Add(result, Kind.Same, before[a].ToString()); a++; b++; }
            else if (b < m && (a == n || lcs[a, b + 1] >= lcs[a + 1, b])) { Add(result, Kind.Added, after[b].ToString()); b++; }
            else { Add(result, Kind.Removed, before[a].ToString()); a++; }
        }
        return result;
    }
    private static void Add(List<Segment> list, Kind kind, string text)
    {
        if (text.Length == 0) return;
        if (list.Count > 0 && list[^1].Kind == kind) list[^1] = list[^1] with { Text = list[^1].Text + text };
        else list.Add(new(kind, text));
    }

    /// <summary>Where the characters that were added are in <paramref name="after"/>.</summary>
    public static List<(int Start, int Length)> AddedRanges(string before, string after)
    {
        var ranges = new List<(int, int)>(); int at = 0;
        foreach (var segment in Segments(before, after))
        {
            if (segment.Kind == Kind.Removed) continue;
            if (segment.Kind == Kind.Added) ranges.Add((at, segment.Text.Length));
            at += segment.Text.Length;
        }
        return ranges;
    }
}

/// <summary>
/// Refuses an AI correction that drops what was said. Fixing a misheard word replaces characters
/// (議時録 → 議事録, かいぎ → 会議); dropping a clause removes them with nothing in their place.
/// Fillers (えー, えっと, …) and punctuation may go.
/// </summary>
public static class CorrectionGuard
{
    private static readonly Regex Fillers = new("えーっと|えーと|えっと|えー|あのー|あのう|うーん|んー|まあ|あー", RegexOptions.CultureInvariant);
    // Roughly the length of the reading: a kanji stands for about two kana (会議 = かいぎ), so turning
    // kana into kanji is not counted as losing text.
    private static int Content(string text) => Fillers.Replace(text, "").Where(char.IsLetterOrDigit).Sum(c => c is >= '一' and <= '鿿' or >= '㐀' and <= '䶿' ? 2 : 1);

    public static bool Accept(string original, string corrected, out string reason)
    {
        reason = "";
        if (original == corrected) return true;
        int lost = 0, longestLoss = 0, longestInvention = 0;
        // Each gap between unchanged text is either a replacement or a pure deletion/insertion.
        string removed = "", added = "";
        void Gap()
        {
            int loss = Content(removed) - Content(added);
            if (loss > 0) { lost += loss; longestLoss = Math.Max(longestLoss, loss); }
            if (removed.Length == 0) longestInvention = Math.Max(longestInvention, Content(added));
            removed = added = "";
        }
        foreach (var segment in TextDiff.Segments(original, corrected))
        {
            if (segment.Kind == TextDiff.Kind.Same) { Gap(); continue; }
            if (segment.Kind == TextDiff.Kind.Removed) removed += segment.Text; else added += segment.Text;
        }
        Gap();
        int total = Math.Max(1, Content(original));
        if (longestLoss >= 3 || lost >= Math.Max(3, total / 10)) { reason = $"AI補正で元の語が消えた（{lost}文字）ため原文を保持します"; return false; }
        if (longestInvention >= 8) { reason = "AI補正で音声にない語が加わったため原文を保持します"; return false; }
        return true;
    }
}
