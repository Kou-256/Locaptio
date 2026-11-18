using System.Diagnostics;
using Locaptio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Locaptio.WinUI;

// --verify-session-home: keeping the text when a recording starts, editing in place (double-click editor,
// Delete on a selection), Clear while a saved recording is open, and Home. Synthetic text, a silent WAV;
// nothing is recorded or played.
public sealed partial class MainWindow
{
    private async Task VerifySessionHomeAsync()
    {
        string work = Path.Combine(Path.GetTempPath(), "Locaptio-verify-home-" + Environment.ProcessId); Directory.CreateDirectory(work);
        string shots = Path.Combine(root, "artifacts", "session-home"); Directory.CreateDirectory(shots);
        string report = Path.Combine(root, "artifacts", "session-home-verification.txt");
        string existing = settings.ExistingTextOnRecording; bool direct = settings.DirectTextEditing, autosave = settings.AutosaveSession, summarize = settings.SummarizeAfterRecording;
        var log = new List<string>(); var failures = new List<string>();
        void Check(bool condition, string name) { log.Add((condition ? "ok   " : "FAIL ") + name); if (!condition) failures.Add(name); }
        void Rows(params string[] texts)
        {
            // Captions are accepted only while listening; stand in for a running session while adding them.
            using var running = new CancellationTokenSource(); captionStop = running;
            long t = Stopwatch.GetTimestamp(); foreach (var text in texts) UpdateCaption(new CaptionUpdate(historyIds.LastOrDefault() + 1, 1, text, null, true, t, 0, 0, "ja"));
            captionStop = null;
        }
        try
        {
            settings.AutosaveSession = false; settings.SummarizeAfterRecording = false;
            confirmReplacementForVerification = _ => Task.FromResult(true); confirmCloseReviewForVerification = () => Task.FromResult(true);
            ((Microsoft.UI.Xaml.Window)this).AppWindow.Resize(new Windows.Graphics.SizeInt32(1500, 900)); await Task.Delay(300);

            // 1. Keeping the text when a new recording starts.
            Rows("最初の会議の内容です。", "議事録を共有します。", "次回は月曜日です。");
            settings.ExistingTextOnRecording = "keep";
            bool proceed = await ConfirmNewRecordingAsync(); PrepareTextForNewRecording();
            Check(proceed && keepExistingText && history.Count == 3, "keep: the three rows stay for the new recording");
            Rows("録音を始めてからの発話です。");
            Check(history.Count == 4 && history[^1].Id > history[2].Id, "keep: new speech continues after the kept rows");
            settings.ExistingTextOnRecording = "clear";
            proceed = await ConfirmNewRecordingAsync(); PrepareTextForNewRecording();
            Check(proceed && !keepExistingText && history.Count == 0, "clear: the text is cleared (after the save question)");

            // 2. Editing in place.
            settings.DirectTextEditing = true;
            Rows("今日はかいぎの議事録を保存します。", "予算について確認します。", "最後に質問を受け付けます。");
            await Task.Delay(300);
            long first = history[0].Id;
            var target = RealizedCaptionRows.First(r => ((CaptionHistoryEntry)r.DataContext).Id == first).Children.OfType<TextBlock>().First();
            BeginInlineEdit(target, first, 0, 3);
            await Task.Delay(200);
            Check(inlineEditBox is { } box && box.Text == "今日はかいぎの議事録を保存します。" && box.SelectionStart == 3 && IsEditingText(), "double-click editor opens over the sentence with the caret where clicked");
            inlineEditBox!.Text = "今日は会議の議事録を保存します。";
            await Task.Delay(100);
            await CaptureComposedAsync(Path.Combine(shots, "inline-editor.png"));
            FinishInlineEdit(true);
            Check(FindCaption(first)?.English == "今日は会議の議事録を保存します。" && edits.IsEdited(first) && inlineEditor is null, "Enter saves the edit");
            UndoEdit();
            Check(FindCaption(first)?.English == "今日はかいぎの議事録を保存します。", "Ctrl+Z undoes it");
            BeginInlineEdit(target, first, 0, 0); inlineEditBox!.Text = "捨てる変更"; FinishInlineEdit(false);
            Check(FindCaption(first)?.English == "今日はかいぎの議事録を保存します。", "Esc discards the change");

            long second = history[1].Id, third = history[2].Id;
            textSelection = new(new(second, 0), new(second, 3), 0); DeleteSelection(textSelection);
            Check(FindCaption(second)?.English == "ついて確認します。", "Delete removes the selected characters: " + FindCaption(second)?.English);
            textSelection = new(new(second, 6), new(third, 3), 0); DeleteSelection(textSelection);
            Check(FindCaption(second)?.English == "ついて確認し" && FindCaption(third)?.English == "質問を受け付けます。", "Delete across two rows: " + FindCaption(second)?.English + " / " + FindCaption(third)?.English);
            textSelection = new(new(third, 0), new(third, FindCaption(third)!.English.Length), 0); DeleteSelection(textSelection);
            Check(FindCaption(third)?.English == "質問を受け付けます。", "a whole sentence is never emptied");
            UndoEdit(); UndoEdit(); UndoEdit();
            Check(FindCaption(second)?.English == "予算について確認します。" && FindCaption(third)?.English == "最後に質問を受け付けます。", "undo restores the deletions");

            // 3. Clear while a saved recording is open closes it too (the file stays).
            string audio = Path.Combine(work, "会議.wav");
            using (var writer = new NAudio.Wave.WaveFileWriter(audio, new NAudio.Wave.WaveFormat(16000, 16, 1))) writer.Write(new byte[32000 * 6], 0, 32000 * 6);
            await RecordingTranscript.SaveAsync(audio, new(1, "会議.wav", DateTimeOffset.Now, false, [new(1, 1, .5, "保存済みの発話です。", null, 1), new(2, 2, 3, "見返しています。", null, 1)]));
            await LoadRecordingAsync(audio); EnsurePlayback(); await Task.Delay(200);
            Check(Reviewing && PlaybackPanel.Visibility == Visibility.Visible && history.Count == 2, "saved recording open for review");
            UpdateCommandStates();
            Check(ClearButton.IsEnabled && HomeButton.IsEnabled, "Clear and Home available while reviewing");
            await ClearAsync();
            Check(!Reviewing && playbackDocument is null && PlaybackPanel.Visibility == Visibility.Collapsed && VideoPanel.Visibility == Visibility.Collapsed && history.Count == 0 && File.Exists(audio), "Clear closes the recording and its text; the file stays");

            // 4. Home.
            await LoadRecordingAsync(audio); Rows("追加の発話です。"); ShowSummaryPane(true); await Task.Delay(200);
            await GoHomeAsync(); UpdateCommandStates();
            Check(!Reviewing && history.Count == 0 && SummaryPane.Visibility == Visibility.Collapsed && SessionTitle.Text == StartTitle && EmptyState.Visibility == Visibility.Visible && !HomeButton.IsEnabled, "Home returns to the start screen");
            await CaptureComposedAsync(Path.Combine(shots, "home.png"));
        }
        catch (Exception ex) { failures.Add(ex.Message); log.Add(ex.ToString()); }
        finally
        {
            settings.ExistingTextOnRecording = existing; settings.DirectTextEditing = direct; settings.AutosaveSession = autosave; settings.SummarizeAfterRecording = summarize; settings.Save();
            confirmReplacementForVerification = null; confirmCloseReviewForVerification = null;
            ClosePlayback(); try { await reviewWrite; } catch { }
            File.WriteAllText(report, (failures.Count == 0 ? "PASS" : "FAIL: " + string.Join("; ", failures)) + Environment.NewLine + string.Join(Environment.NewLine, log));
            Environment.ExitCode = failures.Count == 0 ? 0 : 1;
            try { Directory.Delete(work, true); } catch (IOException) { }
            await ShutdownAsync();
        }
    }
}
