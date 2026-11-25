using System.Diagnostics;
using System.Text;

namespace Locaptio.WinUI;

/// <summary>
/// Crash report: %LOCALAPPDATA%\Locaptio\logs\crash.txt with the version, Windows, the exception and the last
/// actions and status messages before it (WinUI layout failures carry no stack, so the trail is what explains them).
/// The next launch shows it once and offers to copy it or open the folder; nothing is sent anywhere.
/// </summary>
internal static class CrashReport
{
    private static readonly Queue<string> trail = new();
    private static readonly object sync = new();
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Locaptio", "logs");
    public static string FilePath => Path.Combine(Folder, "crash.txt");
    private static string SeenPath => Path.Combine(Folder, "crash.seen");

    /// <summary>Remembers a recent action or status line (the last 40 are kept in memory only).</summary>
    public static void Note(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        lock (sync) { trail.Enqueue($"{DateTime.Now:HH:mm:ss} {text.ReplaceLineEndings(" ")}"); while (trail.Count > 40) trail.Dequeue(); }
    }

    public static void Write(Exception? exception, string kind)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var text = new StringBuilder();
            text.AppendLine($"Locaptio crash report — {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} ({kind})");
            text.AppendLine($"Version: {typeof(CrashReport).Assembly.GetName().Version}  Windows: {Environment.OSVersion.Version}  64-bit: {Environment.Is64BitProcess}");
            text.AppendLine();
            text.AppendLine(exception is null ? "(no exception object)" : $"{exception.GetType().FullName} (HRESULT 0x{exception.HResult:X8}): {exception.Message}");
            if (exception is not null) text.AppendLine(exception.StackTrace ?? "(no stack: the failure happened inside WinUI)");
            if (exception?.InnerException is { } inner) text.AppendLine("Inner: " + inner);
            text.AppendLine();
            text.AppendLine("Recent actions and status:");
            lock (sync) foreach (string line in trail) text.AppendLine("  " + line);
            File.WriteAllText(FilePath, text.ToString(), new UTF8Encoding(false));
        }
        catch (Exception) { }
    }

    /// <summary>A crash report not shown yet (written after the last time the notice was shown).</summary>
    public static string? Unseen()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var written = File.GetLastWriteTimeUtc(FilePath);
            if (File.Exists(SeenPath) && File.GetLastWriteTimeUtc(SeenPath) >= written) return null;
            return File.ReadAllText(FilePath);
        }
        catch (Exception) { return null; }
    }
    public static void MarkSeen() { try { Directory.CreateDirectory(Folder); File.WriteAllText(SeenPath, DateTimeOffset.Now.ToString("O")); } catch (Exception) { } }
    public static void OpenFolder() { try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{FilePath}\"") { UseShellExecute = true }); } catch (Exception) { } }
}

public sealed partial class MainWindow
{
    /// <summary>On launch: shows a crash report written since the last time one was shown.</summary>
    private void ShowCrashNoticeIfAny()
    {
        if (VerificationRun || CrashReport.Unseen() is null) return;
        CrashNotice.IsOpen = true; CrashReport.MarkSeen();
    }
    private async void CrashReportClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (dialogOpen || closing) return;
        string text = File.Exists(CrashReport.FilePath) ? File.ReadAllText(CrashReport.FilePath) : "クラッシュレポートはありません（Locaptio が予期せず終了したことはありません）。";
        dialogOpen = true;
        try
        {
            var box = new Microsoft.UI.Xaml.Controls.TextBox { Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap, Height = 320, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), FontSize = 12 };
            var panel = new Microsoft.UI.Xaml.Controls.StackPanel { Spacing = 8 };
            panel.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock { TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap, Text = "不具合の報告に使う場合は「コピー」して、GitHub の Issues（ヘルプ →「問題を報告」）に貼り付けてください。ファイルのパスなどが含まれるので、必要に応じて書き換えてから送ってください。" });
            panel.Children.Add(box);
            var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog { XamlRoot = Root.XamlRoot, Title = "クラッシュレポート", Content = panel, PrimaryButtonText = "コピー", SecondaryButtonText = "フォルダーを開く", CloseButtonText = "閉じる", RequestedTheme = Root.RequestedTheme };
            var result = await dialog.ShowAsync();
            if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
            {
                var data = new Windows.ApplicationModel.DataTransfer.DataPackage(); data.SetText(text);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data); Status("クラッシュレポートをコピーしました。");
            }
            else if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Secondary) CrashReport.OpenFolder();
        }
        finally { dialogOpen = false; }
    }
    private async void ReportIssueClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/Kou-256/Locaptio/issues/new/choose"));
}
