using System.Text;
using System.Text.RegularExpressions;

namespace Locaptio.Core;

/// <summary>One registered spelling, the misrecognitions it replaces and an optional Japanese rendering.</summary>
public sealed record TermEntry(string Term, string[] Aliases, string Translation = "");

/// <summary>
/// A single user dictionary shared by the recognition hint, the deterministic
/// replacement of known misrecognitions, AI correction and translation.
/// Stored as lines of "表記 | 誤表記, 誤表記 | 訳語" so it fits in the settings file.
/// </summary>
public sealed class TermDictionary
{
    public static readonly TermDictionary Empty = new([]);
    public TermEntry[] Entries { get; }
    private readonly Regex? pattern;
    private readonly Dictionary<string, string> replacements = new(StringComparer.OrdinalIgnoreCase);

    public TermDictionary(IEnumerable<TermEntry> entries)
    {
        Entries = entries.Select(e => new TermEntry(Clean(e.Term), (e.Aliases ?? []).Select(Clean).Where(a => a.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), Clean(e.Translation ?? "")))
            .Where(e => e.Term.Length > 0).GroupBy(e => e.Term, StringComparer.Ordinal).Select(g => g.First()).Take(300).ToArray();
        foreach (var entry in Entries)
            foreach (string alias in entry.Aliases)
                if (!alias.Equals(entry.Term, StringComparison.Ordinal)) replacements.TryAdd(alias, entry.Term);
        if (replacements.Count == 0) return;
        // Longest alias first, so a short alias cannot split a longer registered one.
        var alternatives = replacements.Keys.OrderByDescending(a => a.Length).Select(a =>
            (IsWordCharacter(a[0]) ? @"(?<![A-Za-z0-9])" : "") + Regex.Escape(a) + (IsWordCharacter(a[^1]) ? @"(?![A-Za-z0-9])" : ""));
        pattern = new Regex(string.Join("|", alternatives), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
    private static bool IsWordCharacter(char c) => c < 128 && char.IsLetterOrDigit(c);
    private static string Clean(string value) => value.Replace('\r', ' ').Replace('\n', ' ').Replace('|', '／').Trim();

    public static TermDictionary Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Empty;
        var entries = new List<TermEntry>();
        foreach (string line in text.Split('\n'))
        {
            string[] parts = line.Split('|');
            if (parts[0].Trim().Length == 0) continue;
            entries.Add(new(parts[0], parts.Length > 1 ? parts[1].Split([',', '、', '，'], StringSplitOptions.RemoveEmptyEntries) : [], parts.Length > 2 ? parts[2] : ""));
        }
        return new(entries);
    }
    public string Serialize() => string.Join("\n", Entries.Select(e => e.Term + " | " + string.Join(", ", e.Aliases) + " | " + e.Translation));

    /// <summary>Replaces registered misrecognitions. Text that already contains the correct spelling is left alone.</summary>
    public string Apply(string text)
    {
        if (pattern is null || string.IsNullOrEmpty(text)) return text;
        return pattern.Replace(text, match =>
        {
            string term = replacements[match.Value];
            // "ロカプ" → "ロカプション" must not turn an already correct "ロカプション" into "ロカプションション".
            for (int at = term.IndexOf(match.Value, StringComparison.OrdinalIgnoreCase); at >= 0; at = term.IndexOf(match.Value, at + 1, StringComparison.OrdinalIgnoreCase))
            {
                int start = match.Index - at;
                if (start >= 0 && start + term.Length <= text.Length && string.CompareOrdinal(text, start, term, 0, term.Length) == 0) return match.Value;
            }
            return term;
        });
    }

    private string Join(IEnumerable<string> items, string prefix, int maximum, string separator)
    {
        var result = new StringBuilder(prefix.Trim());
        foreach (string item in items)
        {
            if (result.Length + separator.Length + item.Length > maximum) break;
            if (result.Length > 0) result.Append(separator);
            result.Append(item);
        }
        return result.ToString();
    }
    /// <summary>Recognition hint: the user's free text followed by as many registered spellings as fit.</summary>
    public string SpeechPrompt(string? userPrompt, int maximum = 500) => Join(Entries.Select(e => e.Term), userPrompt ?? "", maximum, ", ");
    /// <summary>Spellings handed to the correcting LLM, with the misrecognitions they should replace.</summary>
    public string CorrectionHint(int maximum = 600) => Join(Entries.Select(e => e.Aliases.Length == 0 ? e.Term : e.Term + "（誤: " + string.Join("・", e.Aliases) + "）"), "", maximum, "、");
    /// <summary>Translation glossary: the user's notes followed by "term = 訳語" for entries that have one.</summary>
    public string TranslationGlossary(string? userGlossary, int maximum = 1000) => Join(Entries.Where(e => e.Translation.Length > 0).Select(e => e.Term + " = " + e.Translation), userGlossary ?? "", maximum, "、");
}
