using Locaptio.Bridge;

internal static class WindowsLanguageChecks
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Run()
    {
        foreach (var (from, to, code) in new[] { ("日本語 (日本)", "英語 (米国)", "en-US"), ("English (United States)", "Japanese (Japan)", "ja-JP") })
        {
            var ui = new Fake(from, [from, to]); var result = CaptionLanguagePolicy.Synchronize(ui, code);
            Check(result.Success && ui.Selected == to && ui.Confirmed == 1 && ui.Selections == 1, "Language mode did not select and confirm the exact target");
        }
        var same = new Fake("日本語 (日本)", []);
        Check(CaptionLanguagePolicy.Synchronize(same, "ja-JP").Success && same.Selections == 0, "Already selected language was unnecessarily changed");
        var manuallyReady = new Fake("日本語 (日本)", []) { ReadyLabel = "日本語 (日本) のライブ キャプションを表示する準備ができました" };
        Check(CaptionLanguagePolicy.Synchronize(manuallyReady, "ja-JP").Success && !manuallyReady.Opened && manuallyReady.Confirmed == 0, "Manual language selection opened an unnecessary picker");
        var recovery = new CaptionLanguageRecoveryState("ja-JP"); recovery.Failed();
        Check(recovery.Observe("English (United States) captions ready", false) is null && recovery.WarningPending, "Wrong locale cleared a language warning");
        Check(recovery.Observe(manuallyReady.ReadyLabel, false) == "language-ready" && !recovery.WarningPending, "Manual language readiness did not clear warning");
        recovery.Failed();
        Check(recovery.Observe(null, true) == "language-manual" && !recovery.WarningPending && recovery.Observe(null, true) is null, "Resumed manual captions left a stale warning or repeated notifications");
        var unavailable = new Fake("日本語 (日本)", ["英語 (英国)", "English (Australia)"]);
        Check(!CaptionLanguagePolicy.Synchronize(unavailable, "en-US").Success && unavailable.Selected == "日本語 (日本)", "Unrelated English variant was silently selected");
        var wrong = new Fake("日本語 (日本)", ["英語 (米国)"]) { FailSelection = true };
        Check(!CaptionLanguagePolicy.Synchronize(wrong, "en-US").Success && wrong.Selected == "日本語 (日本)", "Failed selection did not preserve original language");
        var download = new Fake("英語 (米国)", []) { Ready = false };
        Check(!CaptionLanguagePolicy.Synchronize(download, "en-US").Success, "Model setup requirement was reported as ready");
        var unknown = new Fake("日本語 (日本)", []);
        Check(!CaptionLanguagePolicy.Synchronize(unknown, "unsupported").Success && !unknown.Opened, "Unsupported input opened Windows UI");
        var delayedConfirmation = new Fake("英語 (米国)", ["日本語 (日本)"]) { ThrowOnConfirm = true };
        Check(!CaptionLanguagePolicy.Synchronize(delayedConfirmation, "ja-JP").Success && delayedConfirmation.Selected == "日本語 (日本)" && delayedConfirmation.Confirmed == 1,
            "A confirmation failure reverted an already verified Japanese selection");
        var lateTip = new Confirmation { TipAppearsAt = 350, ContinueReadyAt = 1000 };
        CaptionLanguageConfirmation.Confirm(lateTip, () => lateTip.Time, ms => lateTip.Time += ms);
        Check(lateTip.Confirmed && lateTip.Dismissals > 0 && lateTip.Attempts >= 2 && !lateTip.InvokedBehindTip,
            "Delayed captions-only tip blocked confirmation or received a click through it");
        var repeatedTip = new Confirmation { TipAppearsAt = 0, ReappearAt = 1100, ContinueReadyAt = 1800 };
        CaptionLanguageConfirmation.Confirm(repeatedTip, () => repeatedTip.Time, ms => repeatedTip.Time += ms);
        Check(repeatedTip.Confirmed && repeatedTip.Dismissals >= 2 && !repeatedTip.InvokedBehindTip, "A reappearing tip blocked Continue");
        var notReady = new Confirmation { ContinueReadyAt = long.MaxValue };
        bool timedOut = false;
        try { CaptionLanguageConfirmation.Confirm(notReady, () => notReady.Time, ms => notReady.Time += ms); }
        catch (InvalidOperationException) { timedOut = true; }
        Check(timedOut && !notReady.Confirmed && notReady.Time <= 6100 && notReady.Attempts < 10, "Confirmation was unbounded or reported a pending picker as success");
        var advancedManually = new Confirmation { Confirmed = true };
        CaptionLanguageConfirmation.Confirm(advancedManually, () => advancedManually.Time, ms => advancedManually.Time += ms);
        Check(advancedManually.Attempts == 0, "An already advanced picker was clicked again");
        Console.WriteLine("PASS: late/reappearing captions-only tips, bounded Continue retries, manual completion and preserving verified Japanese after confirmation failure");
        Console.WriteLine("PASS: Japanese/English switching policy, exact language selection, unchanged language, missing target, verification failure, model-not-ready and unsupported input");
    }
    private sealed class Fake(string original, string[] choices) : ICaptionLanguageUi
    {
        public bool Opened, FailSelection, ThrowOnConfirm, Ready = true; public int Selections, Confirmed;
        public string Selected { get; private set; } = original;
        public string? ReadyLabel { get; init; }
        public void Open() => Opened = true;
        public string[] Choices() => choices;
        public void Select(string name) { Selections++; if (!FailSelection) Selected = name; }
        public void Confirm() { Confirmed++; if (ThrowOnConfirm) throw new InvalidOperationException("Continue transition failed"); }
        public bool WaitReady() => Ready;
    }
    private sealed class Confirmation : ICaptionConfirmationUi
    {
        public long Time, TipAppearsAt = long.MaxValue, ReappearAt = long.MaxValue, ContinueReadyAt;
        private long closeAt = long.MaxValue; private int tipGeneration;
        public int Attempts, Dismissals;
        public bool Confirmed, InvokedBehindTip;
        public bool CaptionInfoVisible
        {
            get
            {
                if (Time >= closeAt) { closeAt = long.MaxValue; tipGeneration++; }
                return tipGeneration == 0 ? Time >= TipAppearsAt : tipGeneration == 1 && Time >= ReappearAt;
            }
        }
        // A teaching tip can temporarily hide the underlying picker from UIA.
        public bool PickerVisible => !Confirmed && !CaptionInfoVisible;
        public bool CaptionViewVisible => Confirmed;
        public void DismissCaptionInfo()
        {
            if (closeAt != long.MaxValue) return;
            Dismissals++; closeAt = Time + 250;
        }
        public bool TryContinue()
        {
            Attempts++; InvokedBehindTip |= CaptionInfoVisible;
            if (!CaptionInfoVisible && Time >= ContinueReadyAt) Confirmed = true;
            return Confirmed;
        }
    }
}
