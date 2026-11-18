using System.Diagnostics;
using System.Text;
using Locaptio.Core.AI;

internal static class SetupEncodingChecks
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static async Task RunAsync()
    {
        string folder = Path.Combine(Path.GetTempPath(), "Locaptio-日本語-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "scripts")); Directory.CreateDirectory(Path.Combine(folder, "tools"));
        try
        {
            File.Copy(Path.Combine(Directory.GetCurrentDirectory(), "scripts", "setup-common.ps1"), Path.Combine(folder, "scripts", "setup-common.ps1"));
            await File.WriteAllTextAsync(Path.Combine(folder, "tools", "uv.exe"), "unused fixture");
            const string expected = "信頼されていないマウントポイントが含まれているため、パスをスキャンできません。日本語の診断";
            string child = "[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false; [Console]::WriteLine('" + expected + "'); exit 1";
            string fixture = "param($Model,[switch]$Translation,$Backend)\n. (Join-Path $PSScriptRoot 'setup-common.ps1')\n& powershell.exe -NoProfile -NonInteractive -EncodedCommand "
                + Convert.ToBase64String(Encoding.Unicode.GetBytes(child)) + " | ForEach-Object { [Console]::Error.WriteLine($_) }\nexit 1\n";
            foreach (string name in new[] { "setup-speech.ps1", "setup-llm.ps1" })
                await File.WriteAllTextAsync(Path.Combine(folder, "scripts", name), fixture, new UTF8Encoding(true));
            async Task Failure(Task task)
            {
                try { await task; throw new Exception("Expected setup failure"); }
                catch (IOException ex) { if (!ex.Message.Contains(expected) || ex.Message.Contains('\uFFFD')) throw new Exception("Native Japanese output was corrupted: " + ex.Message); }
            }
            await Failure(SpeechPreparation.EnsureAsync(folder, "small", false, "cpu", null, CancellationToken.None));
            await Failure(LocalLlmPreparation.EnsureAsync(folder, Path.Combine(folder, "models", "llm", "Qwen3-4B-Q4_K_M.gguf"), true, null, CancellationToken.None));
            // Windows PowerShell's own errors (the venv launcher missing, a Japanese throw) are written in the
            // console code page, not UTF-8: they must still read as Japanese.
            async Task PowerShellError(string body, string mustContain)
            {
                await File.WriteAllTextAsync(Path.Combine(folder, "scripts", "setup-speech.ps1"),
                    "param($Model,[switch]$Translation)\n. (Join-Path $PSScriptRoot 'setup-common.ps1')\n$ErrorActionPreference = 'Stop'\n"
                    // As on a PC where setup-common's switch to UTF-8 does not take effect (e.g. restricted language mode).
                    + "[Console]::OutputEncoding = [Text.Encoding]::GetEncoding(932)\n" + body + "\n", new UTF8Encoding(true));
                try { await SpeechPreparation.EnsureAsync(folder, "small", false, "cpu", null, CancellationToken.None); throw new Exception("Expected setup failure"); }
                catch (IOException ex) { if (!ex.Message.Contains(mustContain) || ex.Message.Contains('\uFFFD')) throw new Exception("PowerShell error was corrupted: " + ex.Message); }
            }
            await PowerShellError("& .venv/Scripts/python.exe inference/setup_speech.py", "python.exe");
            const string thrown = "Python（C:\\例\\python.exe）を実行できません（ファイルが削除されています）。";
            await PowerShellError("throw '" + thrown + "'", thrown);
            Console.WriteLine("PASS: native child UTF-8 → Windows PowerShell → redirected .NET stderr; speech/LLM errors preserve complete Japanese; Japanese paths; PowerShell's own console-code-page errors decoded");

            // The chosen local LLM is what the setup script is asked to download.
            await File.WriteAllTextAsync(Path.Combine(folder, "scripts", "setup-llm.ps1"),
                "param($Backend, $Repository = 'none', $File = 'none')\n[Console]::Error.WriteLine(\"ARGS $Backend $Repository $File\")\nexit 1\n", new UTF8Encoding(true));
            foreach (var model in LocalLlmModels.All)
            {
                try { await LocalLlmPreparation.EnsureAsync(folder, model.PathIn(folder), true, null, CancellationToken.None); throw new Exception("Expected setup failure"); }
                catch (IOException ex) { if (!ex.Message.Contains($"ARGS cpu {model.Repository} {model.File}")) throw new Exception("Wrong model requested: " + ex.Message); }
            }
            Check(LocalLlmModels.Resolve(folder, "qwen3-8b", "") == LocalLlmModels.Find("qwen3-8b")!.PathIn(folder), "catalog model path");
            Check(LocalLlmModels.Resolve(folder, LocalLlmModels.Custom, @"D:\m\x.gguf") == @"D:\m\x.gguf", "custom model path");
            Check(LocalLlmModels.Resolve(folder, LocalLlmModels.Custom, "") == LocalLlmModels.Find(LocalLlmModels.Default)!.PathIn(folder), "blank custom path falls back to the default");
            Check(LocalLlmModels.Resolve(folder, "unknown", "") == LocalLlmModels.Find(LocalLlmModels.Default)!.PathIn(folder), "unknown key falls back to the default");
            // The real script refuses files outside the offered Qwen3 GGUF builds before downloading anything.
            var refuse = Process.Start(new ProcessStartInfo("powershell.exe") { ArgumentList = { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
                Path.Combine(Directory.GetCurrentDirectory(), "scripts", "setup-llm.ps1"), "-Repository", "evil/model", "-File", "x.gguf" }, RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true })!;
            await refuse.WaitForExitAsync();
            Check(refuse.ExitCode != 0, "setup-llm.ps1 refuses an unlisted repository");
            Console.WriteLine($"PASS: local LLM choice → download request for each of {LocalLlmModels.All.Length} models; custom/default resolution; unlisted repositories refused");
        }
        finally { Directory.Delete(folder, true); }
    }
}
