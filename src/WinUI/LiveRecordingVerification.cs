using System.Diagnostics;
using Locaptio.Core;
using Microsoft.UI.Xaml;
using NAudio.Wave;

namespace Locaptio.WinUI;

// --verify-live-recording: the whole path with real Windows Live Captions. Japanese test speech is
// PLAYED ALOUD through the default output while Locaptio records PC audio with Windows captions, and
// the recording is checked against its audio during recording. Character times are compared with
// when each character was actually played (Whisper word times on the clean, unrecorded audio placed on
// the playback timeline): from caption appearance alone, and after the content check.
// Run from a prepared runtime folder with artifacts/test-*.wav; the PC volume must be audible to loopback.
public sealed partial class MainWindow
{
    private async Task VerifyLiveRecordingAsync()
    {
        string artifacts = Path.Combine(root, "artifacts"), work = Path.Combine(Path.GetTempPath(), "Locaptio-verify-live-" + Environment.ProcessId);
        Directory.CreateDirectory(artifacts); Directory.CreateDirectory(work);
        string report = Path.Combine(artifacts, "live-recording-verification.txt");
        string engine = settings.SpeechEngine, kind = settings.AudioInputKind, captionMode = settings.CaptionMode, check = settings.ContentCheck;
        bool summarize = settings.SummarizeAfterRecording, autosave = settings.AutosaveSession;
        var log = new List<string>(); var failures = new List<string>();
        void Check(bool condition, string name) { log.Add((condition ? "ok   " : "FAIL ") + name); if (!condition) failures.Add(name); }
        try
        {
            Title = "Locaptio — Windows字幕と録音の通し検証（音声を再生します）";
            settings.SpeechEngine = "windows-live"; settings.AudioInputKind = "system"; settings.CaptionMode = "japanese"; settings.ContentCheck = "during";
            settings.SummarizeAfterRecording = false; settings.AutosaveSession = false; ApplySettings();
            contentCheckInVerification = true; confirmReplacementForVerification = _ => Task.FromResult(true);

            // The speech to play: clips with pauses, and when each sample will be heard.
            var clips = new[] { "test-ja.wav", "test-ja-continuous.wav", "test-ja.wav", "test-ja-continuous.wav" };
            var timeline = new List<float>(); var random = new Random(2);
            void Silence(double seconds) { for (int i = 0; i < (int)(seconds * 16000); i++) timeline.Add(0); }
            Silence(1.0);
            foreach (string name in clips)
            {
                using var reader = new AudioFileReader(Path.Combine(artifacts, name));
                using var resampled = new MediaFoundationResampler(reader, WaveFormat.CreateIeeeFloatWaveFormat(16000, 1));
                var buffer = new byte[64000]; int read;
                while ((read = resampled.Read(buffer, 0, buffer.Length)) > 0) for (int i = 0; i + 3 < read; i += 4) timeline.Add(BitConverter.ToSingle(buffer, i));
                Silence(1.8 + random.NextDouble());
            }
            string played = Path.Combine(work, "played.wav");
            using (var writer = new WaveFileWriter(played, WaveFormat.CreateIeeeFloatWaveFormat(16000, 1))) writer.WriteSamples(timeline.ToArray(), 0, timeline.Count);

            // Truth: what is said when, from the clean audio (never recorded).
            var truth = await RecognizeRecordingAsync("正解データの作成", played, settings.SpeechModel, true, false, false);
            Check(truth is { Count: >= 4 } && truth.All(p => p.Words is { Length: > 0 }), $"reference with word times ({truth?.Count ?? 0} utterances)");
            if (truth is null) return;
            ClosePlayback(); ClearCaptions();

            // Windows captions + recording, then play.
            await StartAsync();
            for (int i = 0; i < 150 && !windowsBridgeConnected && captionStop is not null; i++) await Task.Delay(100);
            Check(windowsBridgeConnected, "Windows Live Captions connected: " + StatusText.Text);
            if (captionStop is null) return;
            string audio = Path.Combine(work, "会議.wav");
            StartRecording(audio);
            Check(liveContentCheck is not null, "content check follows the recording");
            await Task.Delay(1500);
            long playStart = 0;
            using (var reader = new WaveFileReader(played))
            using (var output = new WaveOutEvent())
            {
                output.Init(reader);
                output.Play(); playStart = Stopwatch.GetTimestamp();
                while (output.PlaybackState == PlaybackState.Playing) await Task.Delay(100);
            }
            await Task.Delay(3000); // let the last caption settle
            // Where the played audio starts in the recording (the recorder's clock started at recordingStarted).
            double offset = Stopwatch.GetElapsedTime(recordingStarted, playStart).TotalSeconds;
            int live = liveContentCheck?.Heard ?? -1;
            var stopClock = Stopwatch.StartNew();
            await StopRecordingAsync();
            var appearance = playbackDocument;   // aligned to speech onsets, word times from caption appearance
            await contentCheckTask;
            double afterStop = stopClock.Elapsed.TotalSeconds;
            await StopAsync();
            var checkedDocument = playbackDocument;
            Check(appearance is { Phrases.Length: >= 3 } && checkedDocument is not null, $"captions recorded ({appearance?.Phrases.Length ?? 0} rows)");
            if (appearance is null || checkedDocument is null) return;
            log.Add($"played audio starts {offset:F2}s into the recording; {live} segments checked before stop; finished {afterStop:F1}s after stop");
            log.Add("captions: " + string.Join(" / ", checkedDocument.Phrases.Select(p => $"{p.Seconds:F1} [{(p.Match is double m ? m.ToString("P0") : "-")}] {p.Text}")));

            // Character timing error: each saved character against the nearest same character actually played.
            var truthChars = Characters(truth.Select(p => p with { Seconds = p.Seconds + offset, Words = p.Words!.Select(w => w with { Start = w.Start + offset, End = w.End + offset }).ToArray() }));
            (double Median, double P90, int Count) Error(RecordingDocument document)
            {
                var errors = new List<double>();
                foreach (var (c, at) in Characters(document.Phrases.Where(p => p.Words is { Length: > 0 })))
                {
                    double best = double.MaxValue;
                    foreach (var (tc, tat) in truthChars) if (tc == c && Math.Abs(tat - at) < Math.Abs(best)) best = tat - at;
                    if (Math.Abs(best) < 4) errors.Add(Math.Abs(best));
                }
                errors.Sort();
                return errors.Count == 0 ? (double.NaN, double.NaN, 0) : (errors[errors.Count / 2], errors[(int)(errors.Count * .9)], errors.Count);
            }
            var before = Error(appearance); var after = Error(checkedDocument);
            log.Add($"character time error, caption appearance only: median {before.Median:F2}s, 90% {before.P90:F2}s ({before.Count} chars)");
            log.Add($"character time error, after content check:    median {after.Median:F2}s, 90% {after.P90:F2}s ({after.Count} chars)");
            Check(appearance.Phrases.Count(p => p.Words is { Length: > 0 }) >= appearance.Phrases.Length / 2, "word times from caption appearance");
            Check(before.Count > 20 && before.Median < .8, "caption-appearance word times within 0.8 s (median)");
            Check(after.Count > 20 && after.Median < .25 && after.P90 < .6, "checked word times close to the played speech");
            var matched = checkedDocument.Phrases.Where(p => p.Match is not null).ToArray();
            Check(matched.Length >= checkedDocument.Phrases.Length * 3 / 4 && matched.Count(p => p.Match >= TimingAlignment.LowMatch) >= matched.Length * 3 / 4, "most caption rows match the audio");
            Check(afterStop < 15, $"check finished soon after stop ({afterStop:F1}s)");
        }
        catch (Exception ex) { failures.Add(ex.Message); log.Add(ex.ToString()); }
        finally
        {
            try { if (captionStop is not null) await StopAsync(); } catch { }
            settings.SpeechEngine = engine; settings.AudioInputKind = kind; settings.CaptionMode = captionMode; settings.ContentCheck = check;
            settings.SummarizeAfterRecording = summarize; settings.AutosaveSession = autosave; settings.Save();
            liveContentCheck?.Dispose(); liveContentCheck = null; contentCheckInVerification = false;
            ClosePlayback(); try { await reviewWrite; } catch { }
            File.WriteAllText(report, (failures.Count == 0 ? "PASS" : "FAIL: " + string.Join("; ", failures)) + Environment.NewLine + string.Join(Environment.NewLine, log));
            Environment.ExitCode = failures.Count == 0 ? 0 : 1;
            try { Directory.Delete(work, true); } catch (IOException) { }
            await ShutdownAsync();
        }
    }
    /// <summary>Each letter or digit with the time it is spoken, spreading a word's span over its characters.</summary>
    private static List<(char, double)> Characters(IEnumerable<RecordingPhrase> phrases)
    {
        var result = new List<(char, double)>();
        foreach (var phrase in phrases)
            foreach (var word in phrase.Words ?? [])
                for (int k = 0; k < word.Length; k++)
                {
                    char c = phrase.Text[word.StartIndex + k];
                    if (char.IsLetterOrDigit(c)) result.Add((c, word.Start + (word.End - word.Start) * (k + .5) / word.Length));
                }
        return result;
    }
}
