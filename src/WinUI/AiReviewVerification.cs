using System.Diagnostics;
using Locaptio.Core;
using Locaptio.Core.AI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Locaptio.WinUI;

// --verify-ai-review: recording defaults, term dictionary import/export through the settings page, the marks
// and before/after popup for AI-corrected rows (with revert and undo), and the live summary of blocks.
// Synthetic text and a stand-in summarizer; no model is loaded and nothing is recorded.
public sealed partial class MainWindow
{
    private sealed class StandInSummary : ISummaryBackend
    {
        public int Calls;
        public Task<InferenceResult> SummarizeAsync(string numbered, CancellationToken token)
        {
            Calls++;
            return Task.FromResult(new InferenceResult(!numbered.Contains("質疑応答")
                ? "## 要点\n- 予算の見直しについて議論した [1]\n## 決定事項\n- 次回までに予算案を確定する [2]\n## TODO\n- 田中：資料を共有する（金曜） [3]"
                : "## 要点\n- 最後に質疑応答を行った [1]\n## 決定事項\n- なし\n## TODO\n- なし", 1));
        }
    }

    private async Task VerifyAiReviewAsync()
    {
        string work = Path.Combine(Path.GetTempPath(), "Locaptio-verify-ai-" + Environment.ProcessId); Directory.CreateDirectory(work);
        string shots = Path.Combine(root, "artifacts", "ai-review"); Directory.CreateDirectory(shots);
        string report = Path.Combine(root, "artifacts", "ai-review-verification.txt");
        string dictionary = settings.TermDictionary; bool live = settings.LiveSummary, show = settings.ShowAiChanges, autosave = settings.AutosaveSession;
        var log = new List<string>(); var failures = new List<string>();
        void Check(bool condition, string name) { log.Add((condition ? "ok   " : "FAIL ") + name); if (!condition) failures.Add(name); }
        void Running(Action add) { using var running = new CancellationTokenSource(); captionStop = running; add(); captionStop = null; }
        long Row(string text) { long id = historyIds.LastOrDefault() + 1; Running(() => UpdateCaption(new CaptionUpdate(id, 1, text, null, true, Stopwatch.GetTimestamp(), 0, 0, "ja"))); return id; }
        try
        {
            settings.AutosaveSession = false; settings.ShowAiChanges = true;
            ((Window)this).AppWindow.Resize(new Windows.Graphics.SizeInt32(1500, 900)); await Task.Delay(300);

            // 1. Recording defaults.
            var fresh = new Locaptio.App.Settings();
            Check(fresh.VideoFramerate == 60 && fresh.VideoQuality == "best" && fresh.VideoCountdownSeconds == 3 && VideoQualityValue("best") > VideoQualityValue("high"), "recording defaults: 60 fps, best quality, 3 s countdown");

            // 2. Term dictionary export and import from the settings page.
            settings.TermDictionary = "Locaptio | ロカプション | \n議事録 | 議時録 | minutes"; settings.Save();
            SettingsClick(this, new RoutedEventArgs()); await Task.Delay(300);
            settingsNavigation!.Select("用語辞書"); await Task.Delay(200);
            var page = settingsNavigation.Page("用語辞書");
            var buttons = page.Children.OfType<StackPanel>().First(p => p.Children.OfType<Button>().Any(b => b.Content as string == "エクスポート…"));
            Button Find(string label) => buttons.Children.OfType<Button>().First(b => b.Content as string == label);
            void Press(Button button) => ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button)).Invoke();
            string exported = Path.Combine(work, "辞書.csv");
            termExportPickerForVerification = () => exported; Press(Find("エクスポート…")); await Task.Delay(500);
            var back = File.Exists(exported) ? TermDictionaryFiles.Parse(TermDictionaryFiles.Decode(await File.ReadAllBytesAsync(exported))) : [];
            Check(back.Length == 2 && back[1].Translation == "minutes", "export writes the dictionary as CSV");
            string excel = Path.Combine(work, "excel.csv");
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            await File.WriteAllBytesAsync(excel, System.Text.Encoding.GetEncoding(932).GetBytes("表記,誤表記,訳語\r\n議事録,ぎじろく,\r\n予算案,よさんあん、予算アン,budget plan\r\n"));
            termImportPickerForVerification = () => excel; termImportReplaceForVerification = false; Press(Find("インポート…")); await Task.Delay(500);
            Check(Terms.Entries.Length == 3 && Terms.Entries.First(e => e.Term == "議事録").Aliases.SequenceEqual(["議時録", "ぎじろく"]) && Terms.Entries.Any(e => e.Term == "予算案" && e.Translation == "budget plan"),
                "import (Excel Shift-JIS CSV) adds terms and merges misrecognitions: " + settings.TermDictionary.Replace("\n", " / "));
            int shownRows = page.Children.OfType<StackPanel>().SelectMany(p => p.Children.OfType<Grid>()).Count(g => g.Children.Count == 4);
            Check(shownRows == 3, $"the settings page lists the imported terms ({shownRows})");
            termImportReplaceForVerification = true; Press(Find("インポート…")); await Task.Delay(500);
            Check(Terms.Entries.Length == 2 && Terms.Entries.All(e => e.Term is "議事録" or "予算案"), "import with replace keeps only the file's terms");
            settingsWindow?.Close(); await Task.Delay(300);

            // 3. AI-corrected rows: tint, badge, before/after popup, revert and undo (no dictionary, so only AI changes the text).
            settings.TermDictionary = "";
            long first = Row("今日は議時録を保存します"); Row("予算を確認します");
            var corrected = FindCaption(first)! with { Revision = 2, English = "今日は議事録を保存します。", SourceCorrected = true, AiCorrected = true };
            Running(() => UpdateCaption(corrected)); await Task.Delay(300);
            var row = RealizedCaptionRows.First(r => ((CaptionHistoryEntry)r.DataContext).Id == first);
            var badge = (Border)row.Children[5]; var text = (TextBlock)row.Children[0];
            Check(badge.Visibility == Visibility.Visible && text.TextHighlighters.Any(h => h.Background == AiTint && h.Ranges.Count == 2), $"AI badge and tinted changes on the corrected row (badge={badge.Visibility}, highlighters={string.Join(",", text.TextHighlighters.Select(h => h.Ranges.Count))}, original={edits.Original(first)}, now={FindCaption(first)?.English}, ai={FindCaption(first)?.AiCorrected})");
            Check((ToolTipService.GetToolTip(text) as string)?.Contains("今日は議時録を保存します") == true, "hovering the text shows the text before correction");
            ShowAiChange(badge, first); await Task.Delay(400);
            await CaptureComposedAsync(Path.Combine(shots, "ai-change.png"));
            var panel = (StackPanel)aiChangeFlyout!.Content;
            Check(panel.Children.OfType<RichTextBlock>().Any(), "popup compares before and after");
            var revert = panel.Children.OfType<StackPanel>().Last().Children.OfType<Button>().First();
            Press(revert); await Task.Delay(200);
            Check(FindCaption(first)?.English == "今日は議時録を保存します" && badge.Visibility == Visibility.Collapsed, "補正前に戻す restores the recognized text");
            UndoEdit(); await Task.Delay(200);
            Check(FindCaption(first)?.English == "今日は議事録を保存します。", "Ctrl+Z brings the correction back");

            // 4. Live summary of blocks while listening, and the remainder when listening stops.
            ClearCaptions(); var standIn = new StandInSummary(); liveSummaryBackendForVerification = standIn;
            settings.LiveSummary = true; LiveSummaryMenuItem.IsChecked = true;
            string sentence = "今回の会議では来期の予算と人員配置について詳しく話し合い、各部署の要望を整理しました。";
            using (var listening = new CancellationTokenSource())
            {
                captionStop = listening;
                for (int i = 0; i < 40; i++) UpdateCaption(new CaptionUpdate(historyIds.LastOrDefault() + 1, 1, sentence, null, true, Stopwatch.GetTimestamp(), 0, 0, "ja"));
                await LiveSummaryTickAsync();
                Check(liveBlocks.Count == 1 && standIn.Calls >= 1 && SummaryPane.Visibility == Visibility.Visible, $"a block is summarized while listening ({liveBlocks.Count} blocks, {standIn.Calls} calls, pane={SummaryPane.Visibility})");
                for (int i = 0; i < 4; i++) UpdateCaption(new CaptionUpdate(historyIds.LastOrDefault() + 1, 1, "最後に質疑応答を行い、次回の日程を確認しました。", null, true, Stopwatch.GetTimestamp(), 0, 0, "ja"));
                await LiveSummaryTickAsync();
                Check(liveBlocks.Count == 1, "a short new block waits for more speech");
                captionStop = null;
            }
            await LiveSummaryTickAsync(); await Task.Delay(300);
            Check(liveBlocks.Count == 2 && liveBlocks[1].LastId == historyIds[^1], $"the rest is summarized when listening stops ({liveBlocks.Count} blocks, {standIn.Calls} calls, next={liveSummaryNextId}, last={historyIds[^1]})");
            Check(SummaryContent.Children.OfType<TextBlock>().First().Text.StartsWith("ライブ要約") && SummaryContent.Children.OfType<StackPanel>().Count() == 2, "pane shows both blocks, newest first");
            await CaptureComposedAsync(Path.Combine(shots, "live-summary.png"));
        }
        catch (Exception ex) { failures.Add(ex.Message); log.Add(ex.ToString()); }
        finally
        {
            captionStop = null; liveSummaryBackendForVerification = null; termExportPickerForVerification = termImportPickerForVerification = null; termImportReplaceForVerification = null;
            settings.TermDictionary = dictionary; settings.LiveSummary = live; settings.ShowAiChanges = show; settings.AutosaveSession = autosave; settings.Save();
            File.WriteAllText(report, (failures.Count == 0 ? "PASS" : "FAIL: " + string.Join("; ", failures)) + Environment.NewLine + string.Join(Environment.NewLine, log));
            Environment.ExitCode = failures.Count == 0 ? 0 : 1;
            try { Directory.Delete(work, true); } catch (IOException) { }
            await ShutdownAsync();
        }
    }
}
