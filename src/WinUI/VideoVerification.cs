using System.Diagnostics;
using Locaptio.Core;
using Locaptio.Core.AI;
using Locaptio.Core.Audio;
using Microsoft.UI.Xaml;
using Windows.Media.Editing;
using Windows.Storage;

namespace Locaptio.WinUI;

// --verify-video: records Locaptio's own window (synthetic text, no sound, nothing else on screen; the region test keeps it on top),
// pauses, takes a screenshot, replays the video with captions, burns subtitles into a copy and
// checks the caption is in the frames, and reads the audio track of an MP4. Files go to a temp folder.
public sealed partial class MainWindow
{
    private sealed class CountingSpeech : ISpeechRecognitionBackend
    {
        public int Calls;
        public Task<InferenceResult> RecognizeAsync(float[] samples, CancellationToken token, bool final = true) { Calls++; return Task.FromResult(new InferenceResult("動画の音声 " + Calls, 1)); }
    }
    private static async Task<double> BottomBrightnessAsync(string video, double seconds)
    {
        var composition = new MediaComposition();
        composition.Clips.Add(await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(video)));
        var stream = await composition.GetThumbnailAsync(TimeSpan.FromSeconds(seconds), 640, 0, VideoFramePrecision.NearestFrame);
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
        var pixels = (await decoder.GetPixelDataAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Ignore, new(), Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation, Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage)).DetachPixelData();
        int width = (int)decoder.PixelWidth, height = (int)decoder.PixelHeight; long sum = 0, count = 0;
        // White caption text in the lower fifth of the frame raises the share of bright pixels there.
        for (int y = height * 4 / 5; y < height; y++) for (int x = width / 5; x < width * 4 / 5; x++) { int i = (y * width + x) * 4; if (pixels[i] > 230 && pixels[i + 1] > 230 && pixels[i + 2] > 230) sum++; count++; }
        return sum / (double)Math.Max(1, count);
    }
    private async Task VerifyVideoAsync()
    {
        string artifacts = Path.Combine(root, "artifacts"); Directory.CreateDirectory(artifacts);
        string work = Path.Combine(Path.GetTempPath(), "Locaptio-verify-video-" + Environment.ProcessId); Directory.CreateDirectory(work);
        var log = new List<string>(); var failures = new List<string>();
        void Check(bool condition, string name) { log.Add((condition ? "ok   " : "FAIL ") + name); if (!condition) failures.Add(name); }
        string saved = System.Text.Json.JsonSerializer.Serialize(settings);
        try
        {
            Title = "Locaptio — 録画の検証";
            settings.BackgroundOpacity = 1; settings.CaptionMode = "japanese"; ApplySettings();
            var size = AppWindow.Size; await Task.Delay(300);
            confirmReplacementForVerification = _ => Task.FromResult(true); confirmNewRecordingForVerification = () => Task.FromResult(true);
            string requested = Path.Combine(work, "画面録画.mp4");
            recordingFilePickerForVerification = () => Task.FromResult<string?>(requested);
            var target = new CaptureTarget(false, "", hwnd, "Locaptio（検証）", size.Width, size.Height);
            captureChoiceForVerification = () => Task.FromResult<CaptureChoice?>(new(target, false, false, true, true, 30, "standard", 3, false, false));
            Check(CaptureTargets.List().Any(t => t.IsDisplay) && CaptureTargets.Snapshot(target, 320) is { Width: 320 }, "targets listed and window preview captured");

            // The first attempt fails as an unusable hardware encoder would; recording must still start (software encoding).
            videoAttemptsToFailForVerification = 1;
            VideoToggleClick(VideoToggle, new RoutedEventArgs());
            for (int i = 0; i < 200 && !(VideoRecording && recordingTranscript?.Started != long.MaxValue); i++) await Task.Delay(100);
            videoAttemptsToFailForVerification = 0;
            Check(VideoRecording && recordingTranscript!.Started != long.MaxValue && VideoControls.Visibility == Visibility.Visible && VideoToggle.IsChecked == true && !RecordToggle.IsEnabled, "recording started");
            Check(StatusText.Text.Contains("ソフトウェアで録画"), "fell back to software encoding after a failed start: " + StatusText.Text);
            Check(new Locaptio.App.Settings().VideoMicrophone, "microphone on by default for video");
            // Synthetic captions while recording, spoken on the video's clock.
            captionStop = new();
            UpdateCaption(new(1, 1, "画面録画のテストです。最初の発話。", null, true, Stopwatch.GetTimestamp(), 0, 0, "ja"));
            await Task.Delay(1200);
            VideoPauseClick(VideoPauseButton, new RoutedEventArgs()); bool paused = videoPaused && recordingTranscript!.Paused;
            UpdateCaption(new(2, 1, "一時停止中の発話は動画に入りません。", null, true, Stopwatch.GetTimestamp(), 0, 0, "ja"));
            await Task.Delay(1000);
            VideoPauseClick(VideoPauseButton, new RoutedEventArgs()); bool resumed = !videoPaused && !recordingTranscript!.Paused;
            await Task.Delay(500);
            UpdateCaption(new(3, 1, "再開した後の二つ目の発話です。字幕を焼き込みます。", null, true, Stopwatch.GetTimestamp(), 0, 0, "ja"));
            ScreenshotClick(ScreenshotButton, new RoutedEventArgs()); await Task.Delay(1500);
            bool preview = LivePreview.Source is not null;
            captionStop.Dispose(); captionStop = null;
            Check(paused && resumed && preview, $"pause, resume and live preview (paused={paused}, resumed={resumed}, preview={preview})");
            var snapshot = recordingTranscript!.Snapshot();
            // 1.2 s before the pause + 0.5 s after it; Windows caption timing also subtracts its 1 s estimate.
            double expected = 1.7 - (recordingTranscript.Approximate ? 1 : 0);
            Check(snapshot.Phrases.Length == 2 && Math.Abs(snapshot.Phrases[1].Seconds - expected) < .45, "pause excluded from the video clock: " + string.Join(", ", snapshot.Phrases.Select(p => $"{p.Seconds:F2}")));
            string recorded = recordingPath;
            await StopAllAsync();
            for (int i = 0; i < 100 && (recordingStop is not null || !reviewingRecording); i++) await Task.Delay(100);
            var video = await (await StorageFile.GetFileFromPathAsync(recorded)).Properties.GetVideoPropertiesAsync();
            log.Add($"video {video.Width}x{video.Height} {video.Duration} {new FileInfo(recorded).Length} bytes");
            Check(File.Exists(recorded) && video.Duration.TotalSeconds is > 2 and < 5 && video.Width > 0 && Directory.GetFiles(Path.GetDirectoryName(recorded)!, "スクリーンショット_*.png").Length == 1
                && bookmarks.Any(b => b.Label == "スクリーンショット"), "video, screenshot and its bookmark saved");
            Check(reviewingRecording && playbackPath == recorded && RecordingLibrary.List(work).Any(r => r.AudioPath == recorded), "recording opens for review and is in the library");

            // Replay with the video on screen and the caption over it.
            Check(EnsurePlayback() && playbackReader is VideoPlaybackClock && VideoPanel.Visibility == Visibility.Visible, "video replay");
            await Task.Delay(800);
            playbackReader!.CurrentTime = TimeSpan.FromSeconds(playbackDocument!.Phrases[1].Seconds + .3); await Task.Delay(400); UpdatePlaybackPosition();
            Check(VideoCaption.Visibility == Visibility.Visible && VideoCaptionText.Text.StartsWith("再開した後"), "caption shown over the video: " + VideoCaptionText.Text);
            UpdateCommandStates(); await Task.Delay(300);
            // Laid out, not just marked visible: the custom toolbar must give the button space.
            Check(BurnSubtitlesButton.Visibility == Visibility.Visible && BurnSubtitlesButton.IsEnabled && BurnSubtitlesButton.ActualWidth > 40, "subtitle export offered for video");
            await Task.Delay(500); await CaptureElementAsync(Root, Path.Combine(artifacts, "video-review.png"));

            // Burn subtitles into a copy and look for the caption in its frames.
            string burned = Path.Combine(work, "画面録画_字幕付き.mp4");
            burnChoiceForVerification = () => Task.FromResult<(string, string, bool, bool, string)?>(("source", "large", false, true, burned));
            BurnSubtitlesClick(this, new RoutedEventArgs());
            await Task.Delay(500);
            for (int i = 0; i < 1200 && (exclusiveTask || captionStop is not null); i++) await Task.Delay(100);
            double at = playbackDocument!.Phrases[1].Seconds + .5;
            double before = File.Exists(burned) ? await BottomBrightnessAsync(recorded, at) : 0, after = File.Exists(burned) ? await BottomBrightnessAsync(burned, at) : 0;
            log.Add($"bright pixels in the lower fifth: original {before:P2}, subtitled {after:P2}");
            Check(File.Exists(burned) && File.Exists(Path.ChangeExtension(burned, ".srt")) && after > before + .005, "subtitles burned into the copy and SRT saved");
            Check(File.Exists(recorded) && new FileInfo(recorded).Length > 0, "original video unchanged");

            // Video files: the audio track of an MP4 is read for transcription.
            string withAudio = Path.Combine(work, "音声付き.mp4"), speech = Path.Combine(artifacts, File.Exists(Path.Combine(artifacts, "test-ja.wav")) ? "test-ja.wav" : "none.wav");
            if (File.Exists(speech))
            {
                var composition = new MediaComposition();
                string frame = Path.Combine(work, "frame.png"); DrawCaption("音声付きの動画", frame, 640, 360, "large");
                var clip = await MediaClip.CreateFromImageFileAsync(await StorageFile.GetFileFromPathAsync(frame), TimeSpan.FromSeconds(4));
                composition.Clips.Add(clip);
                composition.BackgroundAudioTracks.Add(await BackgroundAudioTrack.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(speech)));
                await File.WriteAllBytesAsync(withAudio, []);
                await composition.RenderToFileAsync(await StorageFile.GetFileFromPathAsync(withAudio), MediaTrimmingPreference.Precise);
                var counting = new CountingSpeech(); var phrases = new List<RecordingPhrase>();
                await FileTranscription.RunAsync(withAudio, counting, "ja", p => { phrases.Add(p); return Task.CompletedTask; }, _ => { }, CancellationToken.None);
                Check(counting.Calls > 0 && phrases.Count > 0 && SpeechOnsets.Find(SpeechOnsets.Envelope(withAudio)).Count > 0, $"MP4 audio track read for transcription ({phrases.Count} phrases)");
            }
            else log.Add("skip MP4 audio check: artifacts/test-ja.wav not found");
            ClosePlayback();

            // Region picker: sample targets (no real screen), drag/number region drawn over the preview and saved.
            {
                var sampleDisplay = new CaptureTarget(true, @"\\.\SAMPLE1", 0, "ディスプレイ1（サンプル）", 1920, 1080);
                var sampleWindow = new CaptureTarget(false, "", 1, "サンプルのウィンドウ", 800, 600);
                targetsForVerification = [sampleDisplay, sampleWindow];
                snapshotForVerification = (t, width) => { int w = Math.Min(width, t.Width), h = Math.Max(1, t.Height * w / t.Width); using var image = SampleScreen(t.IsDisplay ? "desktop" : "slide", w, h, "定例会議：リリース日程"); return Pixels(image); };
                (bool Drawn, CaptureRegion? Region, string Hint) part = default, window = default, whole = default;
                captureDialogForVerification = async dialog =>
                {
                    await Task.Delay(200);
                    window = regionChoiceForVerification!(sampleWindow, new(10, 10, 300, 200));
                    whole = regionChoiceForVerification!(sampleDisplay, new(0, 0, 1920, 1080));
                    part = regionChoiceForVerification!(sampleDisplay, new(101, 50, 641, 361));
                    // Shown once for a picture of the region over the preview.
                    var showing = dialog.ShowAsync(); await Task.Delay(900);
                    await CaptureComposedAsync(Path.Combine(artifacts, "video-region-picker.png"));
                    dialog.Hide(); await showing;
                    return Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary;
                };
                var picked = await ChooseCaptureAsync();
                Check(part.Drawn && part.Region == new CaptureRegion(101, 50, 640, 360) && (part.Hint ?? "").Contains("640×360"), $"region drawn on the preview and made even ({part.Region}; {part.Hint})");
                Check(!window.Drawn && window.Region is null && (window.Hint ?? "").Contains("画面全体"), "no region for a window");
                Check(!whole.Drawn && whole.Region is null, "a region covering the whole display records the whole display");
                Check(picked?.Region == new CaptureRegion(101, 50, 640, 360) && settings.VideoRegion == "101,50,640,360" && picked.Label.Contains("範囲"), "chosen region returned and remembered: " + picked?.Label);
                captureDialogForVerification = async dialog => { await Task.Delay(200); regionChoiceForVerification!(sampleDisplay, new(0, 0, 960, 540)); return Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary; };
                var switched = await ChooseCaptureAsync(picked);
                Check(switched is not null && switched.Region == new CaptureRegion(0, 0, 960, 540) && switched.Framerate == picked!.Framerate && switched.Microphone == picked.Microphone, "while recording, only the target and region change");
                captureDialogForVerification = null; targetsForVerification = null; snapshotForVerification = null;
            }

            // Recording a region of the display (Locaptio's own window, kept on top), switching the region in place and
            // then to the window itself (a new part), joined into one video on stop.
            {
                var display = CaptureTargets.List().FirstOrDefault(t => t.IsDisplay && CaptureTargets.DisplayBounds(t.DeviceName) is { } b
                    && AppWindow.Position.X + AppWindow.Size.Width / 2 >= b.Left && AppWindow.Position.X + AppWindow.Size.Width / 2 < b.Left + b.Width
                    && AppWindow.Position.Y + AppWindow.Size.Height / 2 >= b.Top && AppWindow.Position.Y + AppWindow.Size.Height / 2 < b.Top + b.Height);
                if (display is not null && CaptureTargets.DisplayBounds(display.DeviceName) is { } bounds)
                {
                    SetKeepOnTop(true); Activate(); await Task.Delay(400);
                    var window = AppWindow; var windowTarget = new CaptureTarget(false, "", hwnd, "Locaptio（検証）", window.Size.Width, window.Size.Height);
                    var start = CaptureRegion.Fit(new(window.Position.X - bounds.Left + 40, window.Position.Y - bounds.Top + 40, Math.Min(640, window.Size.Width - 80), Math.Min(400, window.Size.Height - 80)), bounds.Width, bounds.Height)!.Value;
                    string requested2 = Path.Combine(work, "範囲録画.mp4");
                    recordingFilePickerForVerification = () => Task.FromResult<string?>(requested2);
                    var choice = new CaptureChoice(display, false, false, false, false, 30, "standard", 0, false, false, start);
                    captureChoiceForVerification = () => Task.FromResult<CaptureChoice?>(choice);
                    VideoToggleClick(VideoToggle, new RoutedEventArgs());
                    for (int i = 0; i < 200 && !(VideoRecording && recordingTranscript?.Started != long.MaxValue && videoSource is not null); i++) await Task.Delay(100);
                    Check(VideoRecording && videoSource?.SourceRect is { } rect && (int)rect.Width == start.Width && SessionTitle.Text.Contains("範囲"), $"region recording started ({start}; {SessionTitle.Text})");
                    captionStop = new();
                    UpdateCaption(new(11, 1, "範囲を録画しています。", null, true, Stopwatch.GetTimestamp(), 0, 0, "ja"));
                    await Task.Delay(1200);
                    var smaller = new CaptureRegion(start.X, start.Y, start.Width / 2 & ~1, start.Height / 2 & ~1);
                    switchChoiceForVerification = () => Task.FromResult<CaptureChoice?>(choice with { Region = smaller });
                    VideoTargetClick(VideoTargetButton, new RoutedEventArgs()); await Task.Delay(800);
                    Check(videoChoice?.Region == smaller && StatusText.Text.Contains("範囲を切り替えました") && videoSource?.SourceRect is { } r2 && (int)r2.Width == smaller.Width, "region switched in place: " + StatusText.Text);
                    var source = videoSource;
                    switchChoiceForVerification = () => Task.FromResult<CaptureChoice?>(choice with { Target = windowTarget, Region = null });
                    VideoTargetClick(VideoTargetButton, new RoutedEventArgs());
                    for (int i = 0; i < 200 && !(videoSource is ScreenRecorderLib.WindowRecordingSource && !ReferenceEquals(videoSource, source) && recordingTranscript?.Paused == false && videoChoice?.Target == windowTarget); i++) await Task.Delay(100);
                    await Task.Delay(300); // the status line is updated through the dispatcher queue
                    Check(videoSource is ScreenRecorderLib.WindowRecordingSource && videoChoice?.Target == windowTarget && StatusText.Text.Contains("切り替えました") && File.Exists(VideoPartPath(recordingPath, 1)) && !recordingTranscript!.Paused,
                        "switched to another target in a new part: " + StatusText.Text);
                    await Task.Delay(500);
                    UpdateCaption(new(12, 1, "ウィンドウに切り替えた後の発話です。", null, true, Stopwatch.GetTimestamp(), 0, 0, "ja"));
                    await Task.Delay(1200);
                    captionStop.Dispose(); captionStop = null;
                    string recorded2 = recordingPath;
                    await StopAllAsync();
                    for (int i = 0; i < 600 && (recordingStop is not null || !reviewingRecording); i++) await Task.Delay(100);
                    var joined = File.Exists(recorded2) ? await (await StorageFile.GetFileFromPathAsync(recorded2)).Properties.GetVideoPropertiesAsync() : null;
                    log.Add($"joined video {joined?.Width}x{joined?.Height} {joined?.Duration}");
                    Check(joined is not null && joined.Width == start.Width && joined.Height == start.Height && joined.Duration.TotalSeconds is > 2.5 and < 8
                        && Directory.GetFiles(Path.GetDirectoryName(recorded2)!, "*.part*.mp4").Length == 0, "parts joined into one video at the first region's size, part files removed");
                    var phrases = playbackDocument?.Phrases ?? [];
                    Check(reviewingRecording && playbackPath == recorded2 && phrases.Length == 2 && phrases[1].Seconds > phrases[0].Seconds && phrases[1].Seconds < joined!.Duration.TotalSeconds,
                        "speech on both sides of the switch is on the joined video's clock: " + string.Join(", ", phrases.Select(p => $"{p.Seconds:F2}")));
                    await CaptureElementAsync(Root, Path.Combine(artifacts, "video-switch-review.png"));
                    switchChoiceForVerification = null; SetKeepOnTop(false);
                    ClosePlayback();
                }
                else log.Add("skip region recording: Locaptio's display not found");
            }
        }
        catch (Exception ex) { failures.Add("exception: " + ex); }
        finally
        {
            switchChoiceForVerification = null; captureDialogForVerification = null; targetsForVerification = null; snapshotForVerification = null;
            captureChoiceForVerification = null; recordingFilePickerForVerification = null; burnChoiceForVerification = null; confirmReplacementForVerification = null; confirmNewRecordingForVerification = null;
            if (System.Text.Json.JsonSerializer.Deserialize<Locaptio.App.Settings>(saved) is { } original)
                foreach (var property in typeof(Locaptio.App.Settings).GetProperties().Where(p => p.CanWrite)) property.SetValue(settings, property.GetValue(original));
            settings.Save(); ClosePlayback();
            File.WriteAllText(Path.Combine(root, "artifacts", "video-verification.txt"), (failures.Count == 0 ? "PASS" : "FAIL: " + string.Join("; ", failures)) + Environment.NewLine + string.Join(Environment.NewLine, log));
            Environment.ExitCode = failures.Count == 0 ? 0 : 1;
            try { Directory.Delete(work, true); } catch (IOException) { }
            await ShutdownAsync();
        }
    }
}
