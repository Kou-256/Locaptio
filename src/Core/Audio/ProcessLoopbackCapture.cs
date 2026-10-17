using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;
using NAudio.Wasapi.CoreAudioApi.Interfaces;

namespace Locaptio.Core.Audio;
/// <summary>WASAPI virtual process loopback; includes the selected application's children.</summary>
public sealed class ProcessLoopbackCapture(AudioInputSelection selection) : IWaveIn
{
    public WaveFormat WaveFormat { get; set; } = new(48000, 16, 2);
    public event EventHandler<WaveInEventArgs>? DataAvailable;
    public event EventHandler<StoppedEventArgs>? RecordingStopped;
    private Thread? thread;
    private readonly CancellationTokenSource stop = new();
    private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Started => started.Task;
    public void StartRecording()
    {
        using var process = Process.GetProcessById(selection.ProcessId);
        if (process.HasExited || (selection.ProcessStarted != 0 && process.StartTime.ToUniversalTime().Ticks != selection.ProcessStarted)) throw new InvalidOperationException("選択したアプリは終了しています。音声入力を選び直してください。");
        if (thread is not null) throw new InvalidOperationException("すでに録音しています。");
        thread = new Thread(Capture) { IsBackground = true, Name = "Application audio" }; thread.Start();
    }
    public void StopRecording() => stop.Cancel();
    public void Dispose() { StopRecording(); thread?.Join(); stop.Dispose(); }
    private void Capture()
    {
        Exception? error = null;
        try
        {
            using var client = Activate(selection.ProcessId, stop.Token);
            using var signal = new AutoResetEvent(false);
            client.Initialize(AudioClientShareMode.Shared, AudioClientStreamFlags.Loopback | AudioClientStreamFlags.EventCallback, 200000, 0, WaveFormat, Guid.Empty);
            client.SetEventHandle(signal.SafeWaitHandle.DangerousGetHandle());
            var capture = client.AudioCaptureClient; client.Start(); started.TrySetResult();
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    signal.WaitOne(20);
                    while (!stop.IsCancellationRequested && capture.GetNextPacketSize() > 0)
                    {
                        var pointer = capture.GetBuffer(out int frames, out AudioClientBufferFlags flags);
                        try
                        {
                            var data = new byte[frames * WaveFormat.BlockAlign];
                            if ((flags & AudioClientBufferFlags.Silent) == 0) Marshal.Copy(pointer, data, 0, data.Length);
                            DataAvailable?.Invoke(this, new(data, data.Length));
                        }
                        finally { capture.ReleaseBuffer(frames); }
                    }
                }
            }
            finally { client.Stop(); }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { started.TrySetCanceled(); }
        catch (Exception ex) { error = ex; started.TrySetException(ex); }
        finally { RecordingStopped?.Invoke(this, new(error)); }
    }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct BlobVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public uint Length;
        [FieldOffset(16)] public nint Data;
    }
    [DllImport("Mmdevapi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int ActivateAudioInterfaceAsync(string path, in Guid iid, in BlobVariant parameters, IActivateAudioInterfaceCompletionHandler callback, out IActivateAudioInterfaceAsyncOperation operation);
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class Completion : IActivateAudioInterfaceCompletionHandler
    {
        public readonly TaskCompletionSource<AudioClient> Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation)
        {
            try { operation.GetActivateResult(out int result, out object instance); Marshal.ThrowExceptionForHR(result); Ready.TrySetResult(new((IAudioClient)instance)); }
            catch (Exception ex) { Ready.TrySetException(ex); }
        }
    }
    private static AudioClient Activate(int processId, CancellationToken token)
    {
        nint data = Marshal.AllocHGlobal(12);
        bool deferred = false;
        try
        {
            Marshal.WriteInt32(data, 0, 1); Marshal.WriteInt32(data, 4, processId); Marshal.WriteInt32(data, 8, 0);
            var parameters = new BlobVariant { Type = 65, Length = 12, Data = data };
            var callback = new Completion(); var iid = typeof(IAudioClient).GUID;
            Marshal.ThrowExceptionForHR(ActivateAudioInterfaceAsync("VAD\\Process_Loopback", iid, parameters, callback, out var operation));
            try { return callback.Ready.Task.WaitAsync(TimeSpan.FromSeconds(10), token).GetAwaiter().GetResult(); }
            catch
            {
                // The native callback may arrive after cancellation. Keep its parameters alive
                // until completion, but let the capture thread and app close immediately.
                deferred = true;
                _ = callback.Ready.Task.ContinueWith(completed =>
                {
                    if (completed.IsCompletedSuccessfully) completed.Result.Dispose();
                    else _ = completed.Exception;
                    Marshal.ReleaseComObject(operation); Marshal.FreeHGlobal(data); GC.KeepAlive(callback);
                }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
                throw;
            }
            finally { if (!deferred) { Marshal.ReleaseComObject(operation); GC.KeepAlive(callback); } }
        }
        finally { if (!deferred) Marshal.FreeHGlobal(data); }
    }
}
