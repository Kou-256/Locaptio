using System.Diagnostics;
using Locaptio.Core;
using Locaptio.Core.AI;
using Locaptio.Core.Audio;

namespace Locaptio.WinUI;

/// <summary>
/// Checks recorded text against what is actually heard. Whisper listens to the recording (with word
/// times) and each saved utterance is matched character by character against it (TimingAlignment):
/// matched utterances get real word times, and utterances mostly not heard are marked for checking.
///
/// With Windows captions the text comes from another program and has no word times, so the
/// recording is followed while it is written: finished parts are recognized in the background and
/// only the last seconds remain when recording stops. Whisper captions, video recordings and
/// recordings without captions are checked after stopping.
/// </summary>
public sealed partial class MainWindow
{
    private sealed class LiveContentCheck(string path) : IDisposable
    {
        public readonly string Path = path;
        public readonly CancellationTokenSource Stop = new();
        private int disposed;
        public volatile bool Complete;
        public Task<List<RecordingPhrase>?> Reference = Task.FromResult<List<RecordingPhrase>?>(null);
        public PreparedInference? Models;
        public string Problem = "";
        public readonly Stopwatch Clock = Stopwatch.StartNew();
        public double ModelReady = -1; public int Heard; public string Backend = ""; public readonly List<string> Timeline = new();
        public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) == 0) { Stop.Cancel(); Models?.Dispose(); } }
    }
    private LiveContentCheck? liveContentCheck;
    /// <summary>Other self-checks record without loading Whisper; the content-check self-check turns it on.</summary>
    private bool contentCheckInVerification;

    private bool ContentCheckModelReady(string model)
        => File.Exists(Path.Combine(root, ".venv", "Scripts", "python.exe")) && File.Exists(Path.Combine(root, ".venv", ".locaptio-speech-v2"))
            && File.Exists(Path.Combine(root, "models", "speech-" + model, "model.bin"));

    /// <summary>Starts following an audio recording when captions come from Windows. The file is the WAV being written.</summary>
    private void BeginLiveContentCheck(string path)
    {
        liveContentCheck?.Dispose(); liveContentCheck = null;
        if (settings.ContentCheck != "during" || VerificationRun && !contentCheckInVerification) return;
        if (!contentCheckInVerification && (captionStop is null || !UseWindowsCaptionEngine)) return;
        // Matching characters needs no large model. The light model on the CPU keeps pace with the
        // recording on ordinary PCs and does not depend on a GPU that may fail mid-recording.
        string model = settings.SpeechModel is "tiny" or "base" ? settings.SpeechModel : "small";
        if (!ContentCheckModelReady(model)) model = settings.SpeechModel;
        if (!ContentCheckModelReady(model)) return;
        string wav = Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase) ? path : path + ".recording.wav";
        bool japanese = settings.CaptionMode == "japanese";
        const bool cpu = true;
        var run = new LiveContentCheck(Path.GetFullPath(path)); liveContentCheck = run;
        var token = run.Stop.Token; var normalize = Terms.Apply; string prompt = Terms.SpeechPrompt(settings.SpeechPrompt);
        run.Reference = Task.Run(async () =>
        {
            try
            {
                run.Models = new PreparedInference(root, japanese, model, prompt, "cpu", cpu, false, false, LlmModelFile, _ => { });
                await Task.WhenAll(run.Models.Ready, run.Models.AccuracyReady).WaitAsync(token);
                run.Models.Speech.RequestTimeout = TimeSpan.FromMinutes(5);
                // As for whole-file recognition: earlier text must not steer the next segment.
                run.Models.Speech.ResetSpeechContext();
                var speech = run.Models.Speech; run.ModelReady = run.Clock.Elapsed.TotalSeconds; run.Backend = speech.Backend + " " + speech.Model;
                var phrases = new List<RecordingPhrase>();
                await FileTranscription.RunGrowingAsync(wav, () => run.Complete, speech, japanese ? "ja" : "en", phrase => { phrases.Add(phrase); run.Heard = phrases.Count; lock (run.Timeline) run.Timeline.Add($"{phrase.Seconds:F1}-{phrase.EndSeconds:F1}s@{run.Clock.Elapsed.TotalSeconds:F1}"); return Task.CompletedTask; }, token, normalize, message => { lock (run.Timeline) run.Timeline.Add($"{message}@{run.Clock.Elapsed.TotalSeconds:F1}"); });
                return phrases;
            }
            catch (OperationCanceledException) { return null; }
            catch (Exception ex) { run.Problem = ex.Message; return null; }
        });
    }

    private Task contentCheckTask = Task.CompletedTask;
    /// <summary>
    /// After a recording is saved: finishes the live check (or runs one now) and applies it to the
    /// recording's current text, so edits made while it runs are kept. Leaves everything unchanged when
    /// the check is off or unavailable, or another recording has been opened meanwhile.
    /// </summary>
    private async Task CheckRecordedContentAsync(string path)
    {
        var run = liveContentCheck?.Path == Path.GetFullPath(path) ? liveContentCheck : null;
        if (run is not null) liveContentCheck = null;
        var document = playbackDocument;
        try
        {
            if (settings.ContentCheck == "off" || document is null || document.Phrases.Length == 0 || closing) return;
            if (VerificationRun && !contentCheckInVerification) return;
            List<RecordingPhrase>? reference = null;
            if (run is not null)
            {
                run.Complete = true;
                Status("音声と文章を照合しています（録音の最後の部分）…");
                reference = await run.Reference;
                if (reference is null && run.Problem.Length > 0) Status("録音中の照合を続けられなかったため、録音全体を照合します: " + run.Problem);
            }
            string model = settings.SpeechModel;
            if (reference is null)
            {
                if (!ContentCheckModelReady(model)) { Status("音声と文章の照合には音声認識モデルが必要です。「同期を改善」を実行すると準備できます（設定→保存先・録音で照合をOFFにできます）。"); return; }
                bool japanese = !document.Phrases.Any(p => p.Language == "en");
                reference = await RecognizeRecordingAsync("音声と文章の照合", path, model, japanese, false, false);
                if (reference is null || closing) return;
            }
            // The text may have been edited while the audio was checked; match the latest version.
            if (!reviewingRecording || playbackPath != path || playbackDocument is null) return;
            document = playbackDocument;
            var result = await Task.Run(() => TimingAlignment.Apply(document, reference, document.ApproximateTiming ? 8 : 4));
            if (result.Document.Phrases.All(p => p.Match is null) || !ReferenceEquals(playbackDocument, document)) return;
            playbackDocument = result.Document;
            await reviewWrite; await RecordingTranscript.SaveAsync(path, result.Document);
            try { await RecordingLibrary.SaveTextAsync(path, result.Document); } catch (IOException) { }
            highlightedPlaybackPhrase = null; RefreshRowMarks(); if (playbackReader is not null) UpdatePlaybackPosition();
            PlaybackHint.Text = "文字をクリックすると、その発話から再生します。音声と照合した発話は、実際に話された位置でハイライトします。";
            Status($"音声と文章を照合しました：{result.Aligned} / {result.Total} 件の発話に、実際に話された位置の単語時刻を付けました。"
                + (result.Low > 0 ? $" 音声と一致しない部分が多い発話 {result.Low} 件に印（！）を付けています。" : " 音声と大きく食い違う発話はありません。"));
        }
        catch (Exception ex) { Status("音声と文章を照合できませんでした。時刻は推定のまま保存しています: " + ex.Message); }
        finally { run?.Dispose(); }
    }
}
