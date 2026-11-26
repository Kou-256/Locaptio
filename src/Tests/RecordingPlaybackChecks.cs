using System.Diagnostics;
using Locaptio.Core;
using Locaptio.Core.AI;

internal static class RecordingPlaybackChecks
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static async Task RunAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Locaptio-recording-check-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            // Screen recording region: kept inside the display, even sizes, whole display means none.
            Check(CaptureRegion.Fit(new(100, 50, 801, 601), 1920, 1080) == new CaptureRegion(100, 50, 800, 600), "Region was not made even");
            Check(CaptureRegion.Fit(new(1800, 1000, 400, 300), 1920, 1080) == new CaptureRegion(1520, 780, 400, 300), "Region was not moved inside the display");
            Check(CaptureRegion.Fit(new(10, 10, 5, 5), 1920, 1080) is { Width: CaptureRegion.MinSize, Height: CaptureRegion.MinSize }, "Tiny region was not enlarged");
            Check(CaptureRegion.Fit(new(0, 0, 1920, 1080), 1920, 1080) is null && CaptureRegion.Fit(new(-5, -5, 5000, 5000), 1921, 1081) is null, "Whole display was not treated as no region");
            Check(CaptureRegion.FromCorners(300, 200, 100, 50) == new CaptureRegion(100, 50, 200, 150), "Dragged corners in reverse order");
            Check(CaptureRegion.Parse(new CaptureRegion(1, 2, 300, 400).Serialize()) == new CaptureRegion(1, 2, 300, 400) && CaptureRegion.Parse("") is null && CaptureRegion.Parse("a,b,c,d") is null, "Region settings roundtrip");
            var timed = new RecordingPhrase(1, 1, 0, "Hello world", null, 1, "en", 2, [new(0, 5, 0, 1), new(6, 5, 1, 2)]);
            Check(PlaybackHighlight.Range(timed, timed.Text, 1.2, 2, "word") == (6, 5), "Timed word highlight failed");
            Check(PlaybackHighlight.Range(timed, timed.Text, .5, 2, "character") == (2, 1), "Character timing failed");
            Check(PlaybackHighlight.Range(timed, timed.Text, 3, 2, "word") == (0, 0), "Silence after phrase highlighted");
            var unicode = new RecordingPhrase(1, 1, 0, "A😀が", null, 1);
            Check(PlaybackHighlight.Range(unicode, unicode.Text, 1.1, 3, "character") == (1, 2), "Emoji was split");
            Check(PlaybackHighlight.Range(unicode, unicode.Text, 2.5, 3, "character") == (3, 2), "Combining character was split");
            Check(PlaybackHighlight.Range(timed, timed.Text, 1.2, 2, "phrase") == (0, 11), "Phrase mode changed");
            long start = Stopwatch.GetTimestamp();
            var recording = new RecordingTranscript(Path.Combine(directory, "test.wav"), start, false);
            var first = new CaptionUpdate(1, 1, "テスト会議", "Meeting", true, start + 2 * Stopwatch.Frequency, 0, 0);
            Check(recording.Observe(first), "New phrase missing");
            recording.Observe(first with { Revision = 3, English = "修正後の会議" });
            recording.Observe(first with { Revision = 2, English = "古い文" });
            recording.Observe(first with { Id = 1, Started = start + 6 * Stopwatch.Frequency, English = "Next session", Japanese = "次の会議" });
            Check(!recording.Observe(first with { Id = 2, Started = start - 1 }), "Pre-recording caption linked");
            var document = recording.Snapshot();
            Check(document.Phrases.Length == 2 && document.Phrases[0].Seconds == 2 && document.Phrases[0].Text == "修正後の会議", "Timing, revisions, or reused ID handling failed");
            Check(RecordingTranscript.At(document, 1) is null && RecordingTranscript.At(document, 2)?.Seconds == 2 && RecordingTranscript.At(document, 6)?.Seconds == 6, "Playback highlight boundary failed");
            Check(WindowsCaptionInputPolicy.Supported(new()) && WindowsCaptionInputPolicy.Supported(new("mixed"))
                && !WindowsCaptionInputPolicy.Supported(new("microphone", "specific-device")) && !WindowsCaptionInputPolicy.Supported(new("microphone")) && !WindowsCaptionInputPolicy.Supported(new("application")), "Windows microphone routing failed");
            Check(RecordingTranscript.Search(document, "meeting").Length == 1 && RecordingTranscript.Search(document, "会議").Length == 2 && RecordingTranscript.Search(document, " ").Length == 0, "Bilingual search failed");
            await RecordingTranscript.SaveAsync(recording.AudioPath, document);
            var loaded = await RecordingTranscript.LoadAsync(recording.AudioPath);
            Check(loaded.Phrases.SequenceEqual(document.Phrases) && !File.Exists(RecordingTranscript.Sidecar(recording.AudioPath) + ".tmp"), "Roundtrip/atomic save failed");
            string requested = Path.Combine(directory, "library", "会議.wav");
            string session1 = RecordingLibrary.CreateSessionPath(requested), session2 = RecordingLibrary.CreateSessionPath(requested);
            Check(Path.GetDirectoryName(session1) != Path.GetDirectoryName(session2), "Recording folders collided");
            foreach (string audio in new[] { session1, session2 })
            {
                await File.WriteAllTextAsync(audio, "fixture");
                await RecordingTranscript.SaveAsync(audio, document);
                await RecordingLibrary.SaveTextAsync(audio, document);
            }
            Check(RecordingLibrary.List(Path.Combine(directory, "library")).Length == 2 && (await File.ReadAllTextAsync(session1 + ".txt")).Contains("修正後の会議"), "Recording library/text grouping failed");
            recording.Observe(first with { Revision = 4, English = "" });
            Check(recording.Snapshot().Phrases.Length == 1, "Withdrawn caption retained");
            var windows = new RecordingTranscript(recording.AudioPath, start, true); windows.Observe(first);
            Check(windows.Snapshot().ApproximateTiming && windows.Snapshot().Phrases[0].Seconds == 1, "Windows estimated timing not marked");

            // Exercise the real hidden PowerShell runner with a tiny local fixture;
            // no model, package, audio or user settings are downloaded or modified.
            string runtime = Path.Combine(directory, "runtime"); Directory.CreateDirectory(Path.Combine(runtime, "scripts")); Directory.CreateDirectory(Path.Combine(runtime, "tools"));
            await File.WriteAllTextAsync(Path.Combine(runtime, "tools", "uv.exe"), "unused fixture");
            string model = Path.Combine(runtime, "models", "llm", "Qwen3-4B-Q4_K_M.gguf");
            string script = Path.Combine(runtime, "scripts", "setup-llm.ps1");
            await File.WriteAllTextAsync(script, "param($Backend)\n$ErrorActionPreference='Stop'\nNew-Item -ItemType Directory -Force '.venv-llm/Scripts','.venv-llm/Lib/site-packages/llama_cpp','models/llm' | Out-Null\nSet-Content '.venv-llm/Scripts/python.exe' 'fixture'\nSet-Content '.venv-llm/Lib/site-packages/llama_cpp/__init__.py' 'fixture'\nWrite-Output 'LOCAPTIO_MODEL_DOWNLOAD'\nSet-Content 'models/llm/Qwen3-4B-Q4_K_M.gguf' 'fixture'\n");
            var stages = new List<PreparationProgress>();
            await LocalLlmPreparation.EnsureAsync(runtime, model, true, p => { lock (stages) stages.Add(p); }, CancellationToken.None);
            Check(!LocalLlmPreparation.Required(runtime, model) && stages.Any(p => p.Completed == 3), "Automatic LLM preparation failed");
            await File.WriteAllTextAsync(script, "throw 'must not run again'");
            await LocalLlmPreparation.EnsureAsync(runtime, model, true, null, CancellationToken.None);
            File.Delete(model);
            await File.WriteAllTextAsync(script, "Start-Sleep -Seconds 60");
            using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(500)); var elapsed = Stopwatch.StartNew();
            try { await LocalLlmPreparation.EnsureAsync(runtime, model, true, null, stop.Token); throw new Exception("Setup ignored cancel"); }
            catch (OperationCanceledException) { Check(elapsed.Elapsed.TotalSeconds < 5, "Setup cancellation too slow"); }
            Console.WriteLine("PASS: screen recording region; recording offsets/revisions/reused IDs, bilingual search, sidecar roundtrip/deletion, estimated Windows timing; automatic LLM script, reuse and cancellation without downloads");
        }
        finally { Directory.Delete(directory, true); }
    }
}
