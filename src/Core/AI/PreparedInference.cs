namespace Locaptio.Core.AI;

// Workers are kept alive between listening sessions. Preparing them never captures audio.
public sealed class PreparedInference : IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    public LocalBackend Speech { get; } = new();
    public LocalBackend? Preview { get; }
    public LocalBackend LiveSpeech => Preview ?? Speech;
    public Task AccuracyReady { get; }
    public LocalBackend? Translation { get; }
    public LocalLlmBackend? Llm { get; }
    public Task Ready { get; }
    public Task? LlmReady { get; }
    public bool IsUsable => !Ready.IsFaulted && !Ready.IsCanceled && !AccuracyReady.IsFaulted && !AccuracyReady.IsCanceled
        && (!AccuracyReady.IsCompleted || Speech.ProcessId is not null)
        && (!Ready.IsCompleted || LiveSpeech.ProcessId is not null && (Translation is null || Translation.ProcessId is not null));
    public PreparedInference(string root, bool japanese, string model, string prompt, string device, bool cpu, bool fast, bool useLlm, string llmPath, Action<string> status, bool realtime = false, bool previewCpu = true, string previewModel = "base", Action<PreparationProgress>? progress = null)
    {
        lifetime.CancelAfter(TimeSpan.FromMinutes(device == "npu" ? 30 : 3));
        previewModel = previewModel == "tiny" && File.Exists(Path.Combine(root, "models", "speech-tiny", "model.bin")) ? "tiny" : "base";
        Speech.Status += status;
        if (realtime && fast && !cpu && (device is "cuda" or "gpu") && model is not ("base" or "tiny") && File.Exists(Path.Combine(root, "models", "speech-" + previewModel, "model.bin")))
        {
            Preview = new(); Preview.Status += status;
        }
        if (!japanese) { Translation = new(); Translation.Status += status; }
        void Track(LocalBackend backend, string label)
        {
            progress?.Invoke(new(label, 0, 1, "待機中"));
            backend.PreparationChanged += p => progress?.Invoke(p with { Component = label });
        }
        Track(Speech, "音声認識");
        if (Preview is not null) Track(Preview, "速報モデル");
        if (Translation is not null) Track(Translation, "翻訳モデル");
        if (useLlm) progress?.Invoke(new("文脈AI", 0, 2, "待機中"));
        if (useLlm)
        {
            Llm = new(); Llm.PreparationChanged += p => progress?.Invoke(p with { Component = "文脈AI" }); LlmReady = Llm.StartAsync(root, llmPath, cpu, lifetime.Token);
            _ = ObserveLlmAsync(status);
        }
        AccuracyReady = Speech.StartAsync(root, "speech", cpu, lifetime.Token, false, japanese ? "ja" : "en", model, prompt, cpu ? "cpu" : device, fast);
        var live = Preview is null ? AccuracyReady : Preview.StartAsync(root, "speech", previewCpu, lifetime.Token, false, japanese ? "ja" : "en", previewModel, prompt, previewCpu ? "cpu" : device, fast, draft: true);
        Ready = Translation is null ? live : Task.WhenAll(live, Translation.StartAsync(root, "translation", cpu, lifetime.Token, false, device: device == "cuda" ? "cuda" : "auto"));
        _ = ObserveReadyAsync();
        _ = ObserveProgressAsync();
        async Task ObserveProgressAsync()
        {
            try { await Task.WhenAll(Ready, AccuracyReady); if (LlmReady is not null) await LlmReady; }
            catch (OperationCanceledException) { }
            catch (Exception ex) { progress?.Invoke(new("準備エラー", 0, 1, ex.Message, true)); }
        }
    }
    private async Task ObserveReadyAsync()
    {
        try { await Task.WhenAll(Ready, AccuracyReady); if (LlmReady is not null) await LlmReady; lifetime.CancelAfter(Timeout.InfiniteTimeSpan); }
        catch { /* Consumers and the UI report readiness errors. */ }
    }
    private async Task ObserveLlmAsync(Action<string> status)
    {
        try { await LlmReady!; status("ローカルAI準備完了。文脈補正を使用できます。"); }
        catch (Exception ex) when (ex is not OperationCanceledException) { status("ローカルAI準備失敗: " + ex.Message); }
        catch (OperationCanceledException) { }
    }
    public void Dispose()
    {
        lifetime.Cancel(); Preview?.Dispose(); Speech.Dispose(); Translation?.Dispose(); Llm?.Dispose();
        // Pending initialization registrations may still be using the token source.
        _ = ReleaseAsync();
    }
    private async Task ReleaseAsync()
    {
        try { await Task.WhenAll(Ready, AccuracyReady); if (LlmReady is not null) await LlmReady; } catch { }
        lifetime.Dispose();
    }
}

public sealed class ReadySpeechBackend(LocalBackend backend, Task ready) : ISpeechRecognitionBackend
{
    public async Task<InferenceResult> RecognizeAsync(float[] samples, CancellationToken token, bool final = true)
    {
        await ready.WaitAsync(token); return await backend.RecognizeAsync(samples, token, final);
    }
}
