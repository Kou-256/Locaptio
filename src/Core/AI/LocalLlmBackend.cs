using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Locaptio.Core.AI;
public interface ITranslationRefinementBackend
{
    Task<InferenceResult> RefineAsync(string source, CancellationToken token);
    Task<InferenceResult> RefineAsync(string source, string[] context, string glossary, CancellationToken token) => RefineAsync(source, token);
}
public interface ISpeechCorrectionBackend
{
    Task<InferenceResult> CorrectAsync(string source, string[] context, string language, CancellationToken token);
}
public sealed class ReadyLocalLlm(LocalLlmBackend backend, Task ready) : ITranslationRefinementBackend, ISpeechCorrectionBackend
{
    public async Task<InferenceResult> RefineAsync(string source, CancellationToken token) { await ready.WaitAsync(token); return await backend.RefineAsync(source, token); }
    public async Task<InferenceResult> RefineAsync(string source, string[] context, string glossary, CancellationToken token) { await ready.WaitAsync(token); return await backend.RefineAsync(source, context, glossary, token); }
    public async Task<InferenceResult> CorrectAsync(string source, string[] context, string language, CancellationToken token) { await ready.WaitAsync(token); return await backend.CorrectAsync(source, context, language, token); }
}

public sealed class LocalLlmBackend : ITranslationRefinementBackend, ISpeechCorrectionBackend, ISummaryBackend, IDisposable
{
    /// <summary>Registered spellings from the user's term dictionary, sent with every correction.</summary>
    public string CorrectionGlossary { get; set; } = "";
    private Process? process;
    private readonly SemaphoreSlim serial = new(1);
    private int id;
    private string runtimeRoot = "", configuredModel = "";
    private bool configuredCpu;
    public string Backend { get; private set; } = "未初期化";
    public event Action<PreparationProgress>? PreparationChanged;
    public async Task StartAsync(string root, string modelPath, bool cpu, CancellationToken token)
    {
        runtimeRoot = root; configuredModel = modelPath; configuredCpu = cpu;
        PreparationChanged?.Invoke(new("llm", 0, 2, "LLM環境を確認中"));
        Kill(); process?.Dispose(); process = null;
        var python = Path.Combine(root, ".venv-llm", "Scripts", "python.exe");
        if (string.IsNullOrWhiteSpace(modelPath)) modelPath = Path.Combine(root, "models", "llm", "Qwen3-4B-Q4_K_M.gguf");
        await LocalLlmPreparation.EnsureAsync(root, modelPath, cpu, p => PreparationChanged?.Invoke(p), token);
        try { await LaunchAsync(root, python, modelPath, cpu, token); }
        catch (InvalidOperationException ex) when (LocalLlmPreparation.IsLibraryLoadFailure(ex.Message))
        {
            // Usually no Vulkan driver (vulkan-1.dll) on this PC: the CPU build of llama.cpp needs none.
            // The worker may still hold llama.cpp's DLLs it did load; they must be free before they are replaced.
            var failed = process; Kill();
            if (failed is not null) { try { await failed.WaitForExitAsync(token).WaitAsync(TimeSpan.FromSeconds(10), token); } catch (TimeoutException) { } failed.Dispose(); }
            process = null;
            PreparationChanged?.Invoke(new("llm", 0, 2, "GPU用のAIライブラリを読み込めないため、CPU版に切り替えています（初回のみ）"));
            await LocalLlmPreparation.UseCpuLibraryAsync(root, modelPath, p => PreparationChanged?.Invoke(p), token);
            configuredCpu = true;
            await LaunchAsync(root, python, modelPath, true, token);
        }
    }
    private async Task LaunchAsync(string root, string python, string modelPath, bool cpu, CancellationToken token)
    {
        var start = new ProcessStartInfo(python) { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = new UTF8Encoding(false), StandardInputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false) };
        foreach (string arg in new[] { "-u", Path.Combine(root, "inference", "llm_worker.py"), "--model", modelPath }) start.ArgumentList.Add(arg);
        if (cpu) start.ArgumentList.Add("--cpu");
        start.Environment["PATH"] = LocalLlmPreparation.RuntimePath(root) + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        process = new() { StartInfo = start }; process.ErrorDataReceived += (_, _) => { }; process.Start(); process.BeginErrorReadLine();
        PreparationChanged?.Invoke(new("llm", 1, 2, "LLMモデルを読み込み中"));
        using var registration = token.Register(Kill);
        using var response = await ReadAsync(token);
        if (response.RootElement.GetProperty("type").GetString() != "ready") throw new InvalidOperationException(response.RootElement.GetProperty("message").GetString());
        Backend = response.RootElement.GetProperty("backend").GetString()!;
        PreparationChanged?.Invoke(new("llm", 2, 2, "準備完了"));
    }
    public Task<InferenceResult> RefineAsync(string source, CancellationToken token) => RequestAsync(new { mode = "translate", text = source }, token);
    public Task<InferenceResult> RefineAsync(string source, string[] context, string glossary, CancellationToken token) => RequestAsync(new { mode = "translate", text = source, context, glossary }, token);
    /// <summary>Corrections refused because they dropped or invented words (the original is kept).</summary>
    public int RefusedCorrections { get; private set; }
    public async Task<InferenceResult> CorrectAsync(string source, string[] context, string language, CancellationToken token)
    {
        var result = await RequestAsync(new { mode = "correct", text = source, context, language, glossary = CorrectionGlossary }, token);
        // The model sometimes drops a clause it found redundant; what was said must stay.
        if (CorrectionGuard.Accept(source, result.Text.Trim(), out _)) return result;
        RefusedCorrections++;
        return result with { Text = source };
    }
    // A summary reads far more text than one caption, so it gets a longer limit than live requests.
    public Task<InferenceResult> SummarizeAsync(string numberedTranscript, CancellationToken token) => RequestAsync(new { mode = "summarize", text = numberedTranscript }, token, 240);
    private async Task<InferenceResult> RequestAsync(object request, CancellationToken token, int seconds = 30)
    {
        await serial.WaitAsync(token);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
            if (process is null || process.HasExited) await StartAsync(runtimeRoot, configuredModel, configuredCpu, timeout.Token);
            using var registration = timeout.Token.Register(Kill);
            var payload = JsonSerializer.SerializeToNode(request)!; payload["id"] = ++id;
            await process!.StandardInput.WriteLineAsync(payload.ToJsonString().AsMemory(), timeout.Token);
            await process.StandardInput.FlushAsync(timeout.Token);
            using var response = await ReadAsync(timeout.Token);
            if (response.RootElement.GetProperty("type").GetString() != "result") throw new InvalidOperationException(response.RootElement.GetProperty("message").GetString());
            return new(response.RootElement.GetProperty("text").GetString()!, response.RootElement.GetProperty("ms").GetDouble());
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException($"ローカルAIの処理が{seconds}秒以内に完了しませんでした。" + (seconds == 30 ? "認識結果・標準訳を保持します。" : "文章は変更していません。")); }
        finally { serial.Release(); }
    }
    private async Task<JsonDocument> ReadAsync(CancellationToken token)
    {
        var line = await process!.StandardOutput.ReadLineAsync(token);
        if (line is null) throw new InvalidOperationException("ローカルLLM workerが終了しました。");
        return JsonDocument.Parse(line);
    }
    private void Kill() { try { if (process is { HasExited: false }) process.Kill(true); } catch (InvalidOperationException) { } }
    public void Dispose() { Kill(); process?.Dispose(); }
}
