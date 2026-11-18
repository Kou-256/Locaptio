using Locaptio.Core.AI;

internal static class PreparationChecks
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static async Task RunAsync(bool realWorker)
    {
        // Windows-captions translation needs no speech model, and must not touch .venv while it is ready.
        string root = Directory.CreateTempSubdirectory("locaptio-prep-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".venv", "Scripts"));
            File.WriteAllText(Path.Combine(root, ".venv", "Scripts", "python.exe"), "");
            File.WriteAllText(Path.Combine(root, ".venv", ".locaptio-speech-v2"), "ready");
            using var quick = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await SpeechPreparation.EnsureTranslationAsync(root, false, null, quick.Token);
            Directory.CreateDirectory(Path.Combine(root, "models", "translation"));
            File.WriteAllText(Path.Combine(root, "models", "translation", "model.bin"), "");
            await SpeechPreparation.EnsureTranslationAsync(root, true, null, quick.Token);
            Check(!Directory.Exists(Path.Combine(root, "models", "speech-base")), "Translation-only preparation fetched a speech model");
            // While another preparation holds the environment, the wait is reported instead of failing.
            await SpeechPreparation.EnvironmentGate.WaitAsync();
            string? waiting = null;
            File.Delete(Path.Combine(root, "models", "translation", "model.bin"));
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            try { await SpeechPreparation.EnsureTranslationAsync(root, true, p => waiting = p.Stage, cancel.Token); } catch (OperationCanceledException) { }
            finally { SpeechPreparation.EnvironmentGate.Release(); }
            Check(waiting?.Contains("待って") == true, "Waiting for another preparation was not reported");
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine("PASS: translation-only preparation skips speech models and waits for other setups");
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
