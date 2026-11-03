using System.ComponentModel;

namespace Locaptio.Core;

// Keep the same item/container while partial recognition or translation changes.
// Replacing ObservableCollection items recycles their XAML content and scroll anchor.
public sealed class CaptionHistoryEntry(CaptionUpdate caption) : INotifyPropertyChanged
{
    public CaptionUpdate Caption { get; private set; } = caption;
    public long Id => Caption.Id;
    public string English => Caption.English;
    public string? Japanese => Caption.Japanese;
    public event PropertyChangedEventHandler? PropertyChanged;

    public bool Update(CaptionUpdate next)
    {
        if (next.Id != Id) throw new ArgumentException("A caption row cannot change its identity.", nameof(next));
        if (next.Revision < Caption.Revision || next == Caption) return false;
        var old = Caption; Caption = next;
        if (old.English != next.English) PropertyChanged?.Invoke(this, new(nameof(English)));
        if (old.Japanese != next.Japanese) PropertyChanged?.Invoke(this, new(nameof(Japanese)));
        return true;
    }
}
