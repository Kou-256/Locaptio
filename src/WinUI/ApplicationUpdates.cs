using System.Net.Http;
using Locaptio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Locaptio.WinUI;

public sealed partial class MainWindow
{
    private static readonly HttpClient updateHttp = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly CancellationTokenSource updateStop = new();
    private bool updateBusy;
    private Task? updateOperation;
    private async void CheckUpdatesClick(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(true);
    private static string CurrentAppVersion => (typeof(MainWindow).Assembly.GetName().Version ?? new Version(1, 0, 0)).ToString(3);
    private void UpdateAction(InfoBar notice, string label, Func<Task> action)
    {
        var button = new Button { Content = label };
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try { await action(); }
            catch (Exception ex) { if (!closing) { notice.Severity = InfoBarSeverity.Warning; notice.Message = ex.Message; } }
            finally { button.IsEnabled = true; }
        };
        notice.ActionButton = button;
    }
    private Task CheckForUpdatesAsync(bool manual, InfoBar? target = null)
    {
        if (closing) return Task.CompletedTask;
        var notice = target ?? UpdateNotice;
        if (updateBusy)
        {
            if (manual) { notice.IsOpen = true; notice.Severity = InfoBarSeverity.Informational; notice.Title = "更新処理を実行中です"; notice.Message = "確認またはダウンロードの完了後に、もう一度お試しください。"; notice.ActionButton = null; }
            return Task.CompletedTask;
        }
        return updateOperation = CheckForUpdatesCoreAsync(manual, notice);
    }
    private async Task CheckForUpdatesCoreAsync(bool manual, InfoBar notice)
    {
        if (closing || updateBusy) return;
        updateBusy = true;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(updateStop.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            if (manual) { notice.IsOpen = true; notice.Severity = InfoBarSeverity.Informational; notice.Title = "更新を確認中"; notice.Message = "公式GitHubの公開版を確認しています。"; notice.ActionButton = null; }
            var service = new AppUpdates(updateHttp);
            var current = typeof(MainWindow).Assembly.GetName().Version ?? new Version(1, 0, 0);
            var release = await service.CheckAsync(current, timeout.Token);
            if (closing) return;
            if (release is null)
            {
                if (manual) { notice.Title = "更新の確認が完了しました"; notice.Message = $"現在のバージョン {current.ToString(3)} より新しい公開版はありません。"; }
                return;
            }
            notice.IsOpen = true; notice.Severity = InfoBarSeverity.Informational;
            notice.Title = $"Locaptio {release.Version} が利用できます";
            notice.Message = $"更新ZIP（約{release.Size / 1048576d:F0}MB）をダウンロードできます。";
            if (settings.AutoDownloadUpdates) await DownloadUpdateAsync(service, release, notice);
            else UpdateAction(notice, "ダウンロード", async () =>
            {
                if (updateBusy) return;
                updateBusy = true;
                try { updateOperation = DownloadUpdateAsync(service, release, notice); await updateOperation; }
                finally { updateBusy = false; }
            });
        }
        catch (OperationCanceledException) { if (!closing && manual) { notice.Title = "更新の確認を中断しました"; notice.Message = "通信がタイムアウトした場合は、後で再度お試しください。"; } }
        catch (Exception ex)
        {
            if (!closing && manual) { notice.Severity = InfoBarSeverity.Warning; notice.Title = "更新を確認できませんでした"; notice.Message = ex.Message; }
            else if (!closing && notice.IsOpen) { notice.Severity = InfoBarSeverity.Warning; notice.Message = "ダウンロードできませんでした。ヘルプの「更新を確認」から再試行できます。"; }
        }
        finally { updateBusy = false; }
    }
    private async Task FinishUpdateAsync()
    {
        if (updateOperation is null) return;
        try { await updateOperation.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (Exception) { /* Exit remains bounded even when the network fails. */ }
    }
    private async Task DownloadUpdateAsync(AppUpdates service, AppRelease release, InfoBar notice)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(updateStop.Token);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        notice.ActionButton = null;
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Locaptio", "updates", release.Version.ToString());
        notice.Message = "更新ZIPをダウンロード中: 0%";
        bool finished = false;
        string path = await service.DownloadAsync(release, directory, p => DispatcherQueue.TryEnqueue(() =>
        {
            if (!closing && !finished) notice.Message = p < 100 ? $"更新ZIPをダウンロード中: {p}%" : "ダウンロード完了・SHA-256確認済み";
        }), timeout.Token);
        finished = true;
        if (closing) return;
        notice.IsOpen = true; notice.Severity = InfoBarSeverity.Success;
        notice.Title = $"Locaptio {release.Version} のダウンロード完了";
        notice.Message = $"SHA-256確認済み。ZIPを別フォルダーに展開して起動してください。保存先: {path}";
        UpdateAction(notice, "保存先を開く", async () => { await Windows.System.Launcher.LaunchFolderPathAsync(directory); });
    }
}
