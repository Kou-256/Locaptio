using System.IO;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;

Console.OutputEncoding = new UTF8Encoding(false);
void Send(object data) { Console.WriteLine(JsonSerializer.Serialize(data)); Console.Out.Flush(); }
AutomationElement? window = null, captionText = null;
string last = ""; bool reported = false;
try
{
    while (true)
    {
        try
        {
            if (window is null)
            {
                foreach (var process in Process.GetProcessesByName("LiveCaptions"))
                {
                    using (process)
                    {
                        if (process.MainWindowHandle == 0) continue;
                        window = AutomationElement.FromHandle(process.MainWindowHandle); break;
                    }
                }
                if (window is not null)
                {
                    captionText = window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "CaptionsTextBlock"));
                    last = captionText?.Current.Name ?? "";
                    // Let an existing accessibility provider finish populating its
                    // initial text before excluding the pre-connection history.
                    Thread.Sleep(100);
                    captionText ??= window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "CaptionsTextBlock"));
                    last = captionText?.Current.Name ?? last;
                    // A blank caption window is connected too. Its first recognized
                    // text belongs to this session, rather than to preexisting history.
                    Send(new { type = "baseline", text = last }); reported = false;
                }
            }
            if (window is null)
            {
                if (!reported) Send(new { type = "status", text = "Windowsのライブキャプションを起動し、言語の準備を完了してください。接続を待っています。" });
                reported = true; Thread.Sleep(300); continue;
            }
            captionText ??= window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "CaptionsTextBlock"));
            string text = captionText?.Current.Name ?? last;
            if (text != last) { Send(new { type = "snapshot", text }); last = text; }
            else Send(new { type = "tick" });
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            captionText = null;
            try { _ = window?.Current.ProcessId; }
            catch { window = null; }
            if (!reported) Send(new { type = "status", text = "Windowsの字幕との接続を再確認しています。" });
            reported = true;
        }
        Thread.Sleep(100);
    }
}
catch (Exception ex) { Send(new { type = "error", text = ex.Message }); Environment.ExitCode = 1; }
