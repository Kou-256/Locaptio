using Locaptio.Core;

namespace Locaptio.WinUI;

/// <summary>
/// 「続きを録音／録画」: while a saved recording is open (for example one restored after Locaptio closed unexpectedly),
/// 新規録音 or 新規録画 can continue it instead of making a new file and folder. The new part is recorded next to it
/// ("name.continue.wav/.mp4") and appended when recording stops, so the recording stays one file with one transcript.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The recording being continued and its document as it was when the new part started.</summary>
    private sealed record Continuation(string Target, RecordingDocument Document, string Part);
    private Continuation? continuation;
    /// <summary>The recording restored from an interrupted session; continuing it is offered first.</summary>
    private string restoredInterruptedPath = "";
    /// <summary>Self-check: answers "continue the open recording?" (true = continue).</summary>
    private Func<bool>? continueRecordingForVerification;

    /// <summary>The open recording that 新規録音 (audio) or 新規録画 (video) could continue, or null.</summary>
    private string? ContinuableRecording(bool video) =>
        reviewingRecording && recordingStop is null && playbackDocument is not null && playbackPath.Length > 0 && File.Exists(playbackPath)
        && PlaybackClock.IsVideo(playbackPath) == video && !Path.GetFileNameWithoutExtension(playbackPath).EndsWith(".continue", StringComparison.OrdinalIgnoreCase)
            ? playbackPath : null;

    /// <summary>Remembers what is continued; the new part records to <see cref="Continuation.Part"/>.</summary>
    private string BeginContinuation(string target)
    {
        string part = RecordingContinuation.PartPath(target, PlaybackClock.IsVideo(target) ? ".mp4" : ".wav");
        foreach (string leftover in new[] { part, RecordingTranscript.Sidecar(part), part + ".txt" }) try { if (File.Exists(leftover)) File.Delete(leftover); } catch (IOException) { }
        continuation = new(target, playbackDocument!, part);
        return part;
    }

    /// <summary>
    /// After the new part stopped: appends its media and transcript to the recording and opens the result. Returns false
    /// when it could not be joined; the part is then kept and opened as a recording of its own.
    /// </summary>
    private async Task<bool> FinishContinuationAsync(string part)
    {
        if (continuation is not { } c || !string.Equals(Path.GetFullPath(part), Path.GetFullPath(c.Part), StringComparison.OrdinalIgnoreCase)) return false;
        continuation = null;
        if (recordingTranscript is null || !File.Exists(part) || new FileInfo(part).Length == 0 || !File.Exists(c.Target)) return false;
        try
        {
            var added = await AlignToRecordedSpeechAsync(part, recordingTranscript.Snapshot());
            Status("前の録音の続きとしてつなげています…");
            double offset;
            if (PlaybackClock.IsVideo(c.Target))
            {
                var clip = await Windows.Media.Editing.MediaClip.CreateFromFileAsync(await Windows.Storage.StorageFile.GetFileFromPathAsync(c.Target));
                offset = clip.OriginalDuration.TotalSeconds;
                string joined = Path.Combine(Path.GetDirectoryName(c.Target)!, Path.GetFileNameWithoutExtension(c.Target) + ".joining.mp4");
                var progress = new Progress<double>(percent => StatusText.Text = $"前の録画の続きとしてつなげています… {percent:F0}%");
                await JoinVideosAsync([c.Target, part], joined, settings.VideoFramerate, progress);
                clip = null;
                try { await Task.Run(() => RecordingContinuation.Replace(joined, c.Target)); }
                catch { try { File.Delete(joined); } catch (IOException) { } throw; }
            }
            else offset = await Task.Run(() => RecordingContinuation.AppendAudio(c.Target, part));
            var merged = RecordingContinuation.Append(c.Document, added, offset);
            // Rows on screen are the recording's (its own IDs) followed by the new part's, so the live summary maps onto both.
            merged = merged with { LiveSummary = LiveSummaryForRecording(merged) ?? c.Document.LiveSummary };
            await RecordingTranscript.SaveAsync(c.Target, merged);
            await RecordingLibrary.SaveTextAsync(c.Target, merged);
            foreach (string file in new[] { part, RecordingTranscript.Sidecar(part), part + ".txt" }) try { if (File.Exists(file)) File.Delete(file); } catch (IOException) { }
            DiscardAutosave();
            await LoadRecordingAsync(c.Target);
            PlaybackPanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible; EnsurePlaybackSpace();
            QueueSummaryAfterRecording();
            Status($"前の{(PlaybackClock.IsVideo(c.Target) ? "録画" : "録音")}の続きとして同じファイルに保存しました（{TimeSpan.FromSeconds(offset):hh\\:mm\\:ss} から）: " + c.Target);
            return true;
        }
        catch (Exception ex)
        {
            Status("前の録音につなげられなかったため、続きの部分を別の録音として保存しました（元の録音はそのままです）: " + ex.Message);
            ShowStatusDetails(pin: true);
            return false;
        }
    }
}
