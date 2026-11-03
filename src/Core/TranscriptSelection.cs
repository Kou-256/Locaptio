using System.Text;

namespace Locaptio.Core;

public readonly record struct TranscriptPosition(long Id, int Offset) : IComparable<TranscriptPosition>
{
    public int CompareTo(TranscriptPosition other) => Id != other.Id ? Id.CompareTo(other.Id) : Offset.CompareTo(other.Offset);
}

// A logical range survives virtualization and never joins unrelated language columns.
public sealed record TranscriptSelection(TranscriptPosition Anchor, TranscriptPosition Focus, int Column)
{
    public TranscriptPosition Start => Anchor.CompareTo(Focus) <= 0 ? Anchor : Focus;
    public TranscriptPosition End => Anchor.CompareTo(Focus) <= 0 ? Focus : Anchor;
    public bool IsEmpty => Start == End;
    public bool Contains(long id) => !IsEmpty && id >= Start.Id && id <= End.Id;
    public (int Start, int Length) Range(long id, int textLength)
    {
        if (!Contains(id)) return (0, 0);
        int start = id == Start.Id ? Math.Clamp(Start.Offset, 0, textLength) : 0;
        int end = id == End.Id ? Math.Clamp(End.Offset, 0, textLength) : textLength;
        return (start, Math.Max(0, end - start));
    }
    public string Text(IEnumerable<CaptionHistoryEntry> rows)
    {
        var result = new StringBuilder(); bool first = true;
        foreach (var row in rows)
        {
            if (!Contains(row.Id)) continue;
            string text = Column == 0 ? row.English : row.Japanese ?? "";
            var range = Range(row.Id, text.Length);
            if (!first) result.AppendLine();
            result.Append(text.AsSpan(range.Start, range.Length)); first = false;
        }
        return result.ToString();
    }
}
