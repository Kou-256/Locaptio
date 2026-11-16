using System.Diagnostics;
using System.Runtime.InteropServices;
using Locaptio.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Locaptio.WinUI;

// --verify-site-screenshots: stages the real window with fictional sample text and captures
// the screenshots used on the website (artifacts/site-shots). No audio is recorded or played.
// The window is captured from the screen so dialogs and menus appear exactly as users see them.
public sealed partial class MainWindow
{
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapHeader { public int Size, Width, Height; public short Planes, BitCount; public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant; }
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint window, ref NativePoint point);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleBitmap(nint dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint item);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(nint target, int x, int y, int width, int height, nint source, int sourceX, int sourceY, int operation);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(nint dc, nint bitmap, uint start, uint lines, byte[] bits, ref BitmapHeader header, uint usage);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint item);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("kernel32.dll")] private static extern uint SetThreadExecutionState(uint flags);

    private static async Task CaptureScreenAsync(nint window, string path)
    {
        GetClientRect(window, out var rect); var origin = new NativePoint(); ClientToScreen(window, ref origin);
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
        nint screen = GetDC(0), memory = CreateCompatibleDC(screen), bitmap = CreateCompatibleBitmap(screen, width, height);
        var pixels = new byte[width * height * 4];
        try
        {
            var previous = SelectObject(memory, bitmap);
            BitBlt(memory, 0, 0, width, height, screen, origin.X, origin.Y, 0x00CC0020);
            SelectObject(memory, previous);
            var header = new BitmapHeader { Size = Marshal.SizeOf<BitmapHeader>(), Width = width, Height = -height, Planes = 1, BitCount = 32 };
            GetDIBits(memory, bitmap, 0, (uint)height, pixels, ref header, 0);
        }
        finally { DeleteObject(bitmap); DeleteDC(memory); ReleaseDC(0, screen); }
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Ignore, (uint)width, (uint)height, 96, 96, pixels);
        await encoder.FlushAsync(); stream.Seek(0);
        using var file = File.Create(path); await System.IO.WindowsRuntimeStreamExtensions.AsStreamForRead(stream).CopyToAsync(file);
    }

    private static readonly string[] SiteMeeting =
    [
        "それでは定例会議を始めます。今日は新しい字幕機能のリリース日程を決めたいと思います。",
        "現在のベータ版では、自動保存と編集の機能がほぼ完成しています。",
        "ただ、要約機能はまだ社内テストが足りていません。",
        "では、リリースは来月の十五日にしましょう。要約機能はベータとして表示する形で出します。",
        "異論はありません。それで決定とします。",
        "佐藤さんは今週中に、要約機能のテスト結果を共有してください。",
        "わかりました。金曜日までにまとめます。",
        "あと、ウェブサイトの説明画像も更新が必要ですね。",
        "田中さん、来週の水曜までにイラストを用意してもらえますか。",
        "はい、対応します。用語辞書の初期データは次回の会議で検討しましょう。"
    ];
    private static readonly (string English, string Japanese)[] SiteLecture =
    [
        ("Today we'll look at how speech recognition models turn audio into text.", "今日は、音声認識モデルが音声を文字に変換する仕組みを見ていきます。"),
        ("The audio is first split into short frames of about twenty milliseconds.", "音声はまず、約20ミリ秒の短いフレームに分割されます。"),
        ("Each frame is converted into features that describe its frequencies.", "各フレームは、周波数を表す特徴量に変換されます。"),
        ("A larger model is usually more accurate, but it takes longer to respond.", "一般に大きなモデルほど正確ですが、応答には時間がかかります。"),
        ("That's why live captions often show a quick draft first.", "そのため、ライブ字幕では最初に素早い下書きを表示することがよくあります。"),
        ("The draft is then replaced when a more accurate result is ready.", "より正確な結果が準備できると、その下書きは置き換えられます。"),
        ("Keep in mind that technical terms are the hardest part to recognize.", "専門用語は、認識が最も難しい部分であることを覚えておいてください。")
    ];

    private async Task VerifySiteScreenshotsAsync()
    {
        string shots = Path.Combine(root, "artifacts", "site-shots"), work = Path.Combine(Path.GetTempPath(), "Locaptio-site-shots-" + Environment.ProcessId);
        Directory.CreateDirectory(shots); Directory.CreateDirectory(work);
        var saved = System.Text.Json.JsonSerializer.Serialize(settings);
        var failures = new List<string>(); var captured = new List<string>();
        double scale = GetDpiForWindow(hwnd) / 96.0;
        async Task Shot(string name, int settle = 500)
        {
            await Task.Delay(settle); Activate(); SetForegroundWindow(hwnd); await Task.Delay(250);
            string file = Path.Combine(shots, name + ".png");
            try { await CaptureScreenAsync(hwnd, file); } catch (Exception) { }
            // A locked or sleeping display copies as black: draw the window and its open popups instead.
            if (!File.Exists(file) || IsBlack(file)) await CaptureComposedAsync(file);
            captured.Add(name);
        }
        async Task DialogShot(string name, Func<Task> open)
        {
            var running = open(); await Shot(name, 900);
            foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot)) if (popup.Child is ContentDialog dialog) dialog.Hide();
            try { await running.WaitAsync(TimeSpan.FromSeconds(10)); } catch (Exception ex) { failures.Add(name + ": " + ex.Message); }
            await Task.Delay(300);
        }
        void Reset(string mode)
        {
            ClosePlayback(); ShowSummaryPane(false); summaryItems = [];
            ClearCaptions(); reviewingRecording = false; playbackDocument = null; playbackPath = "";
            PlaybackPanel.Visibility = RecordingSearchPanel.Visibility = RecordingIndicator.Visibility = TaskPanel.Visibility = Visibility.Collapsed;
            RecoveryNotice.IsOpen = false; SetRecordingChecked(false); RecordToggleText.Text = "新規録音"; StopButton.IsEnabled = false; StartButton.IsEnabled = true;
            if (settings.CaptionMode != mode) { modeChanging = true; settings.CaptionMode = mode; ModeSelector.SelectedIndex = mode == "japanese" ? 1 : 0; modeChanging = false; }
            ApplySettings();
        }
        void Feed(IEnumerable<(string Text, string? Translation)> rows, string language, bool refined = false)
        {
            captionStop ??= new(); long start = Stopwatch.GetTimestamp(); int i = 0;
            foreach (var (text, translation) in rows) { i++; UpdateCaption(new(i, 1, text, translation, true, start + i * 37 * Stopwatch.Frequency / 10, 0, 0, language, TranslationRefined: refined && translation is not null)); }
            captionStop.Dispose(); captionStop = null;
        }
        async Task<string> SavedRecording(string name, string[] lines, double[] starts, bool words)
        {
            string folder = Path.Combine(work, name); Directory.CreateDirectory(folder);
            string audio = Path.Combine(folder, name + ".wav");
            using (var writer = new NAudio.Wave.WaveFileWriter(audio, new NAudio.Wave.WaveFormat(16000, 16, 1))) writer.Write(new byte[32000 * 75], 0, 32000 * 75);
            var phrases = lines.Select((text, i) => new RecordingPhrase(i + 1, i + 1, starts[i], text, null, 1, "ja", starts[i] + 5.6,
                words ? PlaybackHighlight.Tokens(text).Select(t => new RecordingWord(t.Index, t.Length, starts[i] + 5.6 * t.Index / text.Length, starts[i] + 5.6 * (t.Index + t.Length) / text.Length)).ToArray() : null)).ToArray();
            await RecordingTranscript.SaveAsync(audio, new(1, name + ".wav", DateTimeOffset.Now.AddDays(-1), false, phrases, [new(starts[3] + .2, "重要", 4), new(starts[5] + .2, "後で確認", 6)], null, "large-v3-turbo"));
            await RecordingLibrary.SaveTextAsync(audio, (await RecordingTranscript.LoadAsync(audio)));
            return audio;
        }
        try
        {
            Title = "Locaptio";
            // Screen copies are black while the display sleeps; keep it on for this run.
            SetThreadExecutionState(0x80000000 | 0x00000002); await Task.Delay(1500);
            confirmReplacementForVerification = _ => Task.FromResult(true);
            settings.BackgroundOpacity = 1; settings.TextOpacity = 1; settings.FontSize = 20; settings.English = settings.Japanese = true; settings.CaptionLineGap = 6;
            settings.SpeechEngine = "windows-live"; settings.CorrectTranscriptWithAi = true; settings.BookmarkLabels = "重要、後で確認、質問、TODO";
            settings.TermDictionary = "Locaptio | ロカプション、ロカプチオ | \n議事録 | 議時録 | minutes\nWASAPI | ワサピ | ";
            RefreshAudioInputs();
            var presenter = (OverlappedPresenter)AppWindow.Presenter; presenter.Restore();
            AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(1280 * scale), (int)(780 * scale)));
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
            AppWindow.Move(new Windows.Graphics.PointInt32(area.X + 40, area.Y + 20));

            // English lecture with Japanese translation.
            Reset("translate"); Feed(SiteLecture.Select(r => (r.English, (string?)r.Japanese)), "en", true);
            StatusText.Text = "Windowsライブキャプション連携 · FuguMT速報翻訳 → Qwen文脈翻訳: ON";
            await Shot("translate");

            // Caption overlay, rendered with its transparency for compositing over a video frame.
            captionWindow = new CaptionWindow(settings); captionWindow.Activate();
            captions.Clear(); foreach (var row in history.TakeLast(1)) captions[row.Id] = row.Caption;
            captionWindow.Update(captions.Values); await Task.Delay(900);
            if (captionWindow.Content is UIElement overlay) await CaptureElementAsync(overlay, Path.Combine(shots, "caption-overlay.png"));
            captionWindow.Close(); captionWindow = null; captured.Add("caption-overlay");
            Reset("japanese"); Feed(SiteMeeting.Take(4).Select(t => (t, (string?)null)), "ja");
            captionWindow = new CaptionWindow(settings); captionWindow.Activate();
            captions.Clear(); foreach (var row in history.TakeLast(1)) captions[row.Id] = row.Caption;
            captionWindow.Update(captions.Values); await Task.Delay(900);
            if (captionWindow.Content is UIElement overlayJa) await CaptureElementAsync(overlayJa, Path.Combine(shots, "caption-overlay-ja.png"));
            captionWindow.Close(); captionWindow = null; captured.Add("caption-overlay-ja");

            // A meeting being recorded: bookmarks, an edited row and the recording indicator.
            Reset("japanese");
            Feed(SiteMeeting.Select(t => (t, (string?)null)), "ja");
            ApplyManualUpdate(edits.Apply(history[5].Caption, "佐藤さんは今週の金曜までに、要約機能のテスト結果を共有してください。", null)!);
            AddBookmark("重要", 4); AddBookmark("後で確認", 6); AddBookmark("TODO", 9);
            SetRecordingChecked(true); RecordToggleText.Text = "録音中"; StopButton.IsEnabled = true; StartButton.IsEnabled = false;
            RecordingIndicator.Visibility = Visibility.Visible; RecordingClock.Text = "00:12:40"; RecordingText.Text = @"C:\Users\…\Music\Locaptio\定例会議-20261116\定例会議.wav";
            SessionTitle.Text = "新規録音中：定例会議-20261116"; StatusText.Text = "「後で確認」を 00:00:18 に付けました。";
            ClearTextSelection(); PauseHistoryFollowing(); CaptionList.ScrollIntoView(history[^1]);
            await Shot("recording");
            // The transcript context menu with copy, edit and bookmark commands.
            SetTextSelection(new(new(2, 0), new(4, 14), 0)); await Task.Delay(200);
            int menuIndex = historyIds.BinarySearch(3);
            if (CaptionList.ContainerFromIndex(menuIndex) is ListViewItem menuItem && menuItem.ContentTemplateRoot is Grid menuRow)
            {
                var target = (FrameworkElement)menuRow.Children[0];
                transcriptSelectionMenu.ShowAt(target, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions { Position = new Windows.Foundation.Point(Math.Min(260, target.ActualWidth - 20), 12) });
                await Shot("copy-menu", 700); transcriptSelectionMenu.Hide();
            }
            ClearTextSelection();
            await DialogShot("edit", () => EditRowAsync(6));
            RecordingIndicator.Visibility = Visibility.Collapsed; SetRecordingChecked(false); RecordToggleText.Text = "新規録音";

            // Crash recovery: the notice and the review dialog.
            Reset("japanese");
            var snapshot = new AutosaveSnapshot(1, DateTimeOffset.Now.AddMinutes(-3), "japanese", null,
                SiteMeeting.Select((t, i) => new AutosaveRow(i + 1, i * 3.7, t, null, "ja")).ToArray(), [new(11.1, "重要", 4)]);
            await SessionAutosave.SaveAsync(pendingRecoveryPath, snapshot);
            RecoveryNotice.Message = $"{snapshot.SavedAt:M月d日 H:mm} まで自動保存した {snapshot.Rows.Length} 件の発話があります。アプリが正常に終了しなかったため残っています。"; RecoveryNotice.IsOpen = true; RecoverMenuItem.IsEnabled = true;
            StatusText.Text = "「開始」または「新規録音」で始めます。";
            await Shot("recovery-notice");
            await DialogShot("recovery", async () => { RecoverSessionClick(this, new RoutedEventArgs()); await Task.Delay(1500); });
            RecoveryNotice.IsOpen = false; SessionAutosave.Discard(pendingRecoveryPath);

            // File transcription in progress.
            Reset("japanese"); Feed(SiteMeeting.Take(6).Select(t => (t, (string?)null)), "ja");
            SessionTitle.Text = "ファイル文字起こし：定例会議.m4a（停止で中止）"; ShowTask("ファイル文字起こし：62%", 62); StatusText.Text = "ファイル文字起こし：62%（停止で中止・認識済み部分は保存）";
            StopButton.IsEnabled = true; StartButton.IsEnabled = false;
            await Shot("file-import"); TaskPanel.Visibility = Visibility.Collapsed;

            // An opened recording: playback, search and word highlighting.
            double[] starts = SiteMeeting.Select((_, i) => 2.0 + i * 6.4).ToArray();
            string audio = await SavedRecording("定例会議", SiteMeeting, starts, true);
            await SavedRecording("製品レビュー", SiteMeeting.Reverse().ToArray(), starts, false);
            Reset("japanese"); await LoadRecordingAsync(audio);
            settings.PlaybackHighlightMode = "word";
            if (playbackReader is not null) { playbackReader.CurrentTime = TimeSpan.FromSeconds(starts[3] + 2.6); UpdatePlaybackPosition(); }
            RecordingSearchPanel.Visibility = Visibility.Visible; RecordingSearch.Text = "要約"; RefreshRecordingSearch();
            StatusText.Text = "文字や検索結果をクリックすると、その発話から再生します。";
            await Shot("playback-search", 900);
            RecordingSearchPanel.Visibility = Visibility.Collapsed;
            if (playbackReader is not null) { playbackReader.CurrentTime = TimeSpan.FromSeconds(starts[5] + 3.1); UpdatePlaybackPosition(); }
            StatusText.Text = "10 / 10 件の発話に単語時刻を付けました。設定の「再生ハイライトの単位」で単語ごとの表示を選べます。";
            await Shot("sync", 700);

            // Summary pane with evidence links.
            summaryItems = TranscriptSummary.Merge([TranscriptSummary.Parse("## 要点\n- 要約機能はまだ社内テストが不足している [3]\n- ウェブサイトの説明画像の更新が必要 [8][9]\n- 用語辞書の初期データは次回の会議で検討する [10]\n## 決定事項\n- リリースは来月の十五日に決定 [4][5]\n- 要約機能はベータとして表示して出す [4]\n## TODO\n- 佐藤さん：要約機能のテスト結果を共有（今週金曜まで） [6][7]\n- 田中さん：説明用のイラストを用意（来週水曜まで） [9][10]", history.Select(r => r.Id).ToArray())]);
            ShowSummaryPane(true); RenderSummary();
            StatusText.Text = "AI要約を作成し、録音フォルダーに保存しました。";
            await Shot("summary", 800); ShowSummaryPane(false);

            // Re-transcription comparison, subtitle export and the library.
            var current = playbackDocument!;
            var candidate = current with { Model = "medium", Phrases = current.Phrases.Select((p, i) => i == 2 ? p with { Text = "ただ、要約機能はまだ社内テストが足りていません。" } : i == 7 ? p with { Text = "あと、Webサイトの説明画像も更新が必要ですね。" } : p).ToArray() };
            current = current with { Phrases = current.Phrases.Select((p, i) => i == 2 ? p with { Text = "ただ、要約機能はまだ社内テストが足りません。" } : i == 7 ? p with { Text = "あと、ウェブサイトの説明画像も更新が必要ですね" } : i == 8 ? p with { Text = "田中さん、来週の水曜までにイラストを用意してもらえますか" } : p).ToArray() };
            await DialogShot("compare", async () => await ShowComparisonAsync(current, candidate, "現在の結果（small）", "新しい結果（medium）", "新しい結果を採用", "現在の結果を維持"));
            await DialogShot("subtitles", async () => { ExportSubtitlesClick(this, new RoutedEventArgs()); await Task.Delay(1500); });
            settings.RecordingSaveDirectory = work;
            await DialogShot("library", () => OpenRecordingLibraryAsync());
            ClosePlayback();
            // Screen recording: target picker, a recording in progress, video replay and subtitled export.
            await CaptureVideoScreensAsync(work, (name, settle) => Shot(name, settle), DialogShot, Reset, (rows, language) => Feed(rows, language));

            // Settings pages.
            SettingsClick(this, new RoutedEventArgs()); await Task.Delay(500);
            foreach (var (page, name) in new[] { ("用語辞書", "dictionary"), ("字幕の見た目", "settings-captions"), ("音声認識・Windows連携", "settings-speech") })
            {
                settingsNavigation!.Select(page); await Task.Delay(500);
                await settingsNavigation.CaptureAsync(Path.Combine(shots, name + ".png")); captured.Add(name);
            }
            settingsWindow?.Close();
        }
        catch (Exception ex) { failures.Add("exception: " + ex); }
        finally
        {
            SetThreadExecutionState(0x80000000);
            confirmReplacementForVerification = null;
            if (System.Text.Json.JsonSerializer.Deserialize<Locaptio.App.Settings>(saved) is { } original)
                foreach (var property in typeof(Locaptio.App.Settings).GetProperties().Where(p => p.CanWrite)) property.SetValue(settings, property.GetValue(original));
            settings.Save(); ClosePlayback();
            File.WriteAllText(Path.Combine(root, "artifacts", "site-screenshots.txt"), (failures.Count == 0 ? "PASS " : "FAIL " + string.Join("; ", failures) + " ") + string.Join(",", captured));
            Environment.ExitCode = failures.Count == 0 ? 0 : 1;
            try { Directory.Delete(work, true); } catch (IOException) { }
            await ShutdownAsync();
        }
    }
}
