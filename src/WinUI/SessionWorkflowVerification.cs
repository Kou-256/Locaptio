using System.Diagnostics;
using Locaptio.Core;
using Locaptio.Core.Audio;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Locaptio.WinUI;
public sealed partial class MainWindow
{
    private Action? restoreInputViewSettings;
    private void PrepareWindowsInputView()
    {
        string json = System.Text.Json.JsonSerializer.Serialize(settings);
        restoreInputViewSettings = () =>
        {
            var original = System.Text.Json.JsonSerializer.Deserialize<Locaptio.App.Settings>(json)!;
            foreach (var property in typeof(Locaptio.App.Settings).GetProperties().Where(p => p.CanRead && p.CanWrite)) property.SetValue(settings, property.GetValue(original));
        };
        settings.SpeechEngine = "windows-live"; settings.CaptionMode = "japanese"; settings.AudioInputKind = "system";
        settings.AudioDeviceId = ""; settings.AudioProcessId = 0; settings.AudioProcessStarted = 0;
        settings.CorrectTranscriptWithAi = settings.UseLocalLlm = settings.SpeakerDiarization = false;
        settings.AutoSwitchWindowsCaptionLanguage = true;
        settings.BackgroundOpacity = 1; RefreshAudioInputs(); ApplySettings();
        var probe = new Microsoft.UI.Xaml.Controls.Button { Content = "録音＋確認音声（検証用）", HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Left, FontSize = 12 };
        Microsoft.UI.Xaml.Controls.Grid.SetRow(probe, 5); Root.Children.Add(probe);
        probe.Click += async (_, _) =>
        {
            probe.IsEnabled = false;
            try
            {
                settings.RecordingSaveDirectory = Path.Combine(root,"artifacts","windows-start-audio-" + Guid.NewGuid().ToString("N"));
                recordingFilePickerForVerification = () => Task.FromResult<string?>(Path.Combine(settings.RecordingSaveDirectory,"20261115_.wav"));
                int? bridge = windowsSession?.BridgeProcessId;
                RecordToggle.IsChecked = true;
                for (int i = 0; i < 100 && (recordingStop is null || dialogOpen); i++) await Task.Delay(50);
                if (recordingStop is null) throw new Exception("Recording did not start");
                using var reader = new NAudio.Wave.AudioFileReader(Path.Combine(root,"artifacts","test-ja.wav"));
                using var player = new NAudio.Wave.WaveOutEvent(); player.Init(reader); player.Play();
                await Task.Delay(reader.TotalTime + TimeSpan.FromSeconds(3));
                bool text = history.Any(row => row.Caption.English.Contains("日本語"));
                File.AppendAllText(Path.Combine(root,"artifacts","windows-input-live-verification.txt"), $"newRecordingFirstSpeech={text}; sameBridge={bridge == windowsSession?.BridgeProcessId}; rows={history.Count}\n");
                await StopRecordingAsync();
                StatusText.Text = text ? "確認音声の録音開始直後の文字を表示しました。" : "確認音声の文字が見つかりませんでした。";
            }
            catch (Exception ex) { StatusText.Text = ex.Message; }
            finally { recordingFilePickerForVerification = null; probe.IsEnabled = true; }
        };
        Title = "Locaptio — Windows入力の確認"; AppWindow.Show(); Activate();
    }
    private sealed class NotificationOnlyHandler : System.Net.Http.HttpMessageHandler
    {
        public int Checks, Downloads;
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.AbsoluteUri != AppUpdates.LatestApi) { Downloads++; throw new Exception("Unexpected automatic download"); }
            Checks++;
            string payload = System.Text.Json.JsonSerializer.Serialize(new { tag_name = "v99.0.0", draft = false, prerelease = false,
                assets = new[] { new { name = "Locaptio-Setup-99.0.0-x64.exe", browser_download_url = "https://github.com/Kou-256/Locaptio/releases/download/v99.0.0/Locaptio-Setup-99.0.0-x64.exe", size = 123, digest = "sha256:" + new string('1',64) } } });
            return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new System.Net.Http.StringContent(payload) });
        }
    }
    private async Task VerifyUpdateNotificationAsync()
    {
        bool original = settings.AutoDownloadUpdates, passed = false; string report = "";
        using var handler = new NotificationOnlyHandler(); using var client = new System.Net.Http.HttpClient(handler);
        try
        {
            settings.AutoDownloadUpdates = true; updateHttpForVerification = client;
            updateApprovalForVerification = () => throw new Exception("Startup unexpectedly opened an approval prompt");
            await CheckForUpdatesAsync(false); await Task.Delay(150);
            passed = handler.Checks == 1 && handler.Downloads == 0 && UpdateNotice.IsOpen && UpdateNotice.ActionButton is not null;
            report = $"PASS={passed}; oldAutoDownload=true; metadataRequests={handler.Checks}; assetDownloads={handler.Downloads}; explicitDownloadButton={UpdateNotice.ActionButton is not null}";
        }
        catch (Exception ex) { report = ex.ToString(); }
        finally
        {
            settings.AutoDownloadUpdates = original; updateHttpForVerification = null; updateApprovalForVerification = null;
            File.WriteAllText(Path.Combine(root,"artifacts","update-notification-verification.txt"),report);
            Environment.ExitCode = passed ? 0 : 1; await ShutdownAsync();
        }
    }
    private async Task VerifyPreparationFailureAsync()
    {
        bool passed = false; string report = "";
        try
        {
            captionsForVerification = async (token, _) =>
            {
                BeginPreparation()(new("音声環境の自動準備", 0, 1, "日本語の診断"));
                await Task.Delay(100, token);
                throw new IOException("信頼されていないマウントポイントが含まれているため、パスをスキャンできません。");
            };
            await StartAsync(); if (captionTask is not null) await captionTask;
            await Task.Delay(100);
            passed = PreparationPanel.Visibility == Visibility.Collapsed && !preparationVisible && StartButton.IsEnabled && captionStop is null
                && StatusText.Text.Contains("信頼されていないマウントポイント");
            report = $"PASS={passed}; failed setup leaves no 0% progress panel; Japanese diagnostic retained; Start available";
        }
        catch (Exception ex) { report = ex.ToString(); }
        finally
        {
            File.WriteAllText(Path.Combine(root, "artifacts", "preparation-failure-verification.txt"), report);
            Environment.ExitCode = passed ? 0 : 1; await ShutdownAsync();
        }
    }
    private async Task VerifyFileImportAsync()
    {
        string directory = settings.RecordingSaveDirectory, model = settings.SpeechModel, device = settings.InferenceDevice, mode = settings.CaptionMode;
        bool cpu = settings.CpuOnly, correction = settings.CorrectTranscriptWithAi;
        bool passed = false; string report = "";
        try
        {
            settings.RecordingSaveDirectory = Path.Combine(root, "artifacts", "file-import-verification-" + Guid.NewGuid().ToString("N"));
            settings.SpeechModel = "small"; settings.InferenceDevice = "cpu"; settings.CpuOnly = true; settings.CaptionMode = "japanese"; settings.CorrectTranscriptWithAi = false;
            ApplySettings();
            string source = Path.Combine(root, "artifacts", "test-ja.wav");
            captionStop = new(); captionTask = ImportAudioAsync(source, captionStop.Token); await captionTask;
            bool imported = reviewingRecording && history.Count > 0 && history.Any(p => p.Caption.English.Contains("日本語")) && File.Exists(playbackPath + ".txt") && File.Exists(source);
            bool restart = captionStop is null && StartButton.IsEnabled && !StopButton.IsEnabled;
            bool replay = playbackDocument?.Phrases.Length == history.Count && playbackReader is not null;
            captionStop = new(); captionStop.Cancel(); captionTask = ImportAudioAsync(source, captionStop.Token); await captionTask;
            bool cancel = captionStop is null && StartButton.IsEnabled && history.Count > 0;
            passed = imported && restart && replay && cancel;
            report = $"PASS={passed}; actualJapaneseImport={imported}; restart={restart}; replay={replay}; cancelPreservesText={cancel}";
        }
        catch (Exception ex) { report = ex.ToString(); }
        finally
        {
            settings.RecordingSaveDirectory = directory; settings.SpeechModel = model; settings.InferenceDevice = device; settings.CaptionMode = mode; settings.CpuOnly = cpu; settings.CorrectTranscriptWithAi = correction;
            File.WriteAllText(Path.Combine(root, "artifacts", "file-import-ui-verification.txt"), report);
            Environment.ExitCode = passed ? 0 : 1; await ShutdownAsync();
        }
    }
    private async Task VerifyLibraryViewAsync()
    {
        string original = settings.RecordingSaveDirectory; double background = settings.BackgroundOpacity;
        try
        {
            Title = "Locaptio — 録音一覧の表示確認"; AppWindow.Show(); Activate();
            settings.BackgroundOpacity = 1; ApplySettings();
            settings.RecordingSaveDirectory = Path.Combine(root, "artifacts", "library-view-1.0.42");
            foreach (string name in new[] { "打ち合わせ", "講義メモ" })
            {
                string path = RecordingLibrary.CreateSessionPath(Path.Combine(settings.RecordingSaveDirectory, name + ".wav"));
                using (var writer = new NAudio.Wave.WaveFileWriter(path, new NAudio.Wave.WaveFormat(16000, 16, 1))) writer.Write(new byte[32000], 0, 32000);
                var document = new RecordingDocument(1, Path.GetFileName(path), DateTimeOffset.Now, false, [new(1, 1, 0, "保存済みの音声とテキストをまとめて開きます。", null, 1)]);
                await RecordingTranscript.SaveAsync(path, document); await RecordingLibrary.SaveTextAsync(path, document);
            }
            await OpenRecordingLibraryAsync();
        }
        finally { settings.RecordingSaveDirectory = original; settings.BackgroundOpacity = background; await ShutdownAsync(); }
    }
    private async Task VerifySessionWorkflowAsync()
    {
        var original = SelectedAudio(); string mode = settings.CaptionMode, directory = settings.RecordingSaveDirectory, format = settings.RecordingFormat;
        CaptionWindow? overlay = null; bool passed = false; string report = "";
        async Task Wait(Func<bool> condition) { for (int i = 0; i < 100 && !condition(); i++) await Task.Delay(50); if (!condition()) throw new Exception("Workflow wait timed out"); }
        try
        {
            settings.CaptionMode = "japanese"; settings.AudioInputKind = "application"; settings.AudioDeviceId = ""; settings.AudioProcessId = Environment.ProcessId;
            using (var process = Process.GetCurrentProcess()) settings.AudioProcessStarted = process.StartTime.ToUniversalTime().Ticks;
            settings.RecordingSaveDirectory = Path.Combine(root, "artifacts", "session-workflow-" + Guid.NewGuid().ToString("N")); settings.RecordingFormat = "wav";
            captionsForVerification = async (token, offset) => { UpdateCaption(new(offset + 1, 1, "保持する会話 " + offset, null, true, Stopwatch.GetTimestamp(), 0, 0, "ja")); await Task.Delay(Timeout.Infinite, token); };
            recordingFilePickerForVerification = () => Task.FromResult<string?>(Path.Combine(settings.RecordingSaveDirectory, "会議.wav"));
            RecordToggle.IsChecked = true; await Wait(() => recordingStop is not null && captionStop is not null && !dialogOpen);
            string first = recordingPath; await Task.Delay(600);
            RecordToggle.IsChecked = false; await Wait(() => recordingStop is null && captionStop is null && StartButton.IsEnabled);
            bool restart = StartButton.IsEnabled && File.Exists(first + ".txt") && File.Exists(RecordingTranscript.Sidecar(first));
            RecordToggle.IsChecked = true; await Wait(() => recordingStop is not null && captionStop is not null && !dialogOpen);
            string second = recordingPath; await Task.Delay(400); RecordToggle.IsChecked = false;
            await Wait(() => recordingStop is null && captionStop is null && StartButton.IsEnabled);
            bool folders = Path.GetDirectoryName(first) != Path.GetDirectoryName(second) && RecordingLibrary.List(settings.RecordingSaveDirectory).Length == 2;
            await StartAsync(); await Wait(() => captionStop is not null && history.Count > 0);
            string before = history[0].English; long beforeId = history[0].Id;
            RefreshAudioInputs(); var choices = ((System.Collections.IEnumerable)AudioSelector.ItemsSource!).Cast<AudioInputChoice>().ToArray();
            AudioSelector.SelectedItem = choices.First(c => c.Selection.Kind == "system" && c.Selection.DeviceId.Length == 0);
            await Wait(() => !inputChanging && history.Count >= 2);
            bool retained = history[0].Id == beforeId && history[0].English == before && history.Select(h => h.Id).Distinct().Count() == history.Count;
            var active = captionStop; updateApprovalForVerification = () => Task.FromResult(false);
            await ConfirmInstallUpdateAsync("must-not-execute.exe", new(new Version(99, 0, 0), new Uri("https://github.com/Kou-256/Locaptio"), 1, "unused"), UpdateNotice);
            bool denied = ReferenceEquals(active, captionStop) && !closing;
            var appearance = new Locaptio.App.Settings { CaptionMode = "japanese", CaptionWrapWidth = 400, CaptionHideSeconds = 30 };
            overlay = new CaptionWindow(appearance); overlay.Activate();
            overlay.Update([new(1, 1, "字幕を動かしても文字全体を表示します。折り返し幅も設定できます。", null, true, Stopwatch.GetTimestamp(), 0, 0, "ja")]);
            await Task.Delay(200); string shape = overlay.ShapeDiagnostic().Split("geometry=")[1];
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
            for (int i = 0; i < 80; i++) { overlay.AppWindow.Move(new Windows.Graphics.PointInt32(area.X + 20 + i % 8 * 12, area.Y + 30 + i % 5 * 10)); if (i % 10 == 0) await Task.Delay(10); }
            await Task.Delay(100);
            bool stable = shape == overlay.ShapeDiagnostic().Split("geometry=")[1] && overlay.HasWholeTextBlocks() && overlay.HasTextOnlyShape();
            int narrow = overlay.AppWindow.Size.Width; appearance.CaptionWrapWidth = 700; overlay.ApplyAppearance(); await Task.Delay(100);
            bool width = overlay.AppWindow.Size.Width > narrow && overlay.HasWholeTextBlocks();
            var defaults = new Locaptio.App.Settings();
            bool initial = defaults.BackgroundOpacity == .9 && defaults.CaptionOpacity == .8 && defaults.CaptionPalette == "cyan-white" && defaults.MenuBarTheme == "matching" && defaults.SpeechEngine == "windows-live" && defaults.CaptionMode == "japanese";
            passed = restart && folders && retained && denied && stable && width && initial;
            report = $"PASS={passed}; recordStopStart={restart}; separateFolders={folders}; inputHistoryRetained={retained}; updateDeniedKeepsSession={denied}; moveShapeStable={stable}; wrapWidth={width}; defaults={initial}; only silent test-process audio recorded";
            await overlay.CaptureAsync(Path.Combine(root, "artifacts", "caption-width-1.0.41.png"));
        }
        catch (Exception ex) { report = "PASS=False; " + ex; }
        finally
        {
            overlay?.Close(); await StopAllAsync(); updateApprovalForVerification = null; captionsForVerification = null; recordingFilePickerForVerification = null;
            settings.CaptionMode = mode; settings.AudioInputKind = original.Kind; settings.AudioDeviceId = original.DeviceId; settings.AudioProcessId = original.ProcessId; settings.AudioProcessStarted = original.ProcessStarted;
            settings.RecordingSaveDirectory = directory; settings.RecordingFormat = format;
            await File.WriteAllTextAsync(Path.Combine(root, "artifacts", "session-workflow-verification.txt"), report);
            Environment.ExitCode = passed ? 0 : 1; await ShutdownAsync();
        }
    }
}
