using Locaptio.Core;
using Locaptio.WinUI;

internal static class WindowsRecoveryChecks
{
    public static async Task RunAsync()
    {
        var silent = new WindowsCaptionWatchdog(0);
        for (int t = 1000; t <= 300_000; t += 1000)
            Assert(silent.Observe(t, false, false) == CaptionRecoveryDecision.None, "Silence restarted Windows");
        var healthy = new WindowsCaptionWatchdog(0);
        for (int t = 1000; t <= 300_000; t += 1000)
            Assert(healthy.Observe(t, true, t % 5000 == 0) == CaptionRecoveryDecision.None, "Updating captions restarted Windows");
        var gaps = new WindowsCaptionWatchdog(0);
        for (int t = 1000; t <= 300_000; t += 1000)
            Assert(gaps.Observe(t, t % 10000 < 5000, false) == CaptionRecoveryDecision.None, "Short bursts accumulated across long silent gaps");
        var sleeping = new WindowsCaptionWatchdog(0);
        Assert(sleeping.Observe(300_000, true, false) == CaptionRecoveryDecision.None, "Sleep/resume counted unobserved audio");
        var stalled = new WindowsCaptionWatchdog(0); var attempts = new List<int>(); int limits = 0;
        for (int t = 1000; t <= 200_000; t += 1000)
        {
            var result = stalled.Observe(t, true, false);
            if (result == CaptionRecoveryDecision.Restart) attempts.Add(t);
            if (result == CaptionRecoveryDecision.LimitReached) limits++;
        }
        Assert(attempts.SequenceEqual(new[] { 20_000, 80_000, 140_000 }) && limits == 1, "Recovery threshold/cooldown/budget failed");
        CaptionRecoveryDecision afterWindow = CaptionRecoveryDecision.None;
        for (int t = 201_000; t <= 620_000; t += 1000) afterWindow = stalled.Observe(t, true, false);
        Assert(afterWindow == CaptionRecoveryDecision.Restart, "Budget did not expire after ten minutes");

        bool enabled = false, audible = true;
        var window = new FakeWindow(); var warnings = new List<string>(); var statuses = new List<string>();
        var recovery = new WindowsCaptionRecovery(() => enabled, statuses.Add, warnings.Add, () => audible, window, 0);
        await recovery.EnsureStartedAsync(CancellationToken.None);
        for (int t = 1000; t <= 50_000; t += 1000) Assert(!await recovery.TickAsync(t, false, CancellationToken.None), "Disabled recovery restarted Windows");
        enabled = true;
        for (int t = 51_000; t < 70_000; t += 1000) Assert(!await recovery.TickAsync(t, false, CancellationToken.None), "Early recovery");
        Assert(await recovery.TickAsync(70_000, false, CancellationToken.None), "Stalled bridge was not scheduled for replacement");
        Assert(window.Starts == 1 && window.Restarts == 1 && warnings.Count == 1 && statuses.Any(s => s.Contains("再表示しました")), "Recovery did not report visible-window completion");
        for (int t = 71_000; t <= 150_000; t += 1000) Assert(!await recovery.TickAsync(t, true, CancellationToken.None), "Recovered captions restarted again");

        var failedWindow = new FakeWindow { Restart = _ => throw new TimeoutException("fixture") };
        var failed = new WindowsCaptionRecovery(() => true, statuses.Add, warnings.Add, () => true, failedWindow, 0);
        for (int t = 1000; t <= 20_000; t += 1000) await failed.TickAsync(t, false, CancellationToken.None);
        Assert(warnings.Last().Contains("再表示できませんでした"), "Failed launch was reported as recovered");

        using var stop = new CancellationTokenSource();
        var blockingWindow = new FakeWindow { Restart = token => Task.Delay(Timeout.Infinite, token) };
        var cancelable = new WindowsCaptionRecovery(() => true, statuses.Add, warnings.Add, () => true, blockingWindow, 0);
        for (int t = 1000; t < 20_000; t += 1000) await cancelable.TickAsync(t, false, stop.Token);
        var pending = cancelable.TickAsync(20_000, false, stop.Token); stop.Cancel();
        try { await pending; throw new Exception("Stop did not cancel recovery"); } catch (OperationCanceledException) { }
        Assert(blockingWindow.Restarts == 1, "Canceled recovery retried");

        var meterError = new WindowsCaptionRecovery(() => true, statuses.Add, warnings.Add, () => throw new InvalidOperationException("meter fixture"), window, 0);
        for (int t = 1000; t <= 50_000; t += 1000) Assert(!await meterError.TickAsync(t, false, CancellationToken.None), "Meter error restarted Windows");
        Assert(window.Restarts == 1, "Meter failure was not safe");
        Console.WriteLine("PASS: audible stall, silence, active captions, short bursts, resume, cooldown, retry limit/expiry, ON/OFF, bridge replacement, launch failure, cancellation and audio-meter failure");
    }

    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class FakeWindow : IWindowsCaptionWindow
    {
        public int Starts, Restarts;
        public Func<CancellationToken, Task>? Restart;
        public Task EnsureStartedAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); Starts++; return Task.CompletedTask; }
        public Task RestartAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); Restarts++; return Restart?.Invoke(token) ?? Task.CompletedTask; }
    }
}
