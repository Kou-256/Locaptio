using System.Diagnostics;
using Locaptio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NAudio.Wave;

namespace Locaptio.WinUI;
public sealed partial class MainWindow
{
    private async Task VerifyHistoryAsync()
    {
        bool passed = false; string report = "";
        var originalInput = SelectedAudio();
        try
        {
            await Task.Delay(250);
            ClearCaptions(); captionStop = new();
            var clock = Stopwatch.StartNew();
            for (int i = 1; i <= 20000; i++)
            {
                UpdateCaption(new(i, 1, $"History row {i}: unlimited transcript.", $"履歴 {i}：全行を保持します。", true, Stopwatch.GetTimestamp(), 0, 0));
                if (i % 500 == 0) await Task.Delay(1);
            }
            await Task.Delay(500);
            int realized = RealizedCaptionRows.Length;
            bool unlimited = history.Count == 20000 && transcript.Count == 20000 && history[0].Id == 1 && realized > 0 && realized < 200;
            bool followed = followLatest && historyScroll is not null && historyScroll.ScrollableHeight - historyScroll.VerticalOffset < 5;
            string initial = $"flag={followLatest}, offset={historyScroll?.VerticalOffset}, height={historyScroll?.ScrollableHeight}";
            PauseHistoryFollowing(); historyScroll!.ChangeView(null, 200, null, true); await Task.Delay(300);
            double initialHeight = historyScroll.ScrollableHeight;
            double offset = historyScroll.VerticalOffset;
            bool paused = !followLatest;
            for (int i = 20001; i <= 21000; i++)
                UpdateCaption(new(i, 1, $"More history {i}", "追加字幕", true, Stopwatch.GetTimestamp(), 0, 0));
            // A delayed AI revision to a row in view must not jump to the end.
            UpdateCaption(history[2] with { Revision = 2, English = "Revised earlier row" });
            await Task.Delay(400);
            double drift = Math.Abs(historyScroll.VerticalOffset - offset);
            bool retained = paused && drift < 5 && history.Count == 21000;
            CaptionList.ScrollIntoView(history[0], ScrollIntoViewAlignment.Leading); await Task.Delay(200);
            bool firstAccessible = CaptionList.ContainerFromIndex(0) is ListViewItem;
            CaptionList.ScrollIntoView(history[^1]); await Task.Delay(400);
            bool resumed = followLatest;
            UpdateCaption(new(21001, 1, "Latest row", "最新の字幕", true, Stopwatch.GetTimestamp(), 0, 0));
            await Task.Delay(300);
            resumed &= historyScroll.ScrollableHeight - historyScroll.VerticalOffset < 5;
            double historyLoadMs = clock.Elapsed.TotalMilliseconds;
            if (Environment.GetCommandLineArgs().Contains("--verify-scroll-only"))
            {
                passed = unlimited && followed && retained && firstAccessible && resumed;
                report = $"PASS={passed}; rows={history.Count}; realizedRows={realized}; unlimited={unlimited}; followed={followed}; scrollDrift={drift:F2}px; retained={retained}; firstAccessible={firstAccessible}; resumed={resumed}; offset={historyScroll.VerticalOffset}; height={historyScroll.ScrollableHeight}; initialHeight={initialHeight}; initial=({initial}); followFlag={followLatest}";
                return;
            }
            // Isolate recording from other applications' audio and model inference.
            captionStop.Dispose(); captionStop = null;
            settings.AudioInputKind = "application"; settings.AudioDeviceId = "";
            settings.AudioProcessId = Environment.ProcessId;
            settings.AudioProcessStarted = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
            string path = Path.Combine(root, "artifacts", "history-stop-recording.wav");
            StartRecording(path); bool recordOnlyStopEnabled = StopButton.IsEnabled;
            using var output = new WaveOutEvent();
            output.Init(new NAudio.Wave.SampleProviders.SignalGenerator(48000, 2) { Frequency = 440, Gain = .025 });
            output.Play();
            await Task.Delay(5000);
            GC.Collect(); using var process = Process.GetCurrentProcess(); process.Refresh();
            long memoryBefore = process.PrivateMemorySize64; var cpuBefore = process.TotalProcessorTime; var measure = Stopwatch.StartNew();
            await Task.Delay(50000);
            process.Refresh(); double cpu = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds / measure.Elapsed.TotalMilliseconds * 100;
            long memoryDelta = process.PrivateMemorySize64 - memoryBefore;
            // Also exercise simultaneous caption cancellation without a loaded model.
            captionStop = new(); var token = captionStop.Token;
            async Task FakeCaptionsAsync()
            {
                try { await Task.Delay(Timeout.Infinite, token); } catch (OperationCanceledException) { }
                finally { captionStop?.Dispose(); captionStop = null; StopButton.IsEnabled = recordingStop is not null; }
            }
            captionTask = FakeCaptionsAsync();
            var stopping = Stopwatch.StartNew();
            // Same handler used by the actual Stop button.
            StopClick(StopButton, new RoutedEventArgs());
            for (int i = 0; i < 100 && recordingStop is not null; i++) await Task.Delay(50);
            output.Stop();
            bool stopped = recordingStop is null && RecordToggle.IsChecked == false && captionTask.IsCompleted && !StopButton.IsEnabled;
            using var reader = new AudioFileReader(path); float[] samples = new float[16000]; float peak = 0; int read;
            while ((read = reader.Read(samples, 0, samples.Length)) > 0)
                for (int i = 0; i < read; i++) peak = Math.Max(peak, Math.Abs(samples[i]));
            bool wave = reader.TotalTime.TotalSeconds >= 54 && peak > .001;
            passed = unlimited && followed && retained && firstAccessible && resumed && recordOnlyStopEnabled && stopped && wave && memoryDelta < 64 * 1024 * 1024;
            report = $"PASS={passed}; rows={history.Count}; realizedRows={realized}; unlimited={unlimited}; followed={followed}; paused={paused}; scrollDrift={drift:F2}px; retained={retained}; firstAccessible={firstAccessible}; resumed={resumed}; recordingOnlyStop={recordOnlyStopEnabled}; stopBoth={stopped}; stopMs={stopping.Elapsed.TotalMilliseconds:F0}; WAV={reader.TotalTime.TotalSeconds:F2}s; peak={peak:F3}; recordingCpuPercentOfOneCore={cpu:F2}; privateMemoryDeltaMB={memoryDelta / 1048576d:F2}; historyLoadMs={historyLoadMs:F0}";
        }
        catch (Exception ex) { report = ex.ToString(); }
        finally
        {
            await StopAllAsync(); captionStop?.Dispose(); captionStop = null;
            settings.AudioInputKind = originalInput.Kind; settings.AudioDeviceId = originalInput.DeviceId;
            settings.AudioProcessId = originalInput.ProcessId; settings.AudioProcessStarted = originalInput.ProcessStarted;
            File.WriteAllText(Path.Combine(root, "artifacts", "history-verification.txt"), report);
            Environment.ExitCode = passed ? 0 : 1;
            await ShutdownAsync();
        }
    }
}
