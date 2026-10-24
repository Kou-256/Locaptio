namespace Locaptio.Bridge;

// Silence is still connected. Debounce brief window/provider rebuilds.
internal sealed class CaptionConnectionMonitor
{
    private bool connected;
    private long? missingSince;

    public bool Observe(bool available, long milliseconds)
    {
        if (available) { connected = true; missingSince = null; return false; }
        if (!connected) return false;
        missingSince ??= milliseconds;
        if (milliseconds - missingSince.Value < 500) return false;
        connected = false; missingSince = null;
        return true;
    }
}
