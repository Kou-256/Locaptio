namespace Locaptio.Core;

public enum CaptionRecoveryDecision { None, Restart, LimitReached }

/// <summary>Bounded audio-without-text detection. No audio buffers or transcript are retained.</summary>
public sealed class WindowsCaptionWatchdog(long startedAt)
{
    private long previous = startedAt, lastText = startedAt, lastAudio = startedAt;
    private long audibleMilliseconds;
    private readonly Queue<long> attempts = new();
    private bool limitReported;

    public void ResetObservation(long now)
    {
        previous = lastText = lastAudio = now; audibleMilliseconds = 0;
    }

    public CaptionRecoveryDecision Observe(long now, bool audible, bool textChanged)
    {
        long elapsed = Math.Clamp(now - previous, 0, 1500); previous = now;
        if (textChanged) { lastText = now; audibleMilliseconds = 0; limitReported = false; }
        if (audible) { lastAudio = now; audibleMilliseconds += elapsed; }
        else if (now - lastAudio >= 3000) audibleMilliseconds = 0;
        while (attempts.TryPeek(out long oldest) && now - oldest >= 600_000) { attempts.Dequeue(); limitReported = false; }
        if (!audible || now - lastText < 20_000 || audibleMilliseconds < 20_000) return CaptionRecoveryDecision.None;
        if (attempts.Count > 0 && now - attempts.Last() < 60_000) return CaptionRecoveryDecision.None;
        if (attempts.Count >= 3)
        {
            if (limitReported) return CaptionRecoveryDecision.None;
            limitReported = true;
            return CaptionRecoveryDecision.LimitReached;
        }
        attempts.Enqueue(now); ResetObservation(now);
        return CaptionRecoveryDecision.Restart;
    }
}
