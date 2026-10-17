using NAudio.Wave;
using NAudio.MediaFoundation;

namespace Locaptio.Core.Audio;
public sealed class AudioRecording(AudioInputSelection? input = null)
{
    public event Action<string>? Status;
    public async Task RunAsync(string path, CancellationToken token)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".wav" or ".mp3" or ".m4a")) throw new ArgumentException("録音形式はWAV、MP3、M4Aです。");
        string checkpoint = extension == ".wav" ? path : path + ".recording.wav";
        var recorder = new LoopbackRecorder(input); recorder.Status += message => Status?.Invoke(message);
        await recorder.RunAsync(checkpoint, token);
        if (extension == ".wav") return;
        Status?.Invoke("録音を" + extension + "に変換しています…");
        try
        {
            // Recording is already finalized; cancellation must not discard its final encoding.
            await Task.Run(() =>
            {
                MediaFoundationApi.Startup();
                using var reader = new WaveFileReader(checkpoint);
                using var resampler = new MediaFoundationResampler(reader, new WaveFormat(44100, 16, 1));
                if (extension == ".mp3") MediaFoundationEncoder.EncodeToMp3(resampler, path, 96000);
                else MediaFoundationEncoder.EncodeToAac(resampler, path, 64000);
            });
            File.Delete(checkpoint);
        }
        catch (Exception ex) { throw new IOException($"音声の変換に失敗しました。元の録音は {checkpoint} に残っています。{ex.Message}", ex); }
    }
}
