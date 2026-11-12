using System.Diagnostics;
using System.Text;
using Locaptio.Core.AI;

internal static class SetupEncodingChecks
{
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
            Console.WriteLine("PASS: native child UTF-8 → Windows PowerShell → redirected .NET stderr; speech/LLM errors preserve complete Japanese; Japanese paths");
        }
        finally { Directory.Delete(folder, true); }
    }
}
