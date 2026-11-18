using System.Diagnostics;
using System.Text;
using Locaptio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Locaptio.WinUI;

// Editing with undo, the term dictionary, bookmarks, crash recovery and subtitle export.
public sealed partial class MainWindow
{
    private readonly TranscriptEditHistory edits = new();
    private (bool Japanese, string Model, string Prompt)? preparedModel;
    private TermDictionary terms = TermDictionary.Empty;
    private string? termsSource;
    // In memory a bookmark's PhraseId is the visible row's ID; Seconds is the media time of a
    // recording, or the time since the first utterance when nothing is being recorded.
    private readonly List<RecordingBookmark> bookmarks = new();
    private readonly DispatcherTimer autosaveTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private Task autosaveWrite = Task.CompletedTask, reviewWrite = Task.CompletedTask;
    private bool sessionDirty, autosaveFailed;
    private string autosavePath = "", pendingRecoveryPath = "";
    private long contextRowId;
    private static bool VerificationRun => Environment.GetCommandLineArgs().Any(a => a.StartsWith("--verify"));

    private TermDictionary Terms
    {
        get
        {
            if (termsSource != settings.TermDictionary) { termsSource = settings.TermDictionary; terms = TermDictionary.Parse(termsSource); }
            return terms;
        }
    }

    private void InitializeTranscriptTools()
    {
        // Each process writes its own snapshot, so a second window never takes over a running session.
        string folder = VerificationRun ? Path.Combine(Path.GetTempPath(), "Locaptio-verify-autosave-" + Environment.ProcessId) : Path.GetDirectoryName(SessionAutosave.DefaultPath)!;
        autosavePath = Path.Combine(folder, $"session-{Environment.ProcessId}.json"); pendingRecoveryPath = Path.Combine(folder, "pending.json");
        autosaveTimer.Tick += (_, _) => WriteAutosave(); autosaveTimer.Start();
        history.CollectionChanged += (_, _) => EmptyState.Visibility = history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        // The full status lives in a popup; keep its latest line visible at the bottom of the window.
        StatusLine.Text = StatusText.Text;
        StatusText.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) => StatusLine.Text = StatusText.Text.ReplaceLineEndings(" "));
        void Shortcut(Windows.System.VirtualKey key, Windows.System.VirtualKeyModifiers modifiers, Action action)
        {
            var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
            // Text boxes keep their own Ctrl+Z / Ctrl+B; the shortcut acts on the transcript otherwise.
            accelerator.Invoked += (_, e) => { if (IsEditingText() || dialogOpen) return; e.Handled = true; action(); };
            Root.KeyboardAccelerators.Add(accelerator);
        }
        Shortcut(Windows.System.VirtualKey.Z, Windows.System.VirtualKeyModifiers.Control, UndoEdit);
        Shortcut(Windows.System.VirtualKey.Y, Windows.System.VirtualKeyModifiers.Control, RedoEdit);
        Shortcut(Windows.System.VirtualKey.B, Windows.System.VirtualKeyModifiers.Control, () => AddBookmark(BookmarkLabels[0]));
        Shortcut(Windows.System.VirtualKey.F2, Windows.System.VirtualKeyModifiers.None, () => EditSelectedClick(this, new RoutedEventArgs()));

        var menu = transcriptSelectionMenu;
        var edit = new MenuFlyoutItem { Text = "この発話を編集…", Icon = new FontIcon { Glyph = "" } };
        edit.Click += async (_, _) => await EditRowAsync(contextRowId);
        var revert = new MenuFlyoutItem { Text = "補正・編集前の原文に戻す", Icon = new FontIcon { Glyph = "" } };
        revert.Click += (_, _) => RevertRow(contextRowId);
        var mark = new MenuFlyoutSubItem { Text = "ブックマークを付ける", Icon = new FontIcon { Glyph = "" } };
        var unmark = new MenuFlyoutItem { Text = "ブックマークを外す", Icon = new FontIcon { Glyph = "" } };
        unmark.Click += (_, _) => RemoveBookmarks(contextRowId);
        var register = new MenuFlyoutItem { Text = "選択した語句を用語辞書に登録…", Icon = new FontIcon { Glyph = "" } };
        register.Click += async (_, _) => await RegisterTermAsync();
        menu.Items.Add(new MenuFlyoutSeparator()); menu.Items.Add(edit); menu.Items.Add(revert); menu.Items.Add(mark); menu.Items.Add(unmark); menu.Items.Add(register);
        menu.Opening += (_, _) =>
        {
            // Right-clicking a row acts on that row even when nothing is selected.
            contextRowId = (menu.Target as FrameworkElement)?.DataContext is CaptionHistoryEntry entry ? entry.Id : textSelection is { } selection ? Math.Min(selection.Anchor.Id, selection.Focus.Id) : 0;
            bool row = FindCaption(contextRowId) is not null;
            edit.IsEnabled = mark.IsEnabled = row;
            revert.IsEnabled = row && edits.Differs(FindCaption(contextRowId)!);
            unmark.IsEnabled = bookmarks.Any(b => b.PhraseId == contextRowId);
            register.IsEnabled = SelectedPhrase().Length > 0;
            mark.Items.Clear();
            foreach (string label in BookmarkLabels)
            {
                var item = new MenuFlyoutItem { Text = label, Icon = BookmarkIcon(label) };
                item.Click += (_, _) => AddBookmark(label, contextRowId);
                mark.Items.Add(item);
            }
        };
        Root.Loaded += (_, _) => { if (!VerificationRun) FindInterruptedSession(); };
        Root.Loaded += async (_, _) => { if (Environment.GetCommandLineArgs().Contains("--verify-transcript-tools")) await VerifyTranscriptToolsAsync(); };
        Root.Loaded += async (_, _) => { if (Environment.GetCommandLineArgs().Contains("--verify-real-tools")) await VerifyRealToolsAsync(); };
        Root.Loaded += async (_, _) => { if (Environment.GetCommandLineArgs().Contains("--verify-content-check")) await VerifyContentCheckAsync(); };
        Root.Loaded += async (_, _) => { if (Environment.GetCommandLineArgs().Contains("--verify-live-recording")) await VerifyLiveRecordingAsync(); };
        Root.Loaded += async (_, _) => { if (Environment.GetCommandLineArgs().Contains("--verify-llm-models")) await VerifyLlmModelsAsync(); };
        Root.Loaded += async (_, _) => { if (Environment.GetCommandLineArgs().Contains("--verify-session-home")) await VerifySessionHomeAsync(); };
        Root.Loaded += async (_, _) => { if (Environment.GetCommandLineArgs().Contains("--verify-ai-review")) await VerifyAiReviewAsync(); };
        Root.Loaded += async (_, _) => { if (Environment.GetCommandLineArgs().Contains("--verify-checkbox-glyph")) await VerifyCheckBoxGlyphAsync(); };
        Root.Loaded += async (_, _) => { if (Environment.GetCommandLineArgs().Contains("--verify-translation-modes")) await VerifyTranslationModesAsync(); };
        Root.Loaded += async (_, _) => { if (Environment.GetCommandLineArgs().Contains("--verify-site-screenshots")) await VerifySiteScreenshotsAsync(); };
        Root.Loaded += async (_, _) => { if (Environment.GetCommandLineArgs().Contains("--verify-video")) await VerifyVideoAsync(); };
    }

    private CaptionUpdate? FindCaption(long id)
    {
        int index = historyIds.BinarySearch(id);
        return index >= 0 ? history[index].Caption : null;
    }
    /// <summary>Seconds since the first utterance on screen: the time base of a session that is not being recorded.</summary>
    private double SessionSeconds(long timestamp) => history.Count == 0 ? 0 : Math.Max(0, Stopwatch.GetElapsedTime(history[0].Caption.Started, timestamp).TotalSeconds);
    private double RowSeconds(CaptionUpdate caption)
    {
        if (playbackDocument?.Phrases.FirstOrDefault(p => p.Id == caption.Id && p.Started == caption.Started) is { } phrase) return phrase.Seconds;
        return UsesRecordingClock ? recordingTranscript!.Offset(caption.Started) : SessionSeconds(caption.Started);
    }
    /// <summary>
    /// A live document uses the recording's media time only when every row on screen was spoken
    /// during the current recording. After an input change splits the recording, earlier rows stay
    /// on screen, so the whole document falls back to session time instead of mixing two clocks.
    /// </summary>
    private bool UsesRecordingClock => recordingStop is not null && recordingTranscript is not null && (history.Count == 0 || history[0].Caption.Started >= recordingTranscript.Started);
    private string RowTime(long id) => FindCaption(id) is { } caption ? FormatMediaTime(RowSeconds(caption)) : "";

    /// <summary>The transcript on screen as a timed document: the open recording, or the live session.</summary>
    private RecordingDocument SessionDocument()
    {
        if (reviewingRecording && playbackDocument is not null) return playbackDocument;
        var phrases = history.Select(row => new RecordingPhrase(row.Id, row.Caption.Started, RowSeconds(row.Caption), row.English, row.Japanese, row.Caption.Revision, row.Caption.SourceLanguage,
            Original: edits.Original(row.Id) is { } original && original != row.English ? original : null, Edited: edits.IsEdited(row.Id))).ToArray();
        return new(1, Path.GetFileName(recordingPath), DateTimeOffset.Now, true, phrases, bookmarks.Count == 0 ? null : bookmarks.OrderBy(b => b.Seconds).ToArray());
    }

    // ---- Editing ------------------------------------------------------------------------

    /// <summary>Publishes a manual change everywhere the row is shown or saved.</summary>
    private void ApplyManualUpdate(CaptionUpdate update)
    {
        ClearTextSelection(); selectedRowUpdates.Remove(update.Id);
        if (recordingStop is not null && recordingTranscript is not null)
        {
            recordingTranscript.Observe(update);
            recordingTranscript.Annotate(update.Id, update.Started, edits.Original(update.Id) is { } original && original != update.English ? original : null, edits.IsEdited(update.Id)); indexDirty = true;
        }
        transcript.Update(update); UpdateHistory(update);
        if (captions.ContainsKey(update.Id)) { captions[update.Id] = update; captionWindow?.Update(captions.Values); }
        if (reviewingRecording && playbackDocument is not null)
        {
            // Word times describe the previous wording; a changed text falls back to estimated highlighting.
            playbackDocument = playbackDocument with
            {
                Phrases = playbackDocument.Phrases.Select(p => p.Id != update.Id ? p : p with
                {
                    Text = update.English, Translation = update.Japanese, Revision = update.Revision, Words = p.Text == update.English ? p.Words : null, Match = p.Text == update.English ? p.Match : null,
                    Original = edits.Original(update.Id) is { } original && original != update.English ? original : null, Edited = edits.IsEdited(update.Id)
                }).ToArray()
            };
            highlightedPlaybackPhrase = null; UpdatePlaybackPosition(); RefreshRecordingSearch(); SaveReviewDocument();
        }
        int index = historyIds.BinarySearch(update.Id);
        if (index >= 0 && CaptionList.ContainerFromIndex(index) is ListViewItem item && item.ContentTemplateRoot is Grid row) StyleHistoryRow(row, update);
        sessionDirty = true; SaveButton.IsEnabled = transcript.Count > 0 && !saving; RefreshEditCommands();
    }
    private void RefreshEditCommands() { UndoMenuItem.IsEnabled = edits.CanUndo; RedoMenuItem.IsEnabled = edits.CanRedo; }
    /// <summary>Writes an opened recording's transcript after an edit, bookmark or summary change. Writes never overlap.</summary>
    private void SaveReviewDocument()
    {
        if (playbackDocument is not { } document || !File.Exists(playbackPath)) return;
        string path = playbackPath;
        reviewWrite = reviewWrite.ContinueWith(async _ =>
        {
            try { await RecordingTranscript.SaveAsync(path, document); await RecordingLibrary.SaveTextAsync(path, document); }
            catch (Exception ex) { Status("録音の文字起こしを保存できません: " + ex.Message); }
        }, TaskScheduler.Default).Unwrap();
    }
    private void UndoEdit()
    {
        if (edits.Undo(FindCaption) is not { } update) { Status("元に戻せる修正はありません。"); RefreshEditCommands(); return; }
        ApplyManualUpdate(update); JumpToRow(update.Id, false); Status("修正を元に戻しました。");
    }
    private void RedoEdit()
    {
        if (edits.Redo(FindCaption) is not { } update) { Status("やり直せる修正はありません。"); RefreshEditCommands(); return; }
        ApplyManualUpdate(update); JumpToRow(update.Id, false); Status("修正をやり直しました。");
    }
    private void UndoEditClick(object sender, RoutedEventArgs e) => UndoEdit();
    private void RedoEditClick(object sender, RoutedEventArgs e) => RedoEdit();
    private void RevertRow(long id)
    {
        if (FindCaption(id) is not { } caption || edits.Original(id) is not { } original) return;
        if (edits.Apply(caption, original, caption.Japanese) is { } update) { ApplyManualUpdate(update); Status("認識時の原文に戻しました。Ctrl+Zで取り消せます。"); }
    }
    private async void EditSelectedClick(object sender, RoutedEventArgs e)
    {
        long id = textSelection is { } selection ? Math.Min(selection.Anchor.Id, selection.Focus.Id) : reviewingRecording && highlightedPlaybackPhrase is { } playing ? playing.Id : history.LastOrDefault()?.Id ?? 0;
        if (id == 0) { Status("編集する発話がありません。"); return; }
        await EditRowAsync(id);
    }
    private async Task EditRowAsync(long id)
    {
        if (dialogOpen || closing || FindCaption(id) is not { } caption) return;
        dialogOpen = true;
        try
        {
            bool translated = caption.Japanese is not null || settings.CaptionMode != "japanese" && !reviewingRecording;
            var source = new TextBox { Header = translated ? "原文" : "文章", Text = caption.English, TextWrapping = TextWrapping.Wrap, AcceptsReturn = false, MinWidth = 420 };
            var translation = new TextBox { Header = "日本語訳", Text = caption.Japanese ?? "", TextWrapping = TextWrapping.Wrap, Visibility = translated ? Visibility.Visible : Visibility.Collapsed };
            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(new TextBlock { Text = RowTime(id) + "　誤認識を直接修正できます。修正は Ctrl+Z で元に戻せます。", FontSize = 12, TextWrapping = TextWrapping.Wrap });
            content.Children.Add(source); content.Children.Add(translation);
            if (edits.Original(id) is { } original && original != caption.English)
            {
                var before = new StackPanel { Spacing = 6, Padding = new Thickness(10, 8, 10, 8), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(120, 133, 133, 133)) };
                before.Children.Add(new TextBlock { Text = "認識時の原文（AI補正・辞書・編集の前）", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                before.Children.Add(new TextBlock { Text = original, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
                var restore = new Button { Content = "この原文を入力欄に戻す", FontSize = 12, Padding = new Thickness(8, 3, 8, 3) };
                restore.Click += (_, _) => source.Text = original;
                before.Children.Add(restore); content.Children.Add(before);
            }
            var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "発話を編集", Content = content, PrimaryButtonText = "保存", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Primary, RequestedTheme = Root.RequestedTheme };
            source.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = source.Text.Trim().Length > 0;
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || closing) return;
            // Recognition may have revised the row while the dialog was open; edit what is shown now.
            if (FindCaption(id) is not { } current) return;
            if (edits.Apply(current, source.Text, translated ? translation.Text : current.Japanese) is { } update) { ApplyManualUpdate(update); Status("発話を修正しました。Ctrl+Zで元に戻せます。"); }
        }
        catch (Exception ex) { Status("編集できません: " + ex.Message); }
        finally { dialogOpen = false; }
    }
    private async void ShowChangesClick(object sender, RoutedEventArgs e)
    {
        if (dialogOpen || closing) return;
        var changed = history.Where(row => edits.Differs(row.Caption) || edits.IsEdited(row.Id)).Take(500).ToArray();
        if (changed.Length == 0) { Status("補正・編集された発話はありません。"); ShowStatusDetails(); return; }
        dialogOpen = true;
        try
        {
            var list = new StackPanel { Spacing = 10 };
            ContentDialog dialog = null!;
            foreach (var row in changed)
            {
                long id = row.Id; string original = edits.Original(id) ?? row.English;
                var item = new Grid { ColumnSpacing = 10, RowSpacing = 2, Padding = new Thickness(10, 8, 10, 8), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(110, 133, 133, 133)) };
                item.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); item.ColumnDefinitions.Add(new ColumnDefinition()); item.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                item.RowDefinitions.Add(new RowDefinition()); item.RowDefinitions.Add(new RowDefinition());
                void Cell(UIElement element, int rowIndex, int column) { Grid.SetRow((FrameworkElement)element, rowIndex); Grid.SetColumn((FrameworkElement)element, column); item.Children.Add(element); }
                Cell(new TextBlock { Text = "原文", FontSize = 12, Opacity = .7 }, 0, 0);
                Cell(new TextBlock { Text = original, TextWrapping = TextWrapping.Wrap, Opacity = .7, IsTextSelectionEnabled = true }, 0, 1);
                Cell(new TextBlock { Text = edits.IsEdited(id) ? "編集後" : "補正後", FontSize = 12 }, 1, 0);
                Cell(new TextBlock { Text = row.English, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }, 1, 1);
                var actions = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
                var jump = new Button { Content = RowTime(id) + " へ移動", FontSize = 12, Padding = new Thickness(8, 3, 8, 3), HorizontalAlignment = HorizontalAlignment.Stretch };
                jump.Click += (_, _) => { dialog.Hide(); JumpToRow(id, reviewingRecording); };
                var revert = new Button { Content = "原文に戻す", FontSize = 12, Padding = new Thickness(8, 3, 8, 3), HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = original != row.English };
                revert.Click += (_, _) => { RevertRow(id); list.Children.Remove(item); };
                actions.Children.Add(jump); actions.Children.Add(revert);
                Grid.SetRowSpan(actions, 2); Cell(actions, 0, 2);
                list.Children.Add(item);
            }
            var content = new StackPanel { Spacing = 10 };
            content.Children.Add(new TextBlock { Text = $"AI補正・用語辞書・手動の編集で、認識時の原文から変わった発話 {changed.Length} 件です。", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
            content.Children.Add(new ScrollViewer { Content = list, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 14, 0) });
            dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "変更された発話と原文の比較", Content = content, CloseButtonText = "閉じる", RequestedTheme = Root.RequestedTheme };
            dialog.Resources["ContentDialogMaxWidth"] = 860.0;
            await dialog.ShowAsync();
        }
        catch (Exception ex) { Status(ex.Message); }
        finally { dialogOpen = false; }
    }
    private void JumpToRow(long id, bool play)
    {
        int index = historyIds.BinarySearch(id);
        if (index < 0) return;
        PauseHistoryFollowing(); CaptionList.ScrollIntoView(history[index], ScrollIntoViewAlignment.Leading);
        // Highlight the whole row so the eye finds it after the scroll.
        bool japanese = playbackDocument is not null && captionStop is null ? history[index].Caption.SourceLanguage == "ja" : settings.CaptionMode == "japanese";
        SetTextSelection(new(new(id, 0), new(id, int.MaxValue), japanese || settings.English ? 0 : 1));
        if (play && playbackDocument is not null && recordingStop is null) PlayCaption(id);
    }

    // ---- Term dictionary ------------------------------------------------------------------

    private string SelectedPhrase()
    {
        string text = textSelection is { IsEmpty: false } ? textSelection.Text(history) : releasedSelectionText;
        text = text.Trim();
        return text.Length is > 0 and <= 40 && !text.Contains('\n') ? text : "";
    }
    private async Task RegisterTermAsync()
    {
        string selected = SelectedPhrase();
        if (selected.Length == 0 || dialogOpen || closing) return;
        dialogOpen = true;
        try
        {
            var term = new TextBox { Header = "正しい表記", Text = selected, MinWidth = 380 };
            var alias = new TextBox { Header = "誤って認識された表記（この表記を正しい表記に置き換えます）", Text = selected };
            var translation = new TextBox { Header = "日本語訳（英日翻訳用・任意）" };
            var apply = new CheckBox { Content = "表示中の文章にも適用する（Ctrl+Zで元に戻せます）", IsChecked = true };
            var content = new StackPanel { Spacing = 10 };
            content.Children.Add(new TextBlock { Text = "登録した表記は、認識のヒント、誤表記の置き換え、AI補正、翻訳で共通して使います。", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
            content.Children.Add(term); content.Children.Add(alias); content.Children.Add(translation); content.Children.Add(apply);
            var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "用語辞書に登録", Content = content, PrimaryButtonText = "登録", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Primary, RequestedTheme = Root.RequestedTheme };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || term.Text.Trim().Length == 0) return;
            var entries = Terms.Entries.ToList();
            string[] aliases = alias.Text.Split([',', '、', '，'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(a => a != term.Text.Trim()).ToArray();
            int existing = entries.FindIndex(entry => entry.Term == term.Text.Trim());
            if (existing >= 0) entries[existing] = entries[existing] with { Aliases = entries[existing].Aliases.Concat(aliases).Distinct().ToArray(), Translation = translation.Text.Trim().Length > 0 ? translation.Text : entries[existing].Translation };
            else entries.Add(new(term.Text, aliases, translation.Text));
            settings.TermDictionary = new TermDictionary(entries).Serialize(); settings.Save();
            int replaced = apply.IsChecked == true ? ApplyTermsToTranscript() : 0;
            Status($"用語辞書に「{term.Text.Trim()}」を登録しました。" + (replaced > 0 ? $"表示中の {replaced} 件の発話に適用しました。" : "") + "認識のヒントとAI補正には次回の開始から使います。");
        }
        catch (Exception ex) { Status("用語を登録できません: " + ex.Message); }
        finally { dialogOpen = false; }
    }
    private int ApplyTermsToTranscript()
    {
        int count = 0;
        foreach (var row in history.ToArray())
        {
            string replaced = Terms.Apply(row.English);
            if (replaced != row.English && edits.Apply(row.Caption, replaced, row.Japanese) is { } update) { ApplyManualUpdate(update); count++; }
        }
        return count;
    }
    private void TermDictionaryClick(object sender, RoutedEventArgs e)
    {
        SettingsClick(sender, e); settingsNavigation?.Select("用語辞書");
    }
    /// <summary>Self-checks answer the file pickers and the add/replace question.</summary>
    private Func<string?>? termImportPickerForVerification, termExportPickerForVerification;
    private bool? termImportReplaceForVerification;
    private void AddTermDictionarySettings(Action<string, string> section, Func<StackPanel> page, Window owner)
    {
        section("用語辞書", "人名・製品名・研究用語の正しい表記を登録します。認識のヒント、誤表記の置き換え、AI補正、英日翻訳で共通して使います。");
        var content = page();
        var rows = new StackPanel { Spacing = 6 };
        var count = new TextBlock { FontSize = 12 };
        void Store()
        {
            var entries = rows.Children.OfType<Grid>().Select(row => new TermEntry(((TextBox)row.Children[0]).Text,
                ((TextBox)row.Children[1]).Text.Split([',', '、', '，'], StringSplitOptions.RemoveEmptyEntries), ((TextBox)row.Children[2]).Text));
            var dictionary = new TermDictionary(entries);
            settings.TermDictionary = dictionary.Serialize(); settings.Save();
            count.Text = $"登録数: {dictionary.Entries.Length} 件（表記が空の行は保存しません）";
        }
        void AddRow(TermEntry? entry, bool focus = false)
        {
            var row = new Grid { ColumnSpacing = 6 };
            foreach (double width in new[] { 3, 4, 3 }) row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var term = new TextBox { Text = entry?.Term ?? "", PlaceholderText = "正しい表記（例: Locaptio）", MaxLength = 60 };
            var aliases = new TextBox { Text = entry is null ? "" : string.Join("、", entry.Aliases), PlaceholderText = "誤認識されやすい表記（例: ロカプション、low caption）", MaxLength = 200 };
            var translation = new TextBox { Text = entry?.Translation ?? "", PlaceholderText = "日本語訳（任意）", MaxLength = 60 };
            var remove = new Button { Content = new FontIcon { Glyph = "", FontSize = 13 }, Padding = new Thickness(8, 6, 8, 6) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(term, "正しい表記"); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(aliases, "誤認識されやすい表記");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(translation, "日本語訳"); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(remove, "この用語を削除");
            Grid.SetColumn(aliases, 1); Grid.SetColumn(translation, 2); Grid.SetColumn(remove, 3);
            row.Children.Add(term); row.Children.Add(aliases); row.Children.Add(translation); row.Children.Add(remove);
            foreach (var box in new[] { term, aliases, translation }) box.TextChanged += (_, _) => Store();
            remove.Click += (_, _) => { rows.Children.Remove(row); Store(); };
            rows.Children.Add(row);
            if (focus) term.Loaded += (_, _) => term.Focus(FocusState.Programmatic);
        }
        var heading = new Grid { ColumnSpacing = 6, Padding = new Thickness(0, 0, 44, 0) };
        string[] titles = ["正しい表記", "誤認識されやすい表記（、区切り）", "日本語訳（英日翻訳用）"]; double[] widths = [3, 4, 3];
        for (int i = 0; i < 3; i++)
        {
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(widths[i], GridUnitType.Star) });
            var title = new TextBlock { Text = titles[i], FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }; Grid.SetColumn(title, i); heading.Children.Add(title);
        }
        content.Children.Add(heading); content.Children.Add(rows);
        foreach (var entry in Terms.Entries) AddRow(entry);
        var add = new Button { Content = "＋ 用語を追加" }; add.Click += (_, _) => AddRow(null, true);
        var applyNow = new Button { Content = "表示中の文章に適用" };
        applyNow.Click += (_, _) => { int replaced = ApplyTermsToTranscript(); Status(replaced > 0 ? $"用語辞書を表示中の {replaced} 件の発話に適用しました。Ctrl+Zで元に戻せます。" : "置き換える表記はありませんでした。"); count.Text = replaced > 0 ? $"{replaced} 件の発話に適用しました。" : "置き換える表記はありませんでした。"; };
        void Reload(TermDictionary dictionary) { rows.Children.Clear(); foreach (var entry in dictionary.Entries) AddRow(entry); Store(); }
        var import = new Button { Content = "インポート…" };
        ToolTipService.SetToolTip(import, "CSV（Excel可）・タブ区切り・Locaptioの辞書テキストから読み込みます。");
        import.Click += async (_, _) =>
        {
            try
            {
                var picker = new Microsoft.Windows.Storage.Pickers.FileOpenPicker(owner.AppWindow.Id);
                foreach (string type in new[] { ".csv", ".tsv", ".txt" }) picker.FileTypeFilter.Add(type);
                var file = termImportPickerForVerification is not null ? termImportPickerForVerification() : (await picker.PickSingleFileAsync())?.Path;
                if (file is null) return;
                var imported = TermDictionaryFiles.Parse(TermDictionaryFiles.Decode(await File.ReadAllBytesAsync(file)));
                if (imported.Length == 0) { count.Text = "読み込める用語がありませんでした。1列目に表記、2列目に誤表記（、区切り）、3列目に訳語を書いてください。"; return; }
                bool replace = false;
                if (Terms.Entries.Length > 0)
                {
                    if (termImportReplaceForVerification is { } answer) replace = answer;
                    else
                    {
                        var choice = new ContentDialog { XamlRoot = content.XamlRoot, Title = "用語辞書をインポート", RequestedTheme = ElementTheme.Dark,
                            Content = $"{Path.GetFileName(file)} から {imported.Length} 件を読み込みました。今の辞書（{Terms.Entries.Length} 件）にどう反映しますか？\n「追加」は同じ表記の誤表記をまとめ、新しい表記を加えます。",
                            PrimaryButtonText = "追加", SecondaryButtonText = "置き換え", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Primary };
                        var result = await choice.ShowAsync();
                        if (result == ContentDialogResult.None) return;
                        replace = result == ContentDialogResult.Secondary;
                    }
                }
                var dictionary = replace ? new TermDictionary(imported) : TermDictionaryFiles.Merge(Terms, imported);
                Reload(dictionary);
                Status($"用語辞書に {Path.GetFileName(file)} を{(replace ? "読み込みました（置き換え）" : "追加しました")}。登録数 {dictionary.Entries.Length} 件" + (dictionary.Entries.Length >= TermDictionaryFiles.MaxEntries ? $"（上限 {TermDictionaryFiles.MaxEntries} 件）" : "") + "。");
            }
            catch (Exception ex) { count.Text = "インポートできません: " + ex.Message; }
        };
        var export = new Button { Content = "エクスポート…" };
        ToolTipService.SetToolTip(export, "CSV（UTF-8）で保存します。Excelで開いて編集し、インポートし直せます。");
        export.Click += async (_, _) =>
        {
            try
            {
                string? file = termExportPickerForVerification?.Invoke();
                if (file is null)
                {
                    var picker = new Microsoft.Windows.Storage.Pickers.FileSavePicker(owner.AppWindow.Id) { SuggestedFileName = "Locaptio用語辞書" };
                    picker.FileTypeChoices.Add("CSV（Excel）", new List<string> { ".csv" });
                    file = (await picker.PickSaveFileAsync())?.Path;
                }
                if (file is null) return;
                await File.WriteAllBytesAsync(file, TermDictionaryFiles.ToCsv(Terms));
                Status($"用語辞書（{Terms.Entries.Length} 件）を保存しました: {file}");
            }
            catch (Exception ex) { count.Text = "エクスポートできません: " + ex.Message; }
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; buttons.Children.Add(add); buttons.Children.Add(applyNow); buttons.Children.Add(import); buttons.Children.Add(export);
        content.Children.Add(buttons); content.Children.Add(count);
        content.Children.Add(new TextBlock { Text = "・誤認識されやすい表記は、文字起こしに現れたときに正しい表記へ置き換えます。Windows字幕連携・Whisper・ファイル文字起こしのすべてで、登録した直後から働きます。\n・正しい表記はWhisperの認識ヒントとAI補正にも渡します（次回の開始から）。ヒントは約500文字までのため、多い場合は上の行が優先されます。\n・日本語訳を入れた用語は、ローカルLLMの英日翻訳で訳語として使います。\n・本体の文章を選択して右クリック →「選択した語句を用語辞書に登録」からも追加できます。置き換え前の認識結果は「変更された発話・補正前の原文と比較」で確認できます。", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        Store();
    }

    // ---- Bookmarks ------------------------------------------------------------------------

    private string[] BookmarkLabels
    {
        get
        {
            var labels = (settings.BookmarkLabels ?? "").Split([',', '、', '，', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(l => l.Length <= 20).Distinct().Take(8).ToArray();
            return labels.Length > 0 ? labels : ["重要"];
        }
    }
    private void RefreshBookmarkLabels()
    {
        string[] labels = BookmarkLabels;
        BookmarkButtonText.Text = labels[0]; BookmarkButtonIcon.Foreground = BookmarkBrush(labels[0]);
        var flyout = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom };
        foreach (string label in labels)
        {
            var item = new MenuFlyoutItem { Text = label, Icon = BookmarkIcon(label) };
            item.Click += (_, _) => AddBookmark(label);
            flyout.Items.Add(item);
        }
        BookmarkButton.Flyout = flyout;
    }
    private void BookmarkClick(SplitButton sender, SplitButtonClickEventArgs args) => AddBookmark(BookmarkLabels[0]);
    private void BookmarkMenuClick(object sender, RoutedEventArgs e) => AddBookmark(BookmarkLabels[0]);
    /// <summary>Marks a row, or "now": the utterance being spoken while live, or the playback position in a recording.</summary>
    private void AddBookmark(string label, long rowId = 0)
    {
        if (closing) return;
        long now = Stopwatch.GetTimestamp();
        CaptionUpdate? caption = rowId != 0 ? FindCaption(rowId) : reviewingRecording && playbackDocument is not null
            ? (RecordingTranscript.At(playbackDocument, playbackSeconds) ?? playbackDocument.Phrases.FirstOrDefault()) is { } phrase ? FindCaption(phrase.Id) : null
            : history.LastOrDefault()?.Caption;
        if (caption is null && recordingStop is null) { Status("ブックマークを付ける発話がまだありません。"); return; }
        if (caption is not null && bookmarks.Any(b => b.PhraseId == caption.Id && b.Label == label)) { Status($"この発話には「{label}」が付いています。"); return; }
        RecordingBookmark bookmark;
        if (recordingStop is not null && recordingTranscript is not null)
        {
            // Without a chosen row, a live bookmark records the moment the button was pressed.
            // The recording file always gets its own media time; the screen uses the document's clock.
            bookmark = recordingTranscript.AddBookmark(label, rowId != 0 && caption is not null ? Math.Max(caption.Started, recordingTranscript.Started) : now, caption?.Id ?? 0, caption?.English);
            if (!UsesRecordingClock) bookmark = bookmark with { Seconds = rowId != 0 && caption is not null ? RowSeconds(caption) : SessionSeconds(now) };
        }
        else bookmark = new(reviewingRecording || rowId != 0 ? RowSeconds(caption!) : SessionSeconds(now), label, caption!.Id, caption.English);
        if (recordingStop is not null) indexDirty = true;
        bookmarks.Add(bookmark); BookmarksChanged();
        Status($"「{label}」を {FormatMediaTime(bookmark.Seconds)} に付けました。");
    }
    private void RemoveBookmarks(long rowId) { foreach (var bookmark in bookmarks.Where(b => b.PhraseId == rowId).ToArray()) RemoveBookmark(bookmark); }
    private void RemoveBookmark(RecordingBookmark bookmark)
    {
        if (!bookmarks.Remove(bookmark)) return;
        if (recordingStop is not null && recordingTranscript?.RemoveBookmark(bookmark.Label, bookmark.PhraseId) == true) indexDirty = true;
        BookmarksChanged(); Status("ブックマークを外しました。");
    }
    private void BookmarksChanged()
    {
        if (reviewingRecording && playbackDocument is not null)
        {
            playbackDocument = playbackDocument with { Bookmarks = bookmarks.Count == 0 ? null : bookmarks.OrderBy(b => b.Seconds).ToArray() };
            SaveReviewDocument();
        }
        sessionDirty = true; RefreshBookmarks();
    }
    private void RefreshBookmarks()
    {
        BookmarkPanel.Children.Clear();
        foreach (var bookmark in bookmarks.OrderBy(b => b.Seconds))
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            content.Children.Add(BookmarkIcon(bookmark.Label, 11));
            content.Children.Add(new TextBlock { Text = bookmark.Label, FontSize = 12 });
            content.Children.Add(new TextBlock { Text = FormatMediaTime(bookmark.Seconds), FontSize = 12, FontFamily = new FontFamily("Consolas"), Opacity = .75 });
            var chip = new Button { Content = content, Padding = new Thickness(8, 2, 8, 2), CornerRadius = new CornerRadius(12), BorderBrush = BookmarkBrush(bookmark.Label) };
            ToolTipService.SetToolTip(chip, (string.IsNullOrWhiteSpace(bookmark.Text) ? "" : bookmark.Text + "\n") + "クリックで移動" + (reviewingRecording ? "・再生" : "") + "／右クリックで削除");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(chip, $"ブックマーク {bookmark.Label} {FormatMediaTime(bookmark.Seconds)}");
            chip.Click += (_, _) =>
            {
                if (FindCaption(bookmark.PhraseId) is not null) JumpToRow(bookmark.PhraseId, true);
                else if (reviewingRecording && EnsurePlayback()) { playbackReader!.CurrentTime = TimeSpan.FromSeconds(Math.Clamp(bookmark.Seconds - .35, 0, playbackReader.TotalTime.TotalSeconds)); playbackOutput!.Play(); UpdatePlaybackPosition(); }
            };
            var remove = new MenuFlyoutItem { Text = "このブックマークを削除", Icon = new FontIcon { Glyph = "" } };
            remove.Click += (_, _) => RemoveBookmark(bookmark);
            chip.ContextFlyout = new MenuFlyout { Items = { remove } };
            BookmarkPanel.Children.Add(chip);
        }
        BookmarkScroll.Visibility = bookmarks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var row in RealizedCaptionRows) if (row.DataContext is CaptionHistoryEntry entry) StyleRowMarks(row, entry.Caption);
    }
    private void StyleRowMarks(Grid row, CaptionUpdate caption)
    {
        if (row.Children.Count < 4) return;
        var labels = bookmarks.Where(b => b.PhraseId == caption.Id).Select(b => b.Label).ToArray();
        row.Children[2].Visibility = labels.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (labels.Length > 0) ((Border)row.Children[2]).Background = BookmarkBrush(labels[0]);
        ToolTipService.SetToolTip(row.Children[2], labels.Length > 0 ? "ブックマーク: " + string.Join("・", labels) : null);
        bool edited = edits.IsEdited(caption.Id);
        row.Children[3].Visibility = edited ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(row.Children[3], edited ? "手動で編集した発話（右クリックで原文と比較・元に戻す）" : null);
        if (row.Children.Count < 5) return;
        double? match = reviewingRecording ? PhraseMatch(caption.Id) : null;
        bool low = match is double value && value < TimingAlignment.LowMatch;
        row.Children[4].Visibility = low ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(row.Children[4], low ? $"音声との一致 {match:P0}：この発話の文字の多くが、その時刻の音声から聞き取れませんでした。聞き直して確認してください。" : null);
        StyleAiMark(row, caption);
    }
    private RecordingDocument? matchSource; private Dictionary<long, double>? matches;
    /// <summary>Share of the utterance heard in the audio, from the last content check of the open recording.</summary>
    private double? PhraseMatch(long id)
    {
        if (!ReferenceEquals(matchSource, playbackDocument))
        {
            matchSource = playbackDocument;
            matches = playbackDocument?.Phrases.Where(p => p.Match is not null).GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First().Match!.Value);
        }
        return matches is not null && matches.TryGetValue(id, out var value) ? value : null;
    }
    private void RefreshRowMarks()
    {
        foreach (var row in RealizedCaptionRows) if (row.DataContext is CaptionHistoryEntry entry) StyleRowMarks(row, entry.Caption);
    }
    private string BookmarkExport()
    {
        if (bookmarks.Count == 0) return "";
        var text = new StringBuilder().AppendLine("―― ブックマーク ――");
        foreach (var bookmark in bookmarks.OrderBy(b => b.Seconds))
            text.Append('[').Append(FormatMediaTime(bookmark.Seconds)).Append("] ★").Append(bookmark.Label).Append("　").AppendLine(FindCaption(bookmark.PhraseId)?.English ?? bookmark.Text ?? "");
        return text.ToString();
    }

    // ---- State that follows the transcript -------------------------------------------------

    private void ClearTranscriptTools()
    {
        edits.Clear(); bookmarks.Clear(); RefreshBookmarks(); RefreshEditCommands(); ClearSummary();
        // Clearing is confirmed by the user, so the snapshot of that text is no longer wanted.
        DiscardAutosave();
    }
    /// <summary>Edits, bookmarks and the summary saved with an opened recording. Row IDs were rebound by the caller.</summary>
    private void RestoreTranscriptTools(RecordingDocument document)
    {
        foreach (var phrase in document.Phrases) edits.Restore(phrase.Id, phrase.Original, phrase.Edited);
        bookmarks.Clear();
        foreach (var bookmark in document.Bookmarks ?? [])
            bookmarks.Add(bookmark with { PhraseId = (RecordingTranscript.At(document, bookmark.Seconds + .0005) ?? document.Phrases.FirstOrDefault())?.Id ?? 0 });
        playbackDocument = document with { Bookmarks = bookmarks.Count == 0 ? null : bookmarks.ToArray() };
        RefreshBookmarks(); RestoreSummary(document);
        foreach (var row in RealizedCaptionRows) if (row.DataContext is CaptionHistoryEntry entry) StyleRowMarks(row, entry.Caption);
    }

    // ---- Autosave and recovery --------------------------------------------------------------

    private void WriteAutosave()
    {
        if (!sessionDirty || closing || !autosaveWrite.IsCompleted) return;
        sessionDirty = false;
        // A reviewed recording is already on disk; only a live or restored session needs a snapshot.
        if (!settings.AutosaveSession || reviewingRecording || history.Count == 0) return;
        var rows = history.Select(row => new AutosaveRow(row.Id, RowSeconds(row.Caption), row.English, row.Japanese, row.Caption.SourceLanguage, row.Caption.SpeakerId,
            edits.Original(row.Id) is { } original && original != row.English ? original : null, edits.IsEdited(row.Id))).ToArray();
        var snapshot = new AutosaveSnapshot(1, DateTimeOffset.Now, settings.CaptionMode, recordingStop is not null ? recordingPath : null, rows, bookmarks.Count == 0 ? null : bookmarks.ToArray());
        string path = autosavePath;
        // Up to 1,000 rows every 5 s; a multi-hour session (20,000 rows, a few MB) every 30 s at most.
        autosaveTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(5 + rows.Length / 800.0, 5, 30));
        autosaveWrite = Task.Run(async () =>
        {
            try { await SessionAutosave.SaveAsync(path, snapshot); autosaveFailed = false; }
            catch (Exception ex) { if (!autosaveFailed) Status("自動保存できません: " + ex.Message); autosaveFailed = true; }
        });
    }
    private void DiscardAutosave()
    {
        sessionDirty = false; string path = autosavePath;
        if (path.Length == 0) return;
        autosaveWrite = autosaveWrite.ContinueWith(_ => SessionAutosave.Discard(path), TaskScheduler.Default);
    }
    private async Task FinishAutosaveAsync()
    {
        autosaveTimer.Stop(); DiscardAutosave();
        try { await autosaveWrite; await reviewWrite; } catch { }
    }
    /// <summary>A snapshot whose process is gone was left by a crash, a forced exit or a power loss.</summary>
    private void FindInterruptedSession()
    {
        try
        {
            string folder = Path.GetDirectoryName(autosavePath)!;
            if (!Directory.Exists(folder)) return;
            var running = Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName).Select(p => p.Id).ToHashSet();
            var orphans = Directory.EnumerateFiles(folder, "session-*.json").Where(file => !int.TryParse(Path.GetFileNameWithoutExtension(file)["session-".Length..], out int id) || !running.Contains(id))
                .OrderByDescending(File.GetLastWriteTimeUtc).ToArray();
            // Keep the most recent interrupted session for the user to decide on; older ones were already offered.
            if (orphans.Length > 0 && SessionAutosave.TryLoad(orphans[0]) is not null) { File.Copy(orphans[0], pendingRecoveryPath, true); }
            foreach (string orphan in orphans) SessionAutosave.Discard(orphan);
            if (SessionAutosave.TryLoad(pendingRecoveryPath) is not { } snapshot) { SessionAutosave.Discard(pendingRecoveryPath); return; }
            RecoveryNotice.Message = $"{snapshot.SavedAt:M月d日 H:mm} まで自動保存した {snapshot.Rows.Length} 件の発話があります" + (snapshot.RecordingPath is null ? "。" : "（録音中でした）。") + "アプリが正常に終了しなかったため残っています。";
            RecoveryNotice.IsOpen = true; RecoverMenuItem.IsEnabled = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status("自動保存データを確認できません: " + ex.Message); }
    }
    private async void RecoverSessionClick(object sender, RoutedEventArgs e)
    {
        if (dialogOpen || closing) return;
        if (SessionAutosave.TryLoad(pendingRecoveryPath) is not { } snapshot) { RecoveryNotice.IsOpen = false; RecoverMenuItem.IsEnabled = false; Status("復元できる自動保存データはありません。"); return; }
        if (captionStop is not null || recordingStop is not null) { Status("文字起こし・録音を停止してから復元してください。"); ShowStatusDetails(); return; }
        ContentDialogResult choice;
        dialogOpen = true;
        try
        {
            var content = new StackPanel { Spacing = 10 };
            content.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Text = $"自動保存: {snapshot.SavedAt:yyyy/MM/dd HH:mm:ss}\n発話: {snapshot.Rows.Length} 件" + (snapshot.Bookmarks is { Length: > 0 } marks ? $"／ブックマーク: {marks.Length} 件" : "")
                + (snapshot.RecordingPath is null ? "\n録音: なし（文章のみ）" : "\n録音: " + snapshot.RecordingPath) });
            var preview = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, IsTextSelectionEnabled = true,
                Text = string.Join("\n", (snapshot.Rows.Length <= 8 ? snapshot.Rows : snapshot.Rows.Take(4).Append(new AutosaveRow(0, 0, "…", null, "")).Concat(snapshot.Rows.TakeLast(3))).Select(r => r.Id == 0 ? r.Text : $"[{FormatMediaTime(r.Seconds)}] {r.Text}")) };
            content.Children.Add(new ScrollViewer { Content = preview, MaxHeight = 220, Padding = new Thickness(0, 0, 14, 0) });
            content.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Text = snapshot.RecordingPath is null ? "復元すると、この文章を本体に表示します。続けて保存・編集・開始ができます。" : "復元すると、中断された録音を再生できる状態に直し、文章と対応付けて開きます。録音は最後に書き込まれた位置までです。" });
            var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "前回の未保存セッション", Content = content, PrimaryButtonText = "復元する", SecondaryButtonText = "破棄する", CloseButtonText = "あとで決める", DefaultButton = ContentDialogButton.Primary, RequestedTheme = Root.RequestedTheme };
            choice = await dialog.ShowAsync();
            if (choice == ContentDialogResult.Secondary)
            {
                var confirm = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "自動保存データを破棄しますか？", Content = "破棄した文章は元に戻せません。録音ファイルは削除しません。", PrimaryButtonText = "破棄する", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Close, RequestedTheme = Root.RequestedTheme };
                if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
                SessionAutosave.Discard(pendingRecoveryPath); RecoveryNotice.IsOpen = false; RecoverMenuItem.IsEnabled = false; Status("自動保存データを破棄しました。"); return;
            }
            if (choice != ContentDialogResult.Primary || closing) return;
            if (!await ConfirmTranscriptReplacementAsync("前回のセッションを復元します。")) return;
            await RestoreSessionAsync(snapshot);
            SessionAutosave.Discard(pendingRecoveryPath); RecoveryNotice.IsOpen = false; RecoverMenuItem.IsEnabled = false;
        }
        catch (Exception ex) { Status("復元できません: " + ex.Message + " 自動保存データは残してあります。"); ShowStatusDetails(pin: true); }
        finally { dialogOpen = false; }
    }
    private async Task RestoreSessionAsync(AutosaveSnapshot snapshot)
    {
        ClosePlayback();
        string? audio = await Task.Run(() => SessionAutosave.RecoverRecording(snapshot.RecordingPath));
        if (audio is not null && File.Exists(RecordingTranscript.Sidecar(audio)))
        {
            // The recording's own search data carries the media times; open it like any saved recording.
            await LoadRecordingAsync(audio);
            if (playbackDocument is not null && !File.Exists(audio + ".txt")) await RecordingLibrary.SaveTextAsync(audio, playbackDocument);
            Status("中断された録音と文字起こしを復元しました: " + audio); return;
        }
        ClearCaptions(); reviewingRecording = false; playbackDocument = null; playbackPath = "";
        PlaybackPanel.Visibility = RecordingSearchPanel.Visibility = Visibility.Collapsed;
        if (snapshot.Mode is "translate" or "japanese" or "ja-translate" && snapshot.Mode != settings.CaptionMode)
        {
            // Show the columns the session was captured with, without the mode-change prompt.
            modeChanging = true; settings.CaptionMode = snapshot.Mode; ModeSelector.SelectedIndex = ModeIndex(snapshot.Mode); modeChanging = false; ApplySettings();
        }
        // Place the rows in the past on the session clock, so new speech continues after them in order.
        long now = Stopwatch.GetTimestamp(); double last = snapshot.Rows.Max(r => r.Seconds) + 1;
        var ids = new Dictionary<long, long>();
        foreach (var row in snapshot.Rows)
        {
            var caption = new CaptionUpdate(history.Count + 1, 1, row.Text, row.Translation, true, now - (long)((last - row.Seconds) * Stopwatch.Frequency), 0, 0, row.Language, SpeakerId: row.SpeakerId);
            ids[row.Id] = caption.Id; edits.Restore(caption.Id, row.Original, row.Edited);
            UpdateHistory(caption); transcript.Update(caption);
        }
        foreach (var bookmark in snapshot.Bookmarks ?? [])
            if (ids.TryGetValue(bookmark.PhraseId, out long id) && FindCaption(id) is { } caption) bookmarks.Add(bookmark with { PhraseId = id, Seconds = SessionSeconds(caption.Started) });
        RefreshBookmarks(); RenderCaptions(); SaveButton.IsEnabled = transcript.Count > 0; sessionDirty = true;
        SessionTitle.Text = "復元した文字起こし（未保存）／「テキスト保存」で保存、「開始」で続きを追加";
        Status(audio is null ? "前回の文字起こしを復元しました。保存するまでは引き続き自動保存します。" : "文字起こしを復元しました。録音は再生できる状態に直しましたが、対応データがないため文章のみ表示しています: " + audio);
    }

    // ---- Subtitle export --------------------------------------------------------------------

    private async void ExportSubtitlesClick(object sender, RoutedEventArgs e)
    {
        if (dialogOpen || closing) return;
        var document = SessionDocument();
        if (document.Phrases.Length == 0) { Status("字幕にする文字起こしがありません。"); ShowStatusDetails(); return; }
        dialogOpen = true;
        try
        {
            bool translated = document.Phrases.Any(p => !string.IsNullOrWhiteSpace(p.Translation));
            var content = new StackPanel { Spacing = 10 };
            var choice = new RadioButtons { Header = "字幕にする文章", SelectedIndex = 0 };
            choice.Items.Add(translated ? "原文のみ" : "文字起こし"); if (translated) { choice.Items.Add("日本語訳のみ"); choice.Items.Add("原文と日本語訳（2段）"); }
            content.Children.Add(choice);
            bool wordTimes = document.Phrases.Any(p => p.Words is { Length: > 0 });
            content.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Text =
                (reviewingRecording ? "録音の再生位置に合わせた時刻で書き出します。" : recordingStop is not null ? "録音開始からの時刻で書き出します。" : "録音していないため、最初の発話を 00:00 とした時刻で書き出します。動画と合わせる場合は編集ソフトで開始位置を調整してください。")
                + (document.ApproximateTiming && !wordTimes ? " 発話の時刻は推定を含みます。録音を開いて「同期を改善」を実行すると、ずれを減らせます。" : "")
                + " 長い発話は読みやすい長さに分け、1つの字幕は2行までにします。次の保存画面でSRTまたはVTTを選べます。" });
            var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "字幕ファイル（SRT・VTT）を書き出す", Content = content, PrimaryButtonText = "保存先を選ぶ…", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Primary, RequestedTheme = Root.RequestedTheme };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || closing) return;
            string mode = choice.SelectedIndex == 1 ? "translation" : choice.SelectedIndex == 2 ? "both" : "source";
            string name = reviewingRecording && playbackPath.Length > 0 ? Path.GetFileNameWithoutExtension(playbackPath) : Path.GetFileNameWithoutExtension(TranscriptExport.FileName(settings.TextFileNamePattern, DateTimeOffset.Now));
            var picker = new Microsoft.Windows.Storage.Pickers.FileSavePicker(AppWindow.Id) { Title = "字幕ファイルを保存", SuggestedFileName = name };
            picker.FileTypeChoices.Add("SubRip字幕（動画編集ソフト・プレーヤー向け）", new List<string> { ".srt" });
            picker.FileTypeChoices.Add("WebVTT字幕（Web・ブラウザー向け）", new List<string> { ".vtt" });
            string directory = reviewingRecording && playbackPath.Length > 0 ? Path.GetDirectoryName(playbackPath)! : settings.TextSaveDirectory;
            if (Directory.Exists(directory)) picker.SuggestedFolder = directory;
            var file = await picker.PickSaveFileAsync(); if (file is null || closing) return;
            await WriteSubtitlesAsync(file.Path, document, mode);
            Status($"字幕ファイルを保存しました（{SubtitleExport.Cues(document, mode).Count} 件）: " + file.Path);
        }
        catch (Exception ex) { Status("字幕ファイルを保存できません: " + ex.Message); ShowStatusDetails(pin: true); }
        finally { dialogOpen = false; }
    }
    private static Task WriteSubtitlesAsync(string path, RecordingDocument document, string mode)
        => Path.GetExtension(path).Equals(".vtt", StringComparison.OrdinalIgnoreCase)
            ? File.WriteAllTextAsync(path, SubtitleExport.Vtt(document, mode), new UTF8Encoding(false))
            // Many editors only detect UTF-8 in SRT files from a byte-order mark.
            : File.WriteAllTextAsync(path, SubtitleExport.Srt(document, mode), new UTF8Encoding(true));

    // ---- Long-running work on an opened recording --------------------------------------------

    private void ShowTask(string text, double? percent)
    {
        TaskPanel.Visibility = Visibility.Visible; TaskText.Text = text;
        TaskBar.IsIndeterminate = percent is null; if (percent is double value) TaskBar.Value = Math.Clamp(value, 0, 100);
    }
    /// <summary>Progress posted from a worker can arrive after the task ended; it must not reopen the panel.</summary>
    private void ReportTask(CancellationToken token, string text, double? percent)
    {
        if (!closing && captionStop is { } active && active.Token == token && !token.IsCancellationRequested) ShowTask(text, percent);
    }
    private void TaskCancelClick(object sender, RoutedEventArgs e) { try { captionStop?.Cancel(); } catch (ObjectDisposedException) { } }
    /// <summary>Runs recognition or AI work that must not overlap live captions, with Stop and the cancel button wired up.</summary>
    private async Task<bool> RunExclusiveAsync(string label, Func<CancellationToken, Task> body)
    {
        if (captionStop is not null || recordingStop is not null || closing || modelPreparationStop is not null)
        { Status("文字起こし・録音・モデル準備を停止してから実行してください。"); ShowStatusDetails(); return false; }
        root = FindRoot();
        if (root.Length == 0) { Status("設定の「保存先・録音」で、モデル・推論環境のフォルダーを指定してください。"); ShowStatusDetails(pin: true); return false; }
        var stop = new CancellationTokenSource(); captionStop = stop;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); captionTask = completion.Task;
        StartButton.IsEnabled = RecordToggle.IsEnabled = ModeSelector.IsEnabled = AudioSelector.IsEnabled = false; StopButton.IsEnabled = true;
        exclusiveTask = true; UpdateCommandStates();
        ShowTask(label, null);
        try { await body(stop.Token); return true; }
        catch (OperationCanceledException) { Status(label + "を中止しました。文章は変更していません。"); return false; }
        catch (Exception ex) { Status(label + "に失敗しました: " + ex.Message); ShowStatusDetails(pin: true); return false; }
        finally
        {
            TaskPanel.Visibility = Visibility.Collapsed; preparationEpoch++; preparationVisible = false; PreparationPanel.Visibility = Visibility.Collapsed;
            if (ReferenceEquals(captionStop, stop)) captionStop = null;
            stop.Dispose(); completion.TrySetResult(); exclusiveTask = false; UpdateCommandStates();
            StartButton.IsEnabled = !closing; StopButton.IsEnabled = false;
            RecordToggle.IsEnabled = ModeSelector.IsEnabled = AudioSelector.IsEnabled = !closing;
        }
    }
}
