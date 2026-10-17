using System.Diagnostics;
using Locaptio.Core;

internal static class WindowsCaptionChecks
{
    internal static void Run()
    {
        var tracker = new WindowsCaptionTracker(true); long now = Stopwatch.GetTimestamp();
        long Ms(int ms) => now + (long)(Stopwatch.Frequency * ms / 1000.0);
        var transcript = new TranscriptBuffer();
        var previous = tracker.Observe("接続前の履歴。", now).ToArray();
        if (previous.Length != 0) throw new Exception("Imported old history");
        var first = tracker.Observe("接続前の履歴。今日は", Ms(100)).Single(); transcript.Update(first);
        var revised = tracker.Observe("接続前の履歴。今日も晴れです。", Ms(200)).Single(); transcript.Update(revised);
        if (first.Id != revised.Id || revised.Revision <= first.Revision || revised.English != "今日も晴れです。") throw new Exception("Revision was appended instead of replaced");
        var final = tracker.Observe("接続前の履歴。今日も晴れです。", Ms(1000)).Single(); transcript.Update(final);
        if (!final.Final) throw new Exception("Idle caption did not finalize");
        var next = tracker.Observe("接続前の履歴。今日も晴れです。次の発話。", Ms(1100)).Single(); transcript.Update(next);
        if (next.Id <= final.Id || next.English != "次の発話。") throw new Exception("New phrase repeated old history");
        tracker.ResetBaseline("再接続前の履歴。");
        var reconnect = tracker.Observe("再接続前の履歴。接続後の発話。", Ms(1200)).Single(); transcript.Update(reconnect);
        if (reconnect.Id <= next.Id || transcript.Export(true).Contains("接続前の履歴")) throw new Exception("Reconnect reused IDs or imported history");
        var punctuation = new WindowsCaptionTracker(true); punctuation.ResetBaseline("");
        var phrase = punctuation.Observe("最初の文", Ms(1200)).Single();
        punctuation.Observe("最初の文", Ms(2100)).ToArray();
        var delayed = punctuation.Observe("最初の文。次の文", Ms(2200)).ToArray();
        if (delayed.Length != 2 || delayed[0].Id != phrase.Id || delayed[0].English != "最初の文。" || delayed[1].English != "次の文") throw new Exception("Delayed punctuation started an orphan row");
        var english = new WindowsCaptionTracker(false); english.ResetBaseline("");
        var draft = english.Observe("We have more latitude", Ms(2500)).Single();
        english.Observe("We have more latitude", Ms(4000)).ToArray();
        var rewrite = english.Observe("We get more latitude in post", Ms(4100)).Single();
        if (rewrite.Id != draft.Id || rewrite.English != "We get more latitude in post") throw new Exception("Revised English repeated finalized words");
        var rolling = new WindowsCaptionTracker(false); rolling.ResetBaseline("Old history. Kept text. ");
        var roll = rolling.Observe("Kept text. New speech.", Ms(1300)).Single();
        if (roll.English != "New speech.") throw new Exception("Rolling text duplicated scrollback");
        var window = new WindowsCaptionTracker(false); window.ResetBaseline("");
        var windowFirst = window.Observe("We discuss cameras and detailed images. You get more latitude", Ms(5000)).ToArray();
        var buffer = new TranscriptBuffer(); foreach(var c in windowFirst) buffer.Update(c);
        foreach(var c in window.Observe("detailed images. You get more latitude in post.", Ms(5100))) buffer.Update(c);
        foreach(var c in window.Observe("You get more latitude in post. Your subject gets loud.", Ms(5200))) buffer.Update(c);
        var exported = buffer.Export(false);
        if(exported.Split("You get more latitude").Length!=2 || !exported.Contains("We discuss cameras")) throw new Exception("Rolling caption window duplicated text or lost context: "+exported);
        var cleared = new WindowsCaptionTracker(false); cleared.ResetBaseline("");
        var previousRow = cleared.Observe("These are the previous words.", Ms(5400)).Last();
        var newRow = cleared.Observe("Now consider different audio.", Ms(5500)).Last();
        if(newRow.Id<=previousRow.Id || newRow.English!="Now consider different audio.") throw new Exception("A replaced Windows window erased a preceding sentence");
        var longCaption = new WindowsCaptionTracker(true); longCaption.ResetBaseline("");
        var longText = string.Concat(Enumerable.Repeat("長い文章も保存します。", 30));
        var rows = longCaption.Observe(longText, Ms(1500)).ToArray();
        if (rows.Length < 2 || string.Concat(rows.Select(c => c.English)) != longText) throw new Exception("Long caption lost text");
        // Several hours of rolling OS text must not grow the alignment state.
        foreach (bool japanese in new[] { true, false })
        {
            var longRun = new WindowsCaptionTracker(japanese); longRun.ResetBaseline("");
            var all = new System.Text.StringBuilder(); var actual = new TranscriptBuffer(); string visible = "";
            for (int i = 1; i <= 20000; i++)
            {
                string segment = japanese ? $"記録番号{i:D6}の発話です。" : $"Recording number {i:D6} is complete. ";
                all.Append(segment); visible += segment;
                if (visible.Length > 600) visible = visible[^600..];
                foreach (var update in longRun.Observe(visible, now + Stopwatch.Frequency * i)) actual.Update(update);
            }
            foreach (var update in longRun.Observe(visible, now + Stopwatch.Frequency * 20003L)) actual.Update(update);
            string joined = actual.Export(false).Replace("\r", "").Replace("\n", "").Replace(" ", "");
            string expected = all.ToString().Replace(" ", "");
            int retained = ((string)typeof(WindowsCaptionTracker).GetField("completeText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(longRun)!).Length;
            if (joined != expected || retained > 10000) throw new Exception($"Long Windows session lost/duplicated text or retained unbounded alignment: japanese={japanese}, retained={retained}, expected={expected.Length}, actual={joined.Length}");
            Console.WriteLine($"PASS: 20,000 {(japanese ? "Japanese" : "English")} phrases, all text retained, alignment={retained} chars");
        }
        Console.WriteLine("PASS: Windows captions baseline, revision replacement, idle finalization, new speech, reconnect IDs, rolling history, delayed punctuation, long-text retention");
    }
}
