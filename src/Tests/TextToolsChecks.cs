using System.Text;
using Locaptio.Core;

/// <summary>--text-tools: the text diff, the AI-correction guard and term dictionary files.</summary>
internal static class TextToolsChecks
{
    private static void Sentences()
    {
        var caption = new CaptionUpdate(1, 1, "We start at 3.5 p.m. today. Please join early! Any questions?", "今日は午後3時半に始めます。早めに参加してください！質問はありますか？", true, 0, 0, 0, "en");
        var pieces = CaptionSentences.Expand(caption).ToArray();
        Check(pieces.Length == 3 && pieces[0].English == "We start at 3.5 p.m. today." && pieces[2].Japanese == "質問はありますか？", "sentences and paired translations: " + string.Join(" | ", pieces.Select(p => p.English + "=" + p.Japanese)));
        var japanese = CaptionSentences.Expand(caption with { English = "それでは始めます。資料は共有しました", Japanese = null }).ToArray();
        Check(japanese.Length == 2 && japanese[1].English == "資料は共有しました", "Japanese sentences split at 。");
        var uneven = CaptionSentences.Expand(caption with { Japanese = "全部まとめた訳です。" }).ToArray();
        Check(uneven.Length == 3 && uneven[0].Japanese is null && uneven[2].Japanese == "全部まとめた訳です。", "an unpaired translation stays with the last sentence");
        Check(CaptionSentences.Expand(caption with { English = "One sentence only" }).Count() == 1, "a single sentence is unchanged");
        Console.WriteLine("PASS: one sentence per caption bubble (decimals and abbreviations kept, translations paired when they match)");
    }
    private static void Repetition()
    {
        string source = "You will see a shiny ch shiny ch out of it";
        string looping = "キラキラと光るキラキラと光るキラキラしたキラキラしたキラキラしたキラしたキラしたキラキラしたキラしたキラキラしたキラしたキラしたキラしたキラしたキラしたキラしたキラしたキラしたキラしたキラしたキラしたキラしたキラしたキラしたキラしたキラしたキラした光";
        Check(RepetitionGuard.IsRunaway(source, looping), "the looping translation from the report is detected");
        string cleaned = RepetitionGuard.Clean(source, looping);
        Check(!RepetitionGuard.HasLoop(cleaned) && cleaned.Length < looping.Length / 2, "it is cut back: " + cleaned);
        Check(RepetitionGuard.Collapse("はいはいはいはいはい、わかりました") == "はい、わかりました", "whisper-style repeats are kept once");
        foreach (string normal in new[] { "キラキラした光が見えます。", "どんどん進めましょう。ますます便利になります。", "はい、はい、わかりました。", "Thank you very much for coming today.", "今日の会議では来期の予算について話し合います。" })
            Check(!RepetitionGuard.HasLoop(normal) && RepetitionGuard.Clean(normal, normal) == normal, "ordinary text is untouched: " + normal);
        Check(!RepetitionGuard.IsRunaway("これはチップです", "This is a chip."), "a normal translation is accepted");
        Console.WriteLine("PASS: repetition guard (looping translation detected and cut to: " + cleaned + "; Whisper repeats collapsed; ordinary text untouched)");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    public static void Run()
    {
        Repetition();
        Sentences();
        // Diff: what was added, where.
        var ranges = TextDiff.AddedRanges("議時録を保存します", "議事録を保存します。");
        Check(ranges.SequenceEqual([(1, 1), (9, 1)]), "added ranges: " + string.Join(",", ranges));
        var segments = TextDiff.Segments("予算を確認", "予算の確認");
        Check(string.Concat(segments.Where(s => s.Kind != TextDiff.Kind.Added).Select(s => s.Text)) == "予算を確認"
            && string.Concat(segments.Where(s => s.Kind != TextDiff.Kind.Removed).Select(s => s.Text)) == "予算の確認", "segments rebuild both texts");

        // Corrections that fix what was misheard are kept.
        foreach (var (before, after) in new[]
        {
            ("議時録を保存します", "議事録を保存します。"),
            ("きょうはかいぎのぎじろくをほぞんします", "今日は会議の議事録を保存します。"),
            ("えーと、資料を確認します", "資料を確認します。"),
            ("ロカプションの設定を開きます", "Locaptioの設定を開きます。"),
            ("次回はげつようびです", "次回は月曜日です。"),
        })
            Check(CorrectionGuard.Accept(before, after, out string why), $"refused a fix: {before} → {after} ({why})");
        // Corrections that drop or invent what was said are refused.
        foreach (var (before, after) in new[]
        {
            ("予算の資料を確認してから会議を始めます", "会議を始めます。"),
            ("担当は田中さんと佐藤さんです", "担当は田中さんです。"),
            ("今日はありがとうございました", "今日はありがとうございました。来週の会議では予算について詳しく議論します。"),
            ("まず最初に、前回の議事録を確認しましょう", "前回の議事録を確認しましょう。"),
        })
            Check(!CorrectionGuard.Accept(before, after, out _), $"accepted a lossy correction: {before} → {after}");

        // Term dictionary files.
        var dictionary = new TermDictionary([new("Locaptio", ["ロカプション", "low caption"], "ロカプティオ"), new("議事録", ["議時録"], ""), new("A,B \"社\"", [], "")]);
        byte[] csv = TermDictionaryFiles.ToCsv(dictionary);
        Check(csv[0] == 0xEF && csv[1] == 0xBB && csv[2] == 0xBF, "CSV has a BOM for Excel");
        var back = TermDictionaryFiles.Parse(TermDictionaryFiles.Decode(csv));
        Check(back.Length == 3 && back[0].Aliases.SequenceEqual(["ロカプション", "low caption"]) && back[0].Translation == "ロカプティオ" && back[2].Term == "A,B \"社\"", "CSV round trip: " + string.Join(" / ", back.Select(e => e.Term)));
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var sjis = Encoding.GetEncoding(932).GetBytes("表記,誤表記,訳語\r\n議事録,議時録、ぎじろく,minutes\r\n");
        var excel = TermDictionaryFiles.Parse(TermDictionaryFiles.Decode(sjis));
        Check(excel.Length == 1 && excel[0].Term == "議事録" && excel[0].Aliases.SequenceEqual(["議時録", "ぎじろく"]), "Shift-JIS CSV from Excel");
        var tsv = TermDictionaryFiles.Parse("Locaptio\tロカプ, ロカプション\t\n資料\t\t");
        Check(tsv.Length == 2 && tsv[0].Aliases.Length == 2, "tab-separated");
        var own = TermDictionaryFiles.Parse(dictionary.Serialize());
        Check(own.Length == 3 && own[1].Aliases.SequenceEqual(["議時録"]), "Locaptio's own format");
        var merged = TermDictionaryFiles.Merge(dictionary, [new("議事録", ["ぎじろく"], "minutes"), new("新語", ["しんご"], "")]);
        Check(merged.Entries.Length == 4 && merged.Entries.First(e => e.Term == "議事録").Aliases.SequenceEqual(["議時録", "ぎじろく"]) && merged.Entries.First(e => e.Term == "議事録").Translation == "minutes", "merge keeps and adds misrecognitions");
        // Translation routes.
        Check(Locaptio.Core.AI.TranslationPacks.Route("en", "ja") is { BuiltIn: true }, "English → Japanese is built in");
        Check(Locaptio.Core.AI.TranslationPacks.Route("ja", "en")?.Pack == "ja-en" && Locaptio.Core.AI.TranslationPacks.Route("ja", "zh")?.Pack == "multi"
            && Locaptio.Core.AI.TranslationPacks.Route("en", "ko")?.Pack == "multi", "other directions use the downloadable packs");
        Check(Locaptio.Core.AI.TranslationPacks.Route("ja", "ja") is null && Locaptio.Core.AI.TranslationPacks.Route("ja", "xx") is null, "no route to the same or an unknown language");
        Check(Locaptio.Core.AI.TranslationPacks.Missing(Path.GetTempPath(), Locaptio.Core.AI.TranslationPacks.Route("ja", "en")!)?.Key == "ja-en"
            && Locaptio.Core.AI.TranslationPacks.Missing(Path.GetTempPath(), Locaptio.Core.AI.TranslationPacks.Route("en", "ja")!) is null, "missing pack reported; built-in never missing");
        Console.WriteLine("PASS: translation routes (built-in en→ja, ja-en and multilingual packs, missing pack);");
        Console.WriteLine("PASS: text diff ranges; AI-correction guard keeps 5 fixes and refuses 4 lossy/invented corrections; term dictionary CSV (BOM, quoting), Excel Shift-JIS CSV, TSV, own format and merge");
    }
}
