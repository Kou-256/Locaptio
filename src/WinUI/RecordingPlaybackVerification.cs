using System.Diagnostics;
using Locaptio.Core;
using Microsoft.UI.Xaml;
using NAudio.Wave;

namespace Locaptio.WinUI;
public sealed partial class MainWindow
{
    private async Task VerifyRecordingPlaybackAsync()
    {
        bool passed = false; string report = "";
        try
        {
            string folder = Path.Combine(root, "artifacts", "playback-verification"); Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "synthetic-silence.wav");
            using (var writer = new WaveFileWriter(path, new WaveFormat(16000, 16, 1))) writer.Write(new byte[16000 * 2 * 5], 0, 16000 * 2 * 5);
            recordingStarted = Stopwatch.GetTimestamp() - 5 * Stopwatch.Frequency;
            BeginRecordingIndex(path); captionStop = new();
            var first = new CaptionUpdate(1, 1, "会議の録音を検索して聞き直せます。", "Search and replay the meeting.", true, recordingStarted + 2 * Stopwatch.Frequency, 0, 0);
            UpdateCaption(first); recordingTranscript!.Observe(first); indexDirty = true;
            UpdateCaption(first with { Id = 2, Started = recordingStarted + 3 * Stopwatch.Frequency, English = "文字はドラッグで選択してコピーできます。", Japanese = "Drag to select and copy text." });
            recordingTranscript.Observe(history[^1].Caption);
            await FinishRecordingIndexAsync(path);
            RecordingSearchPanel.Visibility = Visibility.Visible; RecordingSearch.Text = "会議"; RefreshRecordingSearch();
            bool search = RecordingSearchResults.Items.Count == 1 && RecordingSearchCount.Text == "1件";
            PlayCaption(1); await Task.Delay(80);
            bool playback = playbackOutput?.PlaybackState == PlaybackState.Playing && playbackReader!.CurrentTime.TotalSeconds >= .6 && playbackReader.CurrentTime.TotalSeconds < 2.5;
            PlaybackClick(PlaybackButton, new RoutedEventArgs()); bool paused = playbackOutput?.PlaybackState == PlaybackState.Paused;
            PlaybackPosition.Value = 3; bool seek = Math.Abs(playbackReader!.CurrentTime.TotalSeconds - 3) < .2;
            bool highlight = highlightedPlaybackPhrase?.Id == 2;
            await Task.Delay(100);
            highlight &= RealizedCaptionRows.Where(row => row.DataContext is CaptionHistoryEntry entry && entry.Id == 2)
                .SelectMany(row => row.Children.OfType<Microsoft.UI.Xaml.Controls.TextBlock>()).Any(text => text.TextHighlighters.Count > 0);
            SetTextSelection(new(new(1, 0), new(2, 5), 0)); await CopyTextSelectionAsync();
            string copied = await Windows.ApplicationModel.DataTransfer.Clipboard.GetContent().GetTextAsync();
            bool selection = copied.Contains("会議") && copied.Contains(Environment.NewLine);
            ClearTextSelection();
            var loaded = await RecordingTranscript.LoadAsync(path);
            bool persisted = loaded.Phrases.Length == 2 && File.Exists(RecordingTranscript.Sidecar(path));
            captionStop.Dispose(); captionStop = null; await LoadRecordingAsync(path);
            PlayCaption(history[0].Id); bool reopened = history.Count == 2 && playbackOutput?.PlaybackState == PlaybackState.Playing;
            playbackOutput?.Pause();
            PlaybackPosition.Value = playbackDocument!.Phrases[0].Seconds + .1;
            passed = search && playback && paused && seek && selection && persisted && reopened && highlight;
            report = $"PASS={passed}; search={search}; playback={playback}; pause={paused}; seek={seek}; highlight={highlight}; multiRowCopy={selection}; sidecar={persisted}; reopen={reopened}; synthetic silence only; no user audio or models captured";
            await Task.Delay(200);
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap(); await bitmap.RenderAsync(Root);
            var pixels = await bitmap.GetPixelsAsync();
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
            using var reader = Windows.Storage.Streams.DataReader.FromBuffer(pixels); byte[] bytes = new byte[pixels.Length]; reader.ReadBytes(bytes);
            encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, bytes); await encoder.FlushAsync();
            stream.Seek(0); using var imageReader = new Windows.Storage.Streams.DataReader(stream.GetInputStreamAt(0)); await imageReader.LoadAsync((uint)stream.Size); byte[] image = new byte[stream.Size]; imageReader.ReadBytes(image);
            await File.WriteAllBytesAsync(Path.Combine(folder, "playback.png"), image);
        }
        catch (Exception ex) { report = "PASS=False; " + ex; }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(root, "artifacts", "recording-playback-verification.txt"), report);
            Environment.ExitCode = passed ? 0 : 1; await ShutdownAsync();
        }
    }
}
