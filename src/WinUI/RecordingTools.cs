using Locaptio.Core;
using Locaptio.Core.AI;
using Locaptio.Core.Audio;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Locaptio.WinUI;

// Work on a saved recording: recognize it again, compare results, and add word times.
public sealed partial class MainWindow
{
    private static readonly string[] SpeechModelIds = ["tiny", "base", "small", "medium", "large-v3-turbo"];
    private static readonly string[] SpeechModelNames = ["tiny（最軽量）", "base（速度優先）", "small（速度と精度のバランス）", "medium（精度優先・負荷大）", "large-v3-turbo（GPU向け・高精度）"];

    private bool RequireOpenRecording(string action)
    {
        if (reviewingRecording && playbackDocument is not null && File.Exists(playbackPath) && recordingStop is null && captionStop is null) return true;
        Status(action + "は、保存済みの録音を開いてから実行します。「録音一覧」から録音を選んでください。"); ShowStatusDetails();
        return false;
    }

    /// <summary>
    /// Recognizes a whole audio file again. Returns nothing when cancelled or failed; the status line says why.
    /// Models already loaded for live captions are reused when they match, and are never unloaded here,
    /// so the next Start does not have to load them again.
    /// </summary>
    private async Task<List<RecordingPhrase>?> RecognizeRecordingAsync(string label, string audio, string model, bool japanese, bool translate, bool correct)
    {
        List<RecordingPhrase>? result = null;
        ClosePlayback();
        await RunExclusiveAsync(label, async token =>
        {
            bool cpu = settings.CpuOnly || settings.InferenceDevice == "cpu";
            string language = japanese ? "ja" : "en", prompt = Terms.SpeechPrompt(settings.SpeechPrompt);
            var report = BeginPreparation();
            var live = prepared is { IsUsable: true } ready && ready.AccuracyReady.IsCompletedSuccessfully && preparedModel == (japanese, model, prompt)
                && (!translate || ready.Translation is not null && ready.Ready.IsCompletedSuccessfully) && (!correct || ready.LlmReady is { IsCompletedSuccessfully: true }) ? ready : null;
            PreparedInference? own = null;
            try
            {
                if (live is null)
                {
                    await SpeechPreparation.EnsureAsync(root, model, translate, cpu ? "cpu" : settings.InferenceDevice, report, token);
                    own = new PreparedInference(root, japanese, model, prompt, settings.InferenceDevice, cpu, false, correct, LlmModelFile, Status, progress: report);
                    await Task.WhenAll(own.Ready, own.AccuracyReady).WaitAsync(token);
                    if (own.LlmReady is { } llmReady) await llmReady.WaitAsync(token);
                }
                var models = live ?? own!;
                // Whole-file chunks take longer than live captions; recognition context must not carry over.
                var timeouts = (models.Speech.RequestTimeout, models.Translation?.RequestTimeout);
                models.Speech.RequestTimeout = TimeSpan.FromMinutes(5); if (models.Translation is not null) models.Translation.RequestTimeout = TimeSpan.FromMinutes(5);
                models.Speech.ResetSpeechContext();
                if (correct && models.Llm is not null) models.Llm.CorrectionGlossary = Terms.CorrectionHint();
                try
                {
                    var phrases = new List<RecordingPhrase>(); var dictionary = Terms;
                    var progress = new Progress<double>(percent => ReportTask(token, $"{label}：{percent:F0}%", percent));
                    await Task.Run(() => FileTranscription.RunAsync(audio, models.Speech, language, phrase => { phrases.Add(phrase); return Task.CompletedTask; },
                        value => ((IProgress<double>)progress).Report(value), token, translate ? models.Translation : null, correct ? models.Llm : null, dictionary.Apply), token);
                    result = phrases;
                }
                finally
                {
                    models.Speech.RequestTimeout = timeouts.Item1; if (models.Translation is not null) models.Translation.RequestTimeout = timeouts.Item2 ?? TimeSpan.FromSeconds(30);
                    models.Speech.ResetSpeechContext();
                }
            }
            finally { own?.Dispose(); }
        });
        return result;
    }

    // Self-checks answer the dialogs themselves; the recognition, comparison data and saving are the real paths.
    private (string Model, bool Japanese, bool Correct)? retranscribeChoiceForVerification;
    private bool? adoptForVerification;
    private Task? recordingToolTask;
    private void RetranscribeClick(object sender, RoutedEventArgs e) => recordingToolTask = RetranscribeAsync();
    private async Task RetranscribeAsync()
    {
        if (dialogOpen || closing || !RequireOpenRecording("再文字起こし")) return;
        var current = playbackDocument!; string audio = playbackPath; string model; bool japanese, correct;
        if (retranscribeChoiceForVerification is { } chosen) (model, japanese, correct) = chosen;
        else
        {
        dialogOpen = true;
        try
        {
            bool wasTranslated = current.Phrases.Any(p => !string.IsNullOrWhiteSpace(p.Translation)) || current.Phrases.Length > 0 && current.Phrases.All(p => p.Language == "en");
            var models = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = SpeechModelNames,
                SelectedIndex = Math.Max(0, Array.IndexOf(SpeechModelIds, SpeechModelIds.Contains(settings.RetranscribeModel) ? settings.RetranscribeModel : settings.SpeechModel)) };
            var language = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "日本語の文字起こし", "英語 → 日本語 翻訳" }, SelectedIndex = wasTranslated ? 1 : 0 };
            var correction = new CheckBox { Content = "ローカルAIで文脈補正も行う（時間がかかります）", IsChecked = settings.CorrectTranscriptWithAi };
            var content = new StackPanel { Spacing = 10, MinWidth = 420 };
            content.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Text = "保存済みの音声をWhisperで認識し直し、現在の結果と並べて比較します。採用するまで現在の文章は変わりません。どちらの結果も「版」として残り、あとで切り替えられます。" });
            content.Children.Add(new TextBlock { Text = "現在の結果: " + (current.Model ?? "記録なし（Windows字幕連携または以前の版）") + $"／{current.Phrases.Length} 発話", FontSize = 12 });
            content.Children.Add(new TextBlock { Text = "音声認識モデル" }); content.Children.Add(models);
            content.Children.Add(new TextBlock { Text = "言語" }); content.Children.Add(language); content.Children.Add(correction);
            content.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Text = "用語辞書は自動で使います。未準備のモデルは初回だけダウンロードします（ネット接続が必要）。手動の編集と要約は現在の版に残り、新しい結果には引き継ぎません。ブックマークは時刻で引き継ぎます。" });
            var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "別のモデルで再文字起こし", Content = content, PrimaryButtonText = "開始", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Primary, RequestedTheme = Root.RequestedTheme };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || closing) return;
            model = SpeechModelIds[Math.Max(0, models.SelectedIndex)]; japanese = language.SelectedIndex == 0; correct = correction.IsChecked == true;
            settings.RetranscribeModel = model; settings.Save();
        }
        finally { dialogOpen = false; }
        }
        var phrases = await RecognizeRecordingAsync("再文字起こし", audio, model, japanese, !japanese, correct);
        if (phrases is null || closing) { if (!closing) EnsurePlayback(); return; }
        if (phrases.Count == 0) { Status("再文字起こしで発話を認識できませんでした。現在の文章は変更していません。"); ShowStatusDetails(pin: true); EnsurePlayback(); return; }
        string name = model + (correct ? "＋AI補正" : "");
        var candidate = new RecordingDocument(1, Path.GetFileName(audio), current.RecordedAt, false, phrases.ToArray(), current.Bookmarks, null, name);
        bool adopt = adoptForVerification ?? await ShowComparisonAsync(current, candidate, "現在の結果" + (current.Model is null ? "" : $"（{current.Model}）"), $"新しい結果（{name}）", "新しい結果を採用", "現在の結果を維持");
        try
        {
            if (adopt)
            {
                await TranscriptComparison.SaveVersionAsync(audio, "再文字起こし前" + (current.Model is null ? "" : $"（{current.Model}）"), current);
                await reviewWrite; await RecordingTranscript.SaveAsync(audio, candidate); await RecordingLibrary.SaveTextAsync(audio, candidate);
                await LoadRecordingAsync(audio); Status($"新しい結果（{name}）を採用しました。以前の結果は「版を比較・切替」から戻せます。");
            }
            else
            {
                await TranscriptComparison.SaveVersionAsync(audio, $"再文字起こし（{name}）", candidate);
                EnsurePlayback(); Status("現在の結果を維持しました。新しい結果は「版を比較・切替」に残しています。");
            }
        }
        catch (Exception ex) { Status("再文字起こしの結果を保存できません: " + ex.Message); ShowStatusDetails(pin: true); }
    }

    /// <summary>Side-by-side comparison aligned by time. Returns true when the right-hand result should replace the left.</summary>
    private async Task<bool> ShowComparisonAsync(RecordingDocument before, RecordingDocument after, string beforeTitle, string afterTitle, string adoptText, string keepText)
    {
        if (closing) return false;
        bool previous = dialogOpen; dialogOpen = true;
        try
        {
            var rows = TranscriptComparison.Align(before.Phrases, after.Phrases);
            int changed = rows.Count(r => r.Changed);
            double similarity = TranscriptComparison.Similarity(string.Concat(before.Phrases.Select(p => p.Text)), string.Concat(after.Phrases.Select(p => p.Text)));
            int beforeWords = before.Phrases.Count(p => p.Words is { Length: > 0 }), afterWords = after.Phrases.Count(p => p.Words is { Length: > 0 });
            var list = new StackPanel { Spacing = 4 };
            var onlyChanged = new CheckBox { Content = $"違いのある箇所だけ表示（{changed} / {rows.Count}）", IsChecked = changed > 0 && changed < rows.Count };
            var changedBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(46, 245, 185, 66));
            void Fill()
            {
                list.Children.Clear();
                foreach (var row in rows.Where(r => onlyChanged.IsChecked != true || r.Changed).Take(600))
                {
                    var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(8, 5, 8, 5), CornerRadius = new CornerRadius(4), Background = row.Changed ? changedBrush : null };
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) }); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition());
                    grid.Children.Add(new TextBlock { Text = FormatMediaTime(row.Seconds), FontFamily = new FontFamily("Consolas"), FontSize = 12, Opacity = .75 });
                    var left = new TextBlock { Text = row.Before.Length == 0 ? "（なし）" : row.Before, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, Opacity = row.Before.Length == 0 ? .5 : 1 };
                    var right = new TextBlock { Text = row.After.Length == 0 ? "（なし）" : row.After, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, Opacity = row.After.Length == 0 ? .5 : 1 };
                    Grid.SetColumn(left, 1); Grid.SetColumn(right, 2); grid.Children.Add(left); grid.Children.Add(right); list.Children.Add(grid);
                }
                if (list.Children.Count == 0) list.Children.Add(new TextBlock { Text = "違いはありません。", Opacity = .7 });
            }
            onlyChanged.Click += (_, _) => Fill(); Fill();
            var heading = new Grid { ColumnSpacing = 12, Padding = new Thickness(8, 0, 22, 0) };
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) }); heading.ColumnDefinitions.Add(new ColumnDefinition()); heading.ColumnDefinitions.Add(new ColumnDefinition());
            var leftTitle = new TextBlock { Text = beforeTitle, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            var rightTitle = new TextBlock { Text = afterTitle, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(leftTitle, 1); Grid.SetColumn(rightTitle, 2); heading.Children.Add(leftTitle); heading.Children.Add(rightTitle);
            var content = new StackPanel { Spacing = 8 };
            content.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Text =
                $"発話数 {before.Phrases.Length} → {after.Phrases.Length}／文字数 {before.Phrases.Sum(p => p.Text.Length)} → {after.Phrases.Sum(p => p.Text.Length)}／文字の一致率 {similarity:P0}／単語時刻のある発話 {beforeWords} → {afterWords}。発話の区切りが異なるため、時刻で対応付けて並べています。" });
            content.Children.Add(onlyChanged); content.Children.Add(heading);
            content.Children.Add(new ScrollViewer { Content = list, MaxHeight = 400, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 14, 0) });
            var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "文字起こし結果の比較", Content = content, PrimaryButtonText = adoptText, CloseButtonText = keepText, DefaultButton = ContentDialogButton.Close, RequestedTheme = Root.RequestedTheme };
            dialog.Resources["ContentDialogMaxWidth"] = 980.0;
            return await dialog.ShowAsync() == ContentDialogResult.Primary && !closing;
        }
        catch (Exception ex) { Status("比較を表示できません: " + ex.Message); return false; }
        finally { dialogOpen = previous; }
    }

    private async void VersionsClick(object sender, RoutedEventArgs e)
    {
        if (dialogOpen || closing || !RequireOpenRecording("版の比較")) return;
        string audio = playbackPath; var current = playbackDocument!;
        try
        {
            var versions = await TranscriptComparison.LoadVersionsAsync(audio);
            if (versions.Count == 0) { Status("この録音に以前の版はありません。「再文字起こし」や「同期を改善」を実行すると、実行前の結果が版として残ります。"); ShowStatusDetails(); return; }
            TranscriptVersion? selected;
            dialogOpen = true;
            try
            {
                var list = new ListView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 320 };
                foreach (var version in Enumerable.Reverse(versions))
                    list.Items.Add(new ListViewItem { Tag = version, Content = $"{version.SavedAt:yyyy/MM/dd HH:mm}　{version.Label}　（{version.Document.Phrases.Length} 発話）" });
                list.SelectedIndex = 0;
                var content = new StackPanel { Spacing = 10, MinWidth = 440 };
                content.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Text = "以前の文字起こし結果です（新しい順・最大10件）。選んだ版を現在の文章と並べて比較し、切り替えるかどうかを決められます。" });
                content.Children.Add(list);
                var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "文字起こしの版", Content = content, PrimaryButtonText = "現在の結果と比較…", CloseButtonText = "閉じる", DefaultButton = ContentDialogButton.Primary, RequestedTheme = Root.RequestedTheme };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary || closing) return;
                selected = (list.SelectedItem as ListViewItem)?.Tag as TranscriptVersion;
            }
            finally { dialogOpen = false; }
            if (selected is null) return;
            if (!await ShowComparisonAsync(current, selected.Document, "現在の結果", $"{selected.SavedAt:M/d HH:mm} {selected.Label}", "この版に切り替える", "現在の結果を維持")) return;
            await TranscriptComparison.SaveVersionAsync(audio, "切り替え前の結果" + (current.Model is null ? "" : $"（{current.Model}）"), current);
            ClosePlayback(); await reviewWrite;
            await RecordingTranscript.SaveAsync(audio, selected.Document); await RecordingLibrary.SaveTextAsync(audio, selected.Document);
            await LoadRecordingAsync(audio); Status("選んだ版に切り替えました。切り替え前の結果も版として残しています。");
        }
        catch (Exception ex) { Status("版を切り替えられません: " + ex.Message); ShowStatusDetails(pin: true); }
    }

    private void ImproveTimingClick(object sender, RoutedEventArgs e) => recordingToolTask = ImproveTimingAsync();
    private async Task ImproveTimingAsync()
    {
        if (dialogOpen || closing || !RequireOpenRecording("同期の改善")) return;
        var current = playbackDocument!; string audio = playbackPath;
        int timed = current.Phrases.Count(p => p.Words is { Length: > 0 });
        bool japanese = !current.Phrases.Any(p => p.Language == "en");
        string model = settings.SpeechModel;
        dialogOpen = true;
        try
        {
            var content = new StackPanel { Spacing = 10, MaxWidth = 460 };
            content.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Text = "音声をWhisperでもう一度解析し、いまの文章の各発話に、開始・終了と単語ごとの時刻を付けます。再生中のハイライト、文字をクリックしたときの再生位置、字幕ファイルの時刻のずれを減らします。" });
            content.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Text =
                $"・文章（編集・AI補正・訳文を含む）は変更しません。\n・現在、単語時刻のある発話は {timed} / {current.Phrases.Length} 件です" + (current.ApproximateTiming ? "。この録音の発話時刻は推定です" : "") + $"。\n・認識モデル: {model}（設定の音声認識モデル）。未準備の場合は初回だけダウンロードします。\n・解析結果と一致しない発話は、近くの発話に合わせて時刻だけ移動します。\n・実行前の結果は版として残り、「版を比較・切替」から戻せます。" });
            var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "音声と文章の同期を改善", Content = content, PrimaryButtonText = "開始", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Primary, RequestedTheme = Root.RequestedTheme };
            if (!VerificationRun && await dialog.ShowAsync() != ContentDialogResult.Primary || closing) return;
        }
        finally { dialogOpen = false; }
        // Fast first step without a model: start each utterance where speech begins in the audio.
        // It also helps utterances that recognition later cannot match word by word.
        var onsets = await Task.Run(() => SpeechOnsets.Align(current, SpeechOnsets.Find(SpeechOnsets.Envelope(audio))));
        var reference = await RecognizeRecordingAsync("同期の解析", audio, model, japanese, false, false);
        if (reference is null || closing)
        {
            if (closing) return;
            if (onsets.Snapped > 0)
            {
                // Recognition was cancelled or failed; still keep the audio-based start times.
                await TranscriptComparison.SaveVersionAsync(audio, "同期の改善前", current);
                await reviewWrite; await RecordingTranscript.SaveAsync(audio, onsets.Document); await RecordingLibrary.SaveTextAsync(audio, onsets.Document);
                await LoadRecordingAsync(audio);
                Status($"音声に合わせて {onsets.Snapped} / {onsets.Total} 件の発話の開始位置を補正しました。単語ごとの時刻は付けていません。");
            }
            else EnsurePlayback();
            return;
        }
        try
        {
            // Windows caption times are estimates and can be several seconds late; search a wider window for them.
            var result = await Task.Run(() => TimingAlignment.Apply(onsets.Document, reference, onsets.Document.ApproximateTiming ? 8 : 4));
            if (result.Aligned == 0 && onsets.Snapped > 0) result = new(onsets.Document, 0, result.Total);
            if (result.Aligned == 0 && onsets.Snapped == 0) { EnsurePlayback(); Status("音声の解析結果と一致する発話が見つからなかったため、時刻は変更していません。言語や認識モデルを確認してください。"); ShowStatusDetails(pin: true); return; }
            await TranscriptComparison.SaveVersionAsync(audio, "同期の改善前", current);
            await reviewWrite; await RecordingTranscript.SaveAsync(audio, result.Document); await RecordingLibrary.SaveTextAsync(audio, result.Document);
            await LoadRecordingAsync(audio);
            Status((onsets.Snapped > 0 ? $"音声に合わせて {onsets.Snapped} 件の開始位置を補正し、" : "") + $"{result.Aligned} / {result.Total} 件の発話に単語時刻を付けました。" + (result.Aligned < result.Total ? "一致しなかった発話は近くの発話に合わせて移動しています。" : "") + " 設定の「再生ハイライトの単位」で単語ごとの表示を選べます。");
        }
        catch (Exception ex) { Status("同期の結果を保存できません: " + ex.Message); ShowStatusDetails(pin: true); }
    }
}
