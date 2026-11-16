using System.Diagnostics;
using Locaptio.Core;
using Locaptio.Core.AI;
using Locaptio.Core.Audio;
using NAudio.Wave;

/// <summary>
/// --real-tools: the v1.0.48 transcript tools with the installed models. Needs the prepared
/// runtime in the current directory (models, .venv, .venv-llm) and artifacts/test-ja*.wav.
/// Plays and records nothing; only local files are read.
/// </summary>
internal static class RealToolsChecks
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Log(string text) => Console.WriteLine(text);

    private static async Task<List<RecordingPhrase>> RecognizeAsync(string root, string audio, string model, string device, Func<string, string>? normalize = null, string prompt = "")
    {
        using var speech = new LocalBackend { RequestTimeout = TimeSpan.FromMinutes(5) };
        speech.Status += s => Log("  [" + model + "] " + s);
        var clock = Stopwatch.StartNew();
        await speech.StartAsync(root, "speech", device == "cpu", CancellationToken.None, false, "ja", model, prompt, device);
        double load = clock.Elapsed.TotalSeconds; clock.Restart();
        var phrases = new List<RecordingPhrase>();
        await FileTranscription.RunAsync(audio, speech, "ja", p => { phrases.Add(p); return Task.CompletedTask; }, _ => { }, CancellationToken.None, normalize: normalize);
        Log($"  {model}/{speech.Backend}: load {load:F1}s, recognize {clock.Elapsed.TotalSeconds:F1}s, {phrases.Count} phrases, word-timed {phrases.Count(p => p.Words is { Length: > 0 })}");
        foreach (var p in phrases) Log($"    {p.Seconds,6:F2}-{p.EndSeconds,6:F2} {p.Text}");
        return phrases;
    }

    public static async Task RunAsync()
    {
        string root = Directory.GetCurrentDirectory();
        string audio = Path.Combine(root, "artifacts", "test-ja-continuous.wav");
        if (!File.Exists(audio)) audio = Path.Combine(root, "artifacts", "test-ja.wav");
        using (var reader = new AudioFileReader(audio)) Log($"Audio: {Path.GetFileName(audio)} {reader.TotalTime.TotalSeconds:F1}s");

        // 1. Reference: the recording as a v1.0.48 import produces it (word times from the selected model).
        Log("1. Whisper large-v3-turbo (GPU auto, CPU fallback) with word timestamps");
        var reference = await RecognizeAsync(root, audio, "large-v3-turbo", "gpu");
        Check(reference.Count > 0 && reference.All(p => p.Words is { Length: > 0 }), "large-v3-turbo returned no word times");
        Check(reference.SelectMany(p => p.Words!).All(w => w.Start >= 0 && w.End >= w.Start), "Invalid word times");

        // 2. Re-transcribe with another model and compare, as the dialog does.
        Log("2. Re-transcribe with small (CPU) and compare");
        var small = await RecognizeAsync(root, audio, "small", "cpu");
        var rows = TranscriptComparison.Align(reference, small);
        double similarity = TranscriptComparison.Similarity(string.Concat(reference.Select(p => p.Text)), string.Concat(small.Select(p => p.Text)));
        Log($"  aligned rows {rows.Count}, changed {rows.Count(r => r.Changed)}, character agreement {similarity:P0}");
        foreach (var row in rows.Where(r => r.Changed).Take(6)) Log($"    {row.Seconds,6:F2} | {row.Before} | {row.After}");
        Check(similarity > .6 && rows.Count <= reference.Count + small.Count, "Comparison of two real transcripts failed");

        // 3. Improve sync on a transcript with estimated, late times (as Windows captions produce).
        // base words some phrases differently (認識制度 / 文字を越し), like a live caption would.
        var tiny = await RecognizeAsync(root, audio, "base", "cpu");
        Log("3. Improve sync: base's wording, 2.5 s late, no word times → aligned to the large-v3-turbo reference");
        small = tiny;
        var late = new RecordingDocument(1, "a.wav", DateTimeOffset.Now, true,
            small.Select(p => p with { Seconds = p.Seconds + 2.5, EndSeconds = null, Words = null }).ToArray(), [new(small[0].Seconds + 2.5 + .1, "重要", 1)]);
        var result = TimingAlignment.Apply(late, reference, 8);
        var errors = result.Document.Phrases.Zip(small).Select(p => Math.Abs(p.First.Seconds - p.Second.Seconds)).Order().ToArray();
        double median = errors[errors.Length / 2];
        Log($"  aligned {result.Aligned}/{result.Total}; onset error vs. true onset: median {median:F2}s, max {errors[^1]:F2}s (was 2.50s)");
        foreach (var p in result.Document.Phrases.Take(3))
            Log($"    {p.Seconds:F2} {p.Text} :: " + string.Join(" ", (p.Words ?? []).Take(8).Select(w => $"{p.Text.Substring(w.StartIndex, w.Length)}@{w.Start:F2}")));
        Check(result.Aligned >= Math.Max(1, result.Total * 3 / 4) && median < .6 && result.Document.Phrases.All(p => p.Words is { Length: > 0 } || p.Text.Length < 3), "Real alignment did not correct the timing");
        Check(Math.Abs(result.Document.Bookmarks![0].Seconds - (small[0].Seconds + .1)) < .7, "Bookmark not moved with its utterance");
        Check(result.Document.Phrases.Zip(late.Phrases).All(p => p.First.Text == p.Second.Text), "Alignment changed the wording");

        // 4. SRT from real word times.
        var cues = SubtitleExport.Cues(new(1, "a.wav", DateTimeOffset.Now, false, reference.ToArray()));
        string srt = SubtitleExport.Srt(new(1, "a.wav", DateTimeOffset.Now, false, reference.ToArray()));
        Log($"4. SRT: {cues.Count} cues\n" + string.Join("\n", srt.Split("\r\n").Take(8).Select(l => "    " + l)));
        Check(cues.Count >= reference.Count && cues.Zip(cues.Skip(1)).All(p => p.First.End <= p.Second.Start + 1e-6 && p.First.End > p.First.Start), "SRT cue times overlap");

        // 5. Dictionary: replace a word the model really produced, keep the recognized text.
        string word = reference.SelectMany(p => PlaybackHighlight.Tokens(p.Text).Select(t => p.Text.Substring(t.Index, t.Length))).First(t => t.Length >= 2 && t.All(char.IsLetter));
        var dictionary = new TermDictionary([new("【" + word + "】", [word])]);
        Log($"5. Dictionary: \"{word}\" → \"【{word}】\" with the word also sent as the recognition hint");
        var replaced = await RecognizeAsync(root, audio, "base", "gpu", dictionary.Apply, dictionary.SpeechPrompt(""));
        Check(replaced.Any(p => p.Text.Contains("【" + word + "】") && p.Original is { } o && o.Contains(word) && !o.Contains('【')), "Dictionary was not applied to real recognition");

        // 6. Local LLM: correction with the dictionary, then a cited summary.
        Log("6. Qwen3 local LLM");
        using var llm = new LocalLlmBackend();
        var clock = Stopwatch.StartNew();
        await llm.StartAsync(root, "", false, CancellationToken.None);
        Log($"  ready in {clock.Elapsed.TotalSeconds:F1}s: {llm.Backend}");
        var terms = new TermDictionary([new("Locaptio", ["ロカプション", "ロカプチオ"]), new("議事録", ["議時録"])]);
        llm.CorrectionGlossary = terms.CorrectionHint();
        // As in the app: earlier rows already show registered spellings, and the result passes the dictionary again.
        string[] context = [terms.Apply("今日はロカプションという文字起こしアプリの話をします。"), "会議の内容を自動で記録できます。"];
        foreach (string source in new[] { "ロカプチオで義事録を保存します。", "会議のぎじろくを保存します。" })
        {
            var corrected = await llm.CorrectAsync(terms.Apply(source), context, "ja", CancellationToken.None);
            string shown = terms.Apply(corrected.Text);
            Log($"  correction ({corrected.Milliseconds:F0}ms): {source} → model: {corrected.Text} → shown: {shown}");
            Check(shown.Contains("議事録") && (!source.StartsWith("ロカ") || shown.Contains("Locaptio")), "Correction with the dictionary failed");
        }

        (long, string)[] meeting =
        [
            (11, "それでは定例会議を始めます。今日は新しい字幕機能のリリース日程を決めたいと思います。"),
            (12, "現在のベータ版では、自動保存と編集の機能がほぼ完成しています。"),
            (13, "ただ、要約機能はまだ社内テストが足りていません。"),
            (14, "では、リリースは来月の十五日にしましょう。要約機能はベータとして表示する形で出します。"),
            (15, "異論はありません。それで決定とします。"),
            (16, "佐藤さんは今週中に、要約機能のテスト結果を共有してください。"),
            (17, "わかりました。金曜日までにまとめます。"),
            (18, "あと、ウェブサイトの説明画像も更新が必要ですね。"),
            (19, "田中さん、来週の水曜までにイラストを用意してもらえますか。"),
            (20, "はい、対応します。"),
            (21, "最後に、用語辞書の初期データについては次回の会議で検討します。"),
            (22, "本日はここまでです。お疲れさまでした。")
        ];
        int steps = 0; clock.Restart();
        var summary = await TranscriptSummary.RunAsync(llm, meeting, (_, _) => steps++, CancellationToken.None);
        Log($"  summary in {clock.Elapsed.TotalSeconds:F1}s:");
        foreach (var item in summary) Log($"    [{item.Section}] {item.Text}  ← {string.Join(",", item.RowIds)}");
        Check(summary.Any(i => i.Section == "決定事項") && summary.Any(i => i.Section == "TODO") && summary.Count(i => i.RowIds.Length > 0) >= summary.Length * 3 / 4, "Summary sections or citations missing");
        Check(summary.SelectMany(i => i.RowIds).All(id => id is >= 11 and <= 22), "Summary cited a line that does not exist");
        Check(summary.Where(i => i.Section == "決定事項").Any(i => i.RowIds.Intersect([14L, 15L]).Any()), "Decision not traced to the utterance that made it");
        var todos = summary.Where(i => i.Section == "TODO").ToArray();
        Check(todos.Any(i => i.Text.Contains("佐藤")) && todos.Any(i => i.Text.Contains("田中")) && summary.Count(i => i.Section == "要点") <= 6, "TODO owners missing or too many key points");
        Log("PASS: real Whisper word times, re-transcription comparison, timing alignment of late text, SRT, dictionary in recognition, LLM correction with dictionary and cited summary");
    }
}
