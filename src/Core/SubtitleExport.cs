using System.Globalization;
using System.Text;

namespace Locaptio.Core;

/// <summary>SubRip (.srt) and WebVTT (.vtt) output from phrase and word times.</summary>
public static class SubtitleExport
{
    public sealed record Cue(double Start, double End, string Text);

    /// <param name="content">"source", "translation" or "both" (source above its translation).</param>
    public static List<Cue> Cues(RecordingDocument document, string content = "source")
    {
        var cues = new List<Cue>();
        var phrases = document.Phrases;
        for (int i = 0; i < phrases.Length; i++)
        {
            var phrase = phrases[i];
            string source = Flatten(phrase.Text), translation = Flatten(phrase.Translation ?? "");
            string text = content == "translation" ? translation.Length > 0 ? translation : source : content == "both" && translation.Length > 0 ? source + "\n" + translation : source;
            if (text.Length == 0) continue;
            double start = phrase.Seconds, next = i + 1 < phrases.Length ? phrases[i + 1].Seconds : double.MaxValue;
            // Without a recorded end, estimate reading time and never run into the next utterance.
            double end = phrase.EndSeconds is double known && known > start ? known : start + Math.Clamp(Width(source) * .09, 1.5, 8);
            if (next > start) end = Math.Min(end, next);
            if (end - start < .3) end = start + .3;
            if (content == "both" || text.Contains('\n')) { cues.Add(new(start, end, text)); continue; }
            var parts = Split(text, IsWide(text) ? 44 : 84);
            if (parts.Count == 1) { cues.Add(new(start, end, Wrap(text))); continue; }
            // Word times place each part exactly; otherwise share the phrase by text length.
            bool timed = content == "source" && phrase.Words is { Length: > 0 } && text == phrase.Text;
            double total = parts.Sum(p => (double)Width(p.Text)), cursor = start;
            for (int k = 0; k < parts.Count; k++)
            {
                double partEnd = k == parts.Count - 1 ? end : cursor + (end - start) * Width(parts[k].Text) / total;
                if (timed && k < parts.Count - 1)
                {
                    int boundary = parts[k + 1].Index;
                    var word = phrase.Words!.FirstOrDefault(w => w.StartIndex >= boundary);
                    if (word is not null && word.Start > cursor && word.Start < end) partEnd = word.Start;
                }
                cues.Add(new(cursor, Math.Max(partEnd, cursor + .05), Wrap(parts[k].Text))); cursor = partEnd;
            }
        }
        return cues;
    }
    public static string Srt(RecordingDocument document, string content = "source")
    {
        var result = new StringBuilder(); int number = 0;
        foreach (var cue in Cues(document, content))
            result.Append(++number).Append("\r\n").Append(Time(cue.Start, ',')).Append(" --> ").Append(Time(cue.End, ',')).Append("\r\n").Append(cue.Text.Replace("\n", "\r\n")).Append("\r\n\r\n");
        return result.ToString();
    }
    public static string Vtt(RecordingDocument document, string content = "source")
    {
        var result = new StringBuilder("WEBVTT\n\n");
        foreach (var cue in Cues(document, content))
            // "-->" inside a cue would be parsed as a timing line; "&" and "<" start markup.
            result.Append(Time(cue.Start, '.')).Append(" --> ").Append(Time(cue.End, '.')).Append('\n')
                .Append(cue.Text.Replace("&", "&amp;").Replace("<", "&lt;").Replace("-->", "→")).Append("\n\n");
        return result.ToString();
    }
    public static string Time(double seconds, char separator)
    {
        long milliseconds = (long)Math.Round(Math.Max(0, seconds) * 1000);
        return string.Create(CultureInfo.InvariantCulture, $"{milliseconds / 3600000:D2}:{milliseconds / 60000 % 60:D2}:{milliseconds / 1000 % 60:D2}{separator}{milliseconds % 1000:D3}");
    }
    private static string Flatten(string text) => string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    private static bool IsWide(string text) => text.Count(c => c > 0x2E7F) * 2 > text.Length;
    // East Asian characters occupy two columns of a subtitle line.
    private static int Width(string text) => text.Sum(c => c > 0x2E7F ? 2 : 1);
    private static bool Breakable(string text, int index) => index > 0 && index < text.Length && !char.IsLowSurrogate(text[index])
        && (char.IsWhiteSpace(text[index - 1]) || "。、．，！？!?,.;:）」』".Contains(text[index - 1]) || IsWide(text) && text[index] > 0x2E7F && !"。、．，！？）」』ー".Contains(text[index]));
    private static List<(int Index, string Text)> Split(string text, int columns)
    {
        var parts = new List<(int, string)>(); int start = 0;
        while (start < text.Length)
        {
            // Prefer ending a cue at a sentence; otherwise at the last word or clause boundary that fits.
            int width = 0, end = start, soft = -1, sentence = -1;
            while (end < text.Length && width + (text[end] > 0x2E7F ? 2 : 1) <= columns)
            {
                width += text[end] > 0x2E7F ? 2 : 1; end++;
                if (end >= text.Length || !Breakable(text, end)) continue;
                soft = end;
                if ("。．！？!?.".Contains(text[end - 1]) && width > columns / 2) sentence = end;
            }
            if (end < text.Length) end = sentence > start ? sentence : soft > start ? soft : char.IsLowSurrogate(text[end]) ? end - 1 : end;
            string part = text[start..end];
            int leading = part.Length - part.TrimStart().Length;
            if (part.Trim().Length > 0) parts.Add((start + leading, part.Trim()));
            start = end;
        }
        return parts;
    }
    /// <summary>At most two balanced lines per cue.</summary>
    private static string Wrap(string text)
    {
        int width = Width(text), limit = IsWide(text) ? 44 : 84;
        if (width <= limit / 2) return text;
        int best = -1, bestDistance = int.MaxValue, column = 0;
        for (int i = 1; i < text.Length; i++)
        {
            column += text[i - 1] > 0x2E7F ? 2 : 1;
            int distance = Math.Abs(width - 2 * column);
            if (Breakable(text, i) && distance < bestDistance) { best = i; bestDistance = distance; }
        }
        return best < 0 ? text : text[..best].TrimEnd() + "\n" + text[best..].TrimStart();
    }
}
