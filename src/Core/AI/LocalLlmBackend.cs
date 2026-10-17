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

public sealed class LocalLlmBackend : ITranslationRefinementBackend, ISpeechCorrectionBackend, IDisposable
{
    private Process? process;
    private readonly SemaphoreSlim serial = new(1);
    private int id;
    private string runtimeRoot = "", configuredModel = "";
    private bool configuredCpu;
    public string Backend { get; private set; } = "未初期化";
    public async Task StartAsync(string root, string modelPath, bool cpu, CancellationToken token)
    {
        runtimeRoot = root; configuredModel = modelPath; configuredCpu = cpu;
        Kill(); process?.Dispose(); process = null;
        var python = Path.Combine(root, ".venv-llm", "Scripts", "python.exe");
        if (!File.Exists(python)) throw new InvalidOperationException("LLM環境がありません。scripts/setup-llm.ps1を実行してください。");
        if (string.IsNullOrWhiteSpace(modelPath)) modelPath = Path.Combine(root, "models", "llm", "Qwen3-4B-Q4_K_M.gguf");
        var start = new ProcessStartInfo(python) { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = new UTF8Encoding(false), StandardInputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false) };
        foreach (string arg in new[] { "-u", Path.Combine(root, "inference", "llm_worker.py"), "--model", modelPath }) start.ArgumentList.Add(arg);
        if (cpu) start.ArgumentList.Add("--cpu");
        process = new() { StartInfo = start }; process.ErrorDataReceived += (_, _) => { }; process.Start(); process.BeginErrorReadLine();
        using var registration = token.Register(Kill);
        using var response = await ReadAsync(token);
        if (response.RootElement.GetProperty("type").GetString() != "ready") throw new InvalidOperationException(response.RootElement.GetProperty("message").GetString());
        Backend = response.RootElement.GetProperty("backend").GetString()!;
    }
    public Task<InferenceResult> RefineAsync(string source, CancellationToken token) => RequestAsync(new { mode = "translate", text = source }, token);
    public Task<InferenceResult> RefineAsync(string source, string[] context, string glossary, CancellationToken token) => RequestAsync(new { mode = "translate", text = source, context, glossary }, token);
    public Task<InferenceResult> CorrectAsync(string source, string[] context, string language, CancellationToken token) => RequestAsync(new { mode = "correct", text = source, context, language }, token);
    private async Task<InferenceResult> RequestAsync(object request, CancellationToken token)
    {
        await serial.WaitAsync(token);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
            if (process is null || process.HasExited) await StartAsync(runtimeRoot, configuredModel, configuredCpu, timeout.Token);
            using var registration = timeout.Token.Register(Kill);
            var payload = JsonSerializer.SerializeToNode(request)!; payload["id"] = ++id;
            await process!.StandardInput.WriteLineAsync(payload.ToJsonString().AsMemory(), timeout.Token);
            await process.StandardInput.FlushAsync(timeout.Token);
            using var response = await ReadAsync(timeout.Token);
            if (response.RootElement.GetProperty("type").GetString() != "result") throw new InvalidOperationException(response.RootElement.GetProperty("message").GetString());
            return new(response.RootElement.GetProperty("text").GetString()!, response.RootElement.GetProperty("ms").GetDouble());
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("ローカルAIの処理が30秒以内に完了しませんでした。認識結果・標準訳を保持します。"); }
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
