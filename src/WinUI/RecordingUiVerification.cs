using System.Diagnostics;
using System.Runtime.InteropServices;
using Locaptio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Locaptio.WinUI;
public sealed partial class MainWindow
{
    [DllImport("user32.dll")] private static extern bool InvalidateRect(nint window, nint rect, [MarshalAs(UnmanagedType.Bool)] bool erase);
    [DllImport("user32.dll")] private static extern bool UpdateWindow(nint window);

    private async Task VerifyRecordingUiAsync()
    {
        bool passed = false; string report = "";
        var input = SelectedAudio(); string mode = settings.CaptionMode;
        string recordingDirectory = settings.RecordingSaveDirectory, recordingFormat = settings.RecordingFormat;
        long pathCallback = 0;
        try
        {
            // Record only this test process, silently, without models or the user's
            // microphone/system audio. Exercise the same recorder/clock as the UI.
            settings.CaptionMode = "japanese"; settings.AudioInputKind = "application";
            settings.AudioDeviceId = ""; settings.AudioProcessId = Environment.ProcessId;
            using (var current = Process.GetCurrentProcess()) settings.AudioProcessStarted = current.StartTime.ToUniversalTime().Ticks;
            ClearCaptions(); captionStop = new();
            for (int i = 1; i <= 300; i++) UpdateCaption(new(i, 1, $"文字起こしの履歴 {i:D3}", null, true, Stopwatch.GetTimestamp(), 0, 0));
            settings.RecordingSaveDirectory = Path.Combine(root, "artifacts", "recording-checkbox-test");
            settings.RecordingFormat = "wav";
            var naming = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            recordingFilePickerForVerification = () => naming.Task;
            RecordToggle.IsChecked = true; await Task.Delay(50);
            bool waitsForName = dialogOpen && recordingStop is null && !StartButton.IsEnabled && !RecordToggle.IsEnabled;
            naming.SetResult(null); await Task.Delay(100);
            bool cancelDoesNotRecord = recordingStop is null && RecordToggle.IsChecked == false && !dialogOpen;
            naming = new(TaskCreationOptions.RunContinuationsAsynchronously);
            string namedPath = Path.Combine(settings.RecordingSaveDirectory, $"named-recording-{Guid.NewGuid():N}.wav");
            RecordToggle.IsChecked = true;
            await Task.Delay(50); waitsForName &= recordingStop is null;
            naming.SetResult(namedPath);
            await Task.Delay(700);
            recordingFilePickerForVerification = null;
            if (recordingStop is null) throw new Exception("Recorder did not remain active");
            bool automaticRecording = RecordToggle.IsChecked == true && !dialogOpen
                && recordingPath == namedPath && File.Exists(recordingPath) && waitsForName && cancelDoesNotRecord;
            bool compactDetails = StatusDetailsPanel.Visibility == Visibility.Collapsed;
            var anchor = StatusDetailsButton.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point());
            double beforeOverlay = CaptionList.ActualHeight;
            ShowStatusDetails(); await Task.Delay(100);
            var afterAnchor = StatusDetailsButton.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point());
            bool expandable = StatusDetailsPanel.Visibility == Visibility.Visible && anchor == afterAnchor
                && Math.Abs(beforeOverlay - CaptionList.ActualHeight) < .5
                && Canvas.GetTop(StatusDetailsPanel) + StatusDetailsPanel.ActualHeight <= anchor.Y
                && StatusDetailsButton.FontSize == 11 && StatusDetailsButton.ActualWidth < 60;
            StatusDetailsClick(StatusDetailsButton, new RoutedEventArgs()); await Task.Delay(100);
            expandable &= StatusDetailsPanel.Visibility == Visibility.Collapsed;
            var row = history[^1];
            var container = (ListViewItem)CaptionList.ContainerFromIndex(history.Count - 1);
            var content = container.ContentTemplateRoot;
            var text = (TextBlock)((Grid)content).Children[0];
            var backdrop = (TransparentWindowBackdrop)SystemBackdrop;
            // Establish the first backing surface before measuring repeated paints.
            InvalidateRect(hwnd, 0, true); UpdateWindow(hwnd); await Task.Delay(50);
            int clears = backdrop.SurfaceClearCount, eraseRequests = backdrop.EraseRequestCount, pathChanges = 0;
            pathCallback = RecordingText.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) => pathChanges++);
            double viewport = CaptionList.ActualHeight;
            string firstClock = RecordingClock.Text;
            int repetitions = Environment.GetCommandLineArgs().Contains("--preview-recording-ui") ? 500 : 100;
            for (int i = 2; i < repetitions + 2; i++)
            {
                UpdateCaption(row.Caption with { Revision = i, English = $"文字起こしの更新 {i:D3}" });
                // Repeated native invalidation used to repaint the black backing
                // surface each time; the recording window must now retain it.
                InvalidateRect(hwnd, 0, true); UpdateWindow(hwnd);
                await Task.Delay(50);
            }
            bool stable = ReferenceEquals(history[^1], row) && ReferenceEquals(container.ContentTemplateRoot, content) && text.Text == row.English;
            bool clockOnly = pathChanges == 0 && RecordingClock.Text != firstClock && Math.Abs(CaptionList.ActualHeight - viewport) < .5;
            bool retainedSurface = backdrop.EraseRequestCount > eraseRequests && backdrop.SurfaceClearCount == clears;
            bool recordingActive = recordingStop is { IsCancellationRequested:false } && RecordingIndicator.Visibility == Visibility.Visible;
            string selected = text.Text;
            draggingText = true;
            SetTextSelection(new(new(row.Id, 0), new(row.Id, selected.Length), 0));
            await Task.Delay(50);
            bool selectionEnabled = !text.IsTextSelectionEnabled && textSelection?.Text(history) == selected && !followLatest;
            var correction = row.Caption with { Revision = repetitions + 10, English = "選択中も認識は継続し、選択解除後に補正を反映します。" };
            UpdateCaption(correction); await Task.Delay(50);
            bool selectionRetained = textSelection?.Text(history) == selected && text.Text == selected
                && captions[row.Id].English == correction.English;
            FinishTextSelection(); await Task.Delay(50);
            bool releaseRetained = textSelection?.Text(history) == selected && text.Text == selected && text.TextHighlighters.Count > 0;
            ClearTextSelection(); await Task.Delay(100);
            bool correctionResumed = releaseRetained && text.Text == correction.English && selectedRowUpdates.Count == 0;
            var first = history[^3]; var middle = history[^2]; var last = history[^1];
            var range = new TranscriptSelection(new(first.Id, 2), new(last.Id, 5), 0);
            SetTextSelection(range);
            string expectedRange = first.English[2..] + Environment.NewLine + middle.English + Environment.NewLine + last.English[..5];
            bool multiRowCopy = textSelection!.Text(history) == expectedRange;
            UpdateCaption(middle.Caption with { Revision = middle.Caption.Revision + 1, English = "選択中の途中行の補正" });
            bool multiRowRetained = textSelection.Text(history) == expectedRange;
            CaptionList.ScrollIntoView(history[0]); await Task.Delay(100);
            bool recycledSelectionRetained = textSelection.Text(history) == expectedRange;
            CaptionList.ScrollIntoView(last); await Task.Delay(150);
            bool highlightRestored = RealizedCaptionRows.SelectMany(r => r.Children.OfType<TextBlock>()).Any(t => t.TextHighlighters.Count > 0);
            ClearTextSelection(); await Task.Delay(50);
            bool multiRowCorrectionResumed = middle.English == "選択中の途中行の補正" && selectedRowUpdates.Count == 0;
            RecordToggle.IsChecked = false; await StopRecordingAsync();
            bool uncheckedStopsRecordingOnly = recordingStop is null && captionStop is not null;
            await StopAllAsync();
            bool stopped = recordingStop is null && RecordingIndicator.Visibility == Visibility.Collapsed;
            passed = stable && clockOnly && retainedSurface && recordingActive && stopped && automaticRecording && compactDetails && expandable && selectionEnabled && selectionRetained && correctionResumed && uncheckedStopsRecordingOnly && multiRowCopy && multiRowRetained && recycledSelectionRetained && highlightRestored && multiRowCorrectionResumed;
            report = $"PASS={passed}; waitsForFilename={waitsForName}; cancelDoesNotRecord={cancelDoesNotRecord}; namedRecordingStarts={automaticRecording}; detailsInitiallyHidden={compactDetails}; detailsOverlayBelowWithoutMovement={expandable}; multiRowCopy={multiRowCopy}; multiRowRetained={multiRowRetained}; recycledSelectionRetained={recycledSelectionRetained}; highlightRestored={highlightRestored}; multiRowCorrectionResumed={multiRowCorrectionResumed}; selectionEnabled={selectionEnabled}; selectionRetainedDuringCorrection={selectionRetained}; correctionResumed={correctionResumed}; uncheckStopsOnlyRecording={uncheckedStopsRecordingOnly}; stableTextContainer={stable}; recordingClockOnly={clockOnly}; viewportBefore={viewport:F1}; viewportAfter={CaptionList.ActualHeight:F1}; staticPathChanges={pathChanges}; retainedSurface={retainedSurface}; nativeEraseRequests={backdrop.EraseRequestCount - eraseRequests}; repeatedSurfaceClears={backdrop.SurfaceClearCount - clears}; recordingActive={recordingActive}; stopped={stopped}";
        }
        catch (Exception ex) { report = ex.ToString(); }
        finally
        {
            recordingFilePickerForVerification = null;
            if (pathCallback != 0) RecordingText.UnregisterPropertyChangedCallback(TextBlock.TextProperty, pathCallback);
            await StopAllAsync(); captionStop?.Dispose(); captionStop = null;
            settings.CaptionMode = mode; settings.AudioInputKind = input.Kind; settings.AudioDeviceId = input.DeviceId;
            settings.RecordingSaveDirectory = recordingDirectory; settings.RecordingFormat = recordingFormat;
            settings.AudioProcessId = input.ProcessId; settings.AudioProcessStarted = input.ProcessStarted;
            File.WriteAllText(Path.Combine(root, "artifacts", "recording-ui-verification.txt"), report);
            Environment.ExitCode = passed ? 0 : 1; await ShutdownAsync();
        }
    }
}
