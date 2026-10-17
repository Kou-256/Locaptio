using System.Diagnostics;
using Locaptio.Core.Audio;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

internal static class MixedInputChecks
{
    public static async Task RunAsync()
    {
        var mixer = new AudioMixBuffer(320);
        mixer.Add(0, Enumerable.Repeat(.2f, 320).ToArray());
        mixer.Add(1, Enumerable.Repeat(.6f, 320).ToArray());
        if (mixer.Read(320).Any(v => Math.Abs(v - .4f) > .0001)) throw new Exception("Sources were not added on a common timeline");
        mixer.Add(1, Enumerable.Repeat(.8f, 320).ToArray());
        if (mixer.Read(320).Any(v => Math.Abs(v - .4f) > .0001) || mixer.Read(320).Any(v => v != 0)) throw new Exception("Silent source stalled/repeated audio");
        mixer.Add(0, Enumerable.Repeat(-.8f, 320).Concat(Enumerable.Repeat(.6f, 320)).ToArray());
        if (mixer.DroppedSamples != 320 || mixer.Read(320).Any(v => Math.Abs(v - .3f) > .0001)) throw new Exception("Overflow kept stale audio or grew unbounded");
        mixer.Add(0, Enumerable.Repeat(1f, 320).ToArray()); mixer.Add(1, Enumerable.Repeat(1f, 320).ToArray());
        if (mixer.Read(320).Any(v => v != 1)) throw new Exception("Full scale inputs clipped");
        mixer.Add(1, Enumerable.Repeat(.6f, 320).ToArray()); mixer.Clear(1);
        if (mixer.Read(320).Any(v => v != 0)) throw new Exception("Device reconnect retained old packets");
        if (!AudioInputCatalog.List().Any(c => c.Selection.Kind == "mixed")) throw new Exception("Mixed audio selection missing");
        Console.WriteLine("PASS: simultaneous summation, one-sided silence, no replay, bounded overflow/latest audio, clipping headroom, reconnect flush, UI catalog");
        using var source = new LoopbackSource(new("mixed")); using var stop = new CancellationTokenSource();
        var running = source.RunAsync(stop.Token);
        int frames = 0; double peak = 0;
        var consumer = Task.Run(async () =>
        {
            await foreach (var frame in source.Frames.ReadAllAsync())
            {
                if (frame.Samples.Length != 320) throw new Exception("Mixed frames have wrong length");
                frames++; foreach (float v in frame.Samples) peak = Math.Max(peak, Math.Abs(v));
            }
        });
        using var output = new WaveOutEvent(); output.Init(new SignalGenerator(48000, 2) { Frequency = 440, Gain = .08 });
        await Task.Delay(400); output.Play(); await Task.Delay(1600); output.Stop();
        var stopping = Stopwatch.StartNew(); stop.Cancel();
        try { await running.WaitAsync(TimeSpan.FromSeconds(5)); } catch (OperationCanceledException) { }
        await consumer;
        if (frames < 50 || source.SystemSamplesCaptured < 8000 || source.MicrophoneSamplesCaptured < 8000 || peak < .005 || stopping.Elapsed.TotalSeconds > 3) throw new Exception($"Actual mixed capture failed: frames={frames}, system={source.SystemSamplesCaptured}, mic={source.MicrophoneSamplesCaptured}, peak={peak}");
        Console.WriteLine($"PASS: real simultaneous WASAPI PC+mic: frames={frames}, systemSamples={source.SystemSamplesCaptured}, microphoneSamples={source.MicrophoneSamplesCaptured}, peak={peak:F3}, stop={stopping.Elapsed.TotalMilliseconds:F0}ms, drops={source.DroppedFrames}");
        string folder = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "mixed-input"); Directory.CreateDirectory(folder);
        foreach (string format in new[] { "wav", "mp3", "m4a" })
        {
            string path = Path.Combine(folder, "mixed." + format); using var recordingStop = new CancellationTokenSource();
            var recording = new AudioRecording(new("mixed")).RunAsync(path, recordingStop.Token);
            await Task.Delay(350); output.Play(); await Task.Delay(1200); output.Stop(); recordingStop.Cancel(); await recording.WaitAsync(TimeSpan.FromSeconds(10));
            using var reader = new AudioFileReader(path); float[] buffer = new float[4096]; float recordedPeak = 0; int count;
            while ((count = reader.Read(buffer, 0, buffer.Length)) > 0) for (int i = 0; i < count; i++) recordedPeak = Math.Max(recordedPeak, Math.Abs(buffer[i]));
            if (recordedPeak < .005 || reader.TotalTime.TotalSeconds < 1.4 || reader.TotalTime.TotalSeconds > 2.5 || File.Exists(path + ".recording.wav")) throw new Exception("Mixed recording corrupt/doubled timeline: " + format);
            Console.WriteLine($"PASS: mixed {format}: duration={reader.TotalTime.TotalSeconds:F2}s, peak={recordedPeak:F3}, encoding finalized");
        }
    }
}
