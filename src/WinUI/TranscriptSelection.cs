using Locaptio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Locaptio.WinUI;

public sealed partial class MainWindow
{
    private TranscriptSelection? textSelection;
    private bool draggingText;
    private string releasedSelectionText = "";
    private Point selectionPointer;
    private readonly DispatcherTimer selectionScrollTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly MenuFlyout transcriptSelectionMenu = new();

    private void InitializeTranscriptSelection()
    {
        CaptionList.IsTabStop = true;
        CaptionList.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(BeginTextSelection), true);
        CaptionList.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(MoveTextSelection), true);
        CaptionList.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(EndTextSelection), true);
        CaptionList.PointerCaptureLost += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, CaptionList)) { FinishTextSelection(); }
        };
        // ListView with SelectionMode=None may leave keyboard focus on the
        // window itself after a drag. A window-level accelerator still receives
        // Ctrl+C there, unlike a KeyDown handler attached only to the list.
        var copyShortcut = new KeyboardAccelerator { Key = Windows.System.VirtualKey.C, Modifiers = Windows.System.VirtualKeyModifiers.Control };
        copyShortcut.Invoked += async (_, e) =>
        {
            if (!HasTranscriptSelection || IsEditingText()) return;
            e.Handled = true; await CopyTextSelectionAsync();
        };
        Root.KeyboardAccelerators.Add(copyShortcut);
        Root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape && HasTranscriptSelection && !IsEditingText())
            { ClearTextSelection(); e.Handled = true; }
        }), true);
        var menu = transcriptSelectionMenu; var copy = new MenuFlyoutItem { Text = "選択範囲をコピー" };
        copy.Click += async (_, _) => await CopyTextSelectionAsync(); menu.Items.Add(copy); CaptionList.ContextFlyout = menu;
        menu.Opening += (_, _) => copy.IsEnabled = textSelection is { IsEmpty: false } || releasedSelectionText.Length > 0;
        selectionScrollTimer.Tick += (_, _) =>
        {
            if (!draggingText || historyScroll is null) { selectionScrollTimer.Stop(); return; }
            double delta = selectionPointer.Y < 20 ? -18 : selectionPointer.Y > CaptionList.ActualHeight - 20 ? 18 : 0;
            if (delta != 0) { historyScroll.ChangeView(null, Math.Clamp(historyScroll.VerticalOffset + delta, 0, historyScroll.ScrollableHeight), null, true); ExtendTextSelection(selectionPointer); }
        };
        Closed += (_, _) => selectionScrollTimer.Stop();
    }
    private void BeginTextSelection(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(CaptionList);
        if (!point.Properties.IsLeftButtonPressed) return;
        var hit = RealizedCaptionRows.SelectMany(row => row.Children.OfType<TextBlock>()).FirstOrDefault(text =>
        {
            var top = text.TransformToVisual(CaptionList).TransformPoint(new Point());
            return text.Visibility == Visibility.Visible && new Rect(top, new Size(text.ActualWidth, text.ActualHeight)).Contains(point.Position);
        });
        if (hit is null) { ClearTextSelection(); return; }
        int column = Grid.GetColumn(hit);
        var entry = (CaptionHistoryEntry)hit.DataContext;
        int offset = TextIndexAtPoint(hit, point.Position);
        ClearTextSelection();
        textSelection = new(new(entry.Id, offset), new(entry.Id, offset), column);
        selectionPointer = point.Position;
        hit.ReleasePointerCapture(e.Pointer);
        PauseHistoryFollowing();
        // Focus the realized row: ListView.Focus can fail when every item has
        // IsTabStop=False, leaving keys outside the XAML keyboard route.
        bool focused = false;
        if (CaptionList.ContainerFromItem(entry) is ListViewItem item)
        {
            item.IsTabStop = true;
            focused = item.Focus(FocusState.Pointer);
        }
        if (!focused) focused = CaptionList.Focus(FocusState.Pointer);
        draggingText = CaptionList.CapturePointer(e.Pointer);
        if (Environment.GetCommandLineArgs().Contains("--verify-selection-ui"))
            File.WriteAllText(Path.Combine(root, "artifacts", "selection-pointer-start.txt"), $"capture={draggingText}; focus={focused}; focusedType={FocusManager.GetFocusedElement(Root.XamlRoot)?.GetType().Name}; anchor={textSelection.Anchor}");
        selectionScrollTimer.Start(); e.Handled = true;
    }
    private void MoveTextSelection(object sender, PointerRoutedEventArgs e)
    {
        if (!draggingText) return;
        var point = e.GetCurrentPoint(CaptionList);
        if (!point.Properties.IsLeftButtonPressed) { FinishTextSelection(); CaptionList.ReleasePointerCapture(e.Pointer); return; }
        selectionPointer = point.Position;
        ExtendTextSelection(selectionPointer); e.Handled = true;
    }
    private void EndTextSelection(object sender, PointerRoutedEventArgs e)
    {
        if (!draggingText) return;
        ExtendTextSelection(e.GetCurrentPoint(CaptionList).Position);
        FinishTextSelection(); CaptionList.ReleasePointerCapture(e.Pointer); e.Handled = true;
        if (Environment.GetCommandLineArgs().Contains("--verify-selection-ui"))
            File.WriteAllText(Path.Combine(root, "artifacts", "selection-pointer-verification.txt"), $"anchor={textSelection?.Anchor}; focus={textSelection?.Focus}; column={textSelection?.Column}; text={textSelection?.Text(history)}");
    }
    private void FinishTextSelection()
    {
        if (!draggingText) return;
        string copied = textSelection?.Text(history) ?? "";
        draggingText = false; selectionScrollTimer.Stop();
        releasedSelectionText = copied;
        // Mouse release finishes the gesture, not the selection. Keep both the
        // range and its highlights until another click or Escape clears it.
        UpdateHistoryFollowingFromViewport();
    }
    private void ExtendTextSelection(Point point)
    {
        if (textSelection is null) return;
        TextBlock? nearest = null; double distance = double.MaxValue;
        foreach (var row in RealizedCaptionRows)
        {
            if (row.DataContext is not CaptionHistoryEntry) continue;
            var text = (TextBlock)row.Children[textSelection.Column];
            if (text.Visibility != Visibility.Visible) continue;
            var top = text.TransformToVisual(CaptionList).TransformPoint(new Point());
            double d = point.Y < top.Y ? top.Y - point.Y : point.Y > top.Y + text.ActualHeight ? point.Y - top.Y - text.ActualHeight : 0;
            if (d < distance) { distance = d; nearest = text; }
        }
        if (nearest?.DataContext is not CaptionHistoryEntry entry) return;
        SetTextSelection(textSelection with { Focus = new(entry.Id, TextIndexAtPoint(nearest, point)) });
    }
    private int TextIndexAtPoint(TextBlock text, Point listPoint)
    {
        // Work in the text's own layout coordinates, including wrapped lines.
        // Run.ContentStart excludes XAML's inline markers from character offsets.
        var point = CaptionList.TransformToVisual(text).TransformPoint(listPoint);
        var start = text.Inlines.OfType<Run>().FirstOrDefault()?.ContentStart ?? text.ContentStart;
        int nearest = 0; double distance = double.MaxValue;
        for (int i = 0; i <= text.Text.Length; i++)
        {
            if (i > 0 && i < text.Text.Length && char.IsLowSurrogate(text.Text[i])) continue;
            var caret = start.GetPositionAtOffset(i, LogicalDirection.Forward);
            if (caret is null) continue;
            for (int directionIndex = 0; directionIndex < 2; directionIndex++)
            {
                var direction = directionIndex == 0 ? LogicalDirection.Forward : LogicalDirection.Backward;
                var rect = caret.GetCharacterRect(direction);
                if (rect.Height <= 0) continue;
                double dy = point.Y < rect.Top ? rect.Top - point.Y : point.Y > rect.Bottom ? point.Y - rect.Bottom : 0;
                double dx = point.X - rect.X;
                double score = dx * dx + dy * dy * 10000;
                if (score < distance) { nearest = i; distance = score; }
            }
        }
        return nearest;
    }
    private void SetTextSelection(TranscriptSelection selection)
    {
        textSelection = selection; PauseHistoryFollowing();
        foreach (var row in RealizedCaptionRows) ApplyTextSelection(row);
        FlushSelectedRowUpdates();
        if (!draggingText) UpdateHistoryFollowingFromViewport();
    }
    private void ApplyTextSelection(Grid row)
    {
        if (row.DataContext is not CaptionHistoryEntry entry) return;
        for (int column = 0; column < 2; column++)
        {
            var text = (TextBlock)row.Children[column]; text.TextHighlighters.Clear();
            if (textSelection is null || textSelection.Column != column) continue;
            var range = textSelection.Range(entry.Id, text.Text.Length);
            if (range.Length == 0) continue;
            var highlight = new TextHighlighter { Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 0, 120, 215)), Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) };
            highlight.Ranges.Add(new TextRange { StartIndex = range.Start, Length = range.Length }); text.TextHighlighters.Add(highlight);
        }
    }
    private void ClearTextSelection()
    {
        textSelection = null; releasedSelectionText = "";
        foreach (var row in RealizedCaptionRows)
            foreach (var text in row.Children.OfType<TextBlock>()) { text.TextHighlighters.Clear(); text.Select(text.ContentStart, text.ContentStart); }
        FlushSelectedRowUpdates();
        if (!draggingText) UpdateHistoryFollowingFromViewport();
    }
    private bool HasTranscriptSelection => textSelection is { IsEmpty: false } || releasedSelectionText.Length > 0;
    private bool IsEditingText()
    {
        var focused = Root.XamlRoot is null ? null : FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject;
        while (focused is not null)
        {
            if (focused is TextBox or PasswordBox or RichEditBox) return true;
            focused = VisualTreeHelper.GetParent(focused);
        }
        return false;
    }
    private bool copyingSelection;
    private async Task CopyTextSelectionAsync()
    {
        if (!HasTranscriptSelection || copyingSelection) return;
        string copied = textSelection is { IsEmpty: false } ? textSelection.Text(history) : releasedSelectionText;
        if (copied.Length == 0) return;
        copyingSelection = true;
        try
        {
            var data = new Windows.ApplicationModel.DataTransfer.DataPackage(); data.SetText(copied);
            for (int attempt = 0; ; attempt++)
            {
                try { Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data); break; }
                catch (System.Runtime.InteropServices.COMException ex) when (attempt < 3 && (ex.HResult == unchecked((int)0x800401D0) || ex.HResult == unchecked((int)0x80070005)))
                { await Task.Delay(50 * (attempt + 1)); }
            }
            try { Windows.ApplicationModel.DataTransfer.Clipboard.Flush(); }
            catch (System.Runtime.InteropServices.COMException) { /* SetContent succeeded; the running app can still supply the data. */ }
            StatusText.Text = "選択範囲をコピーしました。";
            if (Environment.GetCommandLineArgs().Contains("--verify-selection-ui"))
            {
                File.WriteAllText(Path.Combine(root, "artifacts", "selection-copied-verification.txt"), copied);
                string actual = await Windows.ApplicationModel.DataTransfer.Clipboard.GetContent().GetTextAsync();
                File.WriteAllText(Path.Combine(root, "artifacts", "selection-clipboard-verification.txt"), actual);
            }
        }
        catch (Exception ex) { StatusText.Text = "コピーできません: " + ex.Message; ShowStatusDetails(pin: true); }
        finally { copyingSelection = false; }
    }
}
