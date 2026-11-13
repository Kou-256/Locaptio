namespace Locaptio.Bridge;

internal interface ICaptionMicrophoneUi
{
    void Open();
    bool? Enabled { get; }
    void Toggle();
    bool WaitFor(bool enabled);
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
            if (!ui.WaitFor(desired)) throw new InvalidOperationException("Windows側のマイク設定の変更を確認できませんでした。");
        }
        finally { ui.Close(); }
    }
}
