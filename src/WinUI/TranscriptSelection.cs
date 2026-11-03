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
    private Point selectionPointer;
    private readonly DispatcherTimer selectionScrollTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly MenuFlyout transcriptSelectionMenu = new();

    private void InitializeTranscriptSelection()
    {
        CaptionList.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(BeginTextSelection), true);
        CaptionList.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(MoveTextSelection), true);
        CaptionList.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(EndTextSelection), true);
        CaptionList.PointerCaptureLost += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, CaptionList)) { draggingText = false; selectionScrollTimer.Stop(); }
        };
        CaptionList.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape) { ClearTextSelection(); e.Handled = true; }
            else if (e.Key == Windows.System.VirtualKey.C && textSelection is { IsEmpty: false }
                && Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
            { CopyTextSelection(); e.Handled = true; }
        }), true);
        var menu = transcriptSelectionMenu; var copy = new MenuFlyoutItem { Text = "選択範囲をコピー" };
        copy.Click += (_, _) => CopyTextSelection(); menu.Items.Add(copy); CaptionList.ContextFlyout = menu;
        menu.Opening += (_, _) => copy.IsEnabled = textSelection is { IsEmpty: false };
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
        PauseHistoryFollowing(); CaptionList.Focus(FocusState.Pointer);
        draggingText = CaptionList.CapturePointer(e.Pointer);
        if (Environment.GetCommandLineArgs().Contains("--verify-selection-ui"))
            File.WriteAllText(Path.Combine(root, "artifacts", "selection-pointer-start.txt"), $"capture={draggingText}; anchor={textSelection.Anchor}");
        selectionScrollTimer.Start(); e.Handled = true;
    }
    private void MoveTextSelection(object sender, PointerRoutedEventArgs e)
    {
        if (!draggingText) return;
        selectionPointer = e.GetCurrentPoint(CaptionList).Position;
        ExtendTextSelection(selectionPointer); e.Handled = true;
    }
    private void EndTextSelection(object sender, PointerRoutedEventArgs e)
    {
        if (!draggingText) return;
        ExtendTextSelection(e.GetCurrentPoint(CaptionList).Position);
        draggingText = false; selectionScrollTimer.Stop(); CaptionList.ReleasePointerCapture(e.Pointer); e.Handled = true;
        if (Environment.GetCommandLineArgs().Contains("--verify-selection-ui"))
            File.WriteAllText(Path.Combine(root, "artifacts", "selection-pointer-verification.txt"), $"anchor={textSelection?.Anchor}; focus={textSelection?.Focus}; column={textSelection?.Column}; text={textSelection?.Text(history)}");
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
        textSelection = null;
        foreach (var row in RealizedCaptionRows)
            foreach (var text in row.Children.OfType<TextBlock>()) { text.TextHighlighters.Clear(); text.Select(text.ContentStart, text.ContentStart); }
        FlushSelectedRowUpdates();
    }
    private void CopyTextSelection()
    {
        if (textSelection is null || textSelection.IsEmpty) return;
        try
        {
            string copied = textSelection.Text(history);
            var data = new Windows.ApplicationModel.DataTransfer.DataPackage(); data.SetText(copied);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
            StatusText.Text = "選択範囲をコピーしました。";
            if (Environment.GetCommandLineArgs().Contains("--verify-selection-ui")) File.WriteAllText(Path.Combine(root, "artifacts", "selection-copied-verification.txt"), copied);
        }
        catch (Exception ex) { StatusText.Text = "コピーできません: " + ex.Message; ShowStatusDetails(pin: true); }
    }
}
