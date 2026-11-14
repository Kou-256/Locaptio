namespace Locaptio.Bridge;

// A transient UIA tree replacement is not a new recording boundary.
internal sealed class CaptionBaselineBoundary
{
    private string process = "";
    public bool Begin(string identity)
    {
        if (identity == process) return false;
        process = identity; return true;
    }
}
