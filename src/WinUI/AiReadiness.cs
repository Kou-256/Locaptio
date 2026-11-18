using Locaptio.Core.AI;
using Microsoft.UI.Xaml;

namespace Locaptio.WinUI;

/// <summary>
/// Keeps the AI features ready before Start, and the summary pane in step with live summary:
/// - checking 「AI補正」 or 「ライブ要約」 (or starting with either already on) downloads the local LLM right away
///   when it is missing, and with live summary also loads it, so the first minutes are not spent preparing;
/// - live summary ON shows the pane when listening starts; turning it OFF or clearing hides a pane that has
///   nothing else to show; a summary that arrives reopens it.
/// </summary>
public sealed partial class MainWindow
{
    private CancellationTokenSource? aiPreparation;
    /// <summary>The pane was opened only because live summary is on (so it may close again on its own).</summary>
    private bool liveSummaryAutoShown;
    /// <summary>The user cleared or closed the pane while listening; it stays closed until the next summary.</summary>
    private bool liveSummaryDismissed;
    /// <summary>Self-checks: the download the background preparation would start.</summary>
    private Func<CancellationToken, Task>? aiPreparationForVerification;
    private Task aiPreparationTask = Task.CompletedTask;

    private bool HasSummaryContent => summaryItems.Length > 0 || liveBlocks.Count > 0 || liveDigest is not null;

    /// <summary>Shows or hides the pane to match live summary and listening, without touching a pane the user opened for other summaries.</summary>
    private void SyncLiveSummaryPane()
    {
        if (closing) return;
        bool listening = captionStop is not null || recordingStop is not null;
        if (settings.LiveSummary && listening && !liveSummaryDismissed)
        {
            if (!SummaryShown) { ShowSummaryPane(true); liveSummaryAutoShown = true; }
            RenderSummary();
            return;
        }
        // Speech still to be summarized when listening stops is flushed shortly; keep the pane for it.
        bool pending = settings.LiveSummary && history.Any(r => r.Id >= liveSummaryNextId && r.Caption.Final && r.English.Trim().Length > 0);
        if (SummaryShown && liveSummaryAutoShown && !HasSummaryContent && !pending) ShowSummaryPane(false);
        if (!SummaryShown) liveSummaryAutoShown = false;
        else RenderSummary();
    }

    /// <summary>Starts downloading (and for live summary, loading) the local LLM in the background when an AI feature is on.</summary>
    /// <param name="loadNow">Also load the model for live summary (when it is checked, or listening starts) rather than only downloading it.</param>
    private void PrepareAiInBackground(bool loadNow = true)
    {
        if (closing || !(settings.CorrectTranscriptWithAi || settings.LiveSummary)) { aiPreparation?.Cancel(); return; }
        if (!aiPreparationTask.IsCompleted) return;
        // Self-checks never download or load the real model.
        if (VerificationRun && aiPreparationForVerification is null) return;
        root = FindRoot(); if (root.Length == 0) return;
        bool missing = aiPreparationForVerification is not null || LocalLlmPreparation.Required(root, LlmModelFile);
        // Live summary loads its own model; AI correction loads with Start (Whisper) or on first use (Windows captions).
        // With AI correction on, the summary reuses that model instead of holding a second copy in memory.
        bool load = loadNow && settings.LiveSummary && !settings.CorrectTranscriptWithAi && !VerificationRun && liveSummaryBackendForVerification is null && toolLlm is null;
        if (!missing && !load) return;
        aiPreparation?.Dispose(); aiPreparation = new CancellationTokenSource();
        var token = aiPreparation.Token;
        aiPreparationTask = RunAsync();
        async Task RunAsync()
        {
            try
            {
                if (missing)
                {
                    Status("AI機能の準備を始めました（初回のみ、AIモデル数GBをダウンロードします）。完了するとすぐに使えます。");
                    if (aiPreparationForVerification is not null) await aiPreparationForVerification(token);
                    else await LocalLlmPreparation.EnsureAsync(root, LlmModelFile, settings.CpuOnly || settings.InferenceDevice == "cpu", BeginPreparation(), token);
                }
                if (load && settings.LiveSummary && !closing)
                {
                    await SummaryLlmAsync(token);
                    toolLlmUsed = System.Diagnostics.Stopwatch.GetTimestamp();
                }
                if (!closing && missing) Status("AI機能の準備が完了しました。" + (captionStop is null ? "開始するとすぐにAI補正・ライブ要約を使えます。" : ""));
                liveSummaryNoModelShown = false;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (!closing) { Status("AI機能を準備できませんでした（開始時にもう一度試します）: " + ex.Message); }
        }
    }
}
