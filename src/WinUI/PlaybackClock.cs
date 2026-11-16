using NAudio.Wave;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace Locaptio.WinUI;

/// <summary>
/// The position source for replaying a recording: audio files play through NAudio, video files
/// through the Windows media player shown in the main window. Transcript highlighting, click-to-play,
/// bookmarks and captions all read and set the position through this one interface.
/// </summary>
internal abstract class PlaybackClock : IDisposable
{
    public abstract TimeSpan CurrentTime { get; set; }
    public abstract TimeSpan TotalTime { get; }
    public abstract PlaybackState PlaybackState { get; }
    public abstract void Play();
    public abstract void Pause();
    public abstract void Dispose();

    public static readonly string[] AudioExtensions = [".wav", ".mp3", ".m4a"];
    public static readonly string[] VideoExtensions = [".mp4", ".m4v", ".mov", ".wmv", ".avi", ".mkv"];
    public static bool IsVideo(string path) => VideoExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());
}

internal sealed class AudioPlaybackClock : PlaybackClock
{
    private readonly AudioFileReader reader;
    private readonly WaveOutEvent output = new();
    public AudioPlaybackClock(string path) { reader = new AudioFileReader(path); output.Init(reader); }
    public override TimeSpan CurrentTime { get => reader.CurrentTime; set => reader.CurrentTime = value; }
    public override TimeSpan TotalTime => reader.TotalTime;
    public override PlaybackState PlaybackState => output.PlaybackState;
    public override void Play() => output.Play();
    public override void Pause() => output.Pause();
    public override void Dispose() { output.Stop(); output.Dispose(); reader.Dispose(); }
}

internal sealed class VideoPlaybackClock : PlaybackClock
{
    public MediaPlayer Player { get; } = new() { AutoPlay = false };
    private readonly TimeSpan duration;
    public VideoPlaybackClock(string path)
    {
        // The audio track gives the duration at once; the player reports it only after opening.
        // A video recorded without sound has no audio track; its length then comes from the player once opened.
        try { using var probe = new MediaFoundationReader(path); duration = probe.TotalTime; } catch (Exception) { duration = TimeSpan.Zero; }
        Player.Source = MediaSource.CreateFromUri(new Uri(path));
    }
    public override TimeSpan CurrentTime { get => Player.PlaybackSession.Position; set => Player.PlaybackSession.Position = value; }
    public override TimeSpan TotalTime => Player.PlaybackSession.NaturalDuration > TimeSpan.Zero ? Player.PlaybackSession.NaturalDuration : duration;
    public override PlaybackState PlaybackState => Player.PlaybackSession.PlaybackState switch
    {
        MediaPlaybackState.Playing or MediaPlaybackState.Buffering or MediaPlaybackState.Opening => PlaybackState.Playing,
        MediaPlaybackState.Paused => PlaybackState.Paused,
        _ => PlaybackState.Stopped
    };
    public override void Play() => Player.Play();
    public override void Pause() => Player.Pause();
    public override void Dispose() { Player.Pause(); Player.Source = null; Player.Dispose(); }
}
