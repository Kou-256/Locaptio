using System.Diagnostics;
using Locaptio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Locaptio.WinUI;

public sealed partial class MainWindow
{
    // Uses only synthetic timestamps/text; never opens a microphone or Windows captions.
    private async Task VerifySpeakersAsync()
    {
        var original = (settings.SpeakerDiarization, settings.CaptionMode, settings.English, settings.Japanese);
        bool passed = false; string report = "";
        try
        {
            settings.SpeakerDiarization = true; settings.CaptionMode = "translate"; settings.English = settings.Japanese = true;
            ApplySettings(); ClearCaptions(); captionStop = new();
            using var session = new SpeakerRecognitionSession(new SpeakerEmbeddingBackend(root)); speakerSession = session;
            long start = Stopwatch.GetTimestamp() + Stopwatch.Frequency;
            session.Timeline.Add(new(start, start + 2 * Stopwatch.Frequency, 1, .9));
            session.Timeline.Add(new(start + 3 * Stopwatch.Frequency, start + 5 * Stopwatch.Frequency, 2, .9));
            UpdateCaption(new(1, 1, "First speaker", "最初の話者", true, start, 0, 0, AudioThrough: start + Stopwatch.Frequency));
            UpdateCaption(new(2, 1, "Second speaker", "次の話者", true, start + 3 * Stopwatch.Frequency, 0, 0, AudioThrough: start + 4 * Stopwatch.Frequency));
            await Task.Delay(350);
            bool mainColors = true;
            foreach (var entry in history)
            {
                var container = (ListViewItem)CaptionList.ContainerFromItem(entry);
                var row = (Grid)container.ContentTemplateRoot;
                var expected = CaptionWindow.SpeakerBrush(settings, entry.Caption.SpeakerId).Color;
                mainColors &= row.Children.OfType<TextBlock>().All(t => ((SolidColorBrush)t.Foreground).Color == expected);
            }
            var first = history[0];
            UpdateCaption(first.Caption with { Revision = 2, English = "Corrected speaker text", SourceCorrected = true, SpeakerId = 0 });
            bool corrected = ReferenceEquals(first, history[0]) && history[0].Caption.SpeakerId == 1;
            captionWindow = new CaptionWindow(settings); captionWindow.Activate(); captionWindow.Update(captions.Values);
            await Task.Delay(250);
            var sourceTexts = new List<TextBlock>();
            void Walk(DependencyObject parent)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                {
                    var child = VisualTreeHelper.GetChild(parent, i);
                    if (child is TextBlock text && (text.Text.Contains("speaker") || text.Text.Contains("話者"))) sourceTexts.Add(text);
                    Walk(child);
                }
            }
            Walk(captionWindow.Content);
            bool popout = sourceTexts.Count == 4 && sourceTexts.Select(t => ((SolidColorBrush)t.Foreground).Color).Distinct().Count() == 2;
            bool saved = transcript.Export(true).Contains("[話者1]") && transcript.Export(true).Contains("[話者2]");
            passed = mainColors && corrected && popout && saved;
            report = $"PASS={passed}; mainSpeakerColors={mainColors}; correctedRowRetained={corrected}; popoutSpeakerColors={popout}; savedLabels={saved}";
        }
        catch (Exception ex) { report = ex.ToString(); }
        finally
        {
            speakerSession?.Dispose(); speakerSession = null;
            captionWindow?.Close(); captionWindow = null;
            captionStop?.Dispose(); captionStop = null;
            (settings.SpeakerDiarization, settings.CaptionMode, settings.English, settings.Japanese) = original;
            File.WriteAllText(Path.Combine(root, "artifacts", "speaker-ui-verification.txt"), report);
            Environment.ExitCode = passed ? 0 : 1; await ShutdownAsync();
        }
    }
}
