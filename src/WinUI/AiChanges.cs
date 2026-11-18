using Locaptio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace Locaptio.WinUI;

/// <summary>
/// Where AI correction changed an utterance (setting "ShowAiChanges", on by default): the changed characters
/// are tinted, an "AI" badge marks the row, and hovering the badge shows the text from before the correction,
/// with the differences, and a button to go back to it.
/// </summary>
public sealed partial class MainWindow
{
    private readonly Dictionary<long, (string Before, string After, List<(int Start, int Length)> Ranges)> aiChangeRanges = new();
    private static readonly SolidColorBrush AiTint = new(Windows.UI.Color.FromArgb(90, 124, 92, 255));

    /// <summary>The recognized text before AI correction, when AI changed this row and the user has not edited it.</summary>
    private string? AiOriginal(CaptionUpdate caption)
        => settings.ShowAiChanges && caption.AiCorrected && !edits.IsEdited(caption.Id) && edits.Original(caption.Id) is { } original && original != caption.English ? original : null;

    private List<(int Start, int Length)> AiRanges(long id, string before, string after)
    {
        if (aiChangeRanges.TryGetValue(id, out var cached) && cached.Before == before && cached.After == after) return cached.Ranges;
        var ranges = TextDiff.AddedRanges(before, after);
        aiChangeRanges[id] = (before, after, ranges);
        return ranges;
    }

    private void ApplyAiHighlight(TextBlock text, CaptionHistoryEntry entry, int column)
    {
        if (column != 0 || AiOriginal(entry.Caption) is not { } original) return;
        var highlight = new TextHighlighter { Background = AiTint, Foreground = text.Foreground };
        foreach (var (start, length) in AiRanges(entry.Id, original, entry.Caption.English))
            if (start + length <= entry.Caption.English.Length) highlight.Ranges.Add(new TextRange { StartIndex = start, Length = length });
        if (highlight.Ranges.Count > 0) text.TextHighlighters.Add(highlight);
    }

    private void StyleAiMark(Grid row, CaptionUpdate caption)
    {
        if (row.Children.Count < 6 || row.Children[5] is not Border badge) return;
        string? original = AiOriginal(caption);
        badge.Visibility = original is null ? Visibility.Collapsed : Visibility.Visible;
        if (original is not null)
            ToolTipService.SetToolTip(row.Children[0], "AI補正前：" + original + "\n（右の「AI」にカーソルを合わせると、比較と元に戻す操作ができます）");
        if (badge.Tag is not null) return;
        // Wired once per realized container; the row it shows is read when it opens.
        badge.Tag = true;
        // New text from binding replaces the row's highlights; draw them again for the new text.
        foreach (var column in row.Children.OfType<TextBlock>().Take(2))
            column.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) => ApplyTextSelection(row));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(badge, "AIが補正した発話。補正前の文章を表示");
        void Open() { if (badge.DataContext is CaptionHistoryEntry entry) ShowAiChange(badge, entry.Id); }
        badge.PointerEntered += (_, _) => Open();
        badge.Tapped += (_, e) => { e.Handled = true; Open(); };
    }

    private void ShowAiChange(FrameworkElement anchor, long id)
    {
        if (FindCaption(id) is not { } caption || AiOriginal(caption) is not { } original) return;
        var panel = new StackPanel { Spacing = 8, MaxWidth = 460 };
        panel.Children.Add(new TextBlock { Text = "AI補正前（認識した文章）", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = original, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        panel.Children.Add(new TextBlock { Text = "変更点（赤の取り消し線＝消えた文字、紫＝AIが加えた文字）", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var compare = new RichTextBlock { TextWrapping = TextWrapping.Wrap };
        var paragraph = new Paragraph();
        foreach (var segment in TextDiff.Segments(original, caption.English))
        {
            var run = new Run { Text = segment.Text };
            if (segment.Kind == TextDiff.Kind.Removed) { run.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113)); run.TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough; }
            else if (segment.Kind == TextDiff.Kind.Added) { run.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250)); run.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; }
            paragraph.Inlines.Add(run);
        }
        compare.Blocks.Add(paragraph); panel.Children.Add(compare);
        var flyout = new Flyout { Content = panel, Placement = FlyoutPlacementMode.Left, ShowMode = FlyoutShowMode.Standard };
        var revert = new Button { Content = "補正前に戻す" };
        revert.Click += (_, _) => { flyout.Hide(); RevertRow(id); };
        var close = new Button { Content = "閉じる" }; close.Click += (_, _) => flyout.Hide();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; actions.Children.Add(revert); actions.Children.Add(close);
        panel.Children.Add(actions);
        panel.Children.Add(new TextBlock { Text = "戻した後も Ctrl+Z で補正後に戻せます。", FontSize = 11, Opacity = .7 });
        aiChangeFlyout = flyout;
        flyout.ShowAt(anchor);
    }
    private Flyout? aiChangeFlyout;
}
