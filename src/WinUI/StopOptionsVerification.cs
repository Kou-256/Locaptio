using System.Diagnostics;
using Locaptio.Core;
using Microsoft.UI.Xaml;

namespace Locaptio.WinUI;

public sealed partial class MainWindow
{
    private async Task VerifyStopOptionsAsync()
    {
        bool passed = false; string report = "";
        bool date = settings.ExportSavedAt, duration = settings.ExportDuration, lines = settings.ExportLineCount;
        try
        {
            ClearCaptions(); captionStop = new(); var ownStop = captionStop; transcriptClock.Start();
            UpdateCaption(new(1, 1, "停止後も文章を保持します。", null, true, Stopwatch.GetTimestamp(), 0, 0));
            CaptionClick(CaptionButton, new RoutedEventArgs()); await Task.Delay(100);
            int closed = 0; bool canceledBeforeClose = false;
            stopWindowsCaptionsForVerification = () => { closed++; canceledBeforeClose = ownStop.IsCancellationRequested && captionWindow is null; return Task.CompletedTask; };
            await StopAllAsync();
            var elapsed = transcriptClock.Elapsed; await Task.Delay(50);
            bool stopped = closed == 1 && canceledBeforeClose && captionWindow is null && transcript.Count == 1 && transcriptClock.Elapsed == elapsed && StartButton.IsEnabled;
            settings.ExportSavedAt = settings.ExportDuration = settings.ExportLineCount = true;
            string annotated = ExportTranscriptForSave(false);
            bool metadata = annotated.Contains("保存日時:") && annotated.Contains("セッション時間") && annotated.Contains("1行 / 発話数: 1");
            settings.ExportSavedAt = settings.ExportDuration = settings.ExportLineCount = false;
            bool raw = ExportTranscriptForSave(false) == transcript.Export(false);
            passed = stopped && metadata && raw;
            report = $"PASS={passed}; stopClosesCaptionAndWindowsAfterCancellation={stopped}; historyPreserved={transcript.Count == 1}; exportMetadata={metadata}; metadataOffPreservesBody={raw}; clockStopped={transcriptClock.Elapsed == elapsed}";
        }
        catch (Exception ex) { report = "PASS=False; " + ex; }
        finally
        {
            stopWindowsCaptionsForVerification = null;
            settings.ExportSavedAt = date; settings.ExportDuration = duration; settings.ExportLineCount = lines;
            captionStop?.Dispose(); captionStop = null;
            File.WriteAllText(Path.Combine(root, "artifacts", "stop-options-1.0.35.txt"), report);
            stopWindowsCaptionsForVerification = () =>
            {
                if (!closing) throw new Exception("Windows close ran before shutdown started");
                File.AppendAllText(Path.Combine(root, "artifacts", "stop-options-1.0.35.txt"), "; shutdownClosesWindows=True");
                return Task.CompletedTask;
            };
            Environment.ExitCode = passed ? 0 : 1; await ShutdownAsync();
        }
    }
}
