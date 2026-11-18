using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Locaptio.WinUI;

/// <summary>
/// What happens to the text on screen: keeping it when a new recording starts, Clear while a saved
/// recording is open, and returning to the start screen (Home).
/// </summary>
public sealed partial class MainWindow
{
    private const string StartTitle = "文字起こし（録音なし）／音声も残すときは「新規録音」、保存済みデータは「録音一覧」から";
    /// <summary>The user chose to continue after the text on screen instead of clearing it.</summary>
    private bool keepExistingText;
    /// <summary>Self-checks answer the close-review confirmation.</summary>
    private Func<Task<bool>>? confirmCloseReviewForVerification;

    /// <summary>
    /// Before a new recording: keep the text and continue after it, or clear it (offering to save first).
    /// Returns false when cancelled. Sets <see cref="keepExistingText"/>.
    /// </summary>
    private async Task<bool> ChooseExistingTextAsync(string action)
    {
        keepExistingText = false;
        if (history.Count == 0) return true;
        if (settings.ExistingTextOnRecording == "keep") { keepExistingText = true; return true; }
        if (settings.ExistingTextOnRecording == "clear") return await ConfirmTranscriptReplacementAsync(action + "のため、表示中のテキストをクリアします。");
        bool previousDialog = dialogOpen; dialogOpen = true;
        int choice; bool remember;
        try
        {
            var keep = new RadioButton { Content = "引き継ぐ：表示中のテキストの続きに記録する", IsChecked = true, GroupName = "existing" };
            var save = new RadioButton { Content = "保存してからクリアする", GroupName = "existing" };
            var discard = new RadioButton { Content = "保存せずにクリアする", GroupName = "existing" };
            var always = new CheckBox { Content = "次回からこの選択を使う（設定の「保存先・録音」で変更できます）" };
            var content = new StackPanel { Spacing = 8, MaxWidth = 520 };
            content.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Text = $"{action}を始めます。表示中のテキスト（{history.Count} 件）をどうしますか？" });
            content.Children.Add(keep); content.Children.Add(save); content.Children.Add(discard);
            content.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = .8, Text = "引き継いだテキストは画面と保存するテキストに残ります。録音・録画ファイルの文字起こしには、ここから話した内容が保存されます。" });
            content.Children.Add(always);
            var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "表示中のテキスト", Content = content, PrimaryButtonText = "開始", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Primary, RequestedTheme = Root.RequestedTheme };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || closing) return false;
            choice = keep.IsChecked == true ? 0 : save.IsChecked == true ? 1 : 2; remember = always.IsChecked == true;
        }
        finally { dialogOpen = previousDialog; }
        if (remember) { settings.ExistingTextOnRecording = choice == 0 ? "keep" : "clear"; settings.Save(); }
        if (choice == 0) { keepExistingText = true; return true; }
        return choice == 2 || await SaveTranscriptFileAsync();
    }

    /// <summary>Clears the text for a new recording unless the user chose to keep it.</summary>
    private void PrepareTextForNewRecording()
    {
        LeaveReview();
        if (keepExistingText) Status($"表示中のテキスト（{history.Count} 件）を引き継ぎました。録音・録画ファイルの文字起こしには、ここから話した内容が保存されます。");
        else ClearCaptions();
        windowsSession?.ResetTranscript();
    }

    /// <summary>Closes a saved recording that is open for review. Its files are not touched.</summary>
    private void LeaveReview()
    {
        if (recordingStop is not null) return;
        ClosePlayback();
        reviewingRecording = false; playbackDocument = null; playbackPath = "";
        PlaybackPanel.Visibility = VideoPanel.Visibility = RecordingSearchPanel.Visibility = Visibility.Collapsed;
        VideoStill.Source = null; VideoStill.Visibility = Visibility.Collapsed;
        RefreshRecordingSearch(); RefreshRowMarks();
    }

    private async Task<bool> ConfirmCloseReviewAsync(string title)
    {
        if (confirmCloseReviewForVerification is not null) return await confirmCloseReviewForVerification();
        bool previousDialog = dialogOpen; dialogOpen = true;
        try
        {
            string kind = PlaybackClock.IsVideo(playbackPath) ? "録画" : "録音";
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot, Title = title,
                Content = $"見返し中の{kind}（{Path.GetFileName(Path.GetDirectoryName(playbackPath))}）と文章を閉じます。{kind}ファイルと文字起こしは削除せず、「録音一覧」からいつでも開けます。",
                PrimaryButtonText = "閉じる", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Primary, RequestedTheme = Root.RequestedTheme
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary && !closing;
        }
        finally { dialogOpen = previousDialog; }
    }

    private bool Reviewing => reviewingRecording && recordingStop is null && playbackPath.Length > 0;

    private async void ClearClick(object sender, RoutedEventArgs e) => await ClearAsync();
    private async Task ClearAsync()
    {
        if (dialogOpen || closing) return;
        // An open recording's text is already saved with it; closing it also closes its audio or video.
        if (Reviewing)
        {
            if (!await ConfirmCloseReviewAsync("見返し中の録音・録画を閉じますか？")) return;
            await reviewWrite; LeaveReview();
        }
        else if (!await ConfirmTranscriptReplacementAsync("表示中のテキストをクリアします。")) return;
        ClearCaptions(); windowsSession?.ResetTranscript();
        SessionTitle.Text = captionStop is null ? StartTitle : SessionTitle.Text;
    }

    private async void HomeClick(object sender, RoutedEventArgs e) => await GoHomeAsync();
    /// <summary>Back to the start screen: no recording open, no text, no side panels.</summary>
    private async Task GoHomeAsync()
    {
        if (dialogOpen || closing) return;
        if (recordingStop is not null || captionStop is not null) { Status("録音・文字起こしを停止してから、ホームに戻れます。"); return; }
        if (Reviewing) { if (!await ConfirmCloseReviewAsync("ホームに戻りますか？")) return; await reviewWrite; }
        else if (history.Count > 0 && !await ConfirmTranscriptReplacementAsync("ホームに戻ります。")) return;
        LeaveReview(); ClearCaptions(); windowsSession?.ResetTranscript();
        ShowSummaryPane(false); HideStatusDetails();
        SessionTitle.Text = StartTitle; Status("ホームに戻りました。");
    }
}
