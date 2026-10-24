using System.Diagnostics;
using System.Runtime.InteropServices;
using Locaptio.Core;
using NAudio.CoreAudioApi;

namespace Locaptio.WinUI;

internal interface IWindowsCaptionWindow
{
    Task EnsureStartedAsync(CancellationToken token);
    Task RestartAsync(CancellationToken token);
}

internal sealed class WindowsCaptionRecovery(
    Func<bool> enabled, Action<string> status, Action<string> warning,
    Func<bool>? readAudio = null, IWindowsCaptionWindow? captionWindow = null, long? startedAt = null)
{
    private readonly WindowsCaptionWatchdog watchdog = new(startedAt ?? Environment.TickCount64);
    private readonly IWindowsCaptionWindow window = captionWindow ?? new WindowsCaptionWindow();
    private bool audioErrorReported;

    public Task EnsureStartedAsync(CancellationToken token) => window.EnsureStartedAsync(token);

    // True asks the session to replace its bridge too: a blocked accessibility
    // read in an old bridge must not prevent recovery or Stop.
    public async Task<bool> TickAsync(long now, bool textChanged, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!enabled()) { watchdog.ResetObservation(now); return false; }
        bool audible;
        try
        {
            audible = (readAudio ?? ReadSystemAudio)(); audioErrorReported = false;
        }
        catch (Exception ex)
        {
            watchdog.ResetObservation(now);
            if (!audioErrorReported) status("Windows字幕の自動復旧: 音量を確認できないため判定を保留します。" + ex.Message);
            audioErrorReported = true; return false;
        }
        switch (watchdog.Observe(now, audible, textChanged))
        {
            case CaptionRecoveryDecision.LimitReached:
                warning("Windows字幕の自動復旧を10分間で3回試しましたが、音声があっても字幕が更新されません。自動再起動を一時停止します。Windows側の認識言語・音声入力を確認してください。");
                return false;
            case CaptionRecoveryDecision.Restart:
                warning("PC音声があるのに20秒以上字幕が更新されないため、Windowsライブキャプションを再起動しています。文章と録音は保持します。");
                try
                {
                    await window.RestartAsync(token);
                    status("Windowsライブキャプションのウィンドウを再表示しました。字幕の再接続を待っています。");
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex) { warning("Windowsライブキャプションを再表示できませんでした。" + ex.Message + " Windows側の言語設定・初回設定を確認してください。"); }
                return true;
            default: return false;
        }
    }

    private static bool ReadSystemAudio()
    {
        // Peak metering uses no loopback buffers or inference and follows output
        // device changes. Audio is neither retained nor written to disk.
        using var devices = new MMDeviceEnumerator();
        using var output = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        return output.AudioMeterInformation.MasterPeakValue >= .004f;
    }
}

internal sealed class WindowsCaptionWindow : IWindowsCaptionWindow
{
    private static string Executable => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "LiveCaptions.exe");
    private static readonly int Session = Process.GetCurrentProcess().SessionId;
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindowAsync(nint hwnd, int command);

    private static bool Visible(bool restore)
    {
        bool visible = false;
        foreach (var process in Process.GetProcessesByName("LiveCaptions"))
        {
            using (process)
            {
                try
                {
                    if (process.SessionId != Session) continue;
                    process.Refresh(); nint handle = process.MainWindowHandle;
                    if (handle == 0) continue;
                    if (restore && (!IsWindowVisible(handle) || IsIconic(handle))) ShowWindowAsync(handle, 9);
                    visible |= IsWindowVisible(handle) && !IsIconic(handle);
                }
                catch (InvalidOperationException) { }
            }
        }
        return visible;
    }

    private static void Launch(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // Launch the Windows app through the shell with an explicit normal show
        // state, even when Locaptio itself was minimized or started hidden.
        Process.Start(new ProcessStartInfo(Executable) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Normal })?.Dispose();
    }

    private static async Task<bool> WaitVisibleAsync(int milliseconds, CancellationToken token)
    {
        long until = Environment.TickCount64 + milliseconds;
        do
        {
            token.ThrowIfCancellationRequested();
            if (Visible(true)) return true;
            await Task.Delay(250, token);
        } while (Environment.TickCount64 < until);
        return false;
    }

    public async Task EnsureStartedAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Visible(true)) return;
        Launch(token);
        if (!await WaitVisibleAsync(4000, token)) await RestartAsync(token);
    }

    public async Task RestartAsync(CancellationToken token)
    {
        // Only the current Windows session's genuine OS executable. Do not kill
        // a process tree: recording and Locaptio workers are independent.
        foreach (var process in Process.GetProcessesByName("LiveCaptions"))
        {
            using (process)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (process.SessionId != Session || !string.Equals(process.MainModule?.FileName, Executable, StringComparison.OrdinalIgnoreCase)) continue;
                    process.Kill();
                    await process.WaitForExitAsync(token).WaitAsync(TimeSpan.FromSeconds(3), token);
                }
                catch (InvalidOperationException) { /* Already exited. */ }
            }
        }
        Launch(token);
        if (!await WaitVisibleAsync(10_000, token)) throw new TimeoutException("起動後10秒以内にウィンドウを確認できませんでした。");
    }
}
