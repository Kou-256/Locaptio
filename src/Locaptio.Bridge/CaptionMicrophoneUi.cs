using System.Windows.Automation;

namespace Locaptio.Bridge;

internal sealed class CaptionMicrophoneUi(nint handle) : ICaptionMicrophoneUi
{
    private AutomationElement? microphone, preferences;
    private AutomationElement[] Roots()
    {
        var root = AutomationElement.FromHandle(handle);
        return new[] { root }.Concat(AutomationElement.RootElement.FindAll(TreeScope.Children,
            new PropertyCondition(AutomationElement.ProcessIdProperty, root.Current.ProcessId)).Cast<AutomationElement>()).ToArray();
    }
    private static string Normalize(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c) && c != '&')).ToLowerInvariant();
    private AutomationElement? FindId(string id) => Roots().Select(root => root.FindFirst(TreeScope.Descendants,
        new AndCondition(new PropertyCondition(AutomationElement.AutomationIdProperty, id), new PropertyCondition(AutomationElement.IsOffscreenProperty, false)))).FirstOrDefault(item => item is not null);
    private AutomationElement? FindMenu(params string[] names) => Roots().SelectMany(root => root.FindAll(TreeScope.Descendants,
        new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem), new PropertyCondition(AutomationElement.IsOffscreenProperty, false))).Cast<AutomationElement>())
        .FirstOrDefault(item => names.Any(name => Normalize(name) == Normalize(item.Current.Name)));
    private AutomationElement? FindMicrophone() => FindId("MicrophoneMenuFlyoutItem") ?? FindMenu("マイク オーディオを含める", "マイク音声を含める", "Include microphone audio");
    private static void Invoke(AutomationElement item)
    {
        if (item.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var expand)) { ((ExpandCollapsePattern)expand).Expand(); return; }
        if (item.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke)) { ((InvokePattern)invoke).Invoke(); return; }
        throw new InvalidOperationException("Windows側のメニューを操作できませんでした。");
    }
    private static AutomationElement Wait(Func<AutomationElement?> find)
    {
        long until = Environment.TickCount64 + 3000;
        do { if (find() is { } item && item.Current.IsEnabled) return item; Thread.Sleep(100); } while (Environment.TickCount64 < until);
        throw new InvalidOperationException("Windows側の「設定 → マイクオーディオを含める」が見つかりませんでした。");
    }
    public void Open()
    {
        if ((microphone = FindMicrophone()) is not null) return;
        if ((preferences = FindId("PreferencesButton") ?? FindMenu("設定", "基本設定", "Preferences", "Settings")) is null)
        {
            var settings = AutomationElement.FromHandle(handle).FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "SettingsButton"))
                ?? throw new InvalidOperationException("Windows字幕の設定ボタンが見つかりませんでした。");
            Invoke(settings);
            preferences = Wait(() => FindId("PreferencesButton") ?? FindMenu("設定", "基本設定", "Preferences", "Settings"));
        }
        Invoke(preferences);
        microphone = Wait(FindMicrophone);
    }
    public bool? Enabled
    {
        get
        {
            microphone = FindMicrophone();
            if (microphone?.TryGetCurrentPattern(TogglePattern.Pattern, out var toggle) == true)
                return ((TogglePattern)toggle).Current.ToggleState switch { ToggleState.On => true, ToggleState.Off => false, _ => null };
            return null;
        }
    }
    public void Toggle()
    {
        if (microphone is null) throw new InvalidOperationException("Windowsのマイク設定がありません。");
        if (microphone.TryGetCurrentPattern(TogglePattern.Pattern, out var toggle)) ((TogglePattern)toggle).Toggle();
        else Invoke(microphone);
    }
    public bool WaitFor(bool enabled)
    {
        long until = Environment.TickCount64 + 3000;
        do
        {
            if (FindMicrophone() is null) Open();
            if (Enabled == enabled) return true;
            Thread.Sleep(100);
        } while (Environment.TickCount64 < until);
        return false;
    }
    public void Close()
    {
        try
        {
            if (preferences?.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var expand) == true) ((ExpandCollapsePattern)expand).Collapse();
            if (FindMenu("言語を変更", "Change language", "設定", "Preferences", "Settings") is not null)
            {
                var settings = AutomationElement.FromHandle(handle).FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "SettingsButton"));
                if (settings?.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke) == true) ((InvokePattern)invoke).Invoke();
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException) { }
    }
}
