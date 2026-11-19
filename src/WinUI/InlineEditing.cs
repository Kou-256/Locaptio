using Locaptio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace Locaptio.WinUI;

/// <summary>
/// Editing the transcript directly (setting "DirectTextEditing", on by default): double-click a sentence to
/// edit it in place, or select text and press Delete/Backspace to remove it. Both go through the edit
/// history, so Ctrl+Z undoes them, and a later recognition result never overwrites them.
/// </summary>
public sealed partial class MainWindow
{
    private Popup? inlineEditor;
    private TextBox? inlineEditBox;
    private long inlineEditId; private int inlineEditColumn;

    private TextBlock? inlineEditTarget;
    private void InitializeInlineEditing()
    {
        // While the right-click menu is open, new speech must not scroll its row away (that closes the menu).
        transcriptSelectionMenu.Opening += (_, _) => PauseHistoryFollowing();
        CaptionList.AddHandler(UIElement.DoubleTappedEvent, new DoubleTappedEventHandler(BeginInlineEdit), true);
        Root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(DeleteSelectedText), true);
    }

    private void BeginInlineEdit(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (!settings.DirectTextEditing || dialogOpen || closing || inlineEditor is not null) return;
        var point = e.GetPosition(CaptionList);
        var hit = RealizedCaptionRows.SelectMany(row => row.Children.OfType<TextBlock>()).FirstOrDefault(text =>
        {
            var top = text.TransformToVisual(CaptionList).TransformPoint(new Point());
            return text.Visibility == Visibility.Visible && new Rect(top, new Size(text.ActualWidth, text.ActualHeight)).Contains(point);
        });
        if (hit?.DataContext is not CaptionHistoryEntry entry) return;
        BeginInlineEdit(hit, entry.Id, Grid.GetColumn(hit), TextIndexAtPoint(hit, point));
        e.Handled = true;
    }

    /// <param name="caret">Character position where the caret starts (where the user double-clicked).</param>
    private void BeginInlineEdit(TextBlock target, long id, int column, int caret)
    {
        if (FindCaption(id) is not { } caption) return;
        string? text = column == 0 ? caption.English : caption.Japanese;
        if (text is null) return;
        ClearTextSelection(); PauseHistoryFollowing();
        var box = new TextBox
        {
            Text = text, TextWrapping = TextWrapping.Wrap, AcceptsReturn = false, FontSize = target.FontSize, FontFamily = target.FontFamily,
            Width = Math.Max(160, target.ActualWidth + 18), MinHeight = target.ActualHeight + 10, Padding = new Thickness(8, 4, 8, 4)
        };
        // The theme's text box background is translucent; the sentence underneath would show through.
        uint rgb = CaptionWindow.ParseColor(settings.CaptionSurfaceColor, 0x363636);
        var surface = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        foreach (string key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused" }) box.Resources[key] = surface;
        box.RequestedTheme = Root.RequestedTheme;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, "発話を編集（Enterで確定、Escで取り消し）");
        ToolTipService.SetToolTip(box, "Enterで確定、Escで取り消し。Ctrl+Zで後から元に戻せます。");
        var origin = target.TransformToVisual(Root).TransformPoint(new Point(-9, -5));
        var popup = new Popup { Child = box, XamlRoot = Root.XamlRoot, HorizontalOffset = origin.X, VerticalOffset = origin.Y, IsLightDismissEnabled = false };
        inlineEditor = popup; inlineEditBox = box; inlineEditId = id; inlineEditColumn = column; inlineEditTarget = target;
        box.KeyDown += (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Enter) { args.Handled = true; FinishInlineEdit(true); }
            else if (args.Key == Windows.System.VirtualKey.Escape) { args.Handled = true; FinishInlineEdit(false); }
        };
        // Focus taken by the list itself (rows updating as speech arrives) keeps the edit; clicking elsewhere saves it.
        box.LostFocus += (_, _) => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (!ReferenceEquals(inlineEditBox, box)) return;
            var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject;
            bool listOrNothing = focused is null || focused is ListViewBase || focused is ListViewItem || ReferenceEquals(focused, box);
            if (listOrNothing) box.Focus(FocusState.Programmatic); else FinishInlineEdit(true);
        });
        if (historyScroll is not null) historyScroll.ViewChanged += ScrolledWhileEditing;
        popup.IsOpen = true;
        box.Focus(FocusState.Programmatic);
        box.Select(Math.Clamp(caret, 0, text.Length), 0);
        Status("発話を直接編集しています。Enterで確定、Escで取り消します。");
    }
    // New speech or scrolling must not end the edit: the editor follows its row and stays open until Enter or Esc.
    private void ScrolledWhileEditing(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (inlineEditor is not { } popup) return;
        PauseHistoryFollowing();
        // The row's text block may have been reused for another row; find the one showing the edited row now.
        var target = RealizedCaptionRows.SelectMany(row => row.Children.OfType<TextBlock>())
            .FirstOrDefault(t => t.DataContext is CaptionHistoryEntry entry && entry.Id == inlineEditId && Grid.GetColumn(t) == inlineEditColumn);
        if (target is null) return; // scrolled out of view: keep the editor where it is
        inlineEditTarget = target;
        var origin = target.TransformToVisual(Root).TransformPoint(new Point(-9, -5));
        popup.HorizontalOffset = origin.X; popup.VerticalOffset = origin.Y;
    }

    private void FinishInlineEdit(bool save)
    {
        if (inlineEditor is not { } popup || inlineEditBox is not { } box) return;
        inlineEditor = null; inlineEditBox = null; inlineEditTarget = null;
        if (historyScroll is not null) historyScroll.ViewChanged -= ScrolledWhileEditing;
        popup.IsOpen = false;
        CaptionList.Focus(FocusState.Programmatic);
        if (!save || closing) { Status("編集を取り消しました。"); return; }
        // Recognition may have revised the row meanwhile; the edit applies to what is shown now.
        if (FindCaption(inlineEditId) is not { } current) return;
        var update = inlineEditColumn == 0 ? edits.Apply(current, box.Text, current.Japanese) : edits.Apply(current, current.English, box.Text);
        if (update is not null) { ApplyManualUpdate(update); Status("発話を修正しました。Ctrl+Zで元に戻せます。"); }
        else if (inlineEditColumn == 0 && box.Text.Trim().Length == 0) Status("発話を空にはできません。元の文章のままにしました。");
        else Status("変更はありません。");
    }

    private void DeleteSelectedText(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is not (Windows.System.VirtualKey.Delete or Windows.System.VirtualKey.Back)) return;
        if (!settings.DirectTextEditing || dialogOpen || closing || IsEditingText() || textSelection is not { IsEmpty: false } selection) return;
        e.Handled = true;
        DeleteSelection(selection);
    }

    /// <summary>Removes the selected characters from each selected row. A row is never emptied.</summary>
    private void DeleteSelection(TranscriptSelection selection)
    {
        int changed = 0, kept = 0;
        foreach (long id in history.Where(row => selection.Contains(row.Id)).Select(row => row.Id).ToArray())
        {
            if (FindCaption(id) is not { } caption) continue;
            string text = selection.Column == 0 ? caption.English : caption.Japanese ?? "";
            var (start, length) = selection.Range(id, text.Length);
            if (length == 0) continue;
            string rest = text.Remove(start, length);
            if (selection.Column == 0 && rest.Trim().Length == 0) { kept++; continue; }
            var update = selection.Column == 0 ? edits.Apply(caption, rest, caption.Japanese) : edits.Apply(caption, caption.English, rest);
            if (update is null) continue;
            ApplyManualUpdate(update); changed++;
        }
        ClearTextSelection();
        Status(changed == 0 ? "削除できる文字がありません。" + (kept > 0 ? "発話全体は削除できません。" : "")
            : $"選択した文字を削除しました（{changed} 件の発話）。Ctrl+Zで元に戻せます" + (changed > 1 ? "（1件ずつ）" : "") + "。" + (kept > 0 ? $" 全体が選ばれていた {kept} 件はそのままです。" : ""));
    }
}
