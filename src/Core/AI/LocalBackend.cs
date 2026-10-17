using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Locaptio.Core.AI;

public sealed record InferenceResult(string Text, double Milliseconds);
public interface ISpeechRecognitionBackend { Task<InferenceResult> RecognizeAsync(float[] samples, CancellationToken token, bool final = true); }
public interface ITranslationBackend { Task<InferenceResult> TranslateAsync(string text, CancellationToken token, bool final = true); }

public sealed class LocalBackend : ISpeechRecognitionBackend, ITranslationBackend, IDisposable
{
    private Process? process;
    private Process? inferenceProcess;
    private int sequence;
    private string modelRoot = "", modelKind = "";
    private bool cpuOnly;
    private string language = "en", speechModel = "legacy", speechPrompt = "";
    private string accelerator = "auto";
    private bool fastRecognition;
    private bool draftRecognition;
    private int resetSpeechContext;
    public void ResetSpeechContext() => Interlocked.Exchange(ref resetSpeechContext, 1);
    private readonly SemaphoreSlim serial = new(1);
    public string Backend { get; private set; } = "未初期化";
    public string AvailableProviders { get; private set; } = "—";
    public string Model { get; private set; } = "—";
    public int? ProcessId => process is { HasExited: false } ? process.Id : null;
    public double CpuMilliseconds { get { try { return inferenceProcess?.TotalProcessorTime.TotalMilliseconds ?? 0; } catch { return 0; } } }
    public event Action<string>? Status;

    public async Task StartAsync(string root, string kind, bool cpu, CancellationToken token, bool preferNpu = true, string speechLanguage = "en", string model = "legacy", string prompt = "", string device = "auto", bool fast = false, bool draft = false)
    {
        language = speechLanguage; speechModel = model; speechPrompt = prompt;
        accelerator = device; fastRecognition = fast; draftRecognition = draft;
        try { await StartProcessAsync(root, kind, cpu, token, preferNpu); }
        catch (Exception ex) when (!cpu && accelerator is not ("npu" or "cuda") && ex is not OperationCanceledException)
        {
            Kill(); process?.Dispose(); process = null;
            Status?.Invoke("アクセラレーター初期化失敗、代替バックエンドへ切替: " + ex.Message);
            try { await StartProcessAsync(root, kind, false, token, false); }
            catch (Exception fallback) when (fallback is not OperationCanceledException)
            {
                Kill(); process?.Dispose(); process = null;
                Status?.Invoke("GPU初期化失敗、CPUへ切替: " + fallback.Message);
                await StartProcessAsync(root, kind, true, token, false);
            }
        }
    }
    private async Task StartProcessAsync(string root, string kind, bool cpu, CancellationToken token, bool preferNpu)
    {
        modelRoot = root; modelKind = kind; cpuOnly = cpu;
        inferenceProcess?.Dispose(); inferenceProcess = null;
        Backend = cpu ? "CPU 初期化中" : accelerator == "npu" && kind == "speech" ? "AMD NPU 準備中" : "GPU/CPU 確認中";
        bool directMl = accelerator != "cuda" && kind == "translation" && File.Exists(Path.Combine(root, "models", "translation-onnx", "encoder_model.onnx")) && File.Exists(Path.Combine(root, ".venv-dml", "Scripts", "python.exe"));
        bool windowsMl = accelerator != "cuda" && !cpu && preferNpu && kind == "translation" && File.Exists(Path.Combine(root, "models", "translation-onnx", "encoder_model.onnx")) && File.Exists(Path.Combine(root, ".venv-winml", "Scripts", "python.exe"));
        bool npuSpeech = accelerator == "npu" && kind == "speech";
        var python = Path.Combine(root, windowsMl || npuSpeech ? ".venv-winml" : directMl ? ".venv-dml" : ".venv", "Scripts", "python.exe");
        if (!File.Exists(python)) throw new InvalidOperationException("Python環境がありません。scripts/setup.ps1を実行してください。");
        var start = new ProcessStartInfo(python) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = new UTF8Encoding(false), StandardInputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false), WorkingDirectory = root };
        if (npuSpeech)
        {
            foreach (var arg in new[] { "-u", Path.Combine(root, "inference", "amd_speech_worker.py"), "--models", Path.Combine(root, "models"), "--language", language }) start.ArgumentList.Add(arg);
        }
        else foreach (var arg in new[] { "-u", Path.Combine(root, "inference", "worker.py"), "--kind", kind, "--models", Path.Combine(root, "models"), "--device", cpu ? "cpu" : accelerator == "cuda" ? "cuda" : "auto" }) start.ArgumentList.Add(arg);
        if (windowsMl) start.ArgumentList.Add("--prefer-npu");
        if (kind == "speech" && !npuSpeech)
        {
            foreach (var arg in new[] { "--language", language, "--speech-model", speechModel }) start.ArgumentList.Add(arg);
            if (fastRecognition) start.ArgumentList.Add("--fast");
            if (draftRecognition) start.ArgumentList.Add("--draft");
        }
        start.Environment["HF_HUB_OFFLINE"] = "1";
        start.Environment["TRANSFORMERS_OFFLINE"] = "1";
        process = new Process { StartInfo = start };
        // Drain stderr without retaining audio or caption content.
        process.ErrorDataReceived += (_, _) => { };
        process.Start(); process.BeginErrorReadLine();
        using var registration = token.Register(() => Kill());
        while (true)
        {
            using var response = await ReadAsync(token);
            var item = response.RootElement;
            string? type = item.GetProperty("type").GetString();
            if (type == "ready") { Backend = item.GetProperty("backend").GetString()!; Model = item.TryGetProperty("model", out var model) ? model.GetString()! : "—"; AvailableProviders = string.Join(", ", item.GetProperty("providers").EnumerateArray().Select(p => p.GetString())); inferenceProcess = Process.GetProcessById(item.GetProperty("pid").GetInt32()); return; }
            if (type == "error") throw new InvalidOperationException(item.GetProperty("message").GetString());
            if (type == "status") Status?.Invoke(item.GetProperty("message").GetString()!);
        }
    }

    private async Task<JsonDocument> ReadAsync(CancellationToken token)
    {
        while (true)
        {
            string? line = await process!.StandardOutput.ReadLineAsync(token);
            if (line is null) throw new InvalidOperationException("ローカル推論プロセスが終了しました。");
            try
            {
                var document = JsonDocument.Parse(line);
                if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String) return document;
                document.Dispose();
                if (accelerator != "npu") throw new InvalidOperationException("推論workerの応答形式が不正です。");
            }
            catch (JsonException) when (accelerator == "npu") { /* AMD's native compiler writes diagnostics to stdout. */ }
        }
    }

    public Task<InferenceResult> RecognizeAsync(float[] samples, CancellationToken token, bool final = true)
    {
        byte[] bytes = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return RequestAsync(new { id = Interlocked.Increment(ref sequence), audio = Convert.ToBase64String(bytes), final, prompt = speechPrompt, reset_context = Interlocked.Exchange(ref resetSpeechContext, 0) != 0 }, token);
    }
    public Task<InferenceResult> TranslateAsync(string text, CancellationToken token, bool final = true) => RequestAsync(new { id = Interlocked.Increment(ref sequence), text }, token);
    private async Task<InferenceResult> RequestAsync(object request, CancellationToken token)
    {
        await serial.WaitAsync(token);
        try
        {
            try { return await SendWithTimeoutAsync(request, token); }
            catch (Exception ex) when (accelerator is not ("npu" or "cuda") && ex is not OperationCanceledException && (!cpuOnly || process is null || process.HasExited))
            {
                Kill(); process?.Dispose(); process = null;
                Status?.Invoke("推論worker障害、CPUで再試行: " + ex.Message);
                await StartAsync(modelRoot, modelKind, true, token, false, language, speechModel, speechPrompt, "cpu", fastRecognition, draftRecognition);
                return await SendWithTimeoutAsync(request, token);
            }
        }
        finally { serial.Release(); }
    }
    private async Task<InferenceResult> SendWithTimeoutAsync(object request, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try { return await SendAsync(request, timeout.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("ローカル推論が30秒以内に完了しませんでした。"); }
    }
    private async Task<InferenceResult> SendAsync(object request, CancellationToken token)
    {
            using var registration = token.Register(() => Kill());
            await process!.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), token);
            await process.StandardInput.FlushAsync(token);
            while (true)
            {
                using var response = await ReadAsync(token);
                var item = response.RootElement;
                string? type = item.GetProperty("type").GetString();
                if (type == "result") return new(item.GetProperty("text").GetString()!, item.GetProperty("ms").GetDouble());
                if (type == "error") throw new InvalidOperationException(item.GetProperty("message").GetString());
                if (type == "status") { if (item.TryGetProperty("backend", out var b)) Backend = b.GetString()!; Status?.Invoke(item.GetProperty("message").GetString()!); }
            }
    }
    private void Kill() { try { if (process is { HasExited: false }) process.Kill(true); } catch (InvalidOperationException) { } }
    public void Dispose() { Kill(); process?.Dispose(); inferenceProcess?.Dispose(); }
}
