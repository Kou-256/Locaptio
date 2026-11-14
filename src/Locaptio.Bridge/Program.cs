using System.IO;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;
using System.Runtime.InteropServices;
using Locaptio.Bridge;

Console.OutputEncoding = new UTF8Encoding(false);
// Verification fixture: the session watchdog must recover even when no bridge
// messages arrive. Normal invocations never use this argument.
if (args.Contains("--verify-blocked-pipe")) { Thread.Sleep(Timeout.Infinite); return; }
object outputLock = new();
void Send(object data) { lock (outputLock) { Console.WriteLine(JsonSerializer.Serialize(data)); Console.Out.Flush(); } }
AutomationElement? window = null, captionText = null;
string last = ""; bool reported = false;
var connection = new CaptionConnectionMonitor();
nint windowHandle = 0;
string? requestedLanguage = args.SkipWhile(a => a != "--language").Skip(1).FirstOrDefault();
bool languageHandled = requestedLanguage is null;
string? microphoneArgument = args.SkipWhile(a => a != "--microphone").Skip(1).FirstOrDefault();
bool? includeMicrophone = microphoneArgument is "on" ? true : microphoneArgument is "off" ? false : null;
nint microphoneAppliedWindow = 0;
bool? appliedMicrophone = null;
long pendingMicrophoneRequest = 0;
var baselineBoundary = new CaptionBaselineBoundary();
var commands = new ConcurrentQueue<(long Request, bool Enabled)>();
_ = Task.Run(async () =>
{
    while (await Console.In.ReadLineAsync() is { } line)
    {
        if (line.Length > 1024) continue;
        try
        {
            using var command = JsonDocument.Parse(line);
            var data = command.RootElement;
            if (data.GetProperty("type").GetString() == "microphone" && commands.Count < 4)
                commands.Enqueue((data.GetProperty("request").GetInt64(), data.GetProperty("enabled").GetBoolean()));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { }
    }
});
var languageRecovery = new CaptionLanguageRecoveryState(requestedLanguage ?? "");
long lastLanguageObservation = 0;
void ObserveManualLanguage(bool captionUpdated = false)
{
    if (!languageRecovery.WarningPending || windowHandle == 0) return;
    if (!captionUpdated && Environment.TickCount64 - lastLanguageObservation < 1000) return;
    lastLanguageObservation = Environment.TickCount64;
    string? label = new CaptionLanguageUi(windowHandle).ReadyLabel;
    if (languageRecovery.Observe(label, captionUpdated) is { } type)
        Send(new { type, text = type == "language-ready" ? "手動変更後のWindows字幕言語を確認しました。" : "Windows字幕の更新が再開しました。手動で選択した言語を使用します。" });
}
void ObserveConnection(bool available)
{
    if (connection.Observe(available, Environment.TickCount64))
        Send(new { type = "disconnected", text = "Windowsライブキャプションが閉じられたか、接続が切れました。文字起こしを中断しています。Windowsライブキャプションを起動し直すと、自動で再接続します。" });
}
try
{
    while (true)
    {
        try
        {
            while (commands.TryDequeue(out var command))
            {
                includeMicrophone = command.Enabled; pendingMicrophoneRequest = command.Request;
            }
            if (window is not null && (!NativeWindow.IsWindow(windowHandle) || !NativeWindow.IsWindowVisible(windowHandle)))
            {
                window = captionText = null; windowHandle = 0;
            }
            if (window is null)
            {
                foreach (var process in Process.GetProcessesByName("LiveCaptions"))
                {
                    using (process)
                    {
                        nint handle = process.MainWindowHandle;
                        if (handle == 0 || !NativeWindow.IsWindowVisible(handle)) continue;
                        window = AutomationElement.FromHandle(handle); windowHandle = handle; break;
                    }
                }
                if (window is not null)
                {
                    string processIdentity;
                    using (var nativeProcess = Process.GetProcessById(window.Current.ProcessId)) processIdentity = nativeProcess.Id + ":" + nativeProcess.StartTime.ToUniversalTime().Ticks;
                    if (baselineBoundary.Begin(processIdentity))
                    {
                        captionText = window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "CaptionsTextBlock"));
                        last = captionText?.Current.Name ?? "";
                        Send(new { type = "baseline", text = last });
                        languageHandled = requestedLanguage is null;
                    }
                    if (!languageHandled)
                    {
                        languageHandled = true;
                        Send(new { type = "status", text = "Windows字幕の言語を本体のモードに合わせています…" });
                        var operation = Task.Run(() => CaptionLanguagePolicy.Synchronize(new CaptionLanguageUi(windowHandle), requestedLanguage!));
                        // UI Automation providers can hang even on a read. Bound
                        // the operation independently of the audible-stall watchdog.
                        if (!operation.Wait(TimeSpan.FromSeconds(20)))
                        {
                            Send(new { type = "language-error", text = "Windows字幕の言語操作がタイムアウトしました。Windows側で言語を選択して続行し、LocaptioでStartし直してください。" });
                            Send(new { type = "error", text = "Windowsの言語画面を確認してください。Stop／終了はすぐに実行できます。" });
                            Environment.Exit(1);
                        }
                        var result = operation.GetAwaiter().GetResult();
                        if (!result.Success) languageRecovery.Failed(); else languageRecovery.Verified();
                        Send(new { type = result.Success ? "language-ready" : "language-error", text = result.Message });
                        window = AutomationElement.FromHandle(windowHandle);
                    }
                }
            }
            if (window is not null && includeMicrophone is bool desired &&
                (microphoneAppliedWindow != windowHandle || appliedMicrophone != desired || pendingMicrophoneRequest != 0))
            {
                if (microphoneAppliedWindow != windowHandle || appliedMicrophone != desired)
                {
                    var operation = Task.Run(() => CaptionMicrophonePolicy.Synchronize(new CaptionMicrophoneUi(windowHandle), desired));
                    try
                    {
                        if (!operation.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("マイク設定の変更がタイムアウトしました。");
                        operation.GetAwaiter().GetResult(); microphoneAppliedWindow = windowHandle; appliedMicrophone = desired;
                    }
                    catch (Exception ex)
                    {
                        Send(new { type = "microphone-error", request = pendingMicrophoneRequest, text = "Windows字幕のマイク設定を" + (desired ? "ON" : "OFF") + "にできませんでした。" + ex.GetBaseException().Message });
                        Environment.Exit(1);
                    }
                    captionText = null;
                }
                Send(new { type = "microphone-ready", request = pendingMicrophoneRequest, enabled = desired });
                pendingMicrophoneRequest = 0;
            }
            if (window is null)
            {
                ObserveConnection(false);
                if (!reported) Send(new { type = "status", text = "Windowsのライブキャプションを起動し、言語の準備を完了してください。接続を待っています。" });
                reported = true; Thread.Sleep(300); continue;
            }
            captionText ??= window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "CaptionsTextBlock"));
            ObserveManualLanguage();
            if (captionText is null)
            {
                if (window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "ReadyToCaptionTextBlock")) is not null)
                {
                    ObserveConnection(true);
                    reported = false; Send(new { type = "tick" });
                    Thread.Sleep(100); continue;
                }
                ObserveConnection(false);
                window = null; windowHandle = 0;
                Thread.Sleep(100); continue;
            }
            string text = captionText.Current.Name;
            ObserveConnection(true);
            if (text != last) { ObserveManualLanguage(!string.IsNullOrWhiteSpace(text)); Send(new { type = "snapshot", text }); last = text; }
            else Send(new { type = "tick" });
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            window = captionText = null; windowHandle = 0;
            ObserveConnection(false);
            if (!reported) Send(new { type = "status", text = "Windowsの字幕との接続を再確認しています。" });
            reported = true;
        }
        Thread.Sleep(100);
    }
}
catch (Exception ex) { Send(new { type = "error", text = ex.Message }); Environment.ExitCode = 1; }

internal static class NativeWindow
{
    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        internal uint Size, Flags;
        internal nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        internal int Left, Top, Right, Bottom;
    }
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint process);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    internal static void DismissMenu(nint window)
    {
        uint thread = GetWindowThreadProcessId(window, out uint process);
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        nint target = window;
        // WinUI routes keyboard input through its focused child HWND. Posting to
        // the top-level window leaves the XAML flyout open. Keep all messages
        // inside this Live Captions process, even when another app is foreground.
        if (GetGUIThreadInfo(thread, ref info) && info.Focus != 0)
        {
            GetWindowThreadProcessId(info.Focus, out uint focusedProcess);
            if (focusedProcess == process) target = info.Focus;
        }
        PostMessage(target, 0x0100, 0x1B, 1);
        PostMessage(target, 0x0101, 0x1B, unchecked((nint)0xC0000001));
    }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool PostMessage(nint hwnd, uint message, nint wparam, nint lparam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowVisible(nint hwnd);
}
