using System.Diagnostics;
using System.Text;

namespace Locaptio.Core.AI;

/// <summary>A language Locaptio can translate into (M2M100 language code).</summary>
public sealed record TranslationLanguage(string Code, string Name, string EnglishName);

/// <summary>A translation model downloaded on request from Settings.</summary>
public sealed record TranslationPack(string Key, string Name, string Folder, string Repository, double Gigabytes, string License, string Use)
{
    public bool IsInstalled(string root) => File.Exists(Path.Combine(root, "models", Folder, "model.bin"));
}

/// <summary>Which model translates which language into which.</summary>
public sealed record TranslationRoute(string Source, string Target, string Pack)
{
    public bool BuiltIn => Pack == TranslationPacks.EnglishJapanese;
}

public static class TranslationPacks
{
    /// <summary>The English → Japanese model prepared with speech recognition (FuguMT en-ja).</summary>
    public const string EnglishJapanese = "en-ja";
    public const string JapaneseEnglish = "ja-en", Multilingual = "multi";
    public static readonly TranslationPack[] Downloadable =
    [
        new(JapaneseEnglish, "日本語 → 英語", "translation-ja-en", "staka/fugumt-ja-en", 0.12, "CC-BY-SA-4.0", "日本語の音声を英語に翻訳します（FuguMT）。"),
        new(Multilingual, "多言語（イタリア語・スペイン語など）", "translation-m2m100", "facebook/m2m100_418M", 1.9, "MIT", "イタリア語・スペイン語を含む翻訳に使います（英語→日本語、日本語→英語以外のすべての組み合わせ。M2M100）。"),
    ];
    public static readonly TranslationLanguage[] Languages =
    [
        new("ja", "日本語", "Japanese"), new("en", "英語", "English"), new("zh", "中国語", "Chinese"), new("ko", "韓国語", "Korean"),
        new("fr", "フランス語", "French"), new("de", "ドイツ語", "German"), new("es", "スペイン語", "Spanish"), new("it", "イタリア語", "Italian"),
        new("pt", "ポルトガル語", "Portuguese"), new("ru", "ロシア語", "Russian"), new("vi", "ベトナム語", "Vietnamese"), new("th", "タイ語", "Thai"),
        new("id", "インドネシア語", "Indonesian"),
    ];
    public static string Name(string code) => Languages.FirstOrDefault(l => l.Code == code)?.Name ?? code;
    /// <summary>Deletes a downloaded pack's folder (it can be downloaded again from Settings).</summary>
    public static void Remove(string root, TranslationPack pack)
    {
        string folder = Path.Combine(root, "models", pack.Folder);
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        // Leftover source weights from an interrupted conversion.
        string source = folder + "-source";
        if (Directory.Exists(source)) Directory.Delete(source, true);
    }
    public static TranslationPack? Find(string key) => Downloadable.FirstOrDefault(p => p.Key == key);

    /// <summary>The model for a direction; null when there is nothing to translate.</summary>
    public static TranslationRoute? Route(string source, string target)
    {
        if (source == target || Languages.All(l => l.Code != target)) return null;
        if (source == "en" && target == "ja") return new(source, target, EnglishJapanese);
        if (source == "ja" && target == "en") return new(source, target, JapaneseEnglish);
        return new(source, target, Multilingual);
    }
    /// <summary>As <see cref="Route(string, string)"/>, but uses the multilingual pack when it is installed and the dedicated one is not.</summary>
    public static TranslationRoute? Route(string source, string target, string root)
    {
        var route = Route(source, target);
        if (route is { Pack: JapaneseEnglish } && root.Length > 0 && !Find(JapaneseEnglish)!.IsInstalled(root) && Find(Multilingual)!.IsInstalled(root))
            return route with { Pack = Multilingual };
        return route;
    }
    /// <summary>The pack still to be downloaded for a route, or null when it is ready (or built in).</summary>
    public static TranslationPack? Missing(string root, TranslationRoute route) => route.BuiltIn ? null : Find(route.Pack) is { } pack && !pack.IsInstalled(root) ? pack : null;

    /// <summary>Downloads and converts a pack (scripts/setup-translation.ps1). Only called from Settings.</summary>
    public static async Task EnsureAsync(string root, string key, Action<PreparationProgress>? progress, CancellationToken token)
    {
        var pack = Find(key) ?? throw new ArgumentException("Unknown translation pack", nameof(key));
        await SpeechPreparation.WaitAsync(progress, token);
        try
        {
            if (pack.IsInstalled(root)) return;
            string script = Path.Combine(root, "scripts", "setup-translation.ps1");
            if (!File.Exists(script)) throw new FileNotFoundException("翻訳モデルの準備ファイルがありません。最新版のSetupで再インストールしてください。", script);
            progress?.Invoke(new("翻訳モデル", 0, 2, $"{pack.Name}をダウンロード中（約{pack.Gigabytes:0.##}GB・初回のみ）"));
            string tools = await LocalLlmPreparation.EnsureUvAsync(root, token);
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
            { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-Pack", pack.Key }) start.ArgumentList.Add(arg);
            start.Environment["PATH"] = tools + Path.PathSeparator + LocalLlmPreparation.RuntimePath(root) + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
            start.Environment["PYTHONIOENCODING"] = "utf-8";
            using var process = Process.Start(start) ?? throw new IOException("翻訳モデルの準備を開始できませんでした。");
            using var cancel = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
            var tail = new Queue<string>(); object sync = new();
            Task Read(StreamReader reader) => ProcessText.ReadLinesAsync(reader.BaseStream, line =>
            {
                lock (sync) { tail.Enqueue(line); while (tail.Count > 12) tail.Dequeue(); }
                if (line.Contains("LOCAPTIO_CONVERT")) progress?.Invoke(new("翻訳モデル", 1, 2, "モデルを変換中"));
            }, token);
            await Task.WhenAll(Read(process.StandardOutput), Read(process.StandardError), process.WaitForExitAsync(token));
            token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0 || !pack.IsInstalled(root)) throw new IOException("翻訳モデルを準備できませんでした。ネット接続と空き容量を確認してください。\n" + string.Join("\n", tail));
            progress?.Invoke(new("翻訳モデル", 2, 2, "準備完了"));
        }
        finally { SpeechPreparation.EnvironmentGate.Release(); }
    }
}
