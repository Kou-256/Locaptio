using Locaptio.Core;
using Microsoft.UI.Xaml;

namespace Locaptio.WinUI;

// Website shots for the four languages and live summary (v1.0.54–1.0.56), staged with fictional sample text.
// Part of --verify-site-screenshots; with --only-new only these are taken.
public sealed partial class MainWindow
{
    private static readonly (string Italian, string Spanish)[] SiteItalian =
    [
        ("Oggi parliamo di come organizzare le riunioni del team.", "Hoy hablamos de cómo organizar las reuniones del equipo."),
        ("Ogni settimana condividiamo i progressi e i prossimi passi.", "Cada semana compartimos los avances y los próximos pasos."),
        ("Le decisioni importanti vengono registrate nel verbale.", "Las decisiones importantes se registran en el acta."),
        ("Chi ha domande può scriverle nella chat.", "Quien tenga preguntas puede escribirlas en el chat."),
        ("La prossima riunione sarà giovedì alle dieci.", "La próxima reunión será el jueves a las diez.")
    ];

    private async Task CaptureFeatureScreensAsync(string shots, Func<string, int, Task> shot, Action<string> reset, Action<IEnumerable<(string, string?)>, string> feed, List<string> captured)
    {
        // Four languages: Italian speech translated into Spanish, with each selector open.
        reset("it>es"); feed(SiteItalian.Select(r => (r.Italian, (string?)r.Spanish)), "it");
        StatusText.Text = "Whisper · 話す言語：イタリア語 → スペイン語に翻訳（多言語モデル）";
        await shot("languages", 600);
        ModeSelector.OpenForScreenshot(); await shot("languages-spoken", 700); ModeSelector.CloseForScreenshot();
        TargetSelector.OpenForScreenshot(); await shot("languages-target", 700); TargetSelector.CloseForScreenshot();

        // Live summary while recording.
        reset("ja"); feed(SiteMeeting.Select(t => (t, (string?)null)), "ja");
        liveBlocks.Clear(); liveArchived.Clear(); liveDigest = null;
        liveBlocks.Add(new(1, 5,
        [
            new("要点", "新しい字幕機能のリリース日程を検討した", [1]),
            new("要点", "自動保存と編集はほぼ完成、要約機能はテストが不足している", [2, 3]),
            new("決定事項", "リリースは来月15日。要約機能はベータとして表示する", [4, 5])
        ]));
        liveBlocks.Add(new(6, 10,
        [
            new("要点", "用語辞書の初期データは次回の会議で検討する", [10]),
            new("TODO", "佐藤さん：要約機能のテスト結果を金曜までに共有", [6, 7]),
            new("TODO", "田中さん：ウェブサイトのイラストを来週水曜までに用意", [8, 9])
        ]));
        liveSummaryNextId = 11; settings.LiveSummary = true; LiveSummaryToggle.IsChecked = true;
        captionStop = new();
        SetRecordingChecked(true); RecordToggleText.Text = "録音中"; RecordingIndicator.Visibility = Visibility.Visible;
        RecordingText.Text = @"C:\Users\…\Music\Locaptio\定例会議\定例会議.wav"; RecordingClock.Text = "00:12:48";
        SessionTitle.Text = "新規録音中：定例会議";
        ShowSummaryPane(true); RenderLiveSummary(); UpdateCommandStates();
        StatusText.Text = "ライブ要約：約2.5分ごとに、ここまでの発話を要約しています。";
        await shot("live-summary", 800);

        // The same summary in its own, freely resizable window.
        SummaryPopOutClick(SummaryPopOutButton, new RoutedEventArgs()); await Task.Delay(900);
        if (summaryWindow?.Content is UIElement content) { await CaptureElementAsync(content, Path.Combine(shots, "live-summary-window.png")); captured.Add("live-summary-window"); }
        DockSummary(); await Task.Delay(300);

        captionStop.Dispose(); captionStop = null;
        SetRecordingChecked(false); RecordToggleText.Text = "新規録音"; RecordingIndicator.Visibility = Visibility.Collapsed;
        liveBlocks.Clear(); ShowSummaryPane(false); UpdateCommandStates();

        // Settings: live summary frequency, amount and length.
        SettingsClick(this, new RoutedEventArgs()); await Task.Delay(500);
        settingsNavigation!.Select("AI文字補正"); await Task.Delay(500);
        await settingsNavigation.CaptureAsync(Path.Combine(shots, "settings-live-summary.png")); captured.Add("settings-live-summary");
        settingsWindow?.Close(); await Task.Delay(300);
    }
}
