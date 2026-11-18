using System.Diagnostics;
using Locaptio.Core.AI;

/// <summary>
/// --translation-pack &lt;runtime&gt; &lt;pack&gt; [target]: downloads the pack the way Settings does (if missing),
/// starts a translation worker for it and translates real sentences. Needs network the first time.
/// </summary>
internal static class TranslationPackChecks
{
    public static async Task RunAsync(string root, string key, string target, string source = "ja")
    {
        var pack = TranslationPacks.Find(key) ?? throw new ArgumentException("Unknown pack " + key);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(40));
        var clock = Stopwatch.StartNew();
        await TranslationPacks.EnsureAsync(root, key, p => Console.WriteLine($"  [{p.Component}] {p.Stage}"), timeout.Token);
        Console.WriteLine($"  ready after {clock.Elapsed.TotalSeconds:F0}s; installed={pack.IsInstalled(root)}");
        var route = TranslationPacks.Route(source, target, root) ?? throw new Exception("no route");
        using var backend = new LocalBackend { TranslationTarget = route.Target };
        await backend.StartAsync(root, "translation", true, timeout.Token, false, route.Source, route.BuiltIn ? "legacy" : route.Pack);
        Console.WriteLine($"  worker: {backend.Backend}");
        string[] sentences = source == "en"
            ? ["Today's meeting will discuss the budget for next year.", "Please share the materials by Friday.", "We use a term dictionary to improve speech recognition accuracy."]
            : ["今日の会議では来期の予算について話し合います。", "資料は金曜日までに共有してください。", "音声認識の精度を上げるために用語辞書を使います。"];
        foreach (string sentence in sentences)
        {
            var result = await backend.TranslateAsync(sentence, timeout.Token);
            Console.WriteLine($"  {sentence} → {result.Text} ({result.Milliseconds:F0}ms)");
            if (string.IsNullOrWhiteSpace(result.Text) || result.Text == sentence) throw new Exception("No translation for: " + sentence);
            if (target == "en" && System.Text.RegularExpressions.Regex.IsMatch(result.Text, "[ぁ-んァ-ヶ一-龯]")) throw new Exception("Japanese left in the English translation: " + result.Text);
        }
        Console.WriteLine($"PASS: {pack.Name} ({route.Source}→{route.Target}) downloaded, converted and translating");
    }
}
