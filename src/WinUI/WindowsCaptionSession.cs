using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Locaptio.Core;
using Locaptio.Core.AI;

namespace Locaptio.WinUI;
internal sealed class WindowsCaptionSession(string directory, bool japanese, string root, string device, bool cpu, bool useLlm = true, string llmPath = "", string glossary = "") : IDisposable
{
    private Process? worker;
    private readonly LocalBackend translation = new();
    private readonly LocalLlmBackend llm = new();
    public event Action<CaptionUpdate>? Caption;
    public event Action<string>? Status;
    public async Task RunAsync(CancellationToken token)
    {
        string executable = Path.Combine(directory, "bridge", "Locaptio.Bridge.exe");
        if (!File.Exists(executable)) throw new InvalidOperationException("Windows字幕連携用のbridgeフォルダーがありません。更新版のフォルダー全体を使用してください。");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
        async Task InitializeTranslationAsync()
        {
            translation.Status += s => Status?.Invoke(s);
            await translation.StartAsync(root, "translation", cpu || device == "cpu", linked.Token, false, device: device == "cuda" ? "cuda" : "auto");
            Status?.Invoke("速報翻訳の準備完了。英文表示と並行して日本語訳を更新します。");
        }
        async Task InitializeLlmAsync()
        {
            await llm.StartAsync(root, llmPath, cpu || device == "cpu", linked.Token);
            Status?.Invoke("文脈LLM翻訳の準備完了: " + llm.Backend);
        }
        var fastReady = japanese ? Task.CompletedTask : InitializeTranslationAsync();
        var llmReady = japanese || !useLlm ? Task.CompletedTask : InitializeLlmAsync();
        // Observe initialization even if no sentence is finalized before Stop.
        async Task ObserveAsync(Task task, string label) { try { await task; } catch (OperationCanceledException) { } catch (Exception ex) { Status?.Invoke(label + "準備失敗: " + ex.Message); } }
        var fastObserver = ObserveAsync(fastReady, "速報翻訳"); var qualityObserver = ObserveAsync(llmReady, "文脈LLM翻訳");
        var translator = japanese ? null : new StreamingTranslation(new ReadyTranslation(translation, fastReady), useLlm ? new ReadyLocalLlm(llm, llmReady) : null, glossary);
        if (translator is not null) { translator.Caption += update => Caption?.Invoke(update); translator.Status += s => Status?.Invoke(s); }
        var translating = translator?.RunAsync(linked.Token) ?? Task.CompletedTask;
        try
        {
            // Windows belongs to the user session, not to the disposable bridge's
            // process tree. Stopping the bridge must leave Windows captions open.
            var windowsProcesses = Process.GetProcessesByName("LiveCaptions");
            bool windowsRunning = windowsProcesses.Length > 0; foreach (var process in windowsProcesses) process.Dispose();
            if (!windowsRunning) Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "LiveCaptions.exe")) { UseShellExecute = false })?.Dispose();
            worker = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = new UTF8Encoding(false) }) ?? throw new InvalidOperationException("Windows連携を開始できませんでした。");
            worker.ErrorDataReceived += (_, _) => { }; worker.BeginErrorReadLine();
            using var registration = linked.Token.Register(KillWorker);
            var tracker = new WindowsCaptionTracker(japanese); string snapshot = "";
            while (await worker.StandardOutput.ReadLineAsync(linked.Token) is { } line)
            {
                using var message = JsonDocument.Parse(line); var data = message.RootElement;
                string type = data.GetProperty("type").GetString() ?? "";
                if (type == "error") throw new InvalidOperationException(data.GetProperty("text").GetString());
                if (type == "status") { Status?.Invoke(data.GetProperty("text").GetString()!); continue; }
                if (type is "baseline" or "snapshot") snapshot = data.GetProperty("text").GetString() ?? "";
                if (type == "baseline") { tracker.ResetBaseline(snapshot); Status?.Invoke("Windowsライブキャプション接続済み。認識言語はWindows側で設定してください。"); continue; }
                foreach (var update in tracker.Observe(snapshot, Stopwatch.GetTimestamp()))
                {
                    if (translator is null) Caption?.Invoke(update); else translator.Submit(update);
                }
            }
            if (!token.IsCancellationRequested) throw new InvalidOperationException("Windows字幕連携が終了しました。Windowsのライブキャプションが起動しているか確認してください。");
        }
        finally { linked.Cancel(); KillWorker(); try { await translating; } catch (OperationCanceledException) { } await Task.WhenAll(fastObserver, qualityObserver); }
    }
    private void KillWorker() { try { if (worker is { HasExited: false }) worker.Kill(true); } catch (InvalidOperationException) { } }
    private sealed class ReadyTranslation(LocalBackend backend, Task ready) : ITranslationBackend
    {
        public async Task<InferenceResult> TranslateAsync(string text, CancellationToken token, bool final = true) { await ready.WaitAsync(token); return await backend.TranslateAsync(text, token, final); }
    }
    public void Dispose() { KillWorker(); worker?.Dispose(); translation.Dispose(); llm.Dispose(); }
}
