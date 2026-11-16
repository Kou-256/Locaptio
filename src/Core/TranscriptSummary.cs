using System.Text;
using System.Text.RegularExpressions;
using Locaptio.Core.AI;

namespace Locaptio.Core;

/// <summary>One summary line and the transcript rows it was derived from.</summary>
public sealed record SummaryItem(string Section, string Text, long[] RowIds);
public interface ISummaryBackend { Task<InferenceResult> SummarizeAsync(string numberedTranscript, CancellationToken token); }

/// <summary>
/// Key points, decisions and TODOs from a finished transcript. Utterances are numbered in
/// the prompt and the model must cite those numbers, so every line can be traced back to
/// what was actually said. Long transcripts are summarized in parts that fit the local model.
/// </summary>
public static class TranscriptSummary
{
    public static readonly string[] Sections = ["要点", "決定事項", "TODO"];

    /// <summary>Numbered "[n] text" blocks; n is the 1-based position in <paramref name="rows"/>.</summary>
    public static List<string> Chunks(IReadOnlyList<(long Id, string Text)> rows, int maximumCharacters = 1800)
    {
        var chunks = new List<string>(); var current = new StringBuilder();
        for (int i = 0; i < rows.Count; i++)
        {
            string text = rows[i].Text.ReplaceLineEndings(" ").Trim();
            if (text.Length == 0) continue;
            if (text.Length > 300) text = text[..300] + "…";
            string line = $"[{i + 1}] {text}\n";
            if (current.Length > 0 && current.Length + line.Length > maximumCharacters) { chunks.Add(current.ToString()); current.Clear(); }
            current.Append(line);
        }
        if (current.Length > 0) chunks.Add(current.ToString());
        return chunks;
    }

    private static string? Section(string heading)
    {
        string value = heading.ToUpperInvariant();
        if (value.Contains("TODO") || value.Contains("TO DO") || value.Contains("タスク") || value.Contains("アクション") || value.Contains("宿題") || value.Contains("やること")) return "TODO";
        if (value.Contains("決定") || value.Contains("合意") || value.Contains("DECISION")) return "決定事項";
        if (value.Contains("要点") || value.Contains("要約") || value.Contains("ポイント") || value.Contains("概要") || value.Contains("SUMMARY") || value.Contains("KEY POINT")) return "要点";
        return null;
    }
    public static SummaryItem[] Parse(string raw, IReadOnlyList<long> ids)
    {
        var items = new List<SummaryItem>(); string? section = null;
        foreach (string source in raw.Normalize(NormalizationForm.FormKC).Split('\n'))
        {
            string line = source.Trim();
            if (line.Length == 0) continue;
            bool bullet = Regex.IsMatch(line, @"^([-*・•]|\d+[.)])\s*");
            string body = Regex.Replace(line, @"^([-*・•]|\d+[.)])\s*", "");
            // A heading is a line without evidence that names a section: "## 要点", "【決定事項】", "TODO:".
            if (!Regex.IsMatch(body, @"\[\s*\d") && body.Length <= 24 && Section(body) is { } named && (!bullet || line.StartsWith('#') || body.TrimEnd().EndsWith(':')))
            { section = named; continue; }
            if (line.StartsWith('#')) { section = Section(line) ?? section; continue; }
            if (section is null || !bullet) continue;
            var references = Regex.Matches(body, @"\[\s*(\d+(?:\s*[,、~\-]\s*\d+)*)\s*\]").SelectMany(m => Numbers(m.Groups[1].Value))
                .Where(n => n >= 1 && n <= ids.Count).Distinct().Select(n => ids[n - 1]).ToArray();
            string text = Regex.Replace(body, @"\s*\[\s*\d+(?:\s*[,、~\-]\s*\d+)*\s*\]", "").Replace("**", "").Trim(' ', '　', '。', ':');
            // Small models copy the format's field label ("担当者：") and sometimes misspell it.
            text = Regex.Replace(text, @"^(担当者?|担任者|担当者名|Owner|Assignee)\s*[:：]\s*", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"^([^\s:：、。]{1,12}?(さん|様|氏|君|くん))\s*[:：]\s*", "$1：");
            if (text.Length == 0 || Regex.IsMatch(text, @"^(特に)?(なし|無し|ありません|該当なし|NONE|N/A)$", RegexOptions.IgnoreCase)) continue;
            items.Add(new(section, text, references));
        }
        return items.ToArray();
    }
    private static IEnumerable<int> Numbers(string list)
    {
        foreach (Match part in Regex.Matches(list, @"(\d+)(?:\s*[~\-]\s*(\d+))?"))
        {
            int first = int.Parse(part.Groups[1].Value), last = part.Groups[2].Success ? int.Parse(part.Groups[2].Value) : first;
            // A reversed or implausibly long range keeps only the numbers actually written.
            if (last < first || last - first > 50) { yield return first; yield return last; continue; }
            for (int number = first; number <= last; number++) yield return number;
        }
    }
    public static SummaryItem[] Merge(IEnumerable<SummaryItem[]> parts)
    {
        var merged = parts.SelectMany(p => p).GroupBy(i => (i.Section, i.Text)).Select(g => g.First() with { RowIds = g.SelectMany(i => i.RowIds).Distinct().ToArray() })
            .OrderBy(i => Array.IndexOf(Sections, i.Section)).ToList();
        // A key point that repeats a decision or TODO adds nothing; keep it under the more specific heading.
        var specific = merged.Where(i => i.Section != "要点").Select(i => Key(i.Text)).ToHashSet();
        return merged.Where(i => i.Section != "要点" || !specific.Contains(Key(i.Text))).ToArray();
    }
    private static string Key(string text) => new(text.Where(char.IsLetterOrDigit).ToArray());

    public static async Task<SummaryItem[]> RunAsync(ISummaryBackend backend, IReadOnlyList<(long Id, string Text)> rows, Action<int, int>? progress, CancellationToken token)
    {
        var chunks = Chunks(rows); var ids = rows.Select(r => r.Id).ToArray(); var parts = new List<SummaryItem[]>();
        for (int i = 0; i < chunks.Count; i++)
        {
            token.ThrowIfCancellationRequested(); progress?.Invoke(i, chunks.Count);
            parts.Add(Parse((await backend.SummarizeAsync(chunks[i], token)).Text, ids));
        }
        progress?.Invoke(chunks.Count, chunks.Count);
        return Merge(parts);
    }
    public static string Markdown(IEnumerable<SummaryItem> items, Func<long, string?> evidence)
    {
        var text = new StringBuilder();
        foreach (string section in Sections)
        {
            text.Append("## ").AppendLine(section);
            var lines = items.Where(i => i.Section == section).ToArray();
            if (lines.Length == 0) text.AppendLine("- なし");
            foreach (var item in lines)
            {
                var marks = item.RowIds.Select(evidence).Where(m => !string.IsNullOrEmpty(m)).ToArray();
                text.Append("- ").Append(item.Text).AppendLine(marks.Length == 0 ? "" : "（" + string.Join("、", marks) + "）");
            }
            text.AppendLine();
        }
        return text.ToString();
    }
}
