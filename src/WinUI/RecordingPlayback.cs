using System.Diagnostics;
using Locaptio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Windowing;
using Microsoft.Windows.Storage.Pickers;
using NAudio.Wave;

namespace Locaptio.WinUI;
public sealed partial class MainWindow
{
    private RecordingTranscript? recordingTranscript;
    private RecordingDocument? playbackDocument;
    private string playbackPath = "";
    private WaveOutEvent? playbackOutput;
    private AudioFileReader? playbackReader;
    private readonly DispatcherTimer playbackTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer recordingIndexTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer searchTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private Task indexWrite = Task.CompletedTask;
    private bool indexDirty, updatingPlayback;
    private RecordingPhrase? highlightedPlaybackPhrase;
    private bool followPlayback = true;
    private bool reviewingRecording;
    private string RecordingRoot => string.IsNullOrWhiteSpace(settings.RecordingSaveDirectory)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Locaptio") : settings.RecordingSaveDirectory;
    private sealed record RecordingSearchHit(RecordingPhrase Phrase)
    {
        public string Time => FormatMediaTime(Phrase.Seconds);
        public string Text => Phrase.Text + (string.IsNullOrEmpty(Phrase.Translation) ? "" : " / " + Phrase.Translation);
    }
    private static string FormatMediaTime(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"hh\:mm\:ss");
    private void EnsurePlaybackSpace()
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        int height = Math.Min(area.Height, (int)(600 * GetDpiForWindow(hwnd) / 96.0));
        if (AppWindow.Size.Height < height) AppWindow.Resize(new Windows.Graphics.SizeInt32(AppWindow.Size.Width, height));
    }
    private void InitializeRecordingPlayback()
    {
        playbackTimer.Tick += (_, _) => UpdatePlaybackPosition();
        CaptionList.PointerWheelChanged += (_, _) => followPlayback = false;
        recordingIndexTimer.Tick += async (_, _) => { if (indexWrite.IsCompleted) await SaveRecordingIndexAsync(); };
        searchTimer.Tick += (_, _) => { searchTimer.Stop(); RefreshRecordingSearch(); };
        Closed += (_, _) => { ClosePlayback(); recordingIndexTimer.Stop(); searchTimer.Stop(); };
        var playSelection = new MenuFlyoutItem { Text = "選択した発話から再生" };
        playSelection.Click += (_, _) =>
        {
            if (textSelection is not { } selection) return;
            long id = Math.Min(selection.Anchor.Id, selection.Focus.Id);
            PlayCaption(id);
        };
        transcriptSelectionMenu.Items.Add(playSelection);
        transcriptSelectionMenu.Opening += (_, _) => playSelection.IsEnabled = HasTranscriptSelection && playbackDocument is not null && recordingStop is null;
    }
    private void BeginRecordingIndex(string path)
    {
        ClosePlayback(); PlaybackPanel.Visibility = Visibility.Collapsed;
        reviewingRecording = false; RecordingSearchPanel.Visibility = Visibility.Collapsed;
        recordingTranscript = new(path, recordingStarted, UseWindowsCaptionEngine);
        playbackDocument = null; playbackPath = path; indexDirty = true; recordingIndexTimer.Start();
        RefreshRecordingSearch();
    }
    private async Task SaveRecordingIndexAsync()
    {
        if (!indexDirty || recordingTranscript is null) return;
        await indexWrite;
        var document = recordingTranscript.Snapshot(); string path = recordingTranscript.AudioPath; indexDirty = false;
        try { indexWrite = Task.Run(() => RecordingTranscript.SaveAsync(path, document)); await indexWrite; }
        catch (Exception ex) { indexDirty = true; Status("録音の検索データを保存できません: " + ex.Message); indexWrite = Task.CompletedTask; }
    }
    private async Task FinishRecordingIndexAsync(string path)
    {
        recordingIndexTimer.Stop(); await indexWrite;
        await SaveRecordingIndexAsync();
        if (recordingTranscript?.AudioPath == Path.GetFullPath(path) && File.Exists(path))
        {
            playbackDocument = recordingTranscript.Snapshot(); playbackPath = path;
            try { await RecordingLibrary.SaveTextAsync(path, playbackDocument); }
            catch (Exception ex) { Status("音声・検索データは保存しましたが、テキストの保存に失敗しました: " + ex.Message); }
            reviewingRecording = true; SessionTitle.Text = "保存済み録音を再生：" + Path.GetFileName(Path.GetDirectoryName(path));
            PlaybackPanel.Visibility = Visibility.Visible;
            EnsurePlaybackSpace();
            PlaybackHint.Text = "文字をクリックすると、その発話の少し前から再生します。ドラッグで選択・コピーもできます。"
                + (playbackDocument.ApproximateTiming ? " Windows字幕の時刻は推定です。" : "");
            RefreshRecordingSearch();
        }
    }
    private void ClosePlayback()
    {
        playbackTimer.Stop(); playbackOutput?.Stop(); playbackOutput?.Dispose(); playbackOutput = null;
        playbackReader?.Dispose(); playbackReader = null;
        SetPlaybackHighlight(null);
    }
    private bool EnsurePlayback()
    {
        if (recordingStop is not null) { Status("録音を停止してから聞き直せます。再生音の再録音を防ぐため、録音中は再生しません。"); return false; }
        if (playbackReader is not null) return true;
        if (!File.Exists(playbackPath)) { Status("再生する録音がありません。ファイルから録音を開いてください。"); return false; }
        try
        {
            playbackReader = new AudioFileReader(playbackPath); playbackOutput = new WaveOutEvent(); playbackOutput.Init(playbackReader);
            updatingPlayback = true; PlaybackPosition.Maximum = Math.Max(1, playbackReader.TotalTime.TotalSeconds); updatingPlayback = false;
            PlaybackPanel.Visibility = Visibility.Visible; playbackTimer.Start(); UpdatePlaybackPosition(); return true;
        }
        catch (Exception ex) { ClosePlayback(); Status("録音を再生できません: " + ex.Message); return false; }
    }
    private void PlayFrom(RecordingPhrase phrase)
    {
        if (!EnsurePlayback()) return;
        playbackReader!.CurrentTime = TimeSpan.FromSeconds(Math.Clamp(phrase.Seconds - .35, 0, playbackReader.TotalTime.TotalSeconds));
        followPlayback = true;
        playbackOutput!.Play(); UpdatePlaybackPosition();
    }
    private void PlayCaption(long id)
    {
        int index = historyIds.BinarySearch(id);
        if (index < 0 || playbackDocument is null) return;
        var caption = history[index].Caption;
        var phrase = playbackDocument.Phrases.FirstOrDefault(p => p.Id == id && p.Started == caption.Started);
        if (phrase is not null) PlayFrom(phrase);
    }
    private void PlaybackClick(object sender, RoutedEventArgs e)
    {
        if (!EnsurePlayback()) return;
        if (playbackOutput!.PlaybackState == PlaybackState.Playing) playbackOutput.Pause();
        else { if (playbackReader!.CurrentTime >= playbackReader.TotalTime) playbackReader.CurrentTime = TimeSpan.Zero; playbackOutput.Play(); }
        UpdatePlaybackPosition();
    }
    private void UpdatePlaybackPosition()
    {
        if (playbackReader is null) return;
        updatingPlayback = true; PlaybackPosition.Value = playbackReader.CurrentTime.TotalSeconds; updatingPlayback = false;
        PlaybackTime.Text = FormatMediaTime(playbackReader.CurrentTime.TotalSeconds) + " / " + FormatMediaTime(playbackReader.TotalTime.TotalSeconds);
        PlaybackButton.Content = playbackOutput?.PlaybackState == PlaybackState.Playing ? "一時停止" : "再生";
        SetPlaybackHighlight(playbackDocument is not null && playbackReader.CurrentTime < playbackReader.TotalTime
            ? RecordingTranscript.At(playbackDocument, playbackReader.CurrentTime.TotalSeconds) : null);
    }
    private void SetPlaybackHighlight(RecordingPhrase? phrase)
    {
        if (highlightedPlaybackPhrase == phrase) return;
        highlightedPlaybackPhrase = phrase;
        foreach (var row in RealizedCaptionRows) ApplyTextSelection(row);
        if (phrase is null || !followPlayback || draggingText || HasTranscriptSelection) return;
        int index = historyIds.BinarySearch(phrase.Id);
        if (index >= 0 && history[index].Caption.Started == phrase.Started) CaptionList.ScrollIntoView(history[index]);
    }
    private void ApplyPlaybackHighlight(Microsoft.UI.Xaml.Controls.TextBlock text, CaptionHistoryEntry entry)
    {
        if (highlightedPlaybackPhrase is not { } phrase || entry.Id != phrase.Id || entry.Caption.Started != phrase.Started || text.Text.Length == 0) return;
        var highlight = new Microsoft.UI.Xaml.Documents.TextHighlighter
        {
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 34, 91, 110)),
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White)
        };
        highlight.Ranges.Add(new Microsoft.UI.Xaml.Documents.TextRange { StartIndex = 0, Length = text.Text.Length });
        text.TextHighlighters.Add(highlight);
    }
    private void PlaybackSeekChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (updatingPlayback || !EnsurePlayback()) return;
        playbackReader!.CurrentTime = TimeSpan.FromSeconds(Math.Clamp(e.NewValue, 0, playbackReader.TotalTime.TotalSeconds));
        UpdatePlaybackPosition();
    }
    private void SearchRecordingClick(object sender, RoutedEventArgs e)
    {
        RecordingSearchPanel.Visibility = RecordingSearchPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        if (RecordingSearchPanel.Visibility == Visibility.Visible) { EnsurePlaybackSpace(); RecordingSearch.Focus(FocusState.Programmatic); RefreshRecordingSearch(); }
    }
    private void RecordingSearchChanged(object sender, TextChangedEventArgs e) { searchTimer.Stop(); searchTimer.Start(); }
    private void RefreshRecordingSearch()
    {
        if (RecordingSearch is null) return;
        var document = recordingStop is not null ? recordingTranscript?.Snapshot() : playbackDocument;
        string query = RecordingSearch.Text;
        var matches = document is null ? [] : RecordingTranscript.Search(document, query);
        RecordingSearchResults.ItemsSource = matches.Select(p => new RecordingSearchHit(p)).ToArray();
        RecordingSearchCount.Text = document is null ? "録音後、または「ファイル → 録音と文字起こしを開く」から検索できます。"
            : string.IsNullOrWhiteSpace(query) ? "検索語を入力してください。結果をクリックすると、その発話から再生します。" : $"{matches.Length}件" + (recordingStop is not null ? "（再生は録音停止後）" : "");
    }
    private void RecordingSearchResultClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not RecordingSearchHit hit) return;
        if (recordingStop is not null) { Status("録音停止後に検索結果から聞き直せます。"); return; }
        PlayFrom(hit.Phrase);
        int index = historyIds.BinarySearch(hit.Phrase.Id);
        if (index >= 0) { PauseHistoryFollowing(); CaptionList.ScrollIntoView(history[index], ScrollIntoViewAlignment.Leading); }
    }
    private async void OpenRecordingClick(object sender, RoutedEventArgs e)
        => await OpenRecordingLibraryAsync();
    private async Task OpenRecordingLibraryAsync()
    {
        if (recordingStop is not null || captionStop is not null || dialogOpen) { Status("停止してから録音を開いてください。現在のテキストは先に保存できます。"); return; }
        dialogOpen = true;
        StartButton.IsEnabled = RecordToggle.IsEnabled = ModeSelector.IsEnabled = AudioSelector.IsEnabled = false;
        try
        {
            while (!closing)
            {
                var recordings = await Task.Run(() => RecordingLibrary.List(RecordingRoot));
                var list = new ListView { ItemsSource = recordings, DisplayMemberPath = nameof(SavedRecording.Title), MaxHeight = 300, SelectionMode = ListViewSelectionMode.Single };
                var content = new StackPanel { Spacing = 12 };
                content.Children.Add(new TextBlock { Text = "音声とテキストをまとめて管理します。共有はZIPを作成し、ご自身で送信先を選べます。", TextWrapping = TextWrapping.Wrap });
                content.Children.Add(list);
                var library = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "録音一覧・再生", Content = content, PrimaryButtonText = "開く", SecondaryButtonText = "別の音声…", CloseButtonText = "閉じる", IsPrimaryButtonEnabled = false, RequestedTheme = ElementTheme.Dark };
                string action = "";
                var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                foreach (var item in new[] { ("共有ZIPを作成…", "share"), ("移動…", "move"), ("削除…", "delete") })
                {
                    var button = new Button { Content = item.Item1, IsEnabled = false };
                    list.SelectionChanged += (_, _) => button.IsEnabled = list.SelectedItem is SavedRecording;
                    button.Click += (_, _) => { action = item.Item2; library.Hide(); };
                    actions.Children.Add(button);
                }
                content.Children.Add(actions);
                list.SelectionChanged += (_, _) => library.IsPrimaryButtonEnabled = list.SelectedItem is SavedRecording;
                var result = await library.ShowAsync();
                if (list.SelectedItem is SavedRecording selected)
                {
                    if (action.Length > 0)
                    {
                        try { await ManageRecordingAsync(selected, action); }
                        catch (Exception ex)
                        {
                            await new ContentDialog { XamlRoot = Root.XamlRoot, Title = "録音の操作に失敗しました", Content = ex.Message, CloseButtonText = "閉じる", RequestedTheme = ElementTheme.Dark }.ShowAsync();
                        }
                        continue;
                    }
                    if (result == ContentDialogResult.Primary) { await LoadRecordingAsync(selected.AudioPath); return; }
                }
                if (result != ContentDialogResult.Secondary) return;
                var picker = new FileOpenPicker(AppWindow.Id) { Title = "保存済み録音を開く（隣の .locaptio.json も必要です）" };
                foreach (var extension in new[] { ".wav", ".mp3", ".m4a" }) picker.FileTypeFilter.Add(extension);
                var file = await picker.PickSingleFileAsync(); if (file is null) continue;
                await LoadRecordingAsync(file.Path); return;
            }
        }
        catch (Exception ex) { Status("録音を開けません: " + ex.Message); }
        finally
        {
            dialogOpen = false; StartButton.IsEnabled = !closing && captionStop is null;
            RecordToggle.IsEnabled = ModeSelector.IsEnabled = AudioSelector.IsEnabled = !closing;
        }
    }
    private async Task ManageRecordingAsync(SavedRecording selected, string action)
    {
        if (action == "share")
        {
            var picker = new FileSavePicker(AppWindow.Id) { Title = "共有用ZIPを保存（音声・文字起こし・検索データ）", SuggestedFileName = Path.GetFileNameWithoutExtension(selected.AudioPath) + ".zip" };
            picker.FileTypeChoices.Add("共有用ZIP", new List<string> { ".zip" });
            var file = await picker.PickSaveFileAsync(); if (file is null) return;
            // Windows' save picker may reserve an empty destination; ask before replacing actual content.
            if (File.Exists(file.Path))
            {
                var confirmation = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "既存のZIPを置き換えますか？", Content = file.Path, PrimaryButtonText = "置き換える", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Close, RequestedTheme = ElementTheme.Dark };
                if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;
            }
            string temporary = Path.Combine(Path.GetDirectoryName(file.Path)!, Guid.NewGuid().ToString("N") + ".zip");
            try { await RecordingFiles.ShareAsync(selected.AudioPath, temporary); File.Move(temporary, file.Path, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", file.Path }, UseShellExecute = false });
            return;
        }
        if (action == "move")
        {
            var picker = new FolderPicker(AppWindow.Id) { Title = "録音とテキストの移動先" };
            var folder = await picker.PickSingleFolderAsync(); if (folder is null) return;
            ClosePlayback();
            string moved = await RecordingFiles.MoveAsync(selected.AudioPath, folder.Path);
            if (playbackPath.Equals(selected.AudioPath, StringComparison.OrdinalIgnoreCase)) await LoadRecordingAsync(moved);
            await new ContentDialog { XamlRoot = Root.XamlRoot, Title = "移動しました", Content = moved + "\n移動先のフォルダーからも録音を開けます。", CloseButtonText = "閉じる", RequestedTheme = ElementTheme.Dark }.ShowAsync();
            return;
        }
        var confirm = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "録音をごみ箱に移しますか？", Content = selected.Title + "\n音声・文字起こし・検索データが対象です。", PrimaryButtonText = "ごみ箱に移す", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Close, RequestedTheme = ElementTheme.Dark };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        ClosePlayback();
        foreach (string file in RecordingFiles.Bundle(selected.AudioPath))
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(file, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin, Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
        if (playbackPath.Equals(selected.AudioPath, StringComparison.OrdinalIgnoreCase))
        {
            playbackPath = ""; playbackDocument = null; PlaybackPanel.Visibility = RecordingSearchPanel.Visibility = Visibility.Collapsed;
            SessionTitle.Text = "録音を削除しました。表示中の文章は保存できます。";
        }
    }
    private async Task LoadRecordingAsync(string path)
    {
            RecordingDocument? document = File.Exists(RecordingTranscript.Sidecar(path)) ? await RecordingTranscript.LoadAsync(path) : null;
            ClosePlayback(); ClearCaptions(); playbackPath = path; playbackDocument = document;
            reviewingRecording = true; SessionTitle.Text = "保存済み録音を再生：" + Path.GetFileName(Path.GetDirectoryName(path)) + " / " + Path.GetFileName(path);
            if (document is not null)
                foreach (var phrase in document.Phrases)
                {
                    var caption = new CaptionUpdate(history.Count + 1, phrase.Revision, phrase.Text, phrase.Translation, true, phrase.Started, 0, 0, !string.IsNullOrWhiteSpace(phrase.Translation) ? "en" : phrase.Language);
                    UpdateHistory(caption); transcript.Update(caption);
                }
            // Rebind IDs to visible rows after resolving any reused session IDs.
            if (document is not null) playbackDocument = document with { Phrases = document.Phrases.Select((p, i) => p with { Id = history[i].Id }).ToArray() };
            RenderCaptions();
            SaveButton.IsEnabled = transcript.Count > 0; EnsurePlayback(); RefreshRecordingSearch();
            PlaybackHint.Text = document is null ? "文字起こしデータがないため、音声のみ再生します。新しい録音では検索用データも保存されます。" : "文字や検索結果をクリックすると、その発話から再生します。時刻は発話単位です。";
    }
}
