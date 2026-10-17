using System.ComponentModel;
using System.Windows;

namespace Locaptio.App;
public sealed class CaptionRow(long id) : INotifyPropertyChanged
{
    public long Id { get; } = id;
    public int Revision { get; set; }
    public string English { get; set; } = "";
    public string Japanese { get; set; } = "翻訳中…";
    public double FontSize { get; set; }
    public bool ShowEnglish { get; set; }
    public bool ShowJapanese { get; set; }
    public GridLength EnglishWidth => ShowEnglish ? new(1, GridUnitType.Star) : new(0);
    public GridLength JapaneseWidth => ShowJapanese ? new(1, GridUnitType.Star) : new(0);
    public Visibility EnglishVisibility => ShowEnglish ? Visibility.Visible : Visibility.Collapsed;
    public Visibility JapaneseVisibility => ShowJapanese ? Visibility.Visible : Visibility.Collapsed;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
