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
            // Continuing a recording: the new part's text follows the first on one timeline, its audio is appended.
            {
                var firstDoc = new RecordingDocument(1, "会議.wav", DateTimeOffset.Now, false, [new(1, 1, 0, "最初", null, 1), new(2, 1, 4, "次", null, 1, EndSeconds: 5)], [new(4, "重要", 2)], [new("要点", "前半", [0])]);
                var secondDoc = new RecordingDocument(1, "会議.continue.wav", DateTimeOffset.Now, false, [new(2, 1, 1, "続き", null, 1, EndSeconds: 2, Words: [new(0, 2, 1, 2)]), new(3, 1, 3, "最後", null, 1)], [new(1, "続きの印", 2)]);
                var merged = RecordingContinuation.Append(firstDoc, secondDoc, 10);
                Check(merged.Phrases.Select(p => (p.Id, p.Seconds, p.Text)).SequenceEqual([(1L, 0d, "最初"), (2L, 4d, "次"), (3L, 11d, "続き"), (4L, 13d, "最後")])
                    && merged.Phrases[2].EndSeconds == 12 && merged.Phrases[2].Words![0] == new RecordingWord(0, 2, 11, 12)
                    && merged.Bookmarks!.Select(b => (b.Seconds, b.PhraseId)).SequenceEqual([(4d, 2L), (11d, 3L)]) && merged.Summary!.Length == 1 && merged.AudioFile == "会議.wav", "Continuation text was not appended on one timeline");
                string firstAudio = Path.Combine(directory, "続き.wav"), part = RecordingContinuation.PartPath(firstAudio, ".wav");
                foreach (var (file, seconds) in new[] { (firstAudio, 2), (part, 1) })
                    using (var writer = new NAudio.Wave.WaveFileWriter(file, new NAudio.Wave.WaveFormat(16000, 16, 1))) writer.Write(new byte[32000 * seconds], 0, 32000 * seconds);
                double offset = RecordingContinuation.AppendAudio(firstAudio, part);
                using (var reader = new NAudio.Wave.WaveFileReader(firstAudio)) Check(Math.Abs(offset - 2) < .01 && Math.Abs(reader.TotalTime.TotalSeconds - 3) < .01 && part.EndsWith("続き.continue.wav"), $"Continuation audio was not appended ({offset}s)");
                string mp3 = Path.Combine(directory, "続き.mp3");
                using (var reader = new NAudio.Wave.WaveFileReader(firstAudio))
                using (var resampler = new NAudio.Wave.MediaFoundationResampler(reader, new NAudio.Wave.WaveFormat(44100, 16, 1))) NAudio.Wave.MediaFoundationEncoder.EncodeToMp3(resampler, mp3, 96000);
                double mp3Offset = RecordingContinuation.AppendAudio(mp3, part);
                using (var reader = new NAudio.Wave.MediaFoundationReader(mp3)) Check(Math.Abs(mp3Offset - 3) < .15 && Math.Abs(reader.TotalTime.TotalSeconds - 4) < .2, $"MP3 continuation not appended ({mp3Offset}s, {reader.TotalTime.TotalSeconds}s)");
            }
            // A fragmented MP4 cut off while recording: incomplete fragment dropped, length written, index box added.
            {
                static byte[] Box(string type, params byte[][] parts) { var body = parts.SelectMany(p => p).ToArray(); var box = new byte[8 + body.Length]; System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length); System.Text.Encoding.ASCII.GetBytes(type).CopyTo(box, 4); body.CopyTo(box, 8); return box; }
                static byte[] U32(params uint[] values) { var bytes = new byte[values.Length * 4]; for (int i = 0; i < values.Length; i++) System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(i * 4), values[i]); return bytes; }
                byte[] mvhd = Box("mvhd", U32(0, 0, 0, 1000, 0), new byte[80]);
                byte[] trak = Box("trak", Box("tkhd", U32(0, 0, 0, 7), new byte[68]), Box("mdia", Box("mdhd", U32(0, 0, 0, 30000, 0), new byte[4])));
                byte[] moov = Box("moov", mvhd, trak, Box("mvex", Box("trex", U32(0, 7, 1, 1000, 0, 0))));
                byte[] Fragment(uint samples) => [.. Box("moof", Box("mfhd", U32(0, 1)), Box("traf", Box("tfhd", U32(0x8, 7, 1001)), Box("trun", U32(0x1, samples, 0)))), .. Box("mdat", new byte[16])];
                byte[] whole = [.. Box("ftyp", "isom"u8.ToArray(), U32(0)), .. moov, .. Fragment(30), .. Fragment(60), .. Fragment(90)];
                string cut = Path.Combine(directory, "途中.mp4"); File.WriteAllBytes(cut, whole[..^10]);
                bool repaired = Mp4Repair.Repair(cut); byte[] fixedBytes = File.ReadAllBytes(cut);
                int mvhdAt = whole.AsSpan().IndexOf("mvhd"u8) + 4;
                uint duration = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(fixedBytes.AsSpan(mvhdAt + 16));
                Check(repaired && duration == 3003 && fixedBytes.Length == whole.Length - Fragment(90).Length + 24 && fixedBytes.AsSpan(fixedBytes.Length - 20, 4).SequenceEqual("mfra"u8), $"Cut-off MP4 not repaired (duration {duration})");
                Check(Mp4Repair.Repair(cut) && File.ReadAllBytes(cut).Length == fixedBytes.Length, "Repaired MP4 changed again");
                string lost = Path.Combine(directory, "旧形式.mp4"); File.WriteAllBytes(lost, [.. Box("ftyp", "isom"u8.ToArray(), U32(0)), .. Box("mdat", new byte[64])[..40]]);
                string noFragment = Path.Combine(directory, "断片なし.mp4"); File.WriteAllBytes(noFragment, [.. Box("ftyp", "isom"u8.ToArray(), U32(0)), .. moov, .. Box("moof", Box("mfhd", U32(0, 1)))[..20]]);
                Check(!Mp4Repair.Repair(lost) && !Mp4Repair.Repair(noFragment) && SessionAutosave.RecoverRecording(cut) == cut && SessionAutosave.RecoverRecording(lost) is null, "Unrecoverable MP4 accepted");
            }
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
            Console.WriteLine("PASS: screen recording region; continuation text and audio (WAV, MP3); cut-off MP4 repair; recording offsets/revisions/reused IDs, bilingual search, sidecar roundtrip/deletion, estimated Windows timing; automatic LLM script, reuse and cancellation without downloads");
        }
        finally { Directory.Delete(directory, true); }
    }
}
