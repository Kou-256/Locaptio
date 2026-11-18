using Locaptio.Core;
using Locaptio.Core.AI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Locaptio.WinUI;

// Local-LLM summary (key points, decisions, TODOs) with links back to the utterances it cites.
public sealed partial class MainWindow
{
    private SummaryItem[] summaryItems = [];
    // A local LLM kept for summaries between uses and released after ten idle minutes.
    private LocalLlmBackend? toolLlm;
    private string toolLlmKey = "";
    private long toolLlmUsed;
    private DispatcherTimer? toolLlmTimer;
    /// <summary>The LLM already loaded for live AI correction/translation, or one kept for summaries.</summary>
    private async Task<LocalLlmBackend> SummaryLlmAsync(CancellationToken token)
    {
        if (prepared is { Llm: { } live, LlmReady.IsCompletedSuccessfully: true }) return live;
        bool cpu = settings.CpuOnly || settings.InferenceDevice == "cpu";
        string key = root + "|" + LlmModelFile + "|" + cpu;
        if (toolLlm is not null && toolLlmKey == key) return toolLlm;
        toolLlm?.Dispose(); toolLlm = null;
        var llm = new LocalLlmBackend(); var report = BeginPreparation();
        llm.PreparationChanged += p => report(p with { Component = "文脈AI" });
        try { await llm.StartAsync(root, LlmModelFile, cpu, token); }
        catch { llm.Dispose(); throw; }
        toolLlm = llm; toolLlmKey = key; toolLlmUsed = System.Diagnostics.Stopwatch.GetTimestamp();
        if (toolLlmTimer is null)
        {
            toolLlmTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            toolLlmTimer.Tick += (_, _) =>
            {
                // Several GB of memory: do not hold it while the user does other work.
                if (toolLlm is null || captionStop is not null || System.Diagnostics.Stopwatch.GetElapsedTime(toolLlmUsed).TotalMinutes < 10) return;
                toolLlm.Dispose(); toolLlm = null;
            };
            toolLlmTimer.Start();
        }
        return llm;
    }
    private ISummaryBackend? summaryBackendForVerification;

    private void SummaryClick(object sender, RoutedEventArgs e)
    {
        if (closing) return;
        ShowSummaryPane(true);
        if (summaryItems.Length == 0) RenderSummary();
    }
    private void SummaryCloseClick(object sender, RoutedEventArgs e) => ShowSummaryPane(false);
    private void ShowSummaryPane(bool visible)
    {
        SummaryPane.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        // The pane sits beside the transcript rather than over it.
        double reserved = visible ? SummaryPane.Width + 10 : 0;
        CaptionList.Margin = new Thickness(0, 0, reserved, 0); Header.Margin = new Thickness(0, 0, reserved, 0);
        ResumeHistoryButton.Margin = new Thickness(0, 0, 24 + reserved, 12);
        if (visible) EnsurePlaybackSpace();
    }
    private void ClearSummary()
    {
        summaryItems = []; ResetLiveSummary();
        if (SummaryPane.Visibility == Visibility.Visible) RenderSummary();
    }
    private void RestoreSummary(RecordingDocument document)
    {
        summaryItems = (document.Summary ?? []).Select(item => new SummaryItem(item.Section, item.Text,
            item.Evidence.Select(seconds => RecordingTranscript.At(document, seconds + .0005)?.Id ?? 0).Where(id => id != 0).Distinct().ToArray())).ToArray();
        if (summaryItems.Length > 0 || SummaryPane.Visibility == Visibility.Visible) RenderSummary();
        if (summaryItems.Length > 0) ShowSummaryPane(true);
    }
    private void RenderSummary(string? message = null)
    {
        if (message is null && summaryItems.Length == 0 && liveBlocks.Count > 0) { RenderLiveSummary(); return; }
        SummaryContent.Children.Clear();
        SummaryCopyButton.IsEnabled = SummarySaveButton.IsEnabled = summaryItems.Length > 0;
        SummaryRunButton.Content = summaryItems.Length > 0 ? "作り直す" : "作成";
        if (message is not null || summaryItems.Length == 0)
        {
            SummaryContent.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, Text = message ?? (history.Count == 0
                ? "文字起こしが終わったら、ここに要点・決定事項・TODOを作成できます。"
                : "「作成」を押すと、表示中の文字起こしからローカルAIが要点・決定事項・TODOを抜き出します。") });
            if (message is null) SummaryContent.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = .8,
                Text = "・PC内のローカルLLMで処理し、文章は外部へ送信しません。\n・各項目の時刻を押すと、根拠にした発話へ移動します（録音があれば再生）。\n・AIの要約は誤りや抜けを含むことがあります。重要な内容は根拠の発話で確認してください。\n・文字起こし・録音を停止してから実行します。長い文字起こしは数分かかることがあります。" });
            return;
        }
        foreach (string section in TranscriptSummary.Sections)
        {
            var items = summaryItems.Where(i => i.Section == section).ToArray();
            SummaryContent.Children.Add(new TextBlock { Text = section, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0) });
            if (items.Length == 0) { SummaryContent.Children.Add(new TextBlock { Text = "該当なし", FontSize = 12, Opacity = .65 }); continue; }
            foreach (var item in items)
            {
                var block = new StackPanel { Spacing = 2, Padding = new Thickness(8, 6, 8, 6), CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(28, 128, 128, 128)) };
                block.Children.Add(new TextBlock { Text = item.Text, TextWrapping = TextWrapping.Wrap, FontSize = 13, IsTextSelectionEnabled = true });
                var evidence = new ToolbarPanel();
                foreach (long id in item.RowIds.Where(id => FindCaption(id) is not null).Take(6))
                {
                    var link = new HyperlinkButton { Content = "▶ " + RowTime(id), FontSize = 11, Padding = new Thickness(4, 0, 4, 0), MinHeight = 20 };
                    ToolTipService.SetToolTip(link, FindCaption(id)!.English);
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(link, "根拠の発話 " + RowTime(id) + " へ移動");
                    link.Click += (_, _) => JumpToRow(id, true);
                    evidence.Children.Add(link);
                }
                if (evidence.Children.Count == 0) evidence.Children.Add(new TextBlock { Text = "根拠の発話は示されていません", FontSize = 11, Opacity = .65 });
                block.Children.Add(evidence); SummaryContent.Children.Add(block);
            }
        }
        SummaryContent.Children.Add(new TextBlock { Text = "ローカルAIによる要約です。時刻を押して根拠の発話を確認できます。", TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = .65, Margin = new Thickness(0, 8, 0, 0) });
    }
    private string SummaryMarkdown() => TranscriptSummary.Markdown(summaryItems, id => FindCaption(id) is null ? null : RowTime(id));

    private async void SummaryRunClick(object sender, RoutedEventArgs e) => await RunSummaryAsync();
    private async Task RunSummaryAsync()
    {
        if (dialogOpen || closing) return;
        var rows = history.Select(row => (row.Id, row.English)).ToArray();
        if (rows.Length == 0) { RenderSummary("要約する文字起こしがありません。"); return; }
        if (captionStop is not null || recordingStop is not null) { RenderSummary("文字起こし・録音を停止してから「作成」を押してください。"); return; }
        root = FindRoot();
        string model = LlmModelFile;
        if (summaryBackendForVerification is null && root.Length > 0 && LocalLlmPreparation.Required(root, model))
        {
            // Never start a multi-gigabyte download from a single click without saying so.
            dialogOpen = true;
            try
            {
                var confirm = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "ローカルAIを準備しますか？", RequestedTheme = Root.RequestedTheme, PrimaryButtonText = "準備して要約", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Primary,
                    Content = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = "要約にはローカルLLM（Qwen3）を使います。初回だけAI環境とモデル（数GB）をダウンロードするため、ネット接続と空き容量が必要です。準備後の要約はPC内で実行します。" } };
                if (await confirm.ShowAsync() != ContentDialogResult.Primary || closing) return;
            }
            finally { dialogOpen = false; }
        }
        RenderSummary("要約を作成しています…"); SummaryRunButton.IsEnabled = false;
        SummaryItem[]? created = null;
        try
        {
            ClosePlayback();
            await RunExclusiveAsync("AI要約", async token =>
            {
                void Progress(int done, int total) => DispatcherQueue.TryEnqueue(() => ReportTask(token, $"AI要約：{done} / {total}", total == 0 ? 100 : done * 100.0 / total));
                if (summaryBackendForVerification is not null) { created = await TranscriptSummary.RunAsync(summaryBackendForVerification, rows, Progress, token); return; }
                var llm = await SummaryLlmAsync(token);
                created = await Task.Run(() => TranscriptSummary.RunAsync(llm, rows, Progress, token), token);
                toolLlmUsed = System.Diagnostics.Stopwatch.GetTimestamp();
            });
        }
        finally { SummaryRunButton.IsEnabled = true; }
        if (closing) return;
        if (created is null) { RenderSummary(summaryItems.Length == 0 ? "要約を作成できませんでした。下の「状態」で理由を確認できます。" : null); if (reviewingRecording) EnsurePlayback(); return; }
        if (created.Length == 0) { RenderSummary("要点・決定事項・TODOとして抜き出せる内容が見つかりませんでした。"); if (reviewingRecording) EnsurePlayback(); return; }
        summaryItems = created; RenderSummary();
        if (reviewingRecording && playbackDocument is not null)
        {
            var document = playbackDocument;
            playbackDocument = document with { Summary = created.Select(item => new RecordingSummaryItem(item.Section, item.Text,
                item.RowIds.Select(id => document.Phrases.FirstOrDefault(p => p.Id == id)?.Seconds ?? -1).Where(s => s >= 0).ToArray())).ToArray() };
            SaveReviewDocument();
            string path = RecordingFiles.SummaryFile(playbackPath), markdown = "# " + Path.GetFileNameWithoutExtension(playbackPath) + " の要約\n\n" + SummaryMarkdown();
            try { await File.WriteAllTextAsync(path, markdown, new System.Text.UTF8Encoding(false)); Status("AI要約を作成し、録音フォルダーに保存しました: " + path); }
            catch (Exception ex) { Status("AI要約を作成しましたが、ファイルに保存できません: " + ex.Message); }
            EnsurePlayback();
        }
        else Status("AI要約を作成しました。時刻を押すと根拠の発話へ移動します。");
    }
    /// <summary>Optional: summarize once a recording has been stopped and saved.</summary>
    private void QueueSummaryAfterRecording()
    {
        if (!settings.SummarizeAfterRecording || closing || inputChanging || VerificationRun) return;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () =>
        {
            // Let Stop finish closing captions and the recorder before the model is loaded.
            await Task.Delay(1500);
            if (closing || !reviewingRecording || captionStop is not null || recordingStop is not null || dialogOpen || history.Count < 3 || summaryItems.Length > 0) return;
            ShowSummaryPane(true); await RunSummaryAsync();
        });
    }
    private void SummaryCopyClick(object sender, RoutedEventArgs e)
    {
        if (summaryItems.Length == 0) return;
        try
        {
            var data = new Windows.ApplicationModel.DataTransfer.DataPackage(); data.SetText(SummaryMarkdown());
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data); Status("要約をコピーしました。");
        }
        catch (Exception ex) { Status("コピーできません: " + ex.Message); }
    }
    private async void SummarySaveClick(object sender, RoutedEventArgs e)
    {
        if (summaryItems.Length == 0 || dialogOpen || closing) return;
        dialogOpen = true;
        try
        {
            var picker = new Microsoft.Windows.Storage.Pickers.FileSavePicker(AppWindow.Id) { Title = "要約を保存", SuggestedFileName = Path.GetFileNameWithoutExtension(TranscriptExport.FileName(settings.TextFileNamePattern, DateTimeOffset.Now)) + "要約" };
            picker.FileTypeChoices.Add("Markdown", new List<string> { ".md" }); picker.FileTypeChoices.Add("テキスト", new List<string> { ".txt" });
            if (Directory.Exists(settings.TextSaveDirectory)) picker.SuggestedFolder = settings.TextSaveDirectory;
            var file = await picker.PickSaveFileAsync(); if (file is null || closing) return;
            await File.WriteAllTextAsync(file.Path, SummaryMarkdown(), new System.Text.UTF8Encoding(true)); Status("要約を保存しました: " + file.Path);
        }
        catch (Exception ex) { Status("要約を保存できません: " + ex.Message); }
        finally { dialogOpen = false; }
    }
}
