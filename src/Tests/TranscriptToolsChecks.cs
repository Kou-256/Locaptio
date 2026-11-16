using System.Diagnostics;
using Locaptio.Core;
using Locaptio.Core.AI;
using Locaptio.Core.Audio;
using NAudio.Wave;

/// <summary>Editing, dictionary, subtitles, autosave, comparison, timing and summary logic. No models or audio devices.</summary>
internal static class TranscriptToolsChecks
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private sealed class Summarizer(params string[] replies) : ISummaryBackend
    {
        public readonly List<string> Prompts = new();
        public Task<InferenceResult> SummarizeAsync(string numberedTranscript, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Prompts.Add(numberedTranscript); return Task.FromResult(new InferenceResult(replies[Math.Min(Prompts.Count - 1, replies.Length - 1)], 1)); }
    }
    private sealed class Speech(string text) : ISpeechRecognitionBackend
    {
        public Task<InferenceResult> RecognizeAsync(float[] samples, CancellationToken token, bool final = true) => Task.FromResult(new InferenceResult(text, 1));
    }

    public static async Task RunAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Locaptio-tools-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            Dictionary(); Edits(); Subtitles(); Comparison(); Timing(); await SummaryAsync();
            await AutosaveAsync(directory); await DocumentAsync(directory);
            Console.WriteLine("PASS: term dictionary hint/replacement/glossary; edit undo/redo and revision priority; SRT/VTT cues; time-aligned comparison and versions; word-time alignment; cited summary parsing; autosave fallback, interrupted WAV repair; bookmarks/summary/original roundtrip");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void Dictionary()
    {
        var terms = TermDictionary.Parse("Locaptio | ロカプション, low caption | ロカプティオ\n議事録 | 議時録、 義事録 |\nWASAPI | wasabi |\n\n | 無視 |\n橋爪 | 橋詰");
        Check(terms.Entries.Length == 4 && terms.Entries[0].Aliases.Length == 2 && terms.Entries[0].Translation == "ロカプティオ", "Dictionary parsing failed");
        Check(terms.Apply("ロカプションで議時録を保存し、Low Captionを閉じる") == "Locaptioで議事録を保存し、Locaptioを閉じる", "Alias replacement failed: " + terms.Apply("ロカプションで議時録を保存し、Low Captionを閉じる"));
        // ASCII aliases respect word boundaries; an already correct spelling is never expanded again.
        Check(terms.Apply("wasabi and wasabis, WASAPI") == "WASAPI and wasabis, WASAPI", "Word boundary failed: " + terms.Apply("wasabi and wasabis, WASAPI"));
        var nested = new TermDictionary([new("ロカプション", ["ロカプ"])]);
        Check(nested.Apply("ロカプションとロカプ") == "ロカプションとロカプション", "Nested alias expanded a correct term: " + nested.Apply("ロカプションとロカプ"));
        Check(terms.SpeechPrompt("会議") == "会議, Locaptio, 議事録, WASAPI, 橋爪" && terms.SpeechPrompt("", 12) == "Locaptio", "Recognition hint failed: " + terms.SpeechPrompt("", 12));
        Check(terms.CorrectionHint().Contains("議事録（誤: 議時録・義事録）") && terms.TranslationGlossary("subject = 被写体") == "subject = 被写体、Locaptio = ロカプティオ", "Correction/translation glossary failed");
        Check(TermDictionary.Parse(terms.Serialize()).Entries.SequenceEqual(terms.Entries, EqualityComparer<TermEntry>.Create((a, b) => a!.Term == b!.Term && a.Translation == b.Translation && a.Aliases.SequenceEqual(b.Aliases))), "Dictionary roundtrip failed");
        Check(TermDictionary.Parse(null).Apply("そのまま") == "そのまま" && TermDictionary.Empty.SpeechPrompt("a") == "a", "Empty dictionary changed text");
    }

    private static void Edits()
    {
        var edits = new TranscriptEditHistory(); var rows = new Dictionary<long, CaptionUpdate>();
        var buffer = new TranscriptBuffer();
        void Publish(CaptionUpdate update) { edits.Observe(update); buffer.Update(update); if (!rows.TryGetValue(update.Id, out var old) || old.Revision <= update.Revision) rows[update.Id] = update; }
        Publish(new(1, 1, "議時録を保存", null, true, 10, 0, 0, "ja"));
        Publish(new(1, 2, "議事録を保存", null, true, 10, 0, 0, "ja", SourceCorrected: true));
        Check(edits.Original(1) == "議時録を保存" && edits.Differs(rows[1]) && !edits.IsEdited(1), "Pre-correction text lost");
        var edited = edits.Apply(rows[1], " 議事録を保存します ", null)!; Publish(edited);
        Check(edited.Revision > TranscriptEditHistory.ManualRevision && edited.English == "議事録を保存します" && edits.IsEdited(1) && edits.CanUndo, "Edit not applied");
        // A late AI correction or translation for the same row must not replace the manual edit.
        Publish(new(1, 3, "AIの遅い補正", null, true, 10, 0, 0, "ja", SourceCorrected: true));
        Check(rows[1].English == "議事録を保存します" && buffer.Export(false).Contains("議事録を保存します") && edits.Original(1) == "議時録を保存", "Late correction overwrote an edit");
        Check(edits.Apply(rows[1], "議事録を保存します", null) is null && edits.Apply(rows[1], "  ", null) is null, "No-op or empty edit accepted");
        Publish(edits.Apply(rows[1], "議事録を共有します", "Share the minutes")!);
        var undone = edits.Undo(id => rows.GetValueOrDefault(id))!; Publish(undone);
        Check(undone.English == "議事録を保存します" && undone.Japanese is null && undone.Revision > edited.Revision + 1 && edits.CanRedo, "Undo failed");
        Publish(edits.Undo(id => rows.GetValueOrDefault(id))!);
        Check(rows[1].English == "議事録を保存" && !edits.CanUndo && !edits.IsEdited(1), "Second undo failed");
        Publish(edits.Redo(id => rows.GetValueOrDefault(id))!); Publish(edits.Redo(id => rows.GetValueOrDefault(id))!);
        Check(rows[1].English == "議事録を共有します" && rows[1].Japanese == "Share the minutes" && !edits.CanRedo, "Redo failed");
        Publish(edits.Undo(id => rows.GetValueOrDefault(id))!); Publish(edits.Apply(rows[1], "別の修正", null)!);
        Check(!edits.CanRedo, "A new edit kept a stale redo");
        rows.Remove(1);
        Check(edits.Undo(id => rows.GetValueOrDefault(id)) is null && !edits.CanUndo, "Undo resurrected a cleared row");
        edits.Restore(7, "原文", true);
        Check(edits.IsEdited(7) && edits.Original(7) == "原文", "Saved edit state not restored");
        edits.Clear(); Check(!edits.IsEdited(7) && edits.Original(7) is null, "Clear retained edits");
    }

    private static void Subtitles()
    {
        var document = new RecordingDocument(1, "a.wav", DateTimeOffset.Now, false,
        [
            new(1, 1, 1.5, "こんにちは。", "Hello.", 1, "ja", 3.25),
            new(2, 2, 3661.007, "A & B <c> --> done", null, 1, "en"),
            new(3, 3, 3663, "次の発話", null, 1, "ja")
        ]);
        string srt = SubtitleExport.Srt(document);
        Check(srt.StartsWith("1\r\n00:00:01,500 --> 00:00:03,250\r\nこんにちは。\r\n\r\n2\r\n01:01:01,007 --> 01:01:02,627\r\n"), "SRT timing/numbering failed:\n" + srt);
        string vtt = SubtitleExport.Vtt(document, "both");
        Check(vtt.StartsWith("WEBVTT\n\n00:00:01.500 --> 00:00:03.250\nこんにちは。\nHello.\n\n") && vtt.Contains("A &amp; B &lt;c> → done") && vtt.Split("-->").Length == 4, "VTT escaping/both failed:\n" + vtt);
        Check(SubtitleExport.Cues(document, "translation")[0].Text == "Hello." && SubtitleExport.Cues(document, "translation")[2].Text == "次の発話", "Translation fallback failed");
        // A long utterance becomes several two-line cues; word times place the split.
        string longText = string.Concat(Enumerable.Repeat("これは長い発話の一部です。", 8));
        var words = Enumerable.Range(0, 8).Select(i => new RecordingWord(i * 13, 13, 10 + i * 2, 12 + i * 2)).ToArray();
        var cues = SubtitleExport.Cues(new(1, "a.wav", DateTimeOffset.Now, false, [new(1, 1, 10, longText, null, 1, "ja", 26, words)]));
        Check(cues.Count > 1 && cues.All(c => c.Text.Split('\n').Length <= 2 && c.End > c.Start) && string.Concat(cues.Select(c => c.Text.Replace("\n", ""))) == longText, "Long cue split lost text");
        Check(cues[0].Start == 10 && cues[^1].End == 26 && cues.Zip(cues.Skip(1)).All(p => p.First.End == p.Second.Start) && cues[1].Start % 2 == 0, "Cue times not contiguous or not on word times");
        var english = SubtitleExport.Cues(new(1, "a.wav", DateTimeOffset.Now, false, [new(1, 1, 0, string.Join(" ", Enumerable.Repeat("word", 60)), null, 1, "en", 30)]));
        Check(english.Count > 1 && english.All(c => !c.Text.Contains("wor\n") && c.Text.Split('\n').All(l => l.Length <= 84)), "English wrapping split a word");
        Check(SubtitleExport.Time(-1, ',') == "00:00:00,000" && SubtitleExport.Time(359999.9996, '.') == "100:00:00.000", "Time formatting failed");
    }

    private static void Comparison()
    {
        RecordingPhrase P(double start, double end, string text) => new((long)(start * 10), 1, start, text, null, 1, "ja", end);
        var before = new[] { P(0, 2, "今日は会議です"), P(2.5, 5, "議時録を"), P(5.1, 7, "保存します"), P(20, 22, "以上です") };
        var after = new[] { P(0.2, 2, "今日は会議です"), P(2.4, 7, "議事録を保存します"), P(12, 14, "新しく認識された発話"), P(20.1, 22, "以上です") };
        var rows = TranscriptComparison.Align(before, after);
        Check(rows.Count == 4 && !rows[0].Changed && rows[1] is { Before: "議時録を 保存します", After: "議事録を保存します" } && rows[2] is { Before: "", After: "新しく認識された発話" } && !rows[3].Changed, "Time alignment failed: " + string.Join(" | ", rows));
        Check(TranscriptComparison.Align([], after).Count == 4 && TranscriptComparison.Align(before, []).All(r => r.After == ""), "One-sided comparison failed");
        Check(TranscriptComparison.Similarity("議時録を 保存します。", "議事録を保存します") is > .85 and < 1 && TranscriptComparison.Similarity("Same, text", "same text") == 1 && TranscriptComparison.Similarity("", "x") == 0, "Similarity failed");
    }

    private static void Timing()
    {
        // The saved transcript is two seconds late and worded slightly differently from the reference.
        var saved = new RecordingDocument(1, "a.wav", DateTimeOffset.Now, true,
        [
            new(1, 1, 3, "今日はLocaptioの会議です。", null, 1),
            new(2, 2, 8, "えーと", null, 1),
            new(3, 3, 12, "議事録を保存します", "Save the minutes", 1)
        ], [new(8, "重要", 2)], [new("要点", "保存する", [12])]);
        RecordingWord[] Each(string text, double start, double step) => text.Select((_, i) => new RecordingWord(i, 1, start + i * step, start + (i + 1) * step)).ToArray();
        var reference = new[]
        {
            new RecordingPhrase(1, 1, 1, "今日はｌｏｃａｐｔｉｏの会議です", null, 1, "ja", 4.2, Each("今日はｌｏｃａｐｔｉｏの会議です", 1, .2)),
            new RecordingPhrase(2, 2, 10, "議事録を保存します", null, 1, "ja", 11.8, Each("議事録を保存します", 10, .2))
        };
        var result = TimingAlignment.Apply(saved, reference);
        var first = result.Document.Phrases[0]; var last = result.Document.Phrases[2];
        Check(result.Aligned == 2 && result.Total == 3 && Math.Abs(first.Seconds - 1) < .001 && Math.Abs(last.Seconds - 10) < .001 && Math.Abs(last.EndSeconds!.Value - 11.8) < .001, $"Onsets not corrected: {first.Seconds}, {last.Seconds}");
        Check(first.Text == saved.Phrases[0].Text && last.Translation == "Save the minutes" && result.Document.ApproximateTiming, "Alignment changed wording or flags");
        Check(first.Words is { Length: > 3 } && first.Words.All(w => w.Start >= first.Seconds && w.End >= w.Start && w.StartIndex + w.Length <= first.Text.Length)
            && first.Words.Zip(first.Words.Skip(1)).All(p => p.First.End <= p.Second.Start + 1e-9), "Word times invalid: " + string.Join(" ", (first.Words ?? []).Select(w => $"{w.StartIndex}+{w.Length}@{w.Start:F3}-{w.End:F3}")));
        // "Locaptio" is one highlight unit in the saved text and spans its eight reference characters.
        var name = first.Words!.Single(w => first.Text.Substring(w.StartIndex, w.Length) == "Locaptio");
        Check(Math.Abs(name.Start - 1.6) < .001 && Math.Abs(name.End - 3.2) < .001, $"Word span wrong: {name.Start}-{name.End}");
        Check(PlaybackHighlight.Range(first, first.Text, 1.7, first.EndSeconds!.Value, "word") == (name.StartIndex, name.Length), "Aligned words not used for highlighting");
        // The unmatched filler moves with its neighbour and stays in order; attachments follow their utterance.
        var filler = result.Document.Phrases[1];
        Check(Math.Abs(filler.Seconds - 6) < .001 && filler.Seconds >= first.Seconds && filler.Seconds <= last.Seconds, "Unmatched phrase not shifted: " + filler.Seconds);
        Check(Math.Abs(result.Document.Bookmarks![0].Seconds - 6) < .001 && Math.Abs(result.Document.Summary![0].Evidence[0] - 10) < .001, "Bookmark/summary times not moved");
        var none = TimingAlignment.Apply(saved, [new(1, 1, 3, "まったく別の音声", null, 1, "ja", 5)]);
        Check(none.Aligned == 0 && none.Document.Phrases.SequenceEqual(saved.Phrases), "Unrelated audio changed timing");
        var all = TimingAlignment.Apply(saved with { Phrases = [saved.Phrases[0], saved.Phrases[2]] }, reference);
        Check(all.Aligned == 2 && !all.Document.ApproximateTiming, "Fully aligned document still marked approximate");
    }

    private static async Task SummaryAsync()
    {
        var rows = Enumerable.Range(1, 40).Select(i => ((long)(100 + i), i == 3 ? "来週までに資料を送ります" : "発話その" + i + new string('あ', 80))).ToArray();
        var chunks = TranscriptSummary.Chunks(rows, 1800);
        Check(chunks.Count > 1 && chunks.All(c => c.Length <= 1800) && chunks[0].StartsWith("[1] 発話その1") && chunks[^1].TrimEnd().Split('\n')[^1].StartsWith("[40] "), "Chunking failed");
        string reply = "以下が要約です。\n## 要点\n- **資料**を来週送る。 [3]\n- 根拠のない項目\n・全角の番号 ［１，２］\n## 決定事項\n- なし\n### TODO（担当）\n1. 田中さんが資料を送付 [3][99][3]\n- 特になし\n無関係な行 [5]";
        var items = TranscriptSummary.Parse(reply, rows.Select(r => r.Item1).ToArray());
        Check(items.Length == 4 && items[0] is { Section: "要点", Text: "資料を来週送る" } && items[0].RowIds.SequenceEqual([103L]) && items[1].RowIds.Length == 0
            && items[2].RowIds.SequenceEqual([101L, 102L]) && items[3] is { Section: "TODO", Text: "田中さんが資料を送付" } && items[3].RowIds.SequenceEqual([103L]), "Summary parsing failed: " + string.Join(" | ", items.Select(i => i.Section + ":" + i.Text + ":" + string.Join(",", i.RowIds))));
        var labelled = TranscriptSummary.Merge([TranscriptSummary.Parse("## 要点\n- リリースは15日に決定 [1]\n- 別の要点 [2]\n## 決定事項\n- リリースは15日に決定。 [1]\n## TODO\n- 担任者:佐藤さん:結果を共有（金曜） [3]\n- 田中さん : 画像を用意 [4]", rows.Select(r => r.Item1).ToArray())]);
        Check(labelled.Select(i => i.Section + ":" + i.Text).SequenceEqual(["要点:別の要点", "決定事項:リリースは15日に決定", "TODO:佐藤さん：結果を共有(金曜)", "TODO:田中さん：画像を用意"]), "Summary label cleanup or dedupe failed: " + string.Join(" | ", labelled.Select(i => i.Section + ":" + i.Text)));
        var backend = new Summarizer("## 要点\n- 前半の要点 [1]\n## TODO\n- 資料を送る [3]", "## 要点\n- 前半の要点 [30]\n- 後半の要点 [40]\n## 決定事項\n- 次回は来月 [39]");
        int steps = 0;
        var merged = await TranscriptSummary.RunAsync(backend, rows, (_, _) => steps++, CancellationToken.None);
        Check(backend.Prompts.Count == chunks.Count && steps == chunks.Count + 1 && merged.Select(i => i.Section).SequenceEqual(["要点", "要点", "決定事項", "TODO"])
            && merged[0].RowIds.SequenceEqual([101L, 130L]), "Multi-part summary merge failed: " + string.Join(" | ", merged.Select(i => i.Section + ":" + i.Text)));
        string markdown = TranscriptSummary.Markdown(merged, id => id == 103 ? "00:01:00" : null);
        Check(markdown.Contains("## 決定事項\r\n- 次回は来月") || markdown.Contains("## 決定事項\n- 次回は来月"), "Markdown sections failed");
        Check(markdown.Contains("- 資料を送る（00:01:00）"), "Markdown evidence failed:\n" + markdown);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { await TranscriptSummary.RunAsync(backend, rows, null, canceled.Token); throw new Exception("Summary ignored cancellation"); } catch (OperationCanceledException) { }
    }

    private static async Task AutosaveAsync(string directory)
    {
        string path = Path.Combine(directory, "autosave", "session.json");
        Check(SessionAutosave.TryLoad(path) is null, "Missing autosave loaded");
        var snapshot = new AutosaveSnapshot(1, DateTimeOffset.Now, "japanese", Path.Combine(directory, "rec.wav"),
            [new(2, 3.5, "二つ目", null, "ja"), new(1, 1, "一つ目", "First", "en", 2, "ひとつめ", true), new(3, 5, "  ", null, "ja")], [new(3.5, "重要", 2, "二つ目")]);
        await SessionAutosave.SaveAsync(path, snapshot);
        var loaded = SessionAutosave.TryLoad(path)!;
        Check(loaded.Rows.Select(r => r.Id).SequenceEqual([1L, 2L]) && loaded.Rows[0] is { Original: "ひとつめ", Edited: true, SpeakerId: 2, Translation: "First" } && loaded.Bookmarks!.Single().Label == "重要", "Autosave roundtrip failed");
        // A second save keeps the previous copy; a torn or corrupt main file falls back to it.
        await SessionAutosave.SaveAsync(path, snapshot with { Rows = [new(9, 0, "新しい", null, "ja")] });
        Check(SessionAutosave.TryLoad(path)!.Rows.Single().Id == 9 && File.Exists(path + ".bak") && !File.Exists(path + ".tmp"), "Autosave replace failed");
        await File.WriteAllTextAsync(path, "{ \"Version\": 1, \"Rows\": [");
        Check(SessionAutosave.TryLoad(path)!.Rows.Length == 2, "Corrupt autosave did not fall back");
        SessionAutosave.Discard(path);
        Check(SessionAutosave.TryLoad(path) is null && !File.Exists(path + ".bak"), "Discard left a snapshot");

        // An interrupted WAV: the header still describes an earlier, shorter length.
        string wav = Path.Combine(directory, "rec.wav");
        using (var writer = new WaveFileWriter(wav, new WaveFormat(16000, 16, 1))) { writer.Write(new byte[32000], 0, 32000); }
        long complete = new FileInfo(wav).Length;
        await using (var stream = new FileStream(wav, FileMode.Append)) await stream.WriteAsync(Enumerable.Repeat((byte)1, 64001).ToArray());
        using (var stale = new WaveFileReader(wav)) Check(Math.Abs(stale.TotalTime.TotalSeconds - 1) < .01, "Fixture header is not stale");
        Check(SessionAutosave.RecoverRecording(wav) == wav, "WAV not recovered");
        using (var repaired = new WaveFileReader(wav)) Check(Math.Abs(repaired.TotalTime.TotalSeconds - 3) < .01 && new FileInfo(wav).Length == complete + 64000, "WAV header not repaired: " + repaired.TotalTime);
        Check(SessionAutosave.RecoverRecording(wav) == wav && new FileInfo(wav).Length == complete + 64000, "Repair is not repeatable");
        // MP3/M4A recordings are interrupted while still a WAV checkpoint.
        string mp3 = Path.Combine(directory, "meeting.mp3"); File.Copy(wav, mp3 + ".recording.wav");
        await RecordingTranscript.SaveAsync(mp3, new(1, "meeting.mp3", DateTimeOffset.Now, false, [new(1, 1, 1, "会議", null, 1)]));
        string? recovered = SessionAutosave.RecoverRecording(mp3);
        Check(recovered == Path.Combine(directory, "meeting.recovered.wav") && File.Exists(recovered) && !File.Exists(mp3 + ".recording.wav")
            && (await RecordingTranscript.LoadAsync(recovered)).Phrases.Single().Text == "会議" && SessionAutosave.RecoverRecording(mp3) == recovered, "Checkpoint recovery failed");
        string broken = Path.Combine(directory, "broken.wav"); await File.WriteAllTextAsync(broken, new string('x', 100));
        Check(SessionAutosave.RecoverRecording(broken) is null && SessionAutosave.RecoverRecording(Path.Combine(directory, "missing.wav")) is null && SessionAutosave.RecoverRecording("") is null, "Invalid audio reported as recovered");
    }

    private static async Task DocumentAsync(string directory)
    {
        string audio = Path.Combine(directory, "doc.wav"); await File.WriteAllTextAsync(audio, "fixture");
        long start = Stopwatch.GetTimestamp();
        var recording = new RecordingTranscript(audio, start, false);
        var caption = new CaptionUpdate(1, 1, "議時録", null, true, start + Stopwatch.Frequency, 0, 0, "ja");
        recording.Observe(caption); recording.Annotate(1, caption.Started, "議時録", true);
        recording.Observe(caption with { Revision = TranscriptEditHistory.ManualRevision + 1, English = "議事録" });
        var mark = recording.AddBookmark("後で確認", start + 2 * Stopwatch.Frequency, 1, "議事録");
        recording.AddBookmark("重要", start + Stopwatch.Frequency / 2);
        var document = recording.Snapshot() with { Summary = [new("要点", "議事録の話", [1]), new("", "無効", [1]), new("TODO", "送る", [-1, 1])], Model = "small" };
        Check(document.Phrases.Single() is { Text: "議事録", Original: "議時録", Edited: true } && document.Bookmarks!.Select(b => b.Label).SequenceEqual(["重要", "後で確認"]) && Math.Abs(mark.Seconds - 2) < .001, "Annotation or bookmark order failed");
        await RecordingTranscript.SaveAsync(audio, document);
        var loaded = await RecordingTranscript.LoadAsync(audio);
        Check(loaded.Phrases.Single().Original == "議時録" && loaded.Bookmarks!.Length == 2 && loaded.Summary!.Length == 2 && loaded.Summary[1].Evidence.SequenceEqual([1.0]) && loaded.Model == "small", "Extended document roundtrip failed");
        // Files written before these fields existed still load.
        await File.WriteAllTextAsync(RecordingTranscript.Sidecar(audio), "{\"Version\":1,\"AudioFile\":\"doc.wav\",\"RecordedAt\":\"2026-01-01T00:00:00+09:00\",\"ApproximateTiming\":true,\"Phrases\":[{\"Id\":1,\"Started\":1,\"Seconds\":2,\"Text\":\"旧形式\",\"Translation\":null,\"Revision\":1}]}");
        var legacy = await RecordingTranscript.LoadAsync(audio);
        Check(legacy.Phrases.Single() is { Text: "旧形式", Original: null, Edited: false, Language: "ja" } && legacy.Bookmarks is null && legacy.Summary is null, "Older sidecar no longer loads");
        Check(recording.RemoveBookmark(mark) && recording.Snapshot().Bookmarks!.Length == 1, "Bookmark removal failed");
        await RecordingLibrary.SaveTextAsync(audio, document);
        Check((await File.ReadAllTextAsync(audio + ".txt")).Contains("［★後で確認］議事録"), "Bookmark missing from text");
        await TranscriptComparison.SaveVersionAsync(audio, "再文字起こし前", legacy);
        for (int i = 0; i < 11; i++) await TranscriptComparison.SaveVersionAsync(audio, "版" + i, document);
        var versions = await TranscriptComparison.LoadVersionsAsync(audio);
        Check(versions.Count == 10 && versions[^1].Label == "版10" && versions[0].Label == "版1" && versions[^1].Document.Phrases.Single().Text == "議事録", "Version history failed");
        await File.WriteAllTextAsync(RecordingFiles.SummaryFile(audio), "## 要点");
        Check(RecordingFiles.Bundle(audio).Length == 5, "Bundle omits versions or summary");
        // The dictionary is applied to file transcription before the text is stored, keeping the recognized wording.
        string wav = Path.Combine(directory, "tone.wav");
        using (var writer = new WaveFileWriter(wav, new WaveFormat(16000, 16, 1)))
            for (int i = 0; i < 3 * 16000; i++) writer.WriteSample(i < 8000 ? 0 : (float)(.2 * Math.Sin(i * 2 * Math.PI * 440 / 16000)));
        var phrases = new List<RecordingPhrase>(); var terms = TermDictionary.Parse("議事録 | 議時録");
        await FileTranscription.RunAsync(wav, new Speech("議時録です"), "ja", p => { phrases.Add(p); return Task.CompletedTask; }, _ => { }, CancellationToken.None, normalize: terms.Apply);
        Check(phrases.Count > 0 && phrases.All(p => p.Text == "議事録です" && p.Original == "議時録です"), "Dictionary not applied to file transcription");
    }
}
