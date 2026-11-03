using Locaptio.Core.AI;

internal static class PreparationChecks
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static async Task RunAsync(bool realWorker)
    {
        var tracker = new PreparationTracker();
        tracker.Update(new("NPU", 0, 6, "環境確認"));
        Check(tracker.Percent == 0 && !tracker.Complete, "Preparation started completed");
        tracker.Update(new("NPU", 3, 6, "encoderをコンパイル中"));
        Check(tracker.Percent == 50 && tracker.Description.Contains("3/6") && !tracker.Complete, "Stage fraction was not reported correctly");
        tracker.Update(new("LLM", 0, 2, "読み込み中"));
        Check(tracker.Percent == 25, "Multiple model progress was not aggregated");
        tracker.Update(new("LLM", 2, 2, "準備完了")); tracker.Update(new("NPU", 6, 6, "準備完了"));
        Check(tracker.Percent == 100 && tracker.Complete && !tracker.Failed, "All-ready models did not reach 100%");
        tracker.Update(new("NPU", 6, 6, "検証失敗", true));
        Check(tracker.Failed && !tracker.Complete, "Failed warmup was marked ready");
        tracker.Clear(); Check(tracker.Percent == 0 && !tracker.Complete && !tracker.Failed, "New preparation retained old status");
        if (realWorker)
        {
            using var backend = new LocalBackend();
            var updates = new List<PreparationProgress>(); backend.PreparationChanged += updates.Add;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await backend.StartAsync(Directory.GetCurrentDirectory(), "speech", true, timeout.Token, false, "ja", "base", device: "cpu");
            Check(updates.Any(p => p.Completed == 0) && updates.Any(p => p.Completed == 2 && p.Total == 3) && updates.Last().Fraction == 1, "Actual worker startup did not report warmup and readiness stages");
            Console.WriteLine("PASS: real CPU Whisper worker protocol reported environment, model load, warmup and 100% ready without audio capture");
        }
        Console.WriteLine("PASS: NPU stage fractions, multiple models, remaining stages, ready/failure distinction and reset");
    }
}
