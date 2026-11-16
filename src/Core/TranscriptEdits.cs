namespace Locaptio.Core;

public sealed record TranscriptEdit(long Id, string BeforeText, string? BeforeTranslation, string AfterText, string? AfterTranslation);

/// <summary>
/// Manual corrections with undo/redo, plus the recognized text from before AI correction.
/// A manual edit is published as a very high revision, so a recognition, correction or
/// translation result that arrives later for the same row can never overwrite it.
/// </summary>
public sealed class TranscriptEditHistory
{
    public const int ManualRevision = 1_000_000;
    private readonly List<TranscriptEdit> undo = new(), redo = new();
    private readonly Dictionary<long, string> originals = new();
    private readonly HashSet<long> restoredEdits = new();
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public int Count => undo.Count;

    public void Clear() { undo.Clear(); redo.Clear(); originals.Clear(); restoredEdits.Clear(); }

    /// <summary>Remembers what the recognizer itself produced, before AI correction replaced it.</summary>
    public void Observe(CaptionUpdate update)
    {
        if (update.Revision >= ManualRevision) return;
        if (string.IsNullOrWhiteSpace(update.English)) { originals.Remove(update.Id); return; }
        if (!update.SourceCorrected || !originals.ContainsKey(update.Id)) originals[update.Id] = update.English;
    }
    /// <summary>Restores state saved with a recording or an autosave.</summary>
    public void Restore(long id, string? original, bool edited)
    {
        if (original is not null) originals[id] = original;
        if (edited) restoredEdits.Add(id);
    }
    public string? Original(long id) => originals.GetValueOrDefault(id);
    public bool IsEdited(long id) => restoredEdits.Contains(id) || undo.Any(e => e.Id == id);
    /// <summary>True when the displayed text differs from what was first recognized.</summary>
    public bool Differs(CaptionUpdate caption) => originals.TryGetValue(caption.Id, out string? original) && original != caption.English;

    private static CaptionUpdate Publish(CaptionUpdate current, string text, string? translation)
        => current with { Revision = Math.Max(current.Revision, ManualRevision) + 1, English = text, Japanese = translation, Final = true };

    public CaptionUpdate? Apply(CaptionUpdate current, string text, string? translation)
    {
        text = text.Trim(); translation = string.IsNullOrWhiteSpace(translation) ? null : translation.Trim();
        // Deleting an utterance is a separate action; an empty edit would silently remove the row.
        if (text.Length == 0 || text == current.English && translation == current.Japanese) return null;
        originals.TryAdd(current.Id, current.English);
        undo.Add(new(current.Id, current.English, current.Japanese, text, translation)); redo.Clear();
        return Publish(current, text, translation);
    }
    public CaptionUpdate? Undo(Func<long, CaptionUpdate?> find) => Step(undo, redo, find, e => (e.BeforeText, e.BeforeTranslation));
    public CaptionUpdate? Redo(Func<long, CaptionUpdate?> find) => Step(redo, undo, find, e => (e.AfterText, e.AfterTranslation));
    private static CaptionUpdate? Step(List<TranscriptEdit> from, List<TranscriptEdit> to, Func<long, CaptionUpdate?> find, Func<TranscriptEdit, (string Text, string? Translation)> value)
    {
        while (from.Count > 0)
        {
            var edit = from[^1]; from.RemoveAt(from.Count - 1);
            // The row may have been cleared since the edit; skip it rather than resurrecting text.
            if (find(edit.Id) is not { } current) continue;
            to.Add(edit);
            var (text, translation) = value(edit);
            return Publish(current, text, translation);
        }
        return null;
    }
}
