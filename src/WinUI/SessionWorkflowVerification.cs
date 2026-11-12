using System.Diagnostics;
using Locaptio.Core;
using Locaptio.Core.Audio;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Locaptio.WinUI;
public sealed partial class MainWindow
{
    private async Task VerifyLibraryViewAsync()
    {
        string original = settings.RecordingSaveDirectory; double background = settings.BackgroundOpacity;
        try
        {
            Title = "Locaptio — 録音一覧の表示確認";
            settings.BackgroundOpacity = 1; ApplySettings();
            settings.RecordingSaveDirectory = Path.Combine(root, "artifacts", "library-view-1.0.41");
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
