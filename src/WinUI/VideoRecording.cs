using System.Diagnostics;
using System.Runtime.InteropServices;
using Locaptio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using ScreenRecorderLib;

namespace Locaptio.WinUI;

internal sealed record CaptureChoice(CaptureTarget Target, bool SystemAudio, bool Microphone, bool Cursor, bool Clicks, int Framerate, string Quality, int CountdownSeconds, bool Transcribe, bool HideLocaptio, CaptureRegion? Region = null)
{
    /// <summary>What is recorded, for the title and messages: the display or window, and the part of the display.</summary>
    public string Label => Target.Label + (Region is { } region ? "の範囲（" + region + "）" : "");
}

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
    // The source being recorded (its region can change while recording) and a pending switch to another target.
    private RecordingSourceBase? videoSource;
    private TaskCompletionSource<CaptureChoice>? videoSwitchRequest;
    private Func<Task<CaptureChoice?>>? switchChoiceForVerification;
    /// <summary>One started recorder writing one file.</summary>
    private sealed record VideoRun(Recorder Recorder, RecordingSourceBase Source, long Started, Task<string?> Completed, string Note);

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
            if (!await ConfirmNewRecordingAsync("新規録画") || closing) return;
            var choice = captureChoiceForVerification is not null ? await captureChoiceForVerification() : await ChooseCaptureAsync();
            if (choice is null || closing) { continuation = null; return; }
            string directory = RecordingRoot; Directory.CreateDirectory(directory);
            string? path;
            // Continuing the open recording: the new part records next to it, no new file or folder is asked for.
            if (continuation is { } continued) path = continued.Part;
            else if (recordingFilePickerForVerification is not null) path = await recordingFilePickerForVerification();
            else
            {
                var picker = new Microsoft.Windows.Storage.Pickers.FileSavePicker(AppWindow.Id) { Title = "新規録画：名前を指定（専用フォルダーを作成）", SuggestedFileName = DateTime.Now.ToString("yyyyMMdd") + "_録画" };
                picker.FileTypeChoices.Add("MP4動画", new List<string> { ".mp4" });
                if (Directory.Exists(directory)) picker.SuggestedFolder = directory;
                path = (await picker.PickSaveFileAsync())?.Path;
            }
            if (string.IsNullOrWhiteSpace(path) || closing) return;
            if (!path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) path = Path.ChangeExtension(path, ".mp4");
            var continuing = continuation;
            if (continuing is null && !confirmedExistingText && !await ConfirmNewRecordingAsync("新規録画")) return;
            bool keepWindowsConnection = windowsSession is not null && captionStop is { IsCancellationRequested: false };
            if (!keepWindowsConnection) await StopAsync();
            if (continuing is null)
            {
                settings.RecordingSaveDirectory = Path.GetDirectoryName(Path.GetFullPath(path))!; settings.Save();
                path = RecordingLibrary.CreateSessionPath(path);
            }
            PrepareTextForNewRecording();
            if (choice.CountdownSeconds > 0)
            {
                CountdownOverlay.Visibility = Visibility.Visible;
                for (int i = choice.CountdownSeconds; i > 0 && !closing; i--) { CountdownText.Text = i.ToString(); await Task.Delay(VerificationRun ? 150 : 1000); }
                CountdownOverlay.Visibility = Visibility.Collapsed;
            }
            if (closing) return;
            StartVideoRecording(path, choice);
            if (choice.Transcribe && !keepWindowsConnection) await StartAsync(allowRecordingDialog: true);
            if (continuing is not null) SessionTitle.Text = "続きを録画中：" + Path.GetFileName(Path.GetDirectoryName(continuing.Target)) + " / " + Path.GetFileName(continuing.Target);
        }
        catch (Exception ex) { Status("録画を開始できません: " + VideoErrorText(ex)); ShowStatusDetails(pin: true); }
        finally
        {
            if (recordingStop is null) continuation = null;
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
        SessionTitle.Text = "新規録画中：" + Path.GetFileName(Path.GetDirectoryName(path)) + "（" + choice.Label + "）";
        StopButton.IsEnabled = true; RecordingText.Text = path; RecordingIndicator.Visibility = Visibility.Visible; UpdateDiagnostics();
        recordingTask = RecordVideoAsync(path, choice, recordingStop.Token);
    }

    internal static readonly string[] QualityKeys = ["best", "high", "standard", "small"];
    internal static readonly string[] QualityLabels = ["最高画質", "高画質", "標準", "ファイルサイズ優先"];
    internal static readonly int[] CountdownChoices = [0, 1, 2, 3, 5, 10];
    internal static readonly string[] CountdownLabels = ["なし", "1秒", "2秒", "3秒", "5秒", "10秒"];
    internal static int VideoQualityValue(string key) => key switch { "best" => 95, "high" => 85, "small" => 50, _ => 70 };
    private static string TargetKey(CaptureTarget target) => target.IsDisplay ? "display:" + target.DeviceName : "window:" + target.Title;
    /// <summary>Self-check: start attempts that fail as if the encoder were unusable on this PC.</summary>
    private int videoAttemptsToFailForVerification;
    /// <summary>The recording library (C++/CLI) or the Visual C++ runtime it needs could not be loaded.</summary>
    private static bool IsRecorderMissing(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
            if (ex is FileNotFoundException or FileLoadException or BadImageFormatException or DllNotFoundException
                || ex is TypeInitializationException or TypeLoadException && ex.Message.Contains("ScreenRecorderLib", StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
    private static string VideoErrorText(Exception ex) => IsRecorderMissing(ex)
        ? "録画機能に必要な Microsoft Visual C++ ランタイムを読み込めませんでした。最新版の Locaptio に更新するか、Microsoft の「Visual C++ 再頒布可能パッケージ (x64)」をインストールしてから、もう一度お試しください: https://aka.ms/vs/17/release/vc_redist.x64.exe（詳細: " + ex.Message + "）"
        : ex.Message;

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
        int quality = VideoQualityValue(choice.Quality);
        return new RecorderOptions
        {
            SourceOptions = new SourceOptions { RecordingSources = { source } },
            AudioOptions = audio,
            VideoEncoderOptions = new VideoEncoderOptions
            {
                // Fragments are written as they are made: if Locaptio is closed while recording, the video up to that
                // point can be recovered (Mp4Repair). A single-piece MP4 would keep its index until the end and be lost.
                Framerate = choice.Framerate, Quality = quality, IsHardwareEncodingEnabled = true, IsMp4FastStartEnabled = false, IsFragmentedMp4Enabled = true,
                Encoder = new H264VideoEncoder { BitrateMode = H264BitrateControlMode.Quality }
            },
            MouseOptions = new MouseOptions { IsMousePointerEnabled = choice.Cursor, IsMouseClicksDetected = choice.Cursor && choice.Clicks }
        };
    }

    /// <summary>
    /// Starts a recorder on the choice. Some PCs cannot use the hardware H.264 encoder or Desktop Duplication (for
    /// example a laptop whose screen is driven by another GPU): software encoding, then Windows Graphics Capture are tried.
    /// </summary>
    private async Task<VideoRun> StartVideoRunAsync(string path, CaptureChoice choice, CancellationToken token)
    {
        var attempts = new List<(bool Hardware, RecorderApi? Api, string Note)> { (true, null, ""), (false, null, "ハードウェアエンコーダーを使えなかったため、ソフトウェアで録画しています。") };
        if (choice.Target.IsDisplay) attempts.Add((false, RecorderApi.WindowsGraphicsCapture, "画面の取り込み方式を切り替えて録画しています。"));
        var failures = new List<string>();
        for (int attemptIndex = 0; attemptIndex < attempts.Count; attemptIndex++)
        {
            var attempt = attempts[attemptIndex];
            // The library raises events on its own threads; continuing there and disposing the recorder deadlocks.
            var started = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
            var completed = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            RecordingSourceBase source = choice.Target.IsDisplay
                ? new DisplayRecordingSource(choice.Target.DeviceName) { IsCursorCaptureEnabled = choice.Cursor }
                : new WindowRecordingSource(choice.Target.Window) { IsCursorCaptureEnabled = choice.Cursor, IsBorderRequired = false };
            if (attempt.Api is RecorderApi api && source is DisplayRecordingSource display) display.RecorderApi = api;
            // A region is in the display's pixels; the video frame takes its size.
            if (choice.Target.IsDisplay && choice.Region is { } region) source.SourceRect = new ScreenRect(region.X, region.Y, region.Width, region.Height);
            source.IsVideoFramePreviewEnabled = true; source.VideoFramePreviewSize = new ScreenSize(320, 0);
            source.OnFrameRecorded += (_, e) => PushPreview(e.BitmapData);
            Recorder? recorder = null; bool running = false;
            try
            {
                if (attemptIndex < videoAttemptsToFailForVerification) throw new IOException("simulated encoder failure");
                var options = VideoOptions(choice, source); options.VideoEncoderOptions.IsHardwareEncodingEnabled = attempt.Hardware;
                recorder = await Task.Run(() => Recorder.CreateRecorder(options), token);
                recorder.OnStatusChanged += (_, e) => { if (e.Status == RecorderStatus.Recording) started.TrySetResult(Stopwatch.GetTimestamp()); };
                recorder.OnRecordingComplete += (_, _) => completed.TrySetResult(null);
                recorder.OnRecordingFailed += (_, e) => completed.TrySetResult(string.IsNullOrWhiteSpace(e.Error) ? "不明なエラー" : e.Error);
                videoRecorder = recorder;
                var starting = recorder; await Task.Run(() => starting.Record(path), token);
                var first = await Task.WhenAny(started.Task, completed.Task, Task.Delay(TimeSpan.FromSeconds(20), token));
                if (first == started.Task) { running = true; videoSource = source; return new(recorder, source, await started.Task, completed.Task, attempt.Note); }
                if (first != completed.Task) throw new TimeoutException("録画が20秒以内に始まりませんでした。対象のウィンドウが表示されているか確認してください。");
                failures.Add(await completed.Task ?? "開始前に終了しました");
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not TimeoutException && !IsRecorderMissing(ex)) { failures.Add(ex.Message); }
            finally
            {
                // An attempt that did not start is released with its partial file before the next one.
                if (!running)
                {
                    if (ReferenceEquals(videoRecorder, recorder)) videoRecorder = null;
                    if (recorder is not null) { var failed = recorder; await Task.Run(() => failed.Dispose()); }
                    try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
                }
            }
        }
        throw new IOException("録画を開始できませんでした: " + string.Join(" / ", failures.Distinct()));
    }

    /// <summary>Stops a recorder, waits for its file to be written and releases it. Returns the library's error, if any.</summary>
    private async Task<string?> StopVideoRunAsync(VideoRun run)
    {
        string? error;
        try
        {
            if (run.Recorder.Status is RecorderStatus.Recording or RecorderStatus.Paused) await Task.Run(() => run.Recorder.Stop());
            error = await run.Completed.WaitAsync(TimeSpan.FromMinutes(2));
        }
        catch (Exception ex) { error = ex.Message; }
        if (ReferenceEquals(videoRecorder, run.Recorder)) videoRecorder = null;
        if (ReferenceEquals(videoSource, run.Source)) videoSource = null;
        await Task.Run(() => run.Recorder.Dispose());
        return error;
    }

    /// <summary>The n-th part of a recording whose target was switched (joined into the recording's file at the end).</summary>
    internal static string VideoPartPath(string path, int number) => Path.Combine(Path.GetDirectoryName(path)!, $"{Path.GetFileNameWithoutExtension(path)}.part{number}{Path.GetExtension(path)}");

    private async Task RecordVideoAsync(string path, CaptureChoice choice, CancellationToken token)
    {
        VideoRun? run = null; var parts = new List<string>();
        try
        {
            if (choice.HideLocaptio) ExcludeFromCapture(true);
            run = await StartVideoRunAsync(path, choice, token);
            if (recordingTranscript is not null) recordingTranscript.Started = run.Started;
            recordingStarted = run.Started;
            Status("録画を開始しました：" + choice.Label + (choice.Transcribe ? "。文字起こしも同時に行います。" : "。") + run.Note);
            var stopped = Task.Delay(Timeout.Infinite, token).ContinueWith(_ => { }, TaskScheduler.Default);
            while (true)
            {
                var request = videoSwitchRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var first = await Task.WhenAny(stopped, request.Task, run.Completed);
                if (first == stopped) break;
                if (first == run.Completed) throw new IOException(await run.Completed ?? "録画が途中で終了しました。");
                // Another display or window: the current file is closed and the next part starts on the new target.
                // The moments in between are not in the video, like a pause.
                var next = await request.Task; videoSwitchRequest = null;
                Status("録画する対象を切り替えています…");
                recordingTranscript?.Pause(Stopwatch.GetTimestamp());
                var previous = run; run = null;
                if (await StopVideoRunAsync(previous) is { } error) throw new IOException(error);
                if (parts.Count == 0) { string firstPart = VideoPartPath(path, 1); File.Move(path, firstPart, true); parts.Add(firstPart); }
                string part = VideoPartPath(path, parts.Count + 1); string failure = "";
                try { run = await StartVideoRunAsync(part, next, token); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // The new target could not be recorded: carry on with the previous one.
                    failure = VideoErrorText(ex); next = videoChoice!;
                    run = await StartVideoRunAsync(part, next, token);
                }
                parts.Add(part);
                recordingTranscript?.Resume(run.Started);
                ShowVideoTarget(next);
                Status(failure.Length > 0
                    ? "切り替え先を録画できなかったため、元の対象で録画を続けています: " + failure
                    : "録画する対象を切り替えました：" + next.Label + "。録画を止めると1つの動画にまとめます。" + run.Note);
            }
            videoSwitchRequest = null;
            var last = run; run = null;
            if (await StopVideoRunAsync(last) is { } stopError) throw new IOException(stopError);
            StatusText.Text = "録画を保存しました: " + path;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            StatusText.Text = "録画に失敗しました: " + VideoErrorText(ex); ShowStatusDetails(pin: true);
        }
        finally
        {
            videoSwitchRequest = null;
            // Until the file is saved (and joined to a continued recording), a new recording cannot start.
            RecordToggle.IsEnabled = VideoToggle.IsEnabled = false;
            if (run is not null) await StopVideoRunAsync(run);
            ExcludeFromCapture(false);
            videoRecorder = null; videoSource = null; videoPaused = false; videoChoice = null;
            recordingStop?.Dispose(); recordingStop = null;
            VideoControls.Visibility = LivePreviewFrame.Visibility = Visibility.Collapsed; LivePreview.Source = null; livePreviewBitmap = null;
            RecordingLabel.Text = "● 録音中";
            if (parts.Count > 0) await JoinRecordedPartsAsync(parts, path, choice.Framerate);
            if (File.Exists(path) && new FileInfo(path).Length > 0) await FinishRecordingIndexAsync(path);
            else Status("録画ファイルが作成されませんでした。");
            SetVideoChecked(false); RecordToggle.IsEnabled = VideoToggle.IsEnabled = true; RecordingIndicator.Visibility = Visibility.Collapsed;
            StopButton.IsEnabled = captionStop is not null; StartButton.IsEnabled = !closing && captionStop is null;
        }
    }

    /// <summary>
    /// Joins the parts of a recording whose target was switched into the recording's file (re-encoded at the first part's
    /// size; other sizes are fitted into it). When that fails, the first part becomes the recording and the others stay
    /// next to it.
    /// </summary>
    private async Task JoinRecordedPartsAsync(List<string> parts, string path, int framerate)
    {
        var existing = parts.Where(p => File.Exists(p) && new FileInfo(p).Length > 0).ToList();
        if (existing.Count == 0) return;
        if (existing.Count == 1) { File.Move(existing[0], path, true); return; }
        try
        {
            var progress = new Progress<double>(percent => StatusText.Text = $"録画した部分を1つの動画にまとめています… {percent:F0}%");
            await JoinVideosAsync(existing, path, framerate, progress);
            foreach (var part in existing) try { File.Delete(part); } catch (IOException) { }
            Status("録画を保存しました（" + existing.Count + "つの部分を1つの動画にまとめました）: " + path);
        }
        catch (Exception ex)
        {
            try { if (File.Exists(path)) File.Delete(path); File.Move(existing[0], path, true); } catch (IOException) { }
            Status("録画した部分を1つにまとめられませんでした。最初の部分を録画として開き、残りは同じフォルダーに「.part2」などの名前で残しています: " + ex.Message);
            ShowStatusDetails(pin: true);
        }
    }

    /// <summary>
    /// An H.264/AAC profile for re-encoding at the given size. It is built from the size and frame rate, not copied from
    /// a recording: a recording of a still screen has only a few frames, and a profile taken from it (about 1 fps and a few
    /// hundred bits per second) makes some hardware encoders crash.
    /// </summary>
    internal static Windows.Media.MediaProperties.MediaEncodingProfile EncodingProfile(int width, int height, int framerate, bool audio)
    {
        var profile = Windows.Media.MediaProperties.MediaEncodingProfile.CreateMp4(Windows.Media.MediaProperties.VideoEncodingQuality.HD1080p);
        uint w = (uint)Math.Max(64, width) & ~1u, h = (uint)Math.Max(64, height) & ~1u; framerate = Math.Clamp(framerate, 10, 60);
        profile.Video.Width = w; profile.Video.Height = h;
        profile.Video.FrameRate.Numerator = (uint)framerate; profile.Video.FrameRate.Denominator = 1;
        profile.Video.PixelAspectRatio.Numerator = 1; profile.Video.PixelAspectRatio.Denominator = 1;
        // About 3.7 Mbps for 1080p at 60 fps: plenty for screens, which change little between frames.
        profile.Video.Bitrate = (uint)Math.Clamp(w * h * framerate * .03, 1_000_000, 12_000_000);
        profile.Audio = audio ? Windows.Media.MediaProperties.AudioEncodingProperties.CreateAac(48000, 2, 192000) : null;
        return profile;
    }

    internal static async Task JoinVideosAsync(IReadOnlyList<string> videos, string output, int framerate, IProgress<double>? progress = null)
    {
        var composition = new Windows.Media.Editing.MediaComposition();
        foreach (var video in videos) composition.Clips.Add(await Windows.Media.Editing.MediaClip.CreateFromFileAsync(await Windows.Storage.StorageFile.GetFileFromPathAsync(video)));
        var first = composition.Clips[0].GetVideoEncodingProperties();
        bool audio = composition.Clips.Any(c => c.EmbeddedAudioTracks.Count > 0);
        var profile = EncodingProfile((int)first.Width, (int)first.Height, framerate, audio);
        if (File.Exists(output)) File.Delete(output);
        await File.WriteAllBytesAsync(output, []);
        try
        {
            var render = composition.RenderToFileAsync(await Windows.Storage.StorageFile.GetFileFromPathAsync(output), Windows.Media.Editing.MediaTrimmingPreference.Precise, profile);
            render.Progress = (_, percent) => progress?.Report(percent);
            var result = await render;
            if (result != Windows.Media.Transcoding.TranscodeFailureReason.None) throw new IOException("動画の書き出しに失敗しました: " + result);
        }
        catch { try { if (File.Exists(output) && new FileInfo(output).Length == 0) File.Delete(output); } catch (IOException) { } throw; }
    }

    /// <summary>Title, preview and the stored choice after the recorded target or region changed.</summary>
    private void ShowVideoTarget(CaptureChoice choice)
    {
        videoChoice = choice; livePreviewBitmap = null;
        if (Snap(choice.Target, 320) is { } still) LivePreview.Source = livePreviewBitmap = CaptureTargets.ToBitmap(still);
        if (recordingPath.Length > 0) SessionTitle.Text = "新規録画中：" + Path.GetFileName(Path.GetDirectoryName(recordingPath)) + "（" + choice.Label + "）";
    }

    /// <summary>「対象を変更」: pick another display, window or region without stopping the recording.</summary>
    private async void VideoTargetClick(object sender, RoutedEventArgs e)
    {
        if (!VideoRecording || videoChoice is null || videoRecorder is null || dialogOpen || closing) return;
        if (videoPaused) { Status("一時停止中は録画する対象を変更できません。再開してから変更してください。"); return; }
        CaptureChoice? next;
        dialogOpen = true;
        try { next = switchChoiceForVerification is not null ? await switchChoiceForVerification() : await ChooseCaptureAsync(videoChoice); }
        finally { dialogOpen = false; }
        if (next is not null && VideoRecording && !videoPaused) SwitchVideoTarget(next);
    }

    /// <summary>
    /// The same display with another region (or the whole display) changes in place, without a break in the video;
    /// the frame keeps the size it started with and the new region is fitted into it. Any other target starts a new part.
    /// </summary>
    private void SwitchVideoTarget(CaptureChoice next)
    {
        var current = videoChoice!;
        if (current.Target.IsDisplay && next.Target.IsDisplay && string.Equals(current.Target.DeviceName, next.Target.DeviceName, StringComparison.OrdinalIgnoreCase)
            && videoSource is DisplayRecordingSource display && videoRecorder is { } recorder)
        {
            try
            {
                var region = next.Region ?? new CaptureRegion(0, 0, next.Target.Width & ~1, next.Target.Height & ~1);
                display.SourceRect = new ScreenRect(region.X, region.Y, region.Width, region.Height);
                recorder.GetDynamicOptionsBuilder().SetUpdatedRecordingSource(display).Apply();
                ShowVideoTarget(next);
                Status("録画する範囲を切り替えました：" + next.Label + "。動画の大きさは録画開始時のままで、範囲を合わせて表示します。");
                return;
            }
            catch (Exception ex) { Status("範囲をその場で切り替えられなかったため、録画を区切って切り替えます: " + ex.Message); }
        }
        videoSwitchRequest?.TrySetResult(next);
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

    /// <summary>
    /// Picks the screen or window, with thumbnails, a live preview of the selection and recording options. A display can be
    /// recorded in part: drag on the preview or type the region. While recording (<paramref name="switching"/>), only the
    /// target and region can change.
    /// </summary>
    private async Task<CaptureChoice?> ChooseCaptureAsync(CaptureChoice? switching = null)
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
        const double PreviewWidth = 440, PreviewHeight = 248;
        var preview = new Image { Stretch = Stretch.Uniform };
        // The region is drawn over the preview; dragging on it sets the region.
        var regionBox = new Microsoft.UI.Xaml.Shapes.Rectangle { Stroke = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 255, 196, 0)), StrokeThickness = 2, Fill = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(48, 255, 196, 0)), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        var dragSurface = new Canvas { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), Width = PreviewWidth, Height = PreviewHeight };
        dragSurface.Children.Add(regionBox);
        var previewGrid = new Grid(); previewGrid.Children.Add(preview); previewGrid.Children.Add(dragSurface);
        var previewFrame = new Border { Width = PreviewWidth, Height = PreviewHeight, HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(Microsoft.UI.Colors.Black), CornerRadius = new CornerRadius(6), Child = previewGrid };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(dragSurface, "プレビュー。ドラッグで録画する範囲を指定");
        var previewTitle = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };

        // Whole display or a region (displays only).
        string group = "capture-area-" + Guid.NewGuid().ToString("N");
        var wholeArea = new RadioButton { Content = "全体を録画", GroupName = group, IsChecked = true, MinWidth = 0 };
        var partArea = new RadioButton { Content = "範囲を指定して録画", GroupName = group, MinWidth = 0 };
        NumberBox Number(string header) => new() { Header = header, Minimum = 0, Maximum = 100000, SmallChange = 10, LargeChange = 100, Width = 100, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, ValidationMode = NumberBoxValidationMode.InvalidInputOverwritten };
        var regionX = Number("X（左から）"); var regionY = Number("Y（上から）"); var regionWidth = Number("幅"); var regionHeight = Number("高さ");
        var areaHint = new TextBlock { FontSize = 12, Opacity = .75, TextWrapping = TextWrapping.Wrap, Width = PreviewWidth, HorizontalAlignment = HorizontalAlignment.Left };
        var areaChoice = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 }; areaChoice.Children.Add(wholeArea); areaChoice.Children.Add(partArea);
        var numbers = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var box in new FrameworkElement[] { regionX, regionY, regionWidth, regionHeight }) numbers.Children.Add(box);
        var area = new StackPanel { Spacing = 4 }; area.Children.Add(areaChoice); area.Children.Add(numbers); area.Children.Add(areaHint);

        CheckBox Option(string text, bool value) => new() { Content = text, IsChecked = value, MinWidth = 0 };
        var systemAudio = Option("PC内の音声を録音", settings.VideoSystemAudio);
        var microphone = Option("マイクの音声を録音", settings.VideoMicrophone);
        var cursor = Option("マウスカーソルを映す", settings.VideoCursor);
        var clicks = Option("クリックした位置を強調", settings.VideoClicks);

        var transcribe = Option("録画しながら文字起こし（字幕・検索・聞き直しに使います）", settings.VideoTranscribe);
        var hide = Option("Locaptioの本体ウィンドウを録画に映さない（字幕ウィンドウは映ります）", settings.VideoHideLocaptio);
        var framerate = new ComboBox { ItemsSource = new[] { "15 fps（ファイル小）", "30 fps", "60 fps（動きが滑らか・既定）" }, SelectedIndex = settings.VideoFramerate >= 60 ? 2 : settings.VideoFramerate <= 15 ? 0 : 1, Header = "フレームレート", MinWidth = 200 };
        var countdown = new ComboBox { ItemsSource = CountdownLabels, SelectedIndex = Math.Max(0, Array.IndexOf(CountdownChoices, settings.VideoCountdownSeconds)), Header = "開始前のカウントダウン", MinWidth = 150 };
        var quality = new ComboBox { ItemsSource = QualityLabels, SelectedIndex = Math.Max(0, Array.IndexOf(QualityKeys, settings.VideoQuality)), Header = "画質", MinWidth = 200 };
        cursor.Click += (_, _) => clicks.IsEnabled = cursor.IsChecked == true; clicks.IsEnabled = cursor.IsChecked == true;
        var options = new StackPanel { Spacing = 4 };
        options.Children.Add(new TextBlock { Text = "プレビュー（選んだ対象を表示しています）", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        options.Children.Add(previewFrame); options.Children.Add(previewTitle); options.Children.Add(area);
        if (switching is null)
        {
            var selects = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 6, 0, 0) }; selects.Children.Add(framerate); selects.Children.Add(quality); selects.Children.Add(countdown);
            options.Children.Add(selects);
            foreach (var box in new[] { systemAudio, microphone, cursor, clicks, transcribe, hide }) options.Children.Add(box);
        }
        var layout = new Grid { ColumnSpacing = 18 };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Spacing = 6 };
        left.Children.Add(new TextBlock { Text = switching is null ? "録画する画面全体、またはウィンドウを選んでください。" : "録画を止めずに切り替えます。同じ画面の範囲の変更はその場で、別の画面やウィンドウへの切り替えは録画を区切って行い、停止したときに1つの動画にまとめます。", TextWrapping = TextWrapping.Wrap, FontSize = 12, Width = 460 });
        left.Children.Add(list);
        Grid.SetColumn(options, 1); layout.Children.Add(left); layout.Children.Add(options);
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = switching is null ? "録画する対象を選ぶ" : "録画する対象を変更", Content = new ScrollViewer { Content = layout, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            PrimaryButtonText = switching is null ? "録画を開始" : "切り替える", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Primary, RequestedTheme = Root.RequestedTheme };
        dialog.Resources["ContentDialogMaxWidth"] = 1100.0;
        CaptureTarget? selected = null; WriteableBitmap? previewBitmap = null;
        // The region in the selected display's pixels while 「範囲を指定」 is chosen.
        CaptureRegion? region = switching?.Region ?? CaptureRegion.Parse(settings.VideoRegion);
        bool updatingNumbers = false;

        (double Scale, double Left, double Top)? Fit()
        {
            if (selected is not { Width: > 0, Height: > 0 } target) return null;
            double scale = Math.Min(PreviewWidth / target.Width, PreviewHeight / target.Height);
            return (scale, (PreviewWidth - target.Width * scale) / 2, (PreviewHeight - target.Height * scale) / 2);
        }
        CaptureRegion? Current() => partArea.IsChecked == true && selected is { IsDisplay: true } target && region is { } r ? CaptureRegion.Fit(r, target.Width, target.Height) : null;
        void ShowRegion()
        {
            bool display = selected?.IsDisplay == true;
            partArea.IsEnabled = display; if (!display) wholeArea.IsChecked = true;
            bool part = partArea.IsChecked == true;
            numbers.Visibility = part ? Visibility.Visible : Visibility.Collapsed;
            var fitted = Current();
            areaHint.Text = !display ? "範囲の指定は「画面全体」を選んだときに使えます。"
                : !part ? "プレビューをドラッグすると、その範囲だけを録画できます。"
                : fitted is { } f ? $"プレビューをドラッグするか数値で指定します（画面のピクセル単位、画面は {selected!.Size}）。動画は {f.Width}×{f.Height} になります。"
                : "範囲が画面全体と同じため、全体を録画します。";
            if (fitted is { } shown && Fit() is { } fit)
            {
                regionBox.Width = Math.Max(1, shown.Width * fit.Scale); regionBox.Height = Math.Max(1, shown.Height * fit.Scale);
                Canvas.SetLeft(regionBox, fit.Left + shown.X * fit.Scale); Canvas.SetTop(regionBox, fit.Top + shown.Y * fit.Scale);
                regionBox.Visibility = Visibility.Visible;
            }
            else regionBox.Visibility = Visibility.Collapsed;
            if (fitted is { } values)
            {
                updatingNumbers = true;
                regionX.Value = values.X; regionY.Value = values.Y; regionWidth.Value = values.Width; regionHeight.Value = values.Height;
                updatingNumbers = false;
            }
        }
        void UseNumbers()
        {
            if (updatingNumbers || selected is not { IsDisplay: true }) return;
            static int Value(NumberBox box) => double.IsNaN(box.Value) ? 0 : (int)Math.Round(box.Value);
            region = new CaptureRegion(Value(regionX), Value(regionY), Value(regionWidth), Value(regionHeight));
            ShowRegion();
        }
        foreach (var box in new[] { regionX, regionY, regionWidth, regionHeight }) box.ValueChanged += (_, _) => UseNumbers();
        wholeArea.Checked += (_, _) => ShowRegion();
        partArea.Checked += (_, _) =>
        {
            // A first region: the middle half of the display.
            if (selected is { IsDisplay: true } target && region is null)
                region = new CaptureRegion(target.Width / 4, target.Height / 4, target.Width / 2, target.Height / 2);
            ShowRegion();
        };
        Windows.Foundation.Point? dragStart = null;
        (double X, double Y)? ToDisplay(Windows.Foundation.Point point)
        {
            if (Fit() is not { } fit || selected is null) return null;
            return (Math.Clamp((point.X - fit.Left) / fit.Scale, 0, selected.Width), Math.Clamp((point.Y - fit.Top) / fit.Scale, 0, selected.Height));
        }
        dragSurface.PointerPressed += (_, e) =>
        {
            if (selected is not { IsDisplay: true }) return;
            dragStart = e.GetCurrentPoint(dragSurface).Position; dragSurface.CapturePointer(e.Pointer); e.Handled = true;
        };
        dragSurface.PointerMoved += (_, e) =>
        {
            if (dragStart is not { } start || ToDisplay(start) is not { } a || ToDisplay(e.GetCurrentPoint(dragSurface).Position) is not { } b) return;
            if (Math.Abs(b.X - a.X) < 4 && Math.Abs(b.Y - a.Y) < 4) return;
            region = CaptureRegion.FromCorners(a.X, a.Y, b.X, b.Y);
            if (partArea.IsChecked != true) partArea.IsChecked = true; else ShowRegion();
        };
        void EndDrag(Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) { if (dragStart is null) return; dragStart = null; dragSurface.ReleasePointerCapture(e.Pointer); ShowRegion(); }
        dragSurface.PointerReleased += (_, e) => EndDrag(e);
        dragSurface.PointerCaptureLost += (_, _) => dragStart = null;

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
            dialog.IsPrimaryButtonEnabled = selected is not null; previewBitmap = null; Refresh(); ShowRegion();
        };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) }; timer.Tick += (_, _) => Refresh();
        // The display or window recorded now (or last time) is chosen again when it is still there.
        string lastKey = switching is not null ? TargetKey(switching.Target) : settings.VideoLastTarget;
        int last = targets.ToList().FindIndex(t => TargetKey(t) == lastKey);
        if (region is not null && (switching is null ? settings.VideoRegion.Length > 0 : switching.Region is not null)) partArea.IsChecked = true;
        list.SelectedIndex = Math.Max(0, last); timer.Start();
        regionChoiceForVerification = (target, value) =>
        {
            list.SelectedIndex = targets.IndexOf(target);
            region = value; if (value is null) wholeArea.IsChecked = true; else { partArea.IsChecked = true; ShowRegion(); }
            return (regionBox.Visibility == Visibility.Visible, Current(), areaHint.Text);
        };
        ContentDialogResult result;
        try { result = captureDialogForVerification is not null ? await captureDialogForVerification(dialog) : await dialog.ShowAsync(); }
        finally { timer.Stop(); regionChoiceForVerification = null; }
        if (result != ContentDialogResult.Primary || selected is null) return null;
        var chosenRegion = Current();
        if (switching is not null) return switching with { Target = selected, Region = chosenRegion };
        settings.VideoSystemAudio = systemAudio.IsChecked == true; settings.VideoMicrophone = microphone.IsChecked == true; settings.VideoCursor = cursor.IsChecked == true;
        settings.VideoClicks = clicks.IsChecked == true; settings.VideoCountdownSeconds = CountdownChoices[Math.Max(0, countdown.SelectedIndex)]; settings.VideoCountdown = settings.VideoCountdownSeconds > 0; settings.VideoTranscribe = transcribe.IsChecked == true;
        settings.VideoHideLocaptio = hide.IsChecked == true; settings.VideoFramerate = framerate.SelectedIndex == 2 ? 60 : framerate.SelectedIndex == 0 ? 15 : 30;
        settings.VideoQuality = QualityKeys[Math.Max(0, quality.SelectedIndex)]; settings.VideoLastTarget = TargetKey(selected);
        settings.VideoRegion = chosenRegion?.Serialize() ?? ""; settings.Save();
        return new(selected, settings.VideoSystemAudio, settings.VideoMicrophone, settings.VideoCursor, settings.VideoClicks, settings.VideoFramerate, settings.VideoQuality, settings.VideoCountdownSeconds, settings.VideoTranscribe, settings.VideoHideLocaptio, chosenRegion);
    }
    /// <summary>Self-check: picks a target and region in the open dialog; returns whether the region is drawn, the fitted region and the hint.</summary>
    private Func<CaptureTarget, CaptureRegion?, (bool Drawn, CaptureRegion? Region, string Hint)>? regionChoiceForVerification;
    /// <summary>Self-check: answers the target dialog instead of showing it (it is built and laid out first).</summary>
    private Func<ContentDialog, Task<ContentDialogResult>>? captureDialogForVerification;
}
