using System.Diagnostics;
using System.Text;

namespace Locaptio.Core.AI;

public static class SpeechPreparation
{
    /// <summary>Serializes everything that installs into .venv (speech setup and translation packs).</summary>
    public static readonly SemaphoreSlim EnvironmentGate = new(1);
    private static bool PythonReady(string root) => File.Exists(Path.Combine(root, ".venv", "Scripts", "python.exe")) && File.Exists(Path.Combine(root, ".venv", ".locaptio-speech-v2"));

    /// <summary>Only what a translation worker needs (Windows-captions mode): no speech model is downloaded.</summary>
    public static async Task EnsureTranslationAsync(string root, bool builtIn, Action<PreparationProgress>? progress, CancellationToken token)
    {
        bool Ready() => PythonReady(root) && (!builtIn || File.Exists(Path.Combine(root, "models", "translation", "model.bin")));
        if (Ready()) return;
        await WaitAsync(progress, token);
        try { if (!Ready()) await RunAsync(root, "setup-speech.ps1", ["-TranslationOnly"], progress, token); }
        finally { EnvironmentGate.Release(); }
    }
    public static bool SpeakersReady(string root) => File.Exists(Path.Combine(root, ".venv-speakers", "Scripts", "python.exe"))
        && File.Exists(Path.Combine(root, "models", "speakers", "embedding.onnx")) && File.Exists(Path.Combine(root, "models", "speakers", "silero_vad.onnx"));
    /// <summary>Prepares the speaker-color model (scripts/setup-speakers.ps1) the first time it is used.</summary>
    public static async Task EnsureSpeakersAsync(string root, Action<PreparationProgress>? progress, CancellationToken token)
    {
        if (SpeakersReady(root)) return;
        await WaitAsync(progress, token);
        try { if (!SpeakersReady(root)) await RunAsync(root, "setup-speakers.ps1", [], progress, token); }
        finally { EnvironmentGate.Release(); }
    }
    internal static async Task WaitAsync(Action<PreparationProgress>? progress, CancellationToken token)
    {
        if (EnvironmentGate.Wait(0)) return;
        progress?.Invoke(new("音声環境の自動準備", 0, 1, "実行中の別の準備（翻訳モデルのダウンロードなど）が終わるのを待っています。"));
        await EnvironmentGate.WaitAsync(token);
    }
    public static async Task EnsureAsync(string root, string model, bool translation, string device, Action<PreparationProgress>? progress, CancellationToken token)
    {
        if (model is not ("tiny" or "base" or "small" or "medium" or "large-v3-turbo"))
            throw new ArgumentException("音声モデルを設定で選び直してください。", nameof(model));
        await WaitAsync(progress, token);
        try
        {
            bool python = PythonReady(root);
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
        finally { EnvironmentGate.Release(); }
    }
    private static async Task RunAsync(string root, string script, string[] args, Action<PreparationProgress>? progress, CancellationToken token)
    {
        string file = Path.Combine(root, "scripts", script);
        if (!File.Exists(file)) throw new FileNotFoundException("自動準備ファイルがありません。最新版のSetupで再インストールしてください。", file);
        progress?.Invoke(new("音声環境の自動準備", 0, 1, "初回のみダウンロードします。ネット接続と数GBの空き容量が必要です。Stopで中止できます。"));
        string tools = await LocalLlmPreparation.EnsureUvAsync(root, token);
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", file }.Concat(args)) start.ArgumentList.Add(arg);
        start.Environment["PATH"] = tools + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        using var process = Process.Start(start) ?? throw new IOException("モデル準備を開始できません。");
        using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        Queue<string> tail = new(); object sync = new();
        Task Drain(StreamReader reader) => ProcessText.ReadLinesAsync(reader.BaseStream, line => { lock (sync) { tail.Enqueue(line); while (tail.Count > 20) tail.Dequeue(); } }, token);
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
