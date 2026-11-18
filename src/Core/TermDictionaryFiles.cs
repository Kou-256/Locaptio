using System.Globalization;
using System.Text;

namespace Locaptio.Core;

/// <summary>
/// Term dictionary files. Export writes CSV (UTF-8 with BOM, so Excel opens it): 表記,誤表記,訳語 with the
/// misrecognitions separated by "、" inside one cell. Import reads that CSV — also when Excel saved it in
/// Shift-JIS — tab-separated text, and Locaptio's own "表記 | 誤表記, … | 訳語" lines.
/// </summary>
public static class TermDictionaryFiles
{
    public const int MaxEntries = 300;
    private static readonly char[] AliasSeparators = ['、', ',', '，', ';', '；'];

    public static byte[] ToCsv(TermDictionary dictionary)
    {
        var text = new StringBuilder("表記,誤表記,訳語\r\n");
        foreach (var entry in dictionary.Entries)
            text.Append(Cell(entry.Term)).Append(',').Append(Cell(string.Join("、", entry.Aliases))).Append(',').Append(Cell(entry.Translation)).Append("\r\n");
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];
    }
    private static string Cell(string value) => value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;

    /// <summary>UTF-8 (with or without BOM), else the system's ANSI code page (Shift-JIS on Japanese Windows).</summary>
    public static string Decode(byte[] bytes)
    {
        int skip = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        if (bytes.Length >= 2 && (bytes[0] == 0xFF && bytes[1] == 0xFE)) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        try { return new UTF8Encoding(false, true).GetString(bytes, skip, bytes.Length - skip); }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage).GetString(bytes);
        }
    }

    public static TermEntry[] Parse(string text)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        string first = text.Split('\n').FirstOrDefault(l => l.Trim().Length > 0) ?? "";
        if (first.Contains('|') && !first.Contains('\t')) return TermDictionary.Parse(text).Entries;
        var rows = first.Contains('\t') ? text.Split('\n').Select(l => l.Split('\t')).ToList() : Csv(text);
        var entries = new List<TermEntry>();
        foreach (var cells in rows)
        {
            if (cells.Length == 0 || cells[0].Trim().Length == 0) continue;
            string head = cells[0].Trim().ToLowerInvariant();
            if (entries.Count == 0 && head is "表記" or "正しい表記" or "term") continue;
            entries.Add(new(cells[0], cells.Length > 1 ? cells[1].Split(AliasSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [], cells.Length > 2 ? cells[2] : ""));
        }
        return new TermDictionary(entries).Entries;
    }
    private static List<string[]> Csv(string text)
    {
        var rows = new List<string[]>(); var cells = new List<string>(); var cell = new StringBuilder(); bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cell.Append(c);
            }
            else if (c == '"' && cell.Length == 0) quoted = true;
            else if (c == ',') { cells.Add(cell.ToString()); cell.Clear(); }
            else if (c == '\n') { cells.Add(cell.ToString()); cell.Clear(); rows.Add([.. cells]); cells.Clear(); }
            else cell.Append(c);
        }
        if (cell.Length > 0 || cells.Count > 0) { cells.Add(cell.ToString()); rows.Add([.. cells]); }
        return rows;
    }

    /// <summary>Adds imported terms; a term already registered keeps its misrecognitions and gains the imported ones.</summary>
    public static TermDictionary Merge(TermDictionary current, IEnumerable<TermEntry> imported)
    {
        var merged = current.Entries.ToList();
        foreach (var entry in imported)
        {
            int at = merged.FindIndex(e => e.Term == entry.Term);
            if (at < 0) { merged.Add(entry); continue; }
            var old = merged[at];
            merged[at] = old with { Aliases = old.Aliases.Concat(entry.Aliases).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), Translation = entry.Translation.Length > 0 ? entry.Translation : old.Translation };
        }
        return new TermDictionary(merged);
    }
}
