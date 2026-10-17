using System.Diagnostics;

namespace Locaptio.Core;

// Converts Windows' growing/rolling accessibility text into replaceable caption rows.
public sealed class WindowsCaptionTracker(bool japanese)
{
    private bool initialized, active;
    private string anchor = "", snapshot = "", current = "", lastFinalText = "";
    private long id, sequence, started, changed, lastFinalId, lastFinalStarted;
    private int lastFinalRevision;
    private string lastFinalPrefix = "", windowText = "", completeText = "";
    private int revision;
    public void ResetBaseline(string text) { initialized = true; active = false; anchor = snapshot = windowText = completeText = text; current = ""; lastFinalText = ""; }
    public IEnumerable<CaptionUpdate> Observe(string text, long now)
    {
        if (!initialized) { ResetBaseline(text); yield break; }
        CompactAlignment();
        text = ExpandWindow(text);
        if (text == snapshot)
        {
            if (active && Stopwatch.GetElapsedTime(changed, now).TotalMilliseconds >= (japanese ? 750 : 1400))
            {
                var final = Make(true, now); RememberFinal(); yield return final; active = false; anchor = text; current = "";
            }
            yield break;
        }
        snapshot = text;
        if (!japanese && lastFinalText.Length > 0 && !text.StartsWith(anchor, StringComparison.Ordinal) && text.StartsWith(lastFinalPrefix, StringComparison.Ordinal))
        {
            // A Windows hypothesis can revise words that were just finalized.
            // Reopen that row instead of appending the corrected words twice.
            if (active && id != lastFinalId) yield return new(id, ++revision, "", "", true, started, 0, 0, "en");
            id = lastFinalId; revision = lastFinalRevision; started = lastFinalStarted; anchor = lastFinalPrefix; active = true; lastFinalText = "";
        }
        if (text.StartsWith(anchor, StringComparison.Ordinal)) current = text[anchor.Length..];
        else
        {
            int overlap = 0;
            // Windows can discard the oldest text from its scrollback.
            for (int length = Math.Min(anchor.Length, text.Length); length > 0; length--)
                if (anchor.AsSpan(anchor.Length - length).SequenceEqual(text.AsSpan(0, length))) { overlap = length; break; }
            if (overlap > 0) { anchor = text[..overlap]; current = text[overlap..]; }
            else
            {
                int common = 0;
                while (common < Math.Min(anchor.Length, text.Length) && anchor[common] == text[common]) common++;
                // A correction may reach back across the start of the active row.
                anchor = text[..common]; current = text[common..];
            }
        }
        // Windows may append sentence punctuation after the idle finalization.
        // Attach it to the preceding row instead of starting a row with punctuation.
        if (!active && lastFinalText.Length > 0)
        {
            int count = 0;
            while (count < current.Length && "。！？.!?、,;；".Contains(current[count])) count++;
            if (count > 0)
            {
                string punctuation = current[..count], tail = current[count..];
                lastFinalText += punctuation; current = lastFinalText;
                yield return Make(true, now); lastFinalRevision = revision; anchor += punctuation; current = tail;
            }
        }
        if (!japanese && !active && lastFinalText.Length == 0)
        {
            int leading = 0;
            while (leading < current.Length && (char.IsWhiteSpace(current[leading]) || ".,!?;".Contains(current[leading]))) leading++;
            anchor += current[..leading]; current = current[leading..];
        }
        if (string.IsNullOrWhiteSpace(current)) yield break;
        if (!active) { id = ++sequence; revision = 0; started = now; active = true; }
        changed = now;
        if (!japanese)
        {
            while (System.Text.RegularExpressions.Regex.Matches(current, @"(?<!\d)[.!?](?=\s|$)").Cast<System.Text.RegularExpressions.Match>().FirstOrDefault(m=>m.Index>=14) is { } sentence)
            {
                int cut = sentence.Index + 1; string tail = current[cut..], prefix = current[..cut]; current = prefix;
                var final = Make(true, now); RememberFinal(); yield return final;
                anchor += prefix; current = tail;
                if (string.IsNullOrWhiteSpace(current)) { anchor = text; current = ""; active = false; yield break; }
                id = ++sequence; revision = 0; started = now;
            }
        }
        // Bound each row while keeping all recognized text in the transcript.
        while (current.Length > (japanese ? 180 : 420))
        {
            int cut = current.LastIndexOfAny(['。', '！', '？', '.', '!', '?', ' ', '\n'], japanese ? 160 : 400, japanese ? 130 : 350);
            if (cut < 30) cut = 159;
            string tail = current[(cut + 1)..], prefix = current[..(cut + 1)]; current = prefix;
            var final = Make(true, now); RememberFinal(); yield return final; anchor += prefix; current = tail;
            id = ++sequence; revision = 0; started = now;
        }
        yield return Make(false, now);
    }
    private void CompactAlignment()
    {
        // Completed rows live in TranscriptBuffer; only retain an alignment tail.
        int cut = Math.Min(completeText.Length - 8192, completeText.Length - windowText.Length);
        cut = Math.Min(cut, Math.Min(anchor.Length, snapshot.Length));
        if (lastFinalText.Length > 0) cut = Math.Min(cut, lastFinalPrefix.Length);
        if (cut <= 0) return;
        completeText = completeText[cut..]; snapshot = snapshot[cut..]; anchor = anchor[cut..];
        lastFinalPrefix = lastFinalPrefix.Length >= cut ? lastFinalPrefix[cut..] : "";
    }
    private string ExpandWindow(string text)
    {
        if (text == windowText) return completeText;
        int common = 0;
        while (common < Math.Min(text.Length, windowText.Length) && text[common] == windowText[common]) common++;
        int shift = 0, best = common;
        // Live Captions may discard part of the first visible line, even mid-word.
        // Align that rolling window to retained text before recognizing revisions.
        for (int length = Math.Min(32, text.Length); length >= 6 && shift == 0; length /= 2)
        {
            if (windowText.Length <= length) continue;
            string probe = text[..length];
            for (int at = windowText.IndexOf(probe, 1, StringComparison.Ordinal); at >= 0; at = windowText.IndexOf(probe, at + 1, StringComparison.Ordinal))
            {
                int matches = 0;
                while (at + matches < windowText.Length && matches < text.Length && windowText[at + matches] == text[matches]) matches++;
                if (matches > best) { best = matches; shift = at; }
            }
        }
        if (!active && windowText.Length > 20 && common == 0 && shift == 0)
        { completeText += " " + text; windowText = text; return completeText; }
        int offset = completeText.Length - windowText.Length + shift;
        completeText = completeText[..Math.Max(0, offset)] + text; windowText = text;
        return completeText;
    }
    private void RememberFinal() { lastFinalText = current; lastFinalPrefix = anchor; lastFinalId = id; lastFinalRevision = revision; lastFinalStarted = started; }
    private CaptionUpdate Make(bool final, long now) => new(id, ++revision, current.Trim(), null, final, started, 0, 0, japanese ? "ja" : "en");
}
