using System.Diagnostics;
using Locaptio.Core;
using Locaptio.Core.AI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Locaptio.WinUI;

// --verify-transcript-tools: drives the real window through editing, the dictionary, bookmarks,
// autosave/restore, subtitle export, a saved recording and the summary pane. It uses synthetic
// text, a silent WAV and a fake summary backend: no model, microphone or network is involved.
public sealed partial class MainWindow
{
    private sealed class FixtureSummary : ISummaryBackend
    {
        public Task<InferenceResult> SummarizeAsync(string numberedTranscript, CancellationToken token)
            => Task.FromResult(new InferenceResult("## 要点\n- Locaptioの新機能を確認した [1][2]\n- 議事録は自動保存される [3]\n## 決定事項\n- 来週までに字幕ファイルを共有する [5]\n## TODO\n- 橋爪さんが用語辞書を更新する [4]", 1));
    }
    private static async Task CaptureElementAsync(UIElement element, string path)
    {
        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap(); await bitmap.RenderAsync(element);
        var pixels = await bitmap.GetPixelsAsync();
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(pixels));
        await encoder.FlushAsync(); stream.Seek(0);
        using var file = File.Create(path); await System.IO.WindowsRuntimeStreamExtensions.AsStreamForRead(stream).CopyToAsync(file);
    }
    private async Task VerifyTranscriptToolsAsync()
    {
        string artifacts = Path.Combine(root, "artifacts"); Directory.CreateDirectory(artifacts);
        string report = Path.Combine(artifacts, "transcript-tools-verification.txt");
        string work = Path.Combine(Path.GetTempPath(), "Locaptio-verify-tools-" + Environment.ProcessId); Directory.CreateDirectory(work);
        // Settings are changed in memory only and restored before anything is saved again.
        string mode = settings.CaptionMode, dictionary = settings.TermDictionary, labels = settings.BookmarkLabels; bool autosave = settings.AutosaveSession, summarize = settings.SummarizeAfterRecording;
        double background = settings.BackgroundOpacity;
        var failures = new List<string>();
        void Check(bool condition, string name) { if (!condition) failures.Add(name); }
        try
        {
            Title = "Locaptio — 編集・辞書・ブックマークの検証";
            confirmReplacementForVerification = _ => Task.FromResult(true);
            settings.CaptionMode = "japanese"; settings.TermDictionary = "Locaptio | ロカプション\n議事録 | 議時録"; settings.BookmarkLabels = "重要、後で確認、質問"; settings.AutosaveSession = true; settings.SummarizeAfterRecording = false; settings.BackgroundOpacity = 1;
            modeChanging = true; ModeSelector.SelectedIndex = 1; modeChanging = false; ApplySettings();
            AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(1180 * GetDpiForWindow(hwnd) / 96.0), (int)(720 * GetDpiForWindow(hwnd) / 96.0)));
            await Task.Delay(400);
            await CaptureElementAsync(Root, Path.Combine(artifacts, "transcript-tools-empty.png"));
            Check(EmptyState.Visibility == Visibility.Visible && StatusLine.Text.Length > 0, "empty state and status line");

            // Live captions: dictionary replacement, AI correction, then a manual edit that later revisions cannot undo.
            captionStop = new(); long start = Stopwatch.GetTimestamp();
            string[] lines = ["今日はロカプションの新しい機能を確認します。", "誤認識を直接修正できるようになりました。", "議時録は五秒ごとに自動保存されます。", "用語辞書は橋爪さんが更新します。", "来週までに字幕ファイルを共有しましょう。", "以上で本日の確認を終わります。"];
            for (int i = 0; i < lines.Length; i++) UpdateCaption(new(i + 1, 1, lines[i], null, true, start + i * 4 * Stopwatch.Frequency / 10, 0, 0, "ja"));
            UpdateCaption(new(2, 2, "誤認識を直接修正できるようになりました！", null, true, start + 4 * Stopwatch.Frequency / 10, 0, 0, "ja", SourceCorrected: true, AiCorrected: true));
            Check(history.Count == 6 && history[0].English.StartsWith("今日はLocaptioの") && history[2].English.StartsWith("議事録は") && edits.Original(1) == lines[0] && edits.Differs(history[1].Caption), "dictionary replacement and pre-correction text");
            Check(EmptyState.Visibility == Visibility.Collapsed, "empty state hidden");
            var edited = edits.Apply(history[3].Caption, "用語辞書は橋爪さんが毎週更新します。", null)!; ApplyManualUpdate(edited);
            UpdateCaption(new(4, 3, "遅れて届いた古い認識", null, true, start + 12 * Stopwatch.Frequency / 10, 0, 0, "ja", SourceCorrected: true, AiCorrected: true));
            Check(history[3].English == "用語辞書は橋爪さんが毎週更新します。" && transcript.Export(false).Contains("毎週更新") && edits.IsEdited(4) && UndoMenuItem.IsEnabled, "manual edit survives later revisions");
            UndoEdit(); Check(history[3].English == lines[3] && RedoMenuItem.IsEnabled && !edits.IsEdited(4), "undo");
            RedoEdit(); Check(history[3].English.Contains("毎週") && edits.IsEdited(4), "redo");
            RevertRow(2); Check(history[1].English == lines[1] && edits.CanUndo, "revert to recognized text");
            UndoEdit(); Check(history[1].English.EndsWith("！"), "undo of revert");

            AddBookmark("重要"); AddBookmark("後で確認", 3); AddBookmark("重要");
            Check(bookmarks.Count == 2 && bookmarks.Any(b => b.PhraseId == 6 && b.Label == "重要") && BookmarkPanel.Children.Count == 2 && BookmarkScroll.Visibility == Visibility.Visible, "bookmarks and duplicate rejection");
            Check(ExportTranscriptForSave(false).Contains("★後で確認　議事録は"), "bookmarks in text export");
            await Task.Delay(300);
            bool marks = CaptionList.ContainerFromIndex(2) is ListViewItem marked && marked.ContentTemplateRoot is Grid markedRow && markedRow.Children[2].Visibility == Visibility.Visible
                && CaptionList.ContainerFromIndex(3) is ListViewItem changed && changed.ContentTemplateRoot is Grid changedRow && changedRow.Children[3].Visibility == Visibility.Visible && changedRow.Children[2].Visibility == Visibility.Collapsed;
            Check(marks, "row marks for bookmark and edit");
            captionStop.Dispose(); captionStop = null;

            // Subtitles from a session that was not recorded: times count from the first utterance.
            var live = SessionDocument();
            string srt = Path.Combine(work, "live.srt"), vtt = Path.Combine(work, "live.vtt");
            await WriteSubtitlesAsync(srt, live, "source"); await WriteSubtitlesAsync(vtt, live, "source");
            string srtText = await File.ReadAllTextAsync(srt), vttText = await File.ReadAllTextAsync(vtt);
            Check(srtText.StartsWith("1\r\n00:00:00,000 --> 00:00:00,400\r\n今日はLocaptio") && srtText.Contains("\r\n6\r\n00:00:02,000 --> ") && vttText.StartsWith("WEBVTT\n\n00:00:00.000 --> 00:00:00.400\n") && File.ReadAllBytes(srt)[0] == 0xEF && File.ReadAllBytes(vtt)[0] == (byte)'W', "SRT/VTT export");

            // Autosave: what a crashed process leaves behind restores rows, edit marks and bookmarks.
            sessionDirty = true; WriteAutosave(); await autosaveWrite;
            var snapshot = SessionAutosave.TryLoad(autosavePath);
            Check(snapshot is { Rows.Length: 6, Mode: "japanese", RecordingPath: null } && snapshot.Rows[3] is { Edited: true, Original: not null } && snapshot.Bookmarks is { Length: 2 }, "autosave snapshot");
            await CaptureElementAsync(Root, Path.Combine(artifacts, "transcript-tools-live.png"));
            ClearCaptions(); await autosaveWrite;
            Check(history.Count == 0 && bookmarks.Count == 0 && !edits.CanUndo && SessionAutosave.TryLoad(autosavePath) is null, "clear discards tools state and snapshot");
            if (snapshot is not null) await RestoreSessionAsync(snapshot);
            Check(history.Count == 6 && history[3].English.Contains("毎週") && edits.IsEdited(4) && edits.Original(1) == lines[0] && bookmarks.Count == 2 && bookmarks.Any(b => b.PhraseId == 3 && b.Label == "後で確認") && !reviewingRecording && SaveButton.IsEnabled, "restore after interruption");
            // New speech continues after the restored rows.
            captionStop = new(); long offset = historyIds.LastOrDefault();
            UpdateCaption(new(offset + 1, 1, "復元後の新しい発話です。", null, true, Stopwatch.GetTimestamp(), 0, 0, "ja"));
            captionStop.Dispose(); captionStop = null;
            Check(history.Count == 7 && history[6].Id == 7 && SessionDocument().Phrases.Zip(SessionDocument().Phrases.Skip(1)).All(p => p.First.Seconds <= p.Second.Seconds), "session continues in order after restore");

            // A saved recording: edits, bookmarks and the summary are written to its own files.
            string audio = Path.Combine(work, "会議.wav");
            using (var writer = new NAudio.Wave.WaveFileWriter(audio, new NAudio.Wave.WaveFormat(16000, 16, 1))) writer.Write(new byte[32000 * 30], 0, 32000 * 30);
            var phrases = lines.Select((text, i) => new RecordingPhrase(100 + i, 100 + i, 2 + i * 4, Terms.Apply(text), null, 1, "ja", 5 + i * 4, Original: Terms.Apply(text) == text ? null : text)).ToArray();
            await RecordingTranscript.SaveAsync(audio, new(1, "会議.wav", DateTimeOffset.Now, false, phrases, [new(10.5, "質問", 102)], null, "small"));
            await LoadRecordingAsync(audio); ClosePlayback();
            Check(reviewingRecording && history.Count == 6 && bookmarks.Single() is { Label: "質問", PhraseId: 3 } && edits.Original(1) == lines[0] && PlaybackPanel.Visibility == Visibility.Visible && SessionAutosave.TryLoad(autosavePath) is null, "open recording with bookmark and original text");
            ApplyManualUpdate(edits.Apply(history[4].Caption, "来週の金曜までに字幕ファイルを共有しましょう。", null)!);
            AddBookmark("重要", 5); await reviewWrite;
            var saved = await RecordingTranscript.LoadAsync(audio);
            Check(saved.Phrases[4] is { Text: "来週の金曜までに字幕ファイルを共有しましょう。", Edited: true, Original: not null } && saved.Bookmarks is { Length: 2 } && saved.Bookmarks[1] is { Label: "重要", Seconds: 18 }
                && (await File.ReadAllTextAsync(audio + ".txt")).Contains("［★重要］来週の金曜"), "edits and bookmarks saved with the recording");
            UndoEdit(); await reviewWrite;
            Check((await RecordingTranscript.LoadAsync(audio)).Phrases[4] is { Edited: false, Original: null } && history[4].English == lines[4], "undo saved with the recording");

            summaryBackendForVerification = new FixtureSummary();
            ShowSummaryPane(true); await RunSummaryAsync(); ClosePlayback(); await reviewWrite;
            var summarized = await RecordingTranscript.LoadAsync(audio);
            Check(summaryItems.Length == 4 && summaryItems[0].RowIds.SequenceEqual([1L, 2L]) && summaryItems[2] is { Section: "決定事項" } && summarized.Summary is { Length: 4 } && summarized.Summary[2].Evidence.SequenceEqual([18.0])
                && File.Exists(RecordingFiles.SummaryFile(audio)) && (await File.ReadAllTextAsync(RecordingFiles.SummaryFile(audio))).Contains("- 来週までに字幕ファイルを共有する（00:00:18）") && RecordingFiles.Bundle(audio).Length == 4, "summary with evidence saved");
            Check(SummaryPane.Visibility == Visibility.Visible && CaptionList.Margin.Right > 300 && SummaryCopyButton.IsEnabled && captionStop is null && StartButton.IsEnabled && TaskPanel.Visibility == Visibility.Collapsed, "summary pane and exclusive task cleanup");
            JumpToRow(summaryItems[2].RowIds[0], false);
            Check(textSelection is { IsEmpty: false } && textSelection.Contains(5), "evidence link selects its utterance");
            await Task.Delay(400);
            await CaptureElementAsync(Root, Path.Combine(artifacts, "transcript-tools-recording.png"));
            // Reopening restores the summary and its evidence from the files alone.
            await LoadRecordingAsync(audio); ClosePlayback();
            Check(summaryItems.Length == 4 && summaryItems[3].RowIds.SequenceEqual([4L]) && bookmarks.Count == 2, "summary and bookmarks restored on reopen");

            // Word times added afterwards are used by the open recording.
            var reference = phrases.Select(p => p with { Seconds = p.Seconds - 1, EndSeconds = p.EndSeconds - 1, Words = p.Text.Select((_, i) => new RecordingWord(i, 1, p.Seconds - 1 + i * .1, p.Seconds - 1 + (i + 1) * .1)).ToArray() }).ToArray();
            var aligned = TimingAlignment.Apply(playbackDocument!, reference);
            Check(aligned.Aligned == 6 && aligned.Document.Phrases.All(p => p.Words is { Length: > 0 }) && aligned.Document.Phrases[0].Seconds == 1 && aligned.Document.Summary![2].Evidence[0] == 17 && aligned.Document.Bookmarks![0].Seconds == 9.5, "timing alignment keeps attachments");
            await TranscriptComparison.SaveVersionAsync(audio, "同期の改善前", playbackDocument!);
            Check((await TranscriptComparison.LoadVersionsAsync(audio)).Single().Document.Phrases.Length == 6 && RecordingFiles.Bundle(audio).Length == 5, "version kept beside the recording");

            // The dictionary page stores rows as they are edited.
            SettingsClick(this, new RoutedEventArgs()); await Task.Delay(300);
            settingsNavigation!.Select("用語辞書"); await Task.Delay(300);
            var page = settingsNavigation.Page("用語辞書");
            var termRows = page.Children.OfType<StackPanel>().SelectMany(p => p.Children.OfType<Grid>()).Where(g => g.Children.Count == 4 && g.Children[0] is TextBox).ToArray();
            Check(termRows.Length == 2 && ((TextBox)termRows[1].Children[0]).Text == "議事録" && ((TextBox)termRows[1].Children[1]).Text == "議時録", "dictionary settings rows");
            if (termRows.Length == 2) { ((TextBox)termRows[1].Children[2]).Text = "minutes"; await Task.Delay(100); }
            Check(Terms.Entries.Length == 2 && Terms.Entries[1].Translation == "minutes" && Terms.TranslationGlossary("") == "議事録 = minutes", "dictionary edits stored");
            await settingsNavigation.CaptureAsync(Path.Combine(artifacts, "transcript-tools-dictionary.png"));
            settingsWindow?.Close();
        }
        catch (Exception ex) { failures.Add("exception: " + ex); }
        finally
        {
            summaryBackendForVerification = null; confirmReplacementForVerification = null;
            settings.CaptionMode = mode; settings.TermDictionary = dictionary; settings.BookmarkLabels = labels; settings.AutosaveSession = autosave; settings.SummarizeAfterRecording = summarize; settings.BackgroundOpacity = background;
            settings.Save();
            ClosePlayback(); try { await reviewWrite; } catch { }
            File.WriteAllText(report, failures.Count == 0 ? "PASS: empty state, dictionary replacement, edit/undo/redo/revert, bookmarks, SRT/VTT, autosave and restore, recording edits/bookmarks/summary persistence, evidence links, timing alignment, versions, dictionary settings" : "FAIL: " + string.Join("; ", failures));
            Environment.ExitCode = failures.Count == 0 ? 0 : 1;
            try { Directory.Delete(work, true); Directory.Delete(Path.GetDirectoryName(autosavePath)!, true); } catch (IOException) { }
            await ShutdownAsync();
        }
    }
}
