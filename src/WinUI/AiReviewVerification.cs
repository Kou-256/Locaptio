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

            // 5. During a recording: the toolbar toggle turns it off and on, and turning it on catches up earlier speech.
            //    A long session folds older blocks into "so far" and keeps the originals under an expander.
            ClearCaptions(); standIn = new StandInSummary(); liveSummaryBackendForVerification = standIn;
            using (var recordingRun = new CancellationTokenSource())
            using (var listening = new CancellationTokenSource())
            {
                recordingStop = recordingRun; captionStop = listening; UpdateCommandStates();
                LiveSummaryToggle.IsChecked = false; LiveSummaryToggleClick(LiveSummaryToggle, new RoutedEventArgs());
                Check(!settings.LiveSummary && LiveSummaryMenuItem.IsChecked == false && LiveSummaryToggle.IsEnabled, "toolbar toggle turns live summary off while recording");
                for (int i = 0; i < 40; i++) UpdateCaption(new CaptionUpdate(historyIds.LastOrDefault() + 1, 1, sentence, null, true, Stopwatch.GetTimestamp(), 0, 0, "ja"));
                await LiveSummaryTickAsync();
                Check(liveBlocks.Count == 0 && standIn.Calls == 0, "nothing is summarized while it is off");
                LiveSummaryToggle.IsChecked = true; LiveSummaryToggleClick(LiveSummaryToggle, new RoutedEventArgs());
                Check(settings.LiveSummary && LiveSummaryMenuItem.IsChecked, "toolbar toggle turns live summary on while recording");
                await LiveSummaryTickAsync();
                Check(liveBlocks.Count == 1 && liveBlocks[0].FirstId == historyIds[0], "speech from before it was turned on is caught up");
                for (int block = 0; block < 3; block++)
                {
                    for (int i = 0; i < 40; i++) UpdateCaption(new CaptionUpdate(historyIds.LastOrDefault() + 1, 1, sentence, null, true, Stopwatch.GetTimestamp(), 0, 0, "ja"));
                    await LiveSummaryTickAsync();
                }
                Check(liveDigest is not null && liveArchived.Count == 1 && liveBlocks.Count == LiveRecentBlocks, $"older blocks are folded into a digest ({liveArchived.Count} archived, {liveBlocks.Count} recent)");
                Check(SummaryContent.Children.OfType<Expander>().Count() == 1 && SummaryContent.Children.OfType<StackPanel>().Count() == LiveRecentBlocks + 1, "pane shows recent blocks, the digest and the folded originals");
                await CaptureComposedAsync(Path.Combine(shots, "live-summary-recording.png"));
                captionStop = null; recordingStop = null; UpdateCommandStates();
            }
            log.Add("PASS: live summary toggles while recording, catches up, and condenses older blocks");
            // 5b. 「今までを要約」 while transcribing: summarizes short speech at once, without stopping.
            ClearCaptions(); standIn = new StandInSummary(); liveSummaryBackendForVerification = standIn;
            settings.LiveSummary = false; LiveSummaryMenuItem.IsChecked = false; LiveSummaryToggle.IsChecked = false;
            using (var listening = new CancellationTokenSource())
            {
                captionStop = listening; UpdateCommandStates();
                for (int i = 0; i < 4; i++) UpdateCaption(new CaptionUpdate(historyIds.LastOrDefault() + 1, 1, "最後に質疑応答を行い、次回の日程を確認しました。", null, true, Stopwatch.GetTimestamp(), 0, 0, "ja"));
                Check(SummaryToolButton.IsEnabled && SummaryMenuItem.IsEnabled, "the summary command stays available while transcribing");
                SummaryClick(SummaryToolButton, new RoutedEventArgs()); RenderLiveSummary();
                Check((string)SummaryRunButton.Content == "今までを要約", "the pane offers 「今までを要約」 while transcribing");
                await RunSummaryAsync();
                Check(settings.LiveSummary && liveBlocks.Count == 1 && liveBlocks[0].LastId == historyIds[^1] && captionStop == listening && SummaryContent.Children.OfType<StackPanel>().Count() == 1,
                    $"short speech so far is summarized without stopping ({liveBlocks.Count} blocks, live={settings.LiveSummary})");
                await CaptureComposedAsync(Path.Combine(shots, "summary-so-far.png"));
                // Turning live summary off and on again, and closing and reopening the pane, keep the earlier results.
                int kept = liveBlocks.Count;
                LiveSummaryToggle.IsChecked = false; LiveSummaryToggleClick(LiveSummaryToggle, new RoutedEventArgs());
                SummaryCloseClick(this, new RoutedEventArgs());
                LiveSummaryToggle.IsChecked = true; LiveSummaryToggleClick(LiveSummaryToggle, new RoutedEventArgs());
                Check(liveBlocks.Count == kept && SummaryPane.Visibility == Visibility.Visible && SummaryContent.Children.OfType<StackPanel>().Count() == kept, $"off → on keeps the live summary ({liveBlocks.Count}/{kept}, cards {SummaryContent.Children.OfType<StackPanel>().Count()})");
                SummaryCloseClick(this, new RoutedEventArgs()); SummaryClick(SummaryToolButton, new RoutedEventArgs());
                Check(SummaryContent.Children.OfType<StackPanel>().Count() == kept, "closing and reopening the pane keeps the live summary");
                LiveSummaryToggle.IsChecked = false; LiveSummaryToggleClick(LiveSummaryToggle, new RoutedEventArgs());
                SummaryCloseClick(this, new RoutedEventArgs()); SummaryClick(SummaryToolButton, new RoutedEventArgs());
                Check(SummaryContent.Children.OfType<StackPanel>().Count() == kept, $"while off, the pane still shows earlier live summaries (cards {SummaryContent.Children.OfType<StackPanel>().Count()})");
                for (int i = 0; i < 4; i++) UpdateCaption(new CaptionUpdate(historyIds.LastOrDefault() + 1, 1, "最後に質疑応答を行い、次回の日程を確認しました。", null, true, Stopwatch.GetTimestamp(), 0, 0, "ja"));
                LiveSummaryToggle.IsChecked = true; LiveSummaryToggleClick(LiveSummaryToggle, new RoutedEventArgs());
                await RunSummaryAsync();
                Check(liveBlocks.Count == kept + 1 && liveBlocks[0].LastId < liveBlocks[^1].FirstId, $"after on again, only new speech is added ({liveBlocks.Count} blocks)");
                // A full summary does not hide or discard the live summaries, and 「今までを要約」 keeps the full one.
                summaryItems = [new SummaryItem("要点", "全体の要約", [historyIds[0]])]; RenderSummary();
                bool both = SummaryContent.Children.OfType<TextBlock>().Any(t => t.Text.StartsWith("ライブ要約（")) && SummaryContent.Children.OfType<StackPanel>().Count(p => p.Children.OfType<HyperlinkButton>().Any(h => h.Content is string c && c.Contains('–'))) == liveBlocks.Count;
                for (int i = 0; i < 4; i++) UpdateCaption(new CaptionUpdate(historyIds.LastOrDefault() + 1, 1, "最後に質疑応答を行い、次回の日程を確認しました。", null, true, Stopwatch.GetTimestamp(), 0, 0, "ja"));
                await RunSummaryAsync();
                Check(both && summaryItems.Length == 1 && liveBlocks.Count == kept + 2 && SummaryContent.Children.OfType<TextBlock>().Any(t => t.Text.StartsWith("ライブ要約（")), $"full and live summaries are both kept ({liveBlocks.Count} live blocks)");
                // 5c. Restart and 「前回の作業を復元」: the full summary and live summary come back with working links.
                summaryItems = [new SummaryItem("要点", "全体の要約", [historyIds[1]])];
                int liveCount = liveBlocks.Count; long lastCovered = liveBlocks[^1].LastId;
                var savedRows = history.Select(row => new AutosaveRow(row.Id, RowSeconds(row.Caption), row.English, row.Japanese, row.Caption.SourceLanguage)).ToArray();
                string snapshotPath = Path.Combine(work, "session.json");
                await SessionAutosave.SaveAsync(snapshotPath, new AutosaveSnapshot(1, DateTimeOffset.Now, settings.CaptionMode, null, savedRows, null, SummarySnapshot()));
                captionStop = null; ClearCaptions();
                Check(summaryItems.Length == 0 && liveBlocks.Count == 0, "clearing removes the summary from the screen");
                await RestoreSessionAsync(SessionAutosave.TryLoad(snapshotPath)!);
                Check(summaryItems.Length == 1 && summaryItems[0].RowIds.Length == 1 && FindCaption(summaryItems[0].RowIds[0]) is not null && liveBlocks.Count == liveCount
                    && liveBlocks.All(bl => FindCaption(bl.FirstId) is not null && FindCaption(bl.LastId) is not null) && SummaryPane.Visibility == Visibility.Visible
                    && SummaryContent.Children.OfType<TextBlock>().Any(t => t.Text.StartsWith("ライブ要約（")) && liveSummaryNextId > liveBlocks[^1].LastId,
                    $"restore brings back the full and live summaries ({summaryItems.Length} / {liveBlocks.Count} of {liveCount})");
                await CaptureComposedAsync(Path.Combine(shots, "summary-restored.png"));
                captionStop = listening;
                summaryItems = [];
                captionStop = null; UpdateCommandStates();
            }
            Check(LiveSummaryToggle is CheckBox && LiveSummaryMenuItem.Icon is ImageIcon, "live summary is a toolbar checkbox; its icon is only in the menu bar");

            // 6. Toolbar: hide, reorder, settings rows, persistence and reset (the user's own layout is restored afterwards).
            string[] savedOrder = settings.ToolbarOrder, savedHidden = settings.ToolbarHidden;
            try
            {
                ResetToolbar();
                string[] defaults = CurrentToolbarOrder();
                SetToolbarHidden("ImportButton", true); SetToolbarHidden("StartButton", true);
                MoveToolbarItem("LiveSummaryToggle", -2);
                await Task.Delay(200); MainToolbar.UpdateLayout();
                var order = CurrentToolbarOrder();
                Check(MainToolbar.Hidden.Contains(ImportButton) && Microsoft.UI.Xaml.Controls.Primitives.LayoutInformation.GetLayoutSlot(ImportButton).Width == 0 && !MainToolbar.Hidden.Contains(StartButton) && Microsoft.UI.Xaml.Controls.Primitives.LayoutInformation.GetLayoutSlot(StartButton).Width > 0, "an item hides; 開始 cannot be hidden");
                Check(Array.IndexOf(order, "LiveSummaryToggle") == Array.IndexOf(defaults, "LiveSummaryToggle") - 2 && settings.ToolbarOrder.SequenceEqual(order) && settings.ToolbarHidden.SequenceEqual(["ImportButton"]), "an item moves and both are saved: " + string.Join(",", order));
                await CaptureComposedAsync(Path.Combine(shots, "toolbar-customized.png"));
                // A fresh layout pass from the saved settings gives the same result (as after a restart).
                ResetToolbarOrderForVerification(defaults); ApplyToolbarLayout();
                Check(CurrentToolbarOrder().SequenceEqual(order) && MainToolbar.Hidden.Contains(ImportButton), "saved order and hidden items are applied again");
                var toolbarPage = new StackPanel(); AddToolbarSettings(toolbarPage);
                var rows = toolbarPage.Children.OfType<StackPanel>().Single().Children.OfType<Grid>().ToArray();
                Check(rows.Length == defaults.Length && rows.Any(r => r.Children.OfType<CheckBox>().First().Content as string == "音声ファイル読込" && r.Children.OfType<CheckBox>().First().IsChecked == false)
                    && rows.First(r => (string)r.Children.OfType<CheckBox>().First().Content == "開始").Children.OfType<CheckBox>().First().IsEnabled == false, "settings list every item with show checkboxes (開始 locked)");
                ResetToolbar();
                Check(CurrentToolbarOrder().SequenceEqual(defaults) && MainToolbar.Hidden.Count == 0, "reset restores the default toolbar");
            }
            finally { settings.ToolbarOrder = savedOrder; settings.ToolbarHidden = savedHidden; settings.Save(); ApplyToolbarLayout(); }
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
