using Locaptio.Core.Audio;
using System.Diagnostics;
using NAudio.Wave;
using Locaptio.Core;
using Locaptio.Core.AI;

if (args.Contains("--pipeline"))
{
    string root = Directory.GetCurrentDirectory();
    using var pipelineStop = new CancellationTokenSource();
    using var speech = new LocalBackend();
    using var translation = new LocalBackend();
    await speech.StartAsync(root, "speech", args.Contains("--cpu"), pipelineStop.Token);
    await translation.StartAsync(root, "translation", args.Contains("--cpu"), pipelineStop.Token);
    pipelineStop.CancelAfter(TimeSpan.FromSeconds(int.Parse(args[0])));
    Console.WriteLine($"Speech: {speech.Backend}; Translate: {translation.Backend}");
    var pipeline = new Pipeline(speech, translation);
    int english = 0, japanese = 0;
    pipeline.Caption += caption =>
    {
        if (caption.Japanese is null) english++; else japanese++;
        Console.WriteLine($"{caption.Id}/{caption.Revision} final={caption.Final} EN={caption.English} JA={caption.Japanese} STT={caption.SttMs:F0}ms MT={caption.TranslationMs:F0}ms Total={Stopwatch.GetElapsedTime(caption.Started).TotalMilliseconds:F0}ms");
    };
    pipeline.Status += Console.WriteLine;
    var running = pipeline.RunAsync(pipelineStop.Token);
    Task? playback = null;
    if (args.Contains("--fixture"))
    {
        playback = Task.Run(async () =>
        {
            await Task.Delay(800, pipelineStop.Token);
            using var reader = new WaveFileReader(Path.Combine(root, "artifacts", "test.wav"));
            using var output = new WaveOutEvent();
            output.Init(reader); output.Play();
            while (output.PlaybackState == PlaybackState.Playing) await Task.Delay(100, pipelineStop.Token);
        });
    }
    try { await running; } catch (OperationCanceledException) { }
    if (playback is not null) try { await playback; } catch (OperationCanceledException) { }
    Console.WriteLine($"Completed: English updates={english}, Japanese updates={japanese}, dropped={pipeline.DroppedAudio}");
    if (args.Contains("--fixture") && japanese == 0) Environment.ExitCode = 1;
    return;
}

using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(args.Length > 0 ? int.Parse(args[0]) : 10));
using var source = new LoopbackSource();
source.Status += Console.WriteLine;
var capture = source.RunAsync(stop.Token);
NAudio.Wave.WaveOutEvent? tone = null;
if (args.Contains("--tone"))
{
    tone = new NAudio.Wave.WaveOutEvent();
    tone.Init(new NAudio.Wave.SampleProviders.SignalGenerator(48000, 2) { Frequency = 440, Gain = 0.05 });
    tone.Play();
}
long samples = 0; double peak = 0;
try
{
    await foreach (var frame in source.Frames.ReadAllAsync(stop.Token))
    {
        samples += frame.Samples.Length;
        peak = Math.Max(peak, frame.Samples.Max(v => Math.Abs(v)));
    }
}
catch (OperationCanceledException) { }
try { await capture; } catch (OperationCanceledException) { }
tone?.Dispose();
Console.WriteLine($"Captured samples={samples}; seconds={samples / 16000d:F2}; peak={peak:F4}; dropped={source.DroppedFrames}");
if (samples == 0) Console.WriteLine("無音または音声が再生されていません。音声再生中に再実行してください。");
