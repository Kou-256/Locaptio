using System.Diagnostics;
using System.Runtime.InteropServices;
using Locaptio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using ScreenRecorderLib;

namespace Locaptio.WinUI;

internal sealed record CaptureChoice(CaptureTarget Target, bool SystemAudio, bool Microphone, bool Cursor, bool Clicks, int Framerate, string Quality, bool Countdown, bool Transcribe, bool HideLocaptio);

// Screen and window recording to MP4 with ScreenRecorderLib. The recording takes the place of a
// new audio recording: captions, bookmarks, search and replay use the video file the same way.
public sealed partial class MainWindow
{
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(nint window, uint affinity);
    private Recorder? videoRecorder;
    private bool videoPaused;
    private CaptureChoice? videoChoice;
    private long lastPreviewFrame;
    private WriteableBitmap? livePreviewBitmap;
    private Func<Task<CaptureChoice?>>? captureChoiceForVerification;
    // Screenshots for the website use sample targets instead of the user's real screen and windows.
    private List<CaptureTarget>? targetsForVerification;
    private Func<CaptureTarget, int, (byte[] Pixels, int Width, int Height)?>? snapshotForVerification;
    private (byte[] Pixels, int Width, int Height)? Snap(CaptureTarget target, int width) => snapshotForVerification is { } fake ? fake(target, width) : CaptureTargets.Snapshot(target, width);
    // True from the moment a video recording is requested, before the recorder object exists.
    private bool VideoRecording => recordingStop is not null && videoChoice is not null;

    private void SetVideoChecked(bool value)
    {
        VideoToggle.IsChecked = value; VideoToggleText.Text = value ? "録画中" : "新規録画";
    }

    private async void VideoToggleClick(object sender, RoutedEventArgs e)
    {
        if (closing || !initialized) return;
        if (VideoRecording) { await StopAllAsync(); return; }
        SetVideoChecked(false);
        if (dialogOpen || modelPreparationStop is not null || recordingStop is not null || exclusiveTask)
        {
            if (recordingStop is not null) Status("音声の録音を停止してから録画を始めてください。");
            return;
        }
        dialogOpen = true;
        RecordToggle.IsEnabled = VideoToggle.IsEnabled = StartButton.IsEnabled = ModeSelector.IsEnabled = AudioSelector.IsEnabled = false;
        try
        {
            bool confirmedExistingText = history.Count > 0;
            if (!await ConfirmNewRecordingAsync() || closing) return;
            var choice = captureChoiceForVerification is not null ? await captureChoiceForVerification() : await ChooseCaptureAsync();
            if (choice is null || closing) return;
            string directory = RecordingRoot; Directory.CreateDirectory(directory);
            string? path;
            if (recordingFilePickerForVerification is not null) path = await recordingFilePickerForVerification();
            else
            {
                var picker = new Microsoft.Windows.Storage.Pickers.FileSavePicker(AppWindow.Id) { Title = "新規録画：名前を指定（専用フォルダーを作成）", SuggestedFileName = DateTime.Now.ToString("yyyyMMdd") + "_録画" };
                picker.FileTypeChoices.Add("MP4動画", new List<string> { ".mp4" });
                if (Directory.Exists(directory)) picker.SuggestedFolder = directory;
                path = (await picker.PickSaveFileAsync())?.Path;
            }
            if (string.IsNullOrWhiteSpace(path) || closing) return;
            if (!path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) path = Path.ChangeExtension(path, ".mp4");
            if (!confirmedExistingText && !await ConfirmNewRecordingAsync()) return;
            bool keepWindowsConnection = windowsSession is not null && captionStop is { IsCancellationRequested: false };
            if (!keepWindowsConnection) await StopAsync();
            settings.RecordingSaveDirectory = Path.GetDirectoryName(Path.GetFullPath(path))!; settings.Save();
            path = RecordingLibrary.CreateSessionPath(path);
            ClearCaptions(); windowsSession?.ResetTranscript();
            if (choice.Countdown)
            {
                CountdownOverlay.Visibility = Visibility.Visible;
                for (int i = 3; i > 0 && !closing; i--) { CountdownText.Text = i.ToString(); await Task.Delay(VerificationRun ? 150 : 1000); }
                CountdownOverlay.Visibility = Visibility.Collapsed;
            }
            if (closing) return;
            StartVideoRecording(path, choice);
            if (choice.Transcribe && !keepWindowsConnection) await StartAsync(allowRecordingDialog: true);
        }
        catch (Exception ex) { Status("録画を開始できません: " + ex.Message); ShowStatusDetails(pin: true); }
        finally
        {
            CountdownOverlay.Visibility = Visibility.Collapsed;
            dialogOpen = false; SetVideoChecked(VideoRecording);
            RecordToggle.IsEnabled = VideoToggle.IsEnabled = !closing;
            StartButton.IsEnabled = !closing && captionStop is null;
            ModeSelector.IsEnabled = AudioSelector.IsEnabled = !closing && !inputChanging && !modeChanging;
        }
    }

    private void StartVideoRecording(string path, CaptureChoice choice)
    {
        recordingStop = new(); recordingPath = path; recordingStarted = Stopwatch.GetTimestamp(); videoChoice = choice; videoPaused = false;
        BeginRecordingIndex(path);
        // Nothing is in the video until its first frame; captions before that are not part of it.
        recordingTranscript!.Started = long.MaxValue;
        SetVideoChecked(true); RecordToggle.IsEnabled = false;
        RecordingLabel.Text = "● 録画中"; VideoControls.Visibility = Visibility.Visible; VideoPauseText.Text = "一時停止"; VideoPauseIcon.Glyph = "";
        LivePreviewFrame.Visibility = Visibility.Visible; livePreviewBitmap = null;
        if (Snap(choice.Target, 320) is { } still) LivePreview.Source = livePreviewBitmap = CaptureTargets.ToBitmap(still);
        SessionTitle.Text = "新規録画中：" + Path.GetFileName(Path.GetDirectoryName(path)) + "（" + choice.Target.Label + "）";
        StopButton.IsEnabled = true; RecordingText.Text = path; RecordingIndicator.Visibility = Visibility.Visible; UpdateDiagnostics();
        recordingTask = RecordVideoAsync(path, choice, recordingStop.Token);
    }

    private RecorderOptions VideoOptions(CaptureChoice choice, RecordingSourceBase source)
    {
        var audio = new AudioOptions { IsAudioEnabled = choice.SystemAudio || choice.Microphone };
        try
        {
            if (choice.SystemAudio && Recorder.GetSystemAudioLoopbackDevices().OfType<RecordableAudioLoopbackDevice>().FirstOrDefault(d => d.IsDefaultDevice) is { } output)
                audio.AudioSources.Add(new LoopbackAudioSource(output.DeviceName));
            if (choice.Microphone && Recorder.GetSystemAudioCaptureDevices().OfType<RecordableAudioCaptureDevice>().FirstOrDefault(d => d.IsDefaultDevice) is { } input)
                audio.AudioSources.Add(new CaptureAudioSource(input.DeviceName) { IsAudioCaptureEnabled = true });
        }
        catch (Exception ex) { Status("録画の音声デバイスを確認できません。映像のみ録画します: " + ex.Message); }
        if (audio.AudioSources.Count == 0) audio.IsAudioEnabled = false;
        int quality = choice.Quality switch { "high" => 85, "small" => 50, _ => 70 };
        return new RecorderOptions
        {
            SourceOptions = new SourceOptions { RecordingSources = { source } },
            AudioOptions = audio,
            VideoEncoderOptions = new VideoEncoderOptions
            {
                Framerate = choice.Framerate, Quality = quality, IsHardwareEncodingEnabled = true, IsMp4FastStartEnabled = true,
                Encoder = new H264VideoEncoder { BitrateMode = H264BitrateControlMode.Quality }
            },
            MouseOptions = new MouseOptions { IsMousePointerEnabled = choice.Cursor, IsMouseClicksDetected = choice.Cursor && choice.Clicks }
        };
    }

    private async Task RecordVideoAsync(string path, CaptureChoice choice, CancellationToken token)
    {
        // The library raises events on its own threads; continuing there and disposing the recorder deadlocks.
        var started = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Recorder? recorder = null;
        try
        {
            RecordingSourceBase source = choice.Target.IsDisplay
                ? new DisplayRecordingSource(choice.Target.DeviceName) { IsCursorCaptureEnabled = choice.Cursor }
                : new WindowRecordingSource(choice.Target.Window) { IsCursorCaptureEnabled = choice.Cursor, IsBorderRequired = false };
            source.IsVideoFramePreviewEnabled = true; source.VideoFramePreviewSize = new ScreenSize(320, 0);
            source.OnFrameRecorded += (_, e) => PushPreview(e.BitmapData);
            recorder = await Task.Run(() => Recorder.CreateRecorder(VideoOptions(choice, source)), token);
            recorder.OnStatusChanged += (_, e) => { if (e.Status == RecorderStatus.Recording) started.TrySetResult(Stopwatch.GetTimestamp()); };
            recorder.OnRecordingComplete += (_, _) => completed.TrySetResult(null);
            recorder.OnRecordingFailed += (_, e) => completed.TrySetResult(string.IsNullOrWhiteSpace(e.Error) ? "不明なエラー" : e.Error);
            videoRecorder = recorder;
            if (choice.HideLocaptio) ExcludeFromCapture(true);
            var record = recorder; await Task.Run(() => record.Record(path), token);
            var first = await Task.WhenAny(started.Task, completed.Task, Task.Delay(TimeSpan.FromSeconds(20), token));
            if (first == completed.Task) throw new IOException("録画を開始できませんでした: " + (await completed.Task ?? "開始前に終了しました"));
            if (first != started.Task) throw new TimeoutException("録画が20秒以内に始まりませんでした。対象のウィンドウが表示されているか確認してください。");
            long origin = await started.Task;
            if (recordingTranscript is not null) recordingTranscript.Started = origin;
            recordingStarted = origin;
            Status("録画を開始しました：" + choice.Target.Label + (choice.Transcribe ? "。文字起こしも同時に行います。" : "。"));
            try { await Task.Delay(Timeout.Infinite, token); } catch (OperationCanceledException) { }
            await Task.Run(() => record.Stop());
            string? error = await completed.Task.WaitAsync(TimeSpan.FromMinutes(2));
            if (error is not null) throw new IOException(error);
            StatusText.Text = "録画を保存しました: " + path;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            try { if (recorder is not null && recorder.Status is RecorderStatus.Recording or RecorderStatus.Paused) await Task.Run(() => recorder.Stop()); } catch (Exception) { }
            StatusText.Text = "録画に失敗しました: " + ex.Message; ShowStatusDetails(pin: true);
        }
        finally
        {
            ExcludeFromCapture(false);
            videoRecorder = null; videoPaused = false; videoChoice = null;
            if (recorder is not null) { var dispose = recorder; await Task.Run(() => dispose.Dispose()); }
            recordingStop?.Dispose(); recordingStop = null;
            VideoControls.Visibility = LivePreviewFrame.Visibility = Visibility.Collapsed; LivePreview.Source = null; livePreviewBitmap = null;
            RecordingLabel.Text = "● 録音中";
            if (File.Exists(path) && new FileInfo(path).Length > 0) await FinishRecordingIndexAsync(path);
            else Status("録画ファイルが作成されませんでした。");
            SetVideoChecked(false); RecordToggle.IsEnabled = VideoToggle.IsEnabled = true; RecordingIndicator.Visibility = Visibility.Collapsed;
            StopButton.IsEnabled = captionStop is not null; StartButton.IsEnabled = !closing && captionStop is null;
        }
    }

    /// <summary>Copies a preview frame from the recorder's thread and shows at most four per second.</summary>
    private void PushPreview(FrameBitmapData frame)
    {
        long now = Stopwatch.GetTimestamp();
        if (frame.Width <= 0 || frame.Height <= 0 || Stopwatch.GetElapsedTime(Interlocked.Read(ref lastPreviewFrame), now).TotalMilliseconds < 250) return;
        Interlocked.Exchange(ref lastPreviewFrame, now);
        int width = frame.Width, height = frame.Height, stride = frame.Stride;
        var pixels = new byte[width * height * 4];
        for (int row = 0; row < height; row++) Marshal.Copy(frame.Data + row * stride, pixels, row * width * 4, width * 4);
        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (closing || !VideoRecording) return;
            livePreviewBitmap = CaptureTargets.ToBitmap((pixels, width, height), livePreviewBitmap);
            if (!ReferenceEquals(LivePreview.Source, livePreviewBitmap)) LivePreview.Source = livePreviewBitmap;
        });
    }

    /// <summary>Keeps Locaptio's own main window out of the video. The caption window stays visible on purpose.</summary>
    private void ExcludeFromCapture(bool exclude)
    {
        foreach (var window in new Window?[] { this, settingsWindow, modelHelpWindow })
            if (window is not null) try { SetWindowDisplayAffinity(WinRT.Interop.WindowNative.GetWindowHandle(window), exclude ? 0x11u : 0u); } catch (Exception) { }
    }

    private void VideoPauseClick(object sender, RoutedEventArgs e)
    {
        if (videoRecorder is not { } recorder || recordingTranscript is null) return;
        long now = Stopwatch.GetTimestamp();
        if (videoPaused) { recorder.Resume(); recordingTranscript.Resume(now); videoPaused = false; VideoPauseText.Text = "一時停止"; VideoPauseIcon.Glyph = ""; RecordingLabel.Text = "● 録画中"; Status("録画を再開しました。"); }
        else { recorder.Pause(); recordingTranscript.Pause(now); videoPaused = true; VideoPauseText.Text = "再開"; VideoPauseIcon.Glyph = ""; RecordingLabel.Text = "Ⅱ 一時停止中"; Status("録画を一時停止しました。一時停止中の映像と発話は動画に入りません。"); }
    }

    private async void ScreenshotClick(object sender, RoutedEventArgs e)
    {
        if (videoRecorder is not { } recorder || videoPaused) return;
        string file = Path.Combine(Path.GetDirectoryName(recordingPath)!, $"スクリーンショット_{DateTime.Now:HHmmss}.png");
        bool saved = await Task.Run(() => { try { return recorder.TakeSnapshot(file); } catch (Exception) { return false; } });
        if (!saved) { Status("スクリーンショットを保存できませんでした。"); return; }
        AddBookmark("スクリーンショット"); Status("スクリーンショットを保存しました: " + file);
    }

    /// <summary>Picks the screen or window, with thumbnails, a live preview of the selection and recording options.</summary>
    private async Task<CaptureChoice?> ChooseCaptureAsync()
    {
        var targets = targetsForVerification ?? await Task.Run(CaptureTargets.List);
        if (targets.Count == 0) { Status("録画できる画面やウィンドウが見つかりません。"); return null; }
        var items = new System.Collections.ObjectModel.ObservableCollection<StackPanel>();
        var list = new GridView { SelectionMode = ListViewSelectionMode.Single, ItemsSource = items, MaxHeight = 430, Width = 470 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(list, "録画する対象");
        var thumbnails = new Dictionary<CaptureTarget, Image>();
        foreach (var target in targets)
        {
            var image = new Image { Width = 200, Height = 112, Stretch = Stretch.Uniform };
            var cell = new StackPanel { Width = 212, Spacing = 3, Padding = new Thickness(6), Tag = target };
            cell.Children.Add(new Border { Background = new SolidColorBrush(Microsoft.UI.Colors.Black), CornerRadius = new CornerRadius(4), Child = image });
            var title = new TextBlock { Text = target.Label, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };
            ToolTipService.SetToolTip(cell, target.Label);
            cell.Children.Add(title);
            cell.Children.Add(new TextBlock { Text = target.Kind + "・" + target.Size, FontSize = 11, Opacity = .7 });
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(cell, target.Kind + " " + target.Label);
            items.Add(cell); thumbnails[target] = image;
        }
        // Sample targets (website screenshots) are drawn at once; real windows are captured in the background.
        if (snapshotForVerification is not null) { foreach (var target in targets) if (Snap(target, 200) is { } shot) thumbnails[target].Source = CaptureTargets.ToBitmap(shot); }
        else _ = Task.Run(() =>
        {
            foreach (var target in targets)
                if (Snap(target, 200) is { } shot) DispatcherQueue.TryEnqueue(() => thumbnails[target].Source = CaptureTargets.ToBitmap(shot));
        });
        var preview = new Image { Stretch = Stretch.Uniform };
        var previewFrame = new Border { Width = 440, Height = 248, Background = new SolidColorBrush(Microsoft.UI.Colors.Black), CornerRadius = new CornerRadius(6), Child = preview };
        var previewTitle = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
        CheckBox Option(string text, bool value) => new() { Content = text, IsChecked = value, MinWidth = 0 };
        var systemAudio = Option("PC内の音声を録音", settings.VideoSystemAudio);
        var microphone = Option("マイクの音声を録音", settings.VideoMicrophone);
        var cursor = Option("マウスカーソルを映す", settings.VideoCursor);
        var clicks = Option("クリックした位置を強調", settings.VideoClicks);
        var countdown = Option("開始前に3秒カウントダウン", settings.VideoCountdown);
        var transcribe = Option("録画しながら文字起こし（字幕・検索・聞き直しに使います）", settings.VideoTranscribe);
        var hide = Option("Locaptioの本体ウィンドウを録画に映さない（字幕ウィンドウは映ります）", settings.VideoHideLocaptio);
        var framerate = new ComboBox { ItemsSource = new[] { "15 fps（ファイル小）", "30 fps（標準）", "60 fps（動きが滑らか）" }, SelectedIndex = settings.VideoFramerate >= 60 ? 2 : settings.VideoFramerate <= 15 ? 0 : 1, Header = "フレームレート", MinWidth = 200 };
        var quality = new ComboBox { ItemsSource = new[] { "標準", "高画質", "ファイルサイズ優先" }, SelectedIndex = settings.VideoQuality == "high" ? 1 : settings.VideoQuality == "small" ? 2 : 0, Header = "画質", MinWidth = 200 };
        cursor.Click += (_, _) => clicks.IsEnabled = cursor.IsChecked == true; clicks.IsEnabled = cursor.IsChecked == true;
        var options = new StackPanel { Spacing = 4 };
        options.Children.Add(new TextBlock { Text = "プレビュー（選んだ対象を表示しています）", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        options.Children.Add(previewFrame); options.Children.Add(previewTitle);
        var selects = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 6, 0, 0) }; selects.Children.Add(framerate); selects.Children.Add(quality);
        options.Children.Add(selects);
        foreach (var box in new[] { systemAudio, microphone, cursor, clicks, countdown, transcribe, hide }) options.Children.Add(box);
        var layout = new Grid { ColumnSpacing = 18 };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Spacing = 6 };
        left.Children.Add(new TextBlock { Text = "録画する画面全体、またはウィンドウを選んでください。", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        left.Children.Add(list);
        Grid.SetColumn(options, 1); layout.Children.Add(left); layout.Children.Add(options);
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "録画する対象を選ぶ", Content = new ScrollViewer { Content = layout, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            PrimaryButtonText = "録画を開始", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Primary, RequestedTheme = Root.RequestedTheme };
        dialog.Resources["ContentDialogMaxWidth"] = 1100.0;
        CaptureTarget? selected = null; WriteableBitmap? previewBitmap = null;
        void Refresh()
        {
            if (selected is null) return;
            var target = selected;
            if (snapshotForVerification is not null) { if (Snap(target, 640) is { } sample) preview.Source = previewBitmap = CaptureTargets.ToBitmap(sample, previewBitmap); return; }
            _ = Task.Run(() => Snap(target, 640)).ContinueWith(task =>
                DispatcherQueue.TryEnqueue(() => { if (task.Result is { } shot && ReferenceEquals(target, selected)) preview.Source = previewBitmap = CaptureTargets.ToBitmap(shot, previewBitmap); }), TaskScheduler.Default);
        }
        list.SelectionChanged += (_, _) =>
        {
            selected = (list.SelectedItem as StackPanel)?.Tag as CaptureTarget;
            previewTitle.Text = selected is null ? "" : (selected.IsDisplay ? selected.Label : selected.Kind + "：" + selected.Label) + "（" + selected.Size + "）";
            dialog.IsPrimaryButtonEnabled = selected is not null; previewBitmap = null; Refresh();
        };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) }; timer.Tick += (_, _) => Refresh();
        list.SelectedIndex = 0; timer.Start();
        ContentDialogResult result;
        try { result = await dialog.ShowAsync(); }
        finally { timer.Stop(); }
        if (result != ContentDialogResult.Primary || selected is null) return null;
        settings.VideoSystemAudio = systemAudio.IsChecked == true; settings.VideoMicrophone = microphone.IsChecked == true; settings.VideoCursor = cursor.IsChecked == true;
        settings.VideoClicks = clicks.IsChecked == true; settings.VideoCountdown = countdown.IsChecked == true; settings.VideoTranscribe = transcribe.IsChecked == true;
        settings.VideoHideLocaptio = hide.IsChecked == true; settings.VideoFramerate = framerate.SelectedIndex == 2 ? 60 : framerate.SelectedIndex == 0 ? 15 : 30;
        settings.VideoQuality = quality.SelectedIndex == 1 ? "high" : quality.SelectedIndex == 2 ? "small" : "standard"; settings.Save();
        return new(selected, settings.VideoSystemAudio, settings.VideoMicrophone, settings.VideoCursor, settings.VideoClicks, settings.VideoFramerate, settings.VideoQuality, settings.VideoCountdown, settings.VideoTranscribe, settings.VideoHideLocaptio);
    }
}
