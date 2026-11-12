using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace Locaptio.Core.AI;

/// <summary>Runs the bundled setup, without changing the machine's execution policy or PATH.</summary>
public static class LocalLlmPreparation
{
    private static readonly SemaphoreSlim gate = new(1);
    public const string UvVersion = "0.13.7";
    public const string UvSha256 = "0cdc5b38fe5a8168ed6a25814f5ba43c21d313dd3187314fccb37b7cec46e021";
    public static bool Required(string root, string model) => !File.Exists(Path.Combine(root, ".venv-llm", "Scripts", "python.exe")) || !File.Exists(Path.Combine(root, ".venv-llm", "Lib", "site-packages", "llama_cpp", "__init__.py")) || !File.Exists(model);
    public static async Task EnsureAsync(string root, string model, bool cpu, Action<PreparationProgress>? progress, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            if (!Required(root, model)) return;
            string script = Path.Combine(root, "scripts", "setup-llm.ps1");
            if (!File.Exists(script)) throw new InvalidOperationException("LLM準備用ファイルがありません。Locaptio Setupで再インストールしてください。");
            string defaultModel = Path.Combine(root, "models", "llm", "Qwen3-4B-Q4_K_M.gguf");
            if (!Path.GetFullPath(model).Equals(Path.GetFullPath(defaultModel), StringComparison.OrdinalIgnoreCase) && !File.Exists(model))
                throw new FileNotFoundException("指定したカスタムLLMモデルがありません。設定で既定のモデルに戻すか、ファイルを選び直してください。", model);
            progress?.Invoke(new("llm", 0, 4, "初回のAI環境を自動準備中（インターネット接続が必要）"));
            string tools = await EnsureUvAsync(root, token);
            progress?.Invoke(new("llm", 1, 4, "Python・LLMライブラリを準備中"));
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
            { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false) };
            foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-Backend", cpu ? "cpu" : "vulkan" }) start.ArgumentList.Add(arg);
            start.Environment["PATH"] = tools + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
            start.Environment["PYTHONIOENCODING"] = "utf-8";
            using var process = Process.Start(start) ?? throw new IOException("AI環境の準備を開始できませんでした。");
            using var cancel = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
            var tail = new Queue<string>(); object sync = new();
            async Task ReadAsync(StreamReader reader)
            {
                while (await reader.ReadLineAsync(token) is { } line)
                {
                    lock (sync) { tail.Enqueue(line); while (tail.Count > 12) tail.Dequeue(); }
                    if (line.Contains("LOCAPTIO_MODEL_DOWNLOAD")) progress?.Invoke(new("llm", 2, 4, "AIモデルをダウンロード中（約2.5GB・初回のみ）"));
                }
            }
            await Task.WhenAll(ReadAsync(process.StandardOutput), ReadAsync(process.StandardError), process.WaitForExitAsync(token));
            token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0 || Required(root, model)) throw new IOException("AI環境の自動準備に失敗しました。ネット接続と空き容量を確認し、Stop→Startで再試行してください。\n" + string.Join("\n", tail));
            progress?.Invoke(new("llm", 3, 4, "AIモデルの準備完了・読み込み中"));
        }
        finally { gate.Release(); }
    }
    private static readonly SemaphoreSlim uvGate = new(1);
    internal static async Task<string> EnsureUvAsync(string root, CancellationToken token)
    {
        await uvGate.WaitAsync(token);
        try
        {
            string tools = Path.Combine(root, "tools"); Directory.CreateDirectory(tools);
            string uv = Path.Combine(tools, "uv.exe");
            if (!File.Exists(uv))
            {
                using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
                string zip = Path.Combine(tools, Guid.NewGuid().ToString("N") + ".zip");
                try
                {
                    using var response = await http.GetAsync($"https://github.com/astral-sh/uv/releases/download/{UvVersion}/uv-x86_64-pc-windows-msvc.zip", HttpCompletionOption.ResponseHeadersRead, token);
                    response.EnsureSuccessStatusCode();
                    await using (var target = File.Create(zip)) await response.Content.CopyToAsync(target, token);
                    await using (var input = File.OpenRead(zip))
                        if (!Convert.ToHexString(await SHA256.HashDataAsync(input, token)).Equals(UvSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("環境準備ツールのSHA-256が一致しません。");
                    using var archive = ZipFile.OpenRead(zip);
                    var entry = archive.Entries.Single(e => e.Name == "uv.exe");
                    entry.ExtractToFile(uv, true);
                }
                finally { if (File.Exists(zip)) File.Delete(zip); }
            }
            return tools;
        }
        finally { uvGate.Release(); }
    }

}
