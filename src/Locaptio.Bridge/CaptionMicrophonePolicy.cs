namespace Locaptio.Bridge;

internal interface ICaptionMicrophoneUi
{
    void Open();
    bool? Enabled { get; }
    void Toggle();
    void Close();
}
internal static class CaptionMicrophonePolicy
{
    public static void Synchronize(ICaptionMicrophoneUi ui, bool desired)
    {
        try
        {
            ui.Open();
            bool current = ui.Enabled ?? throw new InvalidOperationException("Windows側のマイク設定の状態を読み取れませんでした。");
            if (current != desired) ui.Toggle();
            // Toggle is a synchronous UIA action. Reopening the dismissed flyout
            // to read its state again repeats the visible operation. Read once,
            // toggle only when needed, and close without a second menu pass.
        }
        finally { ui.Close(); }
    }
}
