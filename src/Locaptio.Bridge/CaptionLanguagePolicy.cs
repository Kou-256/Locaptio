using System.Globalization;

namespace Locaptio.Bridge;

internal interface ICaptionLanguageUi
{
    string? ReadyLabel => null;
    void Open();
    string Selected { get; }
    string[] Choices();
    void Select(string name);
    void Confirm();
    bool WaitReady();
}

internal readonly record struct CaptionLanguageResult(bool Success, string Message);

internal interface ICaptionConfirmationUi
{
    bool CaptionInfoVisible { get; }
    bool PickerVisible { get; }
    bool CaptionViewVisible { get; }
    void DismissCaptionInfo();
    bool TryContinue();
}

internal static class CaptionLanguageConfirmation
{
    // The informational tip may arrive after selection or reappear during a
    // transition. Never treat a temporarily hidden picker as confirmation.
    internal static void Confirm(ICaptionConfirmationUi ui, Func<long>? clock = null, Action<int>? pause = null)
    {
        clock ??= () => Environment.TickCount64; pause ??= Thread.Sleep;
        long deadline = clock() + 6000, nextAttempt = clock() + 200, readySince = -1;
        while (clock() < deadline)
        {
            if (ui.CaptionInfoVisible)
            {
                readySince = -1; ui.DismissCaptionInfo(); nextAttempt = clock() + 200;
            }
            else if (!ui.PickerVisible && ui.CaptionViewVisible)
            {
                if (readySince < 0) readySince = clock();
                if (clock() - readySince >= 300) return;
            }
            else
            {
                readySince = -1;
                if (ui.PickerVisible && clock() >= nextAttempt) { ui.TryContinue(); nextAttempt = clock() + 750; }
            }
            pause(100);
        }
        throw new InvalidOperationException("Windows字幕の言語画面で続行を完了できませんでした。");
    }
}

internal static class CaptionLanguagePolicy
{
    internal static bool ReadyLabelMatches(string? text, string code)
    {
        if (text is null || code is not ("ja-JP" or "en-US")) return false;
        string[] labels = code == "ja-JP" ? ["日本語 (日本)", "Japanese (Japan)"] : ["英語 (米国)", "English (United States)"];
        return labels.Any(label => text.Contains(label, StringComparison.OrdinalIgnoreCase));
    }
    internal static bool Matches(string label, string code)
    {
        if (code is not ("ja-JP" or "en-US")) return false;
        var culture = CultureInfo.GetCultureInfo(code);
        string[] labels = code == "ja-JP"
            ? ["日本語 (日本)", "Japanese (Japan)", code, culture.DisplayName, culture.NativeName, culture.EnglishName]
            : ["英語 (米国)", "English (United States)", code, culture.DisplayName, culture.NativeName, culture.EnglishName];
        string Normalize(string text) => text.Replace('（', '(').Replace('）', ')').Trim();
        return labels.Any(name => string.Equals(Normalize(name), Normalize(label), StringComparison.OrdinalIgnoreCase));
    }
    internal static CaptionLanguageResult Synchronize(ICaptionLanguageUi ui, string code)
    {
        if (code is not ("ja-JP" or "en-US")) return new(false, "対応していない字幕言語です。");
        string? original = null; bool confirmed = false, targetVerified = false;
        try
        {
            if (ReadyLabelMatches(ui.ReadyLabel, code)) return new(true, "Windows字幕は指定した言語で準備済みです。");
            ui.Open(); original = ui.Selected;
            if (!Matches(original, code))
            {
                string? target = ui.Choices().FirstOrDefault(name => Matches(name, code));
                if (target is null) throw new InvalidOperationException("指定言語がWindows側の一覧にありません。");
                ui.Select(target);
            }
            if (!Matches(ui.Selected, code)) throw new InvalidOperationException("選択言語を確認できませんでした。");
            targetVerified = true;
            ui.Confirm(); confirmed = true;
            if (!ui.WaitReady()) return new(false, "言語の準備完了を確認できません。Windows側で言語ファイルの準備・ダウンロードを完了してください。");
            return new(true, $"Windows字幕の言語を{(code == "ja-JP" ? "日本語" : "英語（米国）")}に合わせました。");
        }
        catch (Exception ex)
        {
            // A failed selection must not silently accept an unintended language.
            if (!confirmed && !targetVerified && original is not null)
                try { if (ui.Selected != original) ui.Select(original); ui.Confirm(); } catch { }
            return new(false, targetVerified
                ? "指定言語は選択済みですが、続行を完了できませんでした。Windows側の言語画面を確認してください。" + ex.Message
                : "Windows字幕の言語を自動変更できません。Windows側で言語を選択してください。" + ex.Message);
        }
    }
}

// A failed automatic action is not a permanent error once the user resumes captions.
internal sealed class CaptionLanguageRecoveryState(string code)
{
    public bool WarningPending { get; private set; }
    public void Failed() => WarningPending = true;
    public void Verified() => WarningPending = false;
    public string? Observe(string? readyLabel, bool captionUpdated)
    {
        if (!WarningPending) return null;
        if (CaptionLanguagePolicy.ReadyLabelMatches(readyLabel, code)) { Verified(); return "language-ready"; }
        if (captionUpdated) { Verified(); return "language-manual"; }
        return null;
    }
}
