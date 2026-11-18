namespace Locaptio.Core;

/// <summary>
/// The caption mode is the spoken language, optionally with a translation target: "ja" (Japanese
/// transcription), "en>ja" (English speech translated into Japanese), "it>es" and so on, over Japanese,
/// English, Italian and Spanish. Older settings ("translate", "japanese", "ja-translate") still read.
/// </summary>
public static class CaptionModes
{
    public static readonly string[] Languages = ["ja", "en", "it", "es"];
    public static (string Source, string? Target) Parse(string? mode, string englishTarget = "ja", string japaneseTarget = "en")
    {
        string? Known(string? code, string source) => code is not null && Languages.Contains(code) && code != source ? code : null;
        switch (mode)
        {
            case "japanese": return ("ja", null);
            case "translate": return ("en", Known(englishTarget, "en") ?? "ja");
            case "ja-translate": return ("ja", Known(japaneseTarget, "ja") ?? "en");
        }
        string[] parts = (mode ?? "").Split('>', 2);
        string source = Languages.Contains(parts[0]) ? parts[0] : "ja";
        return (source, parts.Length == 2 ? Known(parts[1], source) : null);
    }
    public static string Format(string source, string? target) => target is null || target == source ? source : source + ">" + target;
    public static string Normalize(string? mode, string englishTarget = "ja", string japaneseTarget = "en")
    {
        var (source, target) = Parse(mode, englishTarget, japaneseTarget);
        return Format(source, target);
    }
    public static bool TranscriptionOnly(string? mode) => Parse(mode).Target is null;
    /// <summary>The Windows Live Captions language for a spoken language.</summary>
    public static string WindowsLanguage(string code) => code switch { "ja" => "ja-JP", "it" => "it-IT", "es" => "es-ES", _ => "en-US" };
}
