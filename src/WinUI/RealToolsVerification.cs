using Locaptio.Core;
using Microsoft.UI.Xaml;

namespace Locaptio.WinUI;

// --verify-real-tools: the recording tools through the app's own code paths with the installed
// Whisper and Qwen models (run from a prepared runtime folder). Dialog answers are supplied;
// recognition, alignment, comparison data, summaries and files are real. No audio is played or captured.
public sealed partial class MainWindow
{
    private async Task VerifyRealToolsAsync()
    {
        string artifacts = Path.Combine(root, "artifacts"), work = Path.Combine(Path.GetTempPath(), "Locaptio-verify-real-" + Environment.ProcessId);
        Directory.CreateDirectory(artifacts); Directory.CreateDirectory(work);
        string report = Path.Combine(artifacts, "real-tools-verification.txt");
        bool summarize = settings.SummarizeAfterRecording, autosave = settings.AutosaveSession;
        var log = new List<string>(); var failures = new List<string>();
        void Check(bool condition, string name) { log.Add((condition ? "ok   " : "FAIL ") + name); if (!condition) failures.Add(name); }
        try
        {
            Title = "Locaptio — 実モデルでの録音ツール検証";
            confirmReplacementForVerification = _ => Task.FromResult(true); settings.SummarizeAfterRecording = false;
            string audio = Path.Combine(work, "会議.wav");
            File.Copy(Path.Combine(artifacts, File.Exists(Path.Combine(artifacts, "test-ja-continuous.wav")) ? "test-ja-continuous.wav" : "test-ja.wav"), audio);
            // A placeholder transcript, as if the recording had been captured with poor recognition.
            await RecordingTranscript.SaveAsync(audio, new(1, "会議.wav", DateTimeOffset.Now, true, [new(1, 1, 0, "（認識できなかった録音）", null, 1)]));
            await LoadRecordingAsync(audio); ClosePlayback();
            Check(reviewingRecording && history.Count == 1, "open recording");

            // Re-transcribe with small and adopt it.
            var started = DateTime.Now;
            retranscribeChoiceForVerification = ("small", true, false); adoptForVerification = true;
            await RetranscribeAsync(); ClosePlayback();
            var adopted = playbackDocument!;
            log.Add($"re-transcribe small: {(DateTime.Now - started).TotalSeconds:F0}s, {adopted.Phrases.Length} phrases: " + string.Join(" / ", adopted.Phrases.Select(p => $"{p.Seconds:F1} {p.Text}")));
            Check(adopted.Model == "small" && adopted.Phrases.Length > 1 && adopted.Phrases.All(p => p.Words is { Length: > 0 }) && history.Count == adopted.Phrases.Length, "re-transcription adopted with word times");
            var versions = await TranscriptComparison.LoadVersionsAsync(audio);
            Check(versions.Count == 1 && versions[0].Document.Phrases.Single().Text == "（認識できなかった録音）", "previous result kept as a version");
            Check(captionStop is null && StartButton.IsEnabled && TaskPanel.Visibility == Visibility.Collapsed && PreparationPanel.Visibility == Visibility.Collapsed, "task UI cleaned up after re-transcription");

            // Make the text late and untimed, as Windows captions leave it, then improve sync with large-v3-turbo.
            var truth = adopted.Phrases;
            await RecordingTranscript.SaveAsync(audio, adopted with { ApproximateTiming = true, Phrases = truth.Select(p => p with { Seconds = p.Seconds + 2.5, EndSeconds = null, Words = null }).ToArray(), Bookmarks = [new(truth[^1].Seconds + 2.6, "重要")] });
            await LoadRecordingAsync(audio); ClosePlayback();
            started = DateTime.Now;
            await ImproveTimingAsync(); ClosePlayback();
            var synced = playbackDocument!;
            var errors = synced.Phrases.Zip(truth).Select(p => Math.Abs(p.First.Seconds - p.Second.Seconds)).Order().ToArray();
            log.Add($"improve sync ({settings.SpeechModel}): {(DateTime.Now - started).TotalSeconds:F0}s, onset error median {errors[errors.Length / 2]:F2}s max {errors[^1]:F2}s; {StatusText.Text}");
            Check(synced.Phrases.Zip(truth).All(p => p.First.Text == p.Second.Text) && synced.Phrases.Count(p => p.Words is { Length: > 0 }) >= synced.Phrases.Length * 3 / 4 && errors[errors.Length / 2] < .6, "sync corrected late text without changing it");
            Check(synced.Bookmarks is { Length: 1 } && Math.Abs(synced.Bookmarks[0].Seconds - (truth[^1].Seconds + .1)) < .7 && bookmarks.Single().PhraseId == history[^1].Id, "bookmark moved with its utterance");
            Check((await TranscriptComparison.LoadVersionsAsync(audio)).Count == 2, "pre-sync version kept");

            // Click-to-play position and word highlight use the new times.
            var phrase = synced.Phrases[^1]; var word = phrase.Words![phrase.Words.Length / 2];
            playbackSeconds = (word.Start + word.End) / 2; highlightedPlaybackPhrase = null; SetPlaybackHighlight(phrase);
            Check(playbackHighlightRange == (word.StartIndex, word.Length) || settings.PlaybackHighlightMode != "word", "word highlight follows aligned times");

            // Summary with the real local LLM.
            started = DateTime.Now; ShowSummaryPane(true); await RunSummaryAsync(); ClosePlayback(); await reviewWrite;
            log.Add($"summary: {(DateTime.Now - started).TotalSeconds:F0}s: " + string.Join(" / ", summaryItems.Select(i => $"[{i.Section}] {i.Text} ←{string.Join(",", i.RowIds)}")) + "; " + StatusText.Text);
            var saved = await RecordingTranscript.LoadAsync(audio);
            Check(summaryItems.Length > 0 && summaryItems.All(i => i.RowIds.All(id => FindCaption(id) is not null)) && saved.Summary?.Length == summaryItems.Length && File.Exists(RecordingFiles.SummaryFile(audio)), "real summary saved with evidence");

            // Subtitles from the aligned document.
            string srt = Path.Combine(work, "会議.srt"); await WriteSubtitlesAsync(srt, playbackDocument!, "source");
            Check(SubtitleExport.Cues(playbackDocument!).Count >= synced.Phrases.Length && File.ReadAllText(srt).Contains(" --> "), "SRT from aligned recording");
            await Task.Delay(500);
            await CaptureElementAsync(Root, Path.Combine(artifacts, "real-tools-recording.png"));
        }
        catch (Exception ex) { failures.Add("exception: " + ex); }
        finally
        {
            retranscribeChoiceForVerification = null; adoptForVerification = null; confirmReplacementForVerification = null;
            settings.SummarizeAfterRecording = summarize; settings.AutosaveSession = autosave; settings.Save();
            ClosePlayback(); try { await reviewWrite; } catch { }
            File.WriteAllText(report, (failures.Count == 0 ? "PASS" : "FAIL: " + string.Join("; ", failures)) + Environment.NewLine + string.Join(Environment.NewLine, log));
            Environment.ExitCode = failures.Count == 0 ? 0 : 1;
            try { Directory.Delete(work, true); } catch (IOException) { }
            await ShutdownAsync();
        }
    }
}
