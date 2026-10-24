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
            if (captionText is null)
            {
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
            if (text != last) { Send(new { type = "snapshot", text }); last = text; }
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
