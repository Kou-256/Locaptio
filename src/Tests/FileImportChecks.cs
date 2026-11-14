using System.IO.Compression;
using Locaptio.Core;
using Locaptio.Core.AI;
using Locaptio.Core.Audio;
using NAudio.Wave;

internal static class FileImportChecks
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class Speech : ISpeechRecognitionBackend
    {
        public int Calls, Maximum;
        public Task<InferenceResult> RecognizeAsync(float[] samples, CancellationToken token, bool final = true)
        { token.ThrowIfCancellationRequested(); Calls++; Maximum = Math.Max(Maximum, samples.Length); return Task.FromResult(new InferenceResult("テスト発話 " + Calls, 1)); }
    }
    public static async Task RunAsync(bool real)
    {
        string directory = Path.Combine(Path.GetTempPath(), "Locaptio-file-import-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string wav = Path.Combine(directory, "音声.wav");
            using (var writer = new WaveFileWriter(wav, new WaveFormat(16000, 16, 1)))
                for (int i = 0; i < 26 * 16000; i++) writer.WriteSample(i < 16000 || i >= 24 * 16000 ? 0 : (float)(.2 * Math.Sin(i * 2 * Math.PI * 440 / 16000)));
            var formats = new List<string> { wav };
            using (var input = new AudioFileReader(wav)) MediaFoundationEncoder.EncodeToMp3(input, Path.Combine(directory, "test.mp3"));
            using (var input = new AudioFileReader(wav)) MediaFoundationEncoder.EncodeToAac(input, Path.Combine(directory, "test.m4a"));
            formats.AddRange(new[] { Path.Combine(directory, "test.mp3"), Path.Combine(directory, "test.m4a") });
            foreach (string format in formats)
            {
                var backend = new Speech(); List<RecordingPhrase> phrases = new(); double percent = 0;
                await FileTranscription.RunAsync(format, backend, "ja", phrase => { phrases.Add(phrase); return Task.CompletedTask; }, p => percent = p, CancellationToken.None);
                Check(backend.Calls >= 2 && backend.Maximum <= 192000 && percent == 100, "Bounded decoding/progress failed: " + format);
                Check(phrases[0].Seconds is >= .8 and <= 1.2 && phrases[1].Seconds > phrases[0].Seconds, "Media offsets failed");
                using var canceled = new CancellationTokenSource(); canceled.Cancel();
                try { await FileTranscription.RunAsync(format, backend, "ja", _ => Task.CompletedTask, _ => { }, canceled.Token); throw new Exception("Cancellation ignored"); }
                catch (OperationCanceledException) { }
            }
            var document = new RecordingDocument(1, "音声.wav", DateTimeOffset.Now, true, [new(1, 1, 1, "会議", null, 1)]);
            await RecordingTranscript.SaveAsync(wav, document); await RecordingLibrary.SaveTextAsync(wav, document);
            string unrelated = Path.Combine(directory, "keep.txt"); await File.WriteAllTextAsync(unrelated, "keep");
            string zip = Path.Combine(directory, "share.zip"); await RecordingFiles.ShareAsync(wav, zip);
            using (var archive = ZipFile.OpenRead(zip)) Check(archive.Entries.Count == 3 && archive.Entries.All(e => !e.FullName.Contains('/')), "Shared bundle contents failed");
            try { await RecordingFiles.ShareAsync(wav, zip); throw new Exception("Existing ZIP overwritten"); } catch (IOException) { }
            string moved = await RecordingFiles.MoveAsync(wav, Path.Combine(directory, "destination"));
            Check(!File.Exists(wav) && RecordingFiles.Bundle(moved).Length == 3 && File.Exists(unrelated), "Bundle move lost data");
            Check((await RecordingTranscript.LoadAsync(moved)).Phrases.Single().Text == "会議", "Moved sidecar cannot reopen");
            using (var canceled = new CancellationTokenSource())
            {
                canceled.Cancel();
                try { await RecordingFiles.MoveAsync(moved, directory, canceled.Token); throw new Exception("Move cancellation ignored"); } catch (OperationCanceledException) { }
                Check(RecordingFiles.Bundle(moved).Length == 3, "Canceled move changed source");
            }
            string runtime = Path.Combine(directory, "runtime"); Directory.CreateDirectory(Path.Combine(runtime, "scripts")); Directory.CreateDirectory(Path.Combine(runtime, "tools"));
            await File.WriteAllTextAsync(Path.Combine(runtime, "tools", "uv.exe"), "unused fixture");
            string script = Path.Combine(runtime, "scripts", "setup-speech.ps1");
            await File.WriteAllTextAsync(script, "param($Model,[switch]$Translation)\nNew-Item -ItemType Directory -Force '.venv/Scripts',('models/speech-'+$Model),'models/translation' | Out-Null\nSet-Content '.venv/Scripts/python.exe' fixture\nSet-Content '.venv/.locaptio-speech-v2' ready\nSet-Content ('models/speech-'+$Model+'/model.bin') fixture\nif ($Translation) { Set-Content 'models/translation/model.bin' fixture }\n");
            await SpeechPreparation.EnsureAsync(runtime, "small", true, "cpu", null, CancellationToken.None);
            Check(File.Exists(Path.Combine(runtime, "models", "speech-small", "model.bin")), "Automatic speech setup failed");
            await File.WriteAllTextAsync(script, "throw 'must not repeat'");
            await SpeechPreparation.EnsureAsync(runtime, "small", true, "cpu", null, CancellationToken.None);
            await File.WriteAllTextAsync(script, "Start-Sleep -Seconds 60");
            using (var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(500)))
                try { await SpeechPreparation.EnsureAsync(runtime, "base", false, "cpu", null, cancel.Token); throw new Exception("Setup cancellation ignored"); } catch (OperationCanceledException) { }
            if (real)
            {
                using var backend = new LocalBackend(); List<RecordingPhrase> recognized = new();
                await backend.StartAsync(Directory.GetCurrentDirectory(), "speech", true, CancellationToken.None, false, "ja", "small", device: "cpu");
                await FileTranscription.RunAsync(Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "test-ja.wav"), backend, "ja", phrase => { recognized.Add(phrase); return Task.CompletedTask; }, _ => { }, CancellationToken.None);
                Check(recognized.Count > 0 && string.Join("", recognized.Select(p => p.Text)).Contains("日本語"), "Real Japanese file transcription failed");
                Console.WriteLine("PASS: actual local Whisper Japanese file recognition");
            }
            Console.WriteLine("PASS: WAV/MP3/M4A real codecs; bounded PCM, offsets, progress, cancellation; ZIP no-overwrite; bundle move and cancellation; automatic speech script, reuse, cancellation");
        }
        finally { Directory.Delete(directory, true); }
    }
}
