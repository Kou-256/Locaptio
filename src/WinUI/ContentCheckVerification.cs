using System.Diagnostics;
using Locaptio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NAudio.Wave;

namespace Locaptio.WinUI;

// --verify-content-check: the recorded-text check with the installed Whisper model, through the app's
// own code (run from a prepared runtime folder with artifacts/test-*.wav). Real speech clips are joined
// into a meeting and written to a WAV at four times real speed, as the recorder writes it; the check
// follows the file while it grows. The saved text is late and untimed like Windows captions, and one
// utterance is replaced by words that were never spoken. No audio is played or captured.
public sealed partial class MainWindow
{
    private async Task VerifyContentCheckAsync()
    {
        string artifacts = Path.Combine(root, "artifacts"), work = Path.Combine(Path.GetTempPath(), "Locaptio-verify-check-" + Environment.ProcessId);
        Directory.CreateDirectory(artifacts); Directory.CreateDirectory(work);
        string report = Path.Combine(artifacts, "content-check-verification.txt");
        string mode = settings.ContentCheck; bool summarize = settings.SummarizeAfterRecording, autosave = settings.AutosaveSession;
        var log = new List<string>(); var failures = new List<string>();
        void Check(bool condition, string name) { log.Add((condition ? "ok   " : "FAIL ") + name); if (!condition) failures.Add(name); }
        try
        {
            Title = "Locaptio — 音声と文章の照合の検証";
            contentCheckInVerification = true; confirmReplacementForVerification = _ => Task.FromResult(true);
            settings.SummarizeAfterRecording = false; settings.AutosaveSession = false; settings.ContentCheck = "during";
            // A meeting of real speech: each clip twice, with pauses.
            var samples = new List<float>(); var random = new Random(9);
            void Silence(double seconds) { for (int i = 0; i < (int)(seconds * 16000); i++) samples.Add((float)((random.NextDouble() * 2 - 1) * .001)); }
            Silence(1.2);
            int rounds = Environment.GetCommandLineArgs().Contains("--long") ? 4 : 1;
            foreach (string name in Enumerable.Repeat(new[] { "test-ja.wav", "test-ja-continuous.wav", "test-ja.wav" }, rounds).SelectMany(n => n))
            {
                using var reader = new AudioFileReader(Path.Combine(artifacts, name));
                using var resampled = new MediaFoundationResampler(reader, WaveFormat.CreateIeeeFloatWaveFormat(16000, 1));
                var buffer = new byte[64000]; int read;
                while ((read = resampled.Read(buffer, 0, buffer.Length)) > 0) for (int i = 0; i + 3 < read; i += 4) samples.Add(BitConverter.ToSingle(buffer, i));
                Silence(1.5);
            }
            double length = samples.Count / 16000.0;

            // What the audio says, with word times: the reference this check must reproduce.
            string truthAudio = Path.Combine(work, "truth.wav");
            using (var writer = new WaveFileWriter(truthAudio, new WaveFormat(16000, 16, 1))) foreach (float v in samples) writer.WriteSample(v);
            var truth = await RecognizeRecordingAsync("正解データの作成", truthAudio, settings.SpeechModel, true, false, false);
            Check(truth is { Count: >= 3 }, $"reference transcript ({truth?.Count ?? 0} utterances)");
            if (truth is null || truth.Count < 3) return;
            log.Add("heard: " + string.Join(" / ", truth.Select(p => $"{p.Seconds:F1} {p.Text}")));

            // Record: the WAV grows at four times real speed (real speed with --realtime) and the check follows it.
            string audio = Path.Combine(work, "会議.wav");
            var recordTimer = Stopwatch.StartNew();
            var writing = Task.Run(async () =>
            {
                using var writer = new WaveFileWriter(audio, new WaveFormat(16000, 16, 1));
                for (int at = 0; at < samples.Count; at += 4000)
                {
                    for (int i = at; i < Math.Min(samples.Count, at + 4000); i++) writer.WriteSample(samples[i]);
                    writer.Flush(); await Task.Delay(Environment.GetCommandLineArgs().Contains("--realtime") ? 250 : 62);
                }
            });
            BeginLiveContentCheck(audio);
            Check(liveContentCheck is not null, "live check started with the recording");
            await writing;
            double recorded = recordTimer.Elapsed.TotalSeconds;
            if (liveContentCheck is { } live) log.Add($"live check: model ready after {live.ModelReady:F1}s ({live.Backend}); {live.Heard} segments recognized before stop");

            // The saved text: Windows-caption-like (1.6 s late, no word times), one utterance never spoken.
            int wrong = truth.Count / 2;
            var saved = truth.Select((p, i) => p with { Seconds = p.Seconds + 1.6, EndSeconds = null, Words = null, Text = i == wrong ? "全く関係のない天気予報の話題です" : p.Text }).ToArray();
            await RecordingTranscript.SaveAsync(audio, new(1, "会議.wav", DateTimeOffset.Now, true, saved));
            var pending = liveContentCheck; await LoadRecordingAsync(audio); liveContentCheck = pending; ClosePlayback();
            var stopTimer = Stopwatch.StartNew();
            await CheckRecordedContentAsync(audio);
            double afterStop = stopTimer.Elapsed.TotalSeconds;
            if (pending is not null) log.Add($"live check problem: [{pending.Problem}] reference {(pending.Reference.IsCompletedSuccessfully ? pending.Reference.Result?.Count.ToString() ?? "null" : pending.Reference.Status.ToString())}");
            if (pending is not null) log.Add($"segments (audio span @ wall clock since start; stop at {recorded:F1}s): " + string.Join(", ", pending.Timeline));
            var checkedDocument = playbackDocument!;
            var match = checkedDocument.Phrases.Select(p => p.Match ?? -1).ToArray();
            log.Add($"recording {length:F0}s written in {recorded:F1}s; finished {afterStop:F1}s after stop; match " + string.Join(" / ", match.Select(m => m.ToString("P0"))));
            Check(match[wrong] < TimingAlignment.LowMatch && match.Where((_, i) => i != wrong).All(m => m >= .8), "content match: spoken rows high, the unspoken row low");
            var errors = checkedDocument.Phrases.Zip(truth).Where((_, i) => i != wrong).Select(p => Math.Abs(p.First.Seconds - p.Second.Seconds)).Order().ToArray();
            log.Add($"start error after check: median {errors[errors.Length / 2]:F2}s, max {errors[^1]:F2}s (saved 1.60s late)");
            Check(errors[^1] < .35, "spoken rows moved to where they were said");
            Check(checkedDocument.Phrases.Where((_, i) => i != wrong).All(p => p.Words is { Length: > 0 }), "spoken rows have word times");
            var reloaded = await RecordingTranscript.LoadAsync(audio);
            Check(reloaded.Phrases.Select(p => p.Match).SequenceEqual(checkedDocument.Phrases.Select(p => p.Match)), "match saved with the recording");
            // Row marks: only the unspoken row shows the warning.
            await Task.Delay(300);
            var marks = RealizedCaptionRows.Where(r => r.Children.Count >= 5).Select(r => (Id: ((CaptionHistoryEntry)r.DataContext).Caption.Id, Shown: r.Children[4].Visibility == Visibility.Visible)).ToArray();
            long wrongId = checkedDocument.Phrases[wrong].Id;
            Check(marks.Length > 0 && marks.All(m => m.Shown == (m.Id == wrongId)), $"warning mark only on the unspoken row ({marks.Count(m => m.Shown)} shown)");
            // The status says what happened.
            Check(StatusText.Text.Contains("照合しました") && StatusText.Text.Contains("1 件"), "status reports the check: " + StatusText.Text);

            // Same recording checked only after stopping, for comparison.
            settings.ContentCheck = "after";
            await RecordingTranscript.SaveAsync(audio, new(1, "会議.wav", DateTimeOffset.Now, true, saved));
            await LoadRecordingAsync(audio); ClosePlayback();
            stopTimer.Restart(); await CheckRecordedContentAsync(audio); double full = stopTimer.Elapsed.TotalSeconds;
            log.Add($"checking only after stop took {full:F1}s");
            Check(playbackDocument!.Phrases.Select(p => p.Match ?? -1).Zip(match).All(p => Math.Abs(p.First - p.Second) < .05), "after-stop check gives the same result");
            Check(afterStop < full, $"checking during recording leaves less to do at stop ({afterStop:F1}s vs {full:F1}s)");
        }
        catch (Exception ex) { failures.Add(ex.Message); log.Add(ex.ToString()); }
        finally
        {
            settings.ContentCheck = mode; settings.SummarizeAfterRecording = summarize; settings.AutosaveSession = autosave; settings.Save();
            liveContentCheck?.Dispose(); liveContentCheck = null; contentCheckInVerification = false;
            ClosePlayback(); try { await reviewWrite; } catch { }
            File.WriteAllText(report, (failures.Count == 0 ? "PASS" : "FAIL: " + string.Join("; ", failures)) + Environment.NewLine + string.Join(Environment.NewLine, log));
            Environment.ExitCode = failures.Count == 0 ? 0 : 1;
            try { Directory.Delete(work, true); } catch (IOException) { }
            await ShutdownAsync();
        }
    }
}
