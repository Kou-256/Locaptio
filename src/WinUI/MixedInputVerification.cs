using System.Diagnostics;
using Locaptio.Core.Audio;
using Microsoft.UI.Xaml;
using NAudio.Wave;

namespace Locaptio.WinUI;
public sealed partial class MainWindow
{
    private async Task VerifyMixedInputAsync()
    {
        bool passed = false; string report = "";
        var originalInput = SelectedAudio();
        var original = (settings.SpeechEngine, settings.CaptionMode, settings.SpeechModel, settings.InferenceDevice, settings.CpuOnly, settings.RealtimeCaptions, settings.UseLocalLlm, settings.CorrectTranscriptWithAi);
        try
        {
            settings.SpeechEngine = "windows-live"; settings.AudioInputKind = "mixed"; settings.AudioDeviceId = ""; settings.AudioProcessId = 0; settings.AudioProcessStarted = 0;
            settings.CaptionMode = "japanese"; settings.SpeechModel = "base"; settings.InferenceDevice = "cpu"; settings.CpuOnly = true;
            settings.RealtimeCaptions = false; settings.UseLocalLlm = false; settings.CorrectTranscriptWithAi = false;
            RefreshAudioInputs(); ApplySettings();
            bool selection = AudioSelector.SelectedItem is AudioInputChoice choice && choice.Selection.Kind == "mixed";
            bool fallback = !UseWindowsCaptionEngine && settings.SpeechEngine == "windows-live";
            string recording = Path.Combine(root, "artifacts", "mixed-input", "winui-mixed.wav"); Directory.CreateDirectory(Path.GetDirectoryName(recording)!);
            StartRecording(recording); await StartAsync();
            for (int i = 0; i < 600 && pipeline is null && captionStop is not null; i++) await Task.Delay(100);
            if (pipeline is null) throw new Exception("Whisper did not start: " + StatusText.Text);
            using var reader = new WaveFileReader(Path.Combine(root, "artifacts", "test-ja.wav")); using var output = new WaveOutEvent();
            output.Init(reader); output.Play(); await Task.Delay(reader.TotalTime + TimeSpan.FromSeconds(2));
            for (int i = 0; i < 200 && !transcript.Export(true).Contains("テスト"); i++) await Task.Delay(100);
            string text = transcript.Export(true); string backend = BackendText.Text;
            bool simultaneous = recordingStop is not null && captionStop is not null && history.Count > 0;
            var stopping = Stopwatch.StartNew(); await StopAllAsync();
            using var wave = new AudioFileReader(recording); float[] buffer = new float[4096]; float peak = 0; int read;
            while ((read = wave.Read(buffer, 0, buffer.Length)) > 0) for (int i = 0; i < read; i++) peak = Math.Max(peak, Math.Abs(buffer[i]));
            passed = selection && fallback && simultaneous && text.Contains("テスト") && peak > .005 && recordingStop is null && captionStop is null && RecordToggle.IsChecked == false;
            report = $"PASS={passed}; selection={selection}; automaticWhisperFallback={fallback}; simultaneousRecordingAndCaptions={simultaneous}; captionRows={history.Count}; WAV={wave.TotalTime.TotalSeconds:F2}s; peak={peak:F3}; stopMs={stopping.Elapsed.TotalMilliseconds:F0}; backend={backend}; text={text}";
        }
        catch (Exception ex) { report = ex.ToString(); }
        finally
        {
            await StopAllAsync();
            settings.AudioInputKind = originalInput.Kind; settings.AudioDeviceId = originalInput.DeviceId; settings.AudioProcessId = originalInput.ProcessId; settings.AudioProcessStarted = originalInput.ProcessStarted;
            (settings.SpeechEngine, settings.CaptionMode, settings.SpeechModel, settings.InferenceDevice, settings.CpuOnly, settings.RealtimeCaptions, settings.UseLocalLlm, settings.CorrectTranscriptWithAi) = original;
            File.WriteAllText(Path.Combine(root, "artifacts", "mixed-input-verification.txt"), report);
            Environment.ExitCode = passed ? 0 : 1; await ShutdownAsync();
        }
    }
}
