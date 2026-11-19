using Locaptio.Core;
using Locaptio.Core.AI;
using Microsoft.UI.Xaml;

namespace Locaptio.WinUI;

/// <summary>
/// Speaker colors (on by default): the speaker model is prepared automatically the first time, and the feature can
/// be switched on or off while transcribing (the 聞き取り menu or Settings) — no restart needed.
/// </summary>
public sealed partial class MainWindow
{
    private CancellationTokenSource? speakerStop;
    private Task speakerWork = Task.CompletedTask;
    private Pipeline? speakerPipeline;

    /// <summary>Starts speaker recognition for the running session (preparing its model first if needed).</summary>
    private void StartSpeakerRecognition(CancellationToken session)
    {
        if (speakerSession is not null || root.Length == 0) return;
        SpeakerStatusText.Visibility = Visibility.Visible; SpeakerStatusText.Text = "話者識別モデルを準備中…";
        var stop = CancellationTokenSource.CreateLinkedTokenSource(session); speakerStop = stop;
        var recognition = new SpeakerRecognitionSession(new SpeakerEmbeddingBackend(root), settings.SpeakerThreshold);
        speakerSession = recognition;
        recognition.Status += text => DispatcherQueue.TryEnqueue(() => { if (!closing && ReferenceEquals(speakerSession, recognition)) SpeakerStatusText.Text = text; });
        recognition.Changed += () => DispatcherQueue.TryEnqueue(() => { if (!closing && ReferenceEquals(speakerSession, recognition)) RefreshSpeakerAssignments(); });
        var input = SelectedAudio(); bool windows = UseWindowsCaptionEngine;
        speakerWork = Task.Run(async () =>
        {
            // The first use downloads the small speaker model (CAMPPlus + Silero VAD, CPU only).
            if (VerificationRun && !SpeechPreparation.SpeakersReady(root)) throw new InvalidOperationException("（自己チェックでは話者識別モデルをダウンロードしません）");
            try { await SpeechPreparation.EnsureSpeakersAsync(root, p => DispatcherQueue.TryEnqueue(() => { if (ReferenceEquals(speakerSession, recognition)) SpeakerStatusText.Text = "話者識別: " + p.Stage; }), stop.Token); }
            catch (Exception ex) when (ex is not OperationCanceledException) { throw new SpeakerSetupException(ex.Message); }
            var worker = recognition.RunAsync(stop.Token);
            if (windows) await Task.WhenAll(worker, MonitorWindowsSpeakersAsync(recognition, worker, input, stop.Token));
            else await worker;
        });
        _ = ObserveSpeakersAsync(speakerWork, recognition);
        if (pipeline is { } running) { running.AudioFrameCaptured += recognition.Submit; speakerPipeline = running; }
    }
    private async Task ObserveSpeakersAsync(Task work, SpeakerRecognitionSession recognition)
    {
        try { await work; }
        catch (OperationCanceledException) { }
        catch (SpeakerSetupException ex) when (!VerificationRun)
        {
            // Speaker colors are on by default; when the model cannot be prepared (offline, blocked), turn them off
            // instead of trying again at every Start. They can be turned on again in Settings or the 聞き取り menu.
            DispatcherQueue.TryEnqueue(() =>
            {
                if (closing) return;
                settings.SpeakerDiarization = false; settings.Save(); SpeakerMenuItem.IsChecked = false;
                SpeakerStatusText.Text = "話者識別: OFF（モデルを準備できませんでした）";
                Status("話者識別モデルを準備できなかったため、話者の色分けをOFFにしました（ネット接続を確認して、設定か「聞き取り」メニューでONに戻せます）: " + ex.Message);
            });
        }
        catch (Exception ex) { DispatcherQueue.TryEnqueue(() => { if (!closing && ReferenceEquals(speakerSession, recognition)) SpeakerStatusText.Text = "話者識別を開始できません: " + ex.Message; }); }
    }
    /// <summary>Stops speaker recognition; the colors already assigned stay on the rows.</summary>
    private async Task StopSpeakerRecognitionAsync(string message = "話者識別: 停止")
    {
        var recognition = speakerSession; if (recognition is null) return;
        if (speakerPipeline is { } attached) { attached.AudioFrameCaptured -= recognition.Submit; speakerPipeline = null; }
        speakerStop?.Cancel();
        try { await speakerWork; } catch (Exception) { }
        recognition.Dispose(); if (ReferenceEquals(speakerSession, recognition)) speakerSession = null;
        speakerStop?.Dispose(); speakerStop = null;
        if (SpeakerStatusText.Visibility == Visibility.Visible) SpeakerStatusText.Text = message;
    }
    /// <summary>Speaker colors on/off, applied at once (also while transcribing).</summary>
    private async void SetSpeakerDiarization(bool on)
    {
        settings.SpeakerDiarization = on; settings.Save();
        SpeakerMenuItem.IsChecked = on;
        if (captionStop is { IsCancellationRequested: false } session)
        {
            if (on) { StartSpeakerRecognition(session.Token); Status("話者の色分けをONにしました。これからの発話を話者ごとに色分けします。"); }
            else { await StopSpeakerRecognitionAsync("話者識別: OFF"); Status("話者の色分けをOFFにしました。"); }
        }
        RenderCaptions();
    }
    private void SpeakerMenuClick(object sender, RoutedEventArgs e) => SetSpeakerDiarization(SpeakerMenuItem.IsChecked);
}

/// <summary>The speaker model could not be prepared (download or install failed).</summary>
internal sealed class SpeakerSetupException(string message) : Exception(message);
