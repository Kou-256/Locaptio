using System.Diagnostics;
using System.Text;

namespace Locaptio.Core.AI;

public static class SpeechPreparation
{
    private static readonly SemaphoreSlim gate = new(1);
    public static async Task EnsureAsync(string root, string model, bool translation, string device, Action<PreparationProgress>? progress, CancellationToken token)
    {
        if (model is not ("tiny" or "base" or "small" or "medium" or "large-v3-turbo"))
            throw new ArgumentException("音声モデルを設定で選び直してください。", nameof(model));
        await gate.WaitAsync(token);
        try
        {
            bool python = File.Exists(Path.Combine(root, ".venv", "Scripts", "python.exe")) && File.Exists(Path.Combine(root, ".venv", ".locaptio-speech-v2"));
            bool speech = File.Exists(Path.Combine(root, "models", "speech-" + model, "model.bin"));
            bool translate = File.Exists(Path.Combine(root, "models", "translation", "model.bin"));
            if (!python || !speech || translation && !translate)
                await RunAsync(root, "setup-speech.ps1", translation ? ["-Model", model, "-Translation"] : ["-Model", model], progress, token);
            if (device == "cuda" && !Directory.Exists(Path.Combine(root, "runtime", "cuda", "bin")))
                await RunAsync(root, "setup-cuda.ps1", [], progress, token);
            if (device == "npu" && (!File.Exists(Path.Combine(root, ".venv-winml", "Scripts", "python.exe")) ||
                new[] { "encoder_model.onnx", "decoder_model.onnx", "vitisai_config_whisper_encoder.json", "vitisai_config_whisper_decoder.json" }
                    .Any(file => !File.Exists(Path.Combine(root, "models", "speech-amd-npu", file)))))
                await RunAsync(root, "setup-amd-speech.ps1", [], progress, token);
        }
        finally { gate.Release(); }
    }
    private static async Task RunAsync(string root, string script, string[] args, Action<PreparationProgress>? progress, CancellationToken token)
    {
        string file = Path.Combine(root, "scripts", script);
        if (!File.Exists(file)) throw new FileNotFoundException("自動準備ファイルがありません。最新版のSetupで再インストールしてください。", file);
        progress?.Invoke(new("音声環境の自動準備", 0, 1, "初回のみダウンロードします。ネット接続と数GBの空き容量が必要です。Stopで中止できます。"));
        string tools = await LocalLlmPreparation.EnsureUvAsync(root, token);
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false) };
        foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", file }.Concat(args)) start.ArgumentList.Add(arg);
        start.Environment["PATH"] = tools + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        using var process = Process.Start(start) ?? throw new IOException("モデル準備を開始できません。");
        using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        Queue<string> tail = new(); object sync = new();
        async Task Drain(StreamReader reader)
        {
            while (await reader.ReadLineAsync(token) is { } line)
                lock (sync) { tail.Enqueue(line); while (tail.Count > 20) tail.Dequeue(); }
        }
        await Task.WhenAll(Drain(process.StandardOutput), Drain(process.StandardError), process.WaitForExitAsync(token));
        token.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
        {
            string detail = string.Join("\n", tail);
            throw new IOException((detail.Contains("os error 448", StringComparison.OrdinalIgnoreCase)
                ? "WindowsがPython環境の参照先へのアクセスを拒否しました（マウントポイントの信頼性エラー）。環境の自動修復を完了できませんでした。\n"
                : "音声環境の自動準備に失敗しました。\n") + detail);
        }
        progress?.Invoke(new("音声環境の自動準備", 1, 1, "ダウンロード完了"));
    }
}
