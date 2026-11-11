using System.Diagnostics;
using Locaptio.App;
using Locaptio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Locaptio.WinUI;

public sealed partial class MainWindow
{
    private async Task VerifyCaptionBehaviorAsync()
    {
        string mode = settings.CaptionMode;
        CaptionWindow? testWindow = null;
        bool passed = false;
        string report = "";
        try
        {
            var appearance = new Settings { CaptionMode = "japanese", CaptionMaxLines = 3, CaptionRows = 3, CaptionWindowWidth = 600 };
            testWindow = new CaptionWindow(appearance); testWindow.Activate(); testWindow.HideIfIdle();
            var sample = new CaptionUpdate(1, 1, "最初の文章です。", null, false, Stopwatch.GetTimestamp(), 0, 0);
            testWindow.Update([sample, sample with { Id = 2, English = "次の文章も途中で文字が切れずに表示されます。" }]);
            await Task.Delay(200);
            bool whole = testWindow.HasWholeTextBlocks();
            appearance.CaptionSentenceMode = true;
            testWindow.ApplyAppearance(); await Task.Delay(150);
            bool sentence = testWindow.RenderedPositions().Length == 1 && testWindow.RenderedPositions()[0].Text.StartsWith("次の");
            testWindow.Update([sample with { Id = 3, English = string.Concat(Enumerable.Repeat("長い文章の折り返しも確認します。", 40)) + "ここが最新の文章です。" }]);
            await Task.Delay(200);
            bool longText = testWindow.HasWholeTextBlocks() && testWindow.RenderedPositions().Last().Text.EndsWith("ここが最新の文章です。")
                && !testWindow.RenderedPositions().Any(p => p.Text.Contains("…")) && testWindow.RenderedPositions().Last().Text.Length < 200;
            await testWindow.CaptureAsync(Path.Combine(root, "artifacts", "caption-lines-1.0.36.png"));
            appearance.CaptionMode = "translate"; appearance.CaptionMaxLines = 4;
            testWindow.Update([sample with { Id = 4, English = string.Concat(Enumerable.Repeat("Earlier English words. ", 40)) + "LATEST-END", Japanese = string.Concat(Enumerable.Repeat("前の文章です。", 40)) + "最新の訳です。🐈‍⬛" }]);
            await Task.Delay(150);
            var bilingual = testWindow.RenderedPositions();
            bool latestBoth = bilingual.Length == 2 && bilingual[0].Text.EndsWith("LATEST-END") && bilingual[1].Text.EndsWith("最新の訳です。🐈‍⬛") && testWindow.HasWholeTextBlocks();


            settings.CaptionMode = "japanese"; ClearCaptions(); captionStop = new(); ApplySettings(); sample = sample with { Started = Stopwatch.GetTimestamp() };
            for (int i = 1; i <= 200; i++) UpdateCaption(sample with { Id = i, English = $"文章{i}：過去の文章も保存します。" });
            await Task.Delay(400);
            historyScroll!.ChangeView(null, Math.Max(0, historyScroll.ScrollableHeight - 16), null, true);
            await Task.Delay(200); UpdateHistoryFollowingFromViewport();
            bool near = followLatest && ResumeHistoryButton.Visibility == Visibility.Collapsed;
            UpdateCaption(sample with { Id = 201, English = "最新の文章です。" }); await Task.Delay(250);
            bool following = followLatest && historyScroll.ScrollableHeight - historyScroll.VerticalOffset < 5;
            historyScroll.ChangeView(null, 0, null, true); await Task.Delay(200); UpdateHistoryFollowingFromViewport();
            double offset = historyScroll.VerticalOffset;
            UpdateCaption(sample with { Id = 202, English = "さらに新しい文章です。" }); await Task.Delay(200);
            bool historical = !followLatest && ResumeHistoryButton.Visibility == Visibility.Visible && Math.Abs(historyScroll.VerticalOffset - offset) < 5;
            var range = new TranscriptSelection(new(history[0].Id, 1), new(history[2].Id, 5), 0);
            draggingText = true; SetTextSelection(range);
            string expected = range.Text(history);
            bool blueDuring = RealizedCaptionRows.SelectMany(r => r.Children.OfType<TextBlock>()).Any(t => t.TextHighlighters.Count > 0);
            FinishTextSelection();
            bool released = !draggingText && textSelection?.Text(history) == expected && releasedSelectionText == expected
                && RealizedCaptionRows.SelectMany(r => r.Children.OfType<TextBlock>()).Any(t => t.TextHighlighters.Count > 0);
            passed = whole && sentence && longText && latestBoth && near && following && historical && blueDuring && released;
            report = $"PASS={passed}; wholeBlocks={whole}; sentenceMode={sentence}; longText={longText}; bilingualLatestSuffix={latestBoth}; latestVisibleFollowing={near}; incomingFollow={following}; historicalRetained={historical}; blueDuringDrag={blueDuring}; releasedSelectionCopyRetained={released}";
        }
        catch (Exception ex) { report = "PASS=False; " + ex; }
        finally
        {
            testWindow?.Close(); settings.CaptionMode = mode;
            File.WriteAllText(Path.Combine(root, "artifacts", "caption-behavior-1.0.36.txt"), report);
            Environment.ExitCode = passed ? 0 : 1; await ShutdownAsync();
        }
    }
}
