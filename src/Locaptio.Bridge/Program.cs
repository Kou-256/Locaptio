using System.IO;
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
void Send(object data) { Console.WriteLine(JsonSerializer.Serialize(data)); Console.Out.Flush(); }
AutomationElement? window = null, captionText = null;
string last = ""; bool reported = false, needsBaseline = true;
var connection = new CaptionConnectionMonitor();
nint windowHandle = 0;
string? requestedLanguage = args.SkipWhile(a => a != "--language").Skip(1).FirstOrDefault();
bool languageHandled = requestedLanguage is null;
string? microphoneArgument = args.SkipWhile(a => a != "--microphone").Skip(1).FirstOrDefault();
bool? includeMicrophone = microphoneArgument is "on" ? true : microphoneArgument is "off" ? false : null;
nint microphoneAppliedWindow = 0;
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
                    if (includeMicrophone is bool desired && microphoneAppliedWindow != windowHandle)
                    {
                        var operation = Task.Run(() => CaptionMicrophonePolicy.Synchronize(new CaptionMicrophoneUi(windowHandle), desired));
                        try
                        {
                            if (!operation.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("マイク設定の変更がタイムアウトしました。");
                            operation.GetAwaiter().GetResult(); microphoneAppliedWindow = windowHandle;
                            Send(new { type = "status", text = "Windows字幕の「マイクオーディオを含める」を" + (desired ? "ON" : "OFF") + "にしました。" });
                        }
                        catch (Exception ex)
                        {
                            Send(new { type = "error", text = "Windows字幕のマイク設定を" + (desired ? "ON" : "OFF") + "にできませんでした。Windows側の「設定 → マイクオーディオを含める」を確認し、Startで再試行してください。" + ex.GetBaseException().Message });
                            Environment.Exit(1);
                        }
                    }
                    needsBaseline = true;
                    captionText = window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "CaptionsTextBlock"));
                    last = captionText?.Current.Name ?? "";
                    // Let an existing accessibility provider finish populating its
                    // initial text before excluding the pre-connection history.
                    Thread.Sleep(100);
                    captionText ??= window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "CaptionsTextBlock"));
                    last = captionText?.Current.Name ?? last;
                }
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
                    if (needsBaseline) { needsBaseline = false; last = ""; reported = false; Send(new { type = "baseline", text = "" }); }
                    else Send(new { type = "tick" });
                    Thread.Sleep(100); continue;
                }
                ObserveConnection(false);
                window = null; windowHandle = 0;
                Thread.Sleep(100); continue;
            }
            string text = captionText.Current.Name;
            ObserveConnection(true);
            // Baseline only after the provider is ready, including a blank one.
            if (needsBaseline)
            {
                last = text; needsBaseline = false; reported = false;
                Send(new { type = "baseline", text });
                continue;
            }
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
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowVisible(nint hwnd);
}
