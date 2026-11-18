using System.Diagnostics;
using Locaptio.Core.AI;

/// <summary>
/// --llm-recovery &lt;runtime&gt;: llama.cpp's DLLs cannot load in that runtime (as on a PC without a Vulkan driver
/// or the Visual C++ runtime). Starting the local LLM must switch to the CPU build by itself and then correct text.
/// Uses the runtime's own model; downloads only the llama.cpp CPU wheel.
/// </summary>
internal static class LlmRecoveryChecks
{
    public static async Task RunAsync(string root)
    {
        string model = LocalLlmModels.Find(LocalLlmModels.Default)!.PathIn(root);
        if (!File.Exists(model)) throw new FileNotFoundException("The runtime needs the default model", model);
        var stages = new List<string>(); var clock = Stopwatch.StartNew();
        using var llm = new LocalLlmBackend();
        llm.PreparationChanged += p => { lock (stages) stages.Add(p.Stage); };
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        await llm.StartAsync(root, model, false, timeout.Token);
        var corrected = await llm.CorrectAsync("今日は会議の議事録を保存しますす。", [], "ja", timeout.Token);
        bool switched = stages.Any(s => s.Contains("CPU版に切り替えています"));
        bool vulkanGone = !File.Exists(Path.Combine(root, ".venv-llm", "Lib", "site-packages", "llama_cpp", "lib", "ggml-vulkan.dll"));
        Console.WriteLine($"  stages: {string.Join(" → ", stages.Distinct())}");
        Console.WriteLine($"  backend: {llm.Backend}; corrected: {corrected.Text}; {clock.Elapsed.TotalSeconds:F0}s");
        if (!switched || corrected.Text.Length == 0) throw new Exception("Local LLM did not recover from a library that cannot load");
        Console.WriteLine($"PASS: llama.cpp could not load; switched to the CPU build automatically (GPU library removed: {vulkanGone}) and corrected text");
    }
}
