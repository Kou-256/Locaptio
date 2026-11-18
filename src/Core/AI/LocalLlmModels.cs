namespace Locaptio.Core.AI;

/// <summary>A local LLM that Locaptio can download: an official Qwen3 GGUF build (Apache-2.0).</summary>
public sealed record LocalLlmModel(string Key, string Name, string Repository, string File, double Gigabytes, string Use)
{
    public string PathIn(string root) => System.IO.Path.Combine(root, "models", "llm", File);
    public bool IsDownloaded(string root) => System.IO.File.Exists(PathIn(root));
}

public static class LocalLlmModels
{
    public const string Default = "qwen3-4b", Custom = "custom";
    public static readonly LocalLlmModel[] All =
    [
        new("qwen3-1.7b", "Qwen3 1.7B", "Qwen/Qwen3-1.7B-GGUF", "Qwen3-1.7B-Q8_0.gguf", 1.8, "軽量・高速。メモリ8GBのPC向け"),
        new("qwen3-4b", "Qwen3 4B", "Qwen/Qwen3-4B-GGUF", "Qwen3-4B-Q4_K_M.gguf", 2.5, "標準（推奨）"),
        new("qwen3-8b", "Qwen3 8B", "Qwen/Qwen3-8B-GGUF", "Qwen3-8B-Q4_K_M.gguf", 5.0, "高精度。メモリ16GB以上推奨"),
        new("qwen3-14b", "Qwen3 14B", "Qwen/Qwen3-14B-GGUF", "Qwen3-14B-Q4_K_M.gguf", 9.0, "最高精度。メモリ32GB以上・GPU推奨"),
    ];
    public static LocalLlmModel? Find(string? key) => All.FirstOrDefault(m => m.Key == key);
    /// <summary>The catalog entry whose file this is, if any (for downloading it).</summary>
    public static LocalLlmModel? ForPath(string root, string path)
        => All.FirstOrDefault(m => string.Equals(System.IO.Path.GetFullPath(m.PathIn(root)), System.IO.Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
    /// <summary>The GGUF file to use: the chosen catalog model, or the user's own file.</summary>
    public static string Resolve(string root, string key, string customPath)
        => key == Custom && !string.IsNullOrWhiteSpace(customPath) ? customPath : (Find(key) ?? Find(Default)!).PathIn(root);
}
