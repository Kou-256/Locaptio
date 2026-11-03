using System.Diagnostics;
using System.Windows.Automation;

namespace Locaptio.Bridge;

// Uses only known Live Captions accessibility controls; never accepts downloads,
// privacy prompts or arbitrary menus. Changes are requested explicitly by Locaptio.
internal sealed class CaptionLanguageUi(nint windowHandle) : ICaptionLanguageUi, ICaptionConfirmationUi
{
    private Thread? continueInvocation, closeTipInvocation;
    public string? ReadyLabel => PickerVisible || CaptionInfoVisible ? null : Find("ReadyToCaptionTextBlock")?.Current.Name;
    public bool CaptionInfoVisible => Find("CaptionOnlyInfoTextBlock") is not null;
    public bool PickerVisible => Find("SpeechModelDropDown") is not null;
    public bool CaptionViewVisible => !PickerVisible && (Find("CaptionsTextBlock") is not null || Find("ReadyToCaptionTextBlock") is not null);
    private AutomationElement? Find(string id)
    {
        var root = AutomationElement.FromHandle(windowHandle);
        var condition = new AndCondition(new PropertyCondition(AutomationElement.AutomationIdProperty, id), new PropertyCondition(AutomationElement.IsOffscreenProperty, false));
        if (root.FindFirst(TreeScope.Descendants, condition) is { } direct) return direct;
        // WinUI teaching tips and flyouts can be separate owned UIA roots.
        var owned = AutomationElement.RootElement.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ProcessIdProperty, root.Current.ProcessId));
        foreach (AutomationElement popup in owned)
            if (popup.FindFirst(TreeScope.Descendants, condition) is { } item) return item;
        return null;
    }
    private AutomationElement Wait(string id, int milliseconds = 2500)
    {
        long until = Environment.TickCount64 + milliseconds;
        do
        {
            if (CaptionInfoVisible) DismissCaptionInfo();
            if (Find(id) is { } element && element.Current.IsEnabled) return element;
            Thread.Sleep(100);
        } while (Environment.TickCount64 < until);
        throw new InvalidOperationException("Windows側の操作項目が見つかりません: " + id);
    }
    private static Thread Invoke(AutomationElement element)
    {
        // Some Windows providers keep Invoke blocked until their dialog closes.
        // A helper thread lets the calling flow discover and operate that dialog.
        Exception? failure = null;
        var action = new Thread(() => { try { ((InvokePattern)element.GetCurrentPattern(InvokePattern.Pattern)).Invoke(); } catch (Exception ex) { failure = ex; } }) { IsBackground = true };
        action.Start();
        if (action.Join(200) && failure is not null) throw failure;
        return action;
    }
    public void Open()
    {
        DismissCaptionInfo();
        long until = Environment.TickCount64 + 2000;
        while (CaptionInfoVisible && Environment.TickCount64 < until) { DismissCaptionInfo(); Thread.Sleep(100); }
        if (CaptionInfoVisible) throw new InvalidOperationException("字幕の案内が閉じるのを確認できませんでした。");
        if (Find("SpeechModelDropDown") is not null) return;
        Invoke(Wait("SettingsButton"));
        Invoke(Wait("ChangeLanguageMenuFlyoutItem"));
        Wait("SpeechModelDropDown");
    }
    public string Selected
    {
        get
        {
            var combo = Wait("SpeechModelDropDown");
            var selection = (SelectionPattern)combo.GetCurrentPattern(SelectionPattern.Pattern);
            return selection.Current.GetSelection().FirstOrDefault()?.Current.Name ?? "";
        }
    }
    public string[] Choices()
    {
        var combo = Wait("SpeechModelDropDown");
        if (combo.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var pattern)) ((ExpandCollapsePattern)pattern).Expand();
        Thread.Sleep(150);
        return combo.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem)).Cast<AutomationElement>().Select(e => e.Current.Name).ToArray();
    }
    public void Select(string name)
    {
        var combo = Wait("SpeechModelDropDown");
        if (combo.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var pattern)) ((ExpandCollapsePattern)pattern).Expand();
        var item = combo.FindFirst(TreeScope.Descendants, new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem), new PropertyCondition(AutomationElement.NameProperty, name)))
            ?? throw new InvalidOperationException("言語が一覧から見つかりません。");
        ((SelectionItemPattern)item.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        if (pattern is ExpandCollapsePattern expand) expand.Collapse();
        Thread.Sleep(300); DismissCaptionInfo();
    }
    public void DismissCaptionInfo()
    {
        if (closeTipInvocation is { IsAlive: true }) return;
        // Scope the close button to this known informational tip. Never use the
        // main window's CloseButton or acknowledge a download/privacy prompt.
        if (Find("CaptionOnlyInfoTextBlock") is not { } info) return;
        try
        {
            int processId = info.Current.ProcessId;
            var parent = TreeWalker.ControlViewWalker.GetParent(info);
            for (int i = 0; parent is not null && i < 6; i++, parent = TreeWalker.ControlViewWalker.GetParent(parent))
            {
                if (parent.Current.ProcessId != processId) break;
                var close = parent.FindFirst(TreeScope.Descendants, new AndCondition(
                    new PropertyCondition(AutomationElement.AutomationIdProperty, "AlternateCloseButton"),
                    new PropertyCondition(AutomationElement.IsOffscreenProperty, false)));
                if (close is not null && close.Current.IsEnabled) { closeTipInvocation = Invoke(close); return; }
            }
        }
        catch (ElementNotAvailableException) { /* The tip closed between observations. */ }
    }
    public bool TryContinue()
    {
        if (continueInvocation is { IsAlive: true } || CaptionInfoVisible || !PickerVisible
            || Find("SelectionReadyToCaptionTextBlock") is null) return false;
        try
        {
            var button = Find("ContinueButton");
            if (button is null || !button.Current.IsEnabled || !button.TryGetCurrentPattern(InvokePattern.Pattern, out _)) return false;
            continueInvocation = Invoke(button); return true;
        }
        catch (ElementNotAvailableException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
    public void Confirm() => CaptionLanguageConfirmation.Confirm(this);
    public bool WaitReady()
    {
        long until = Environment.TickCount64 + 8000;
        do
        {
            if (CaptionInfoVisible) DismissCaptionInfo();
            else if (CaptionViewVisible) return true;
            Thread.Sleep(100);
        } while (Environment.TickCount64 < until);
        return false;
    }
}
