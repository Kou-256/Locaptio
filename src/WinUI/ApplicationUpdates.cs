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
    private bool updatePromptOpen;
    private Func<Task<bool>>? updateApprovalForVerification;
    private async Task<bool> ApproveUpdateAsync(InfoBar notice, string title, string message, string action)
    {
        if (updateApprovalForVerification is not null) return await updateApprovalForVerification();
        if (closing || dialogOpen || updatePromptOpen) return false;
        updatePromptOpen = true;
        try
        {
            if (notice.XamlRoot is null || notice.XamlRoot == Root.XamlRoot)
            {
                if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter && presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized) presenter.Restore();
                Activate();
            }
            else settingsWindow?.Activate();
            var prompt = new ContentDialog { XamlRoot = notice.XamlRoot ?? Root.XamlRoot, Title = title, Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, PrimaryButtonText = action, CloseButtonText = "後で", DefaultButton = ContentDialogButton.Close, RequestedTheme = ElementTheme.Dark };
            return await prompt.ShowAsync() == ContentDialogResult.Primary;
        }
        finally { updatePromptOpen = false; }
    }
    private async Task ConfirmInstallUpdateAsync(string path, AppRelease release, InfoBar notice)
    {
        if (!await ApproveUpdateAsync(notice, $"Locaptio {release.Version} に更新しますか？", "許可すると録音・文字起こしを停止し、セットアップを開きます。「後で」を選ぶと現在の作業を続けます。", "インストールする")) return;
        using (var file = File.OpenRead(path))
            if (!Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(file)).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新ファイルが変更されています。再ダウンロードしてください。");
        await StopAllAsync();
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        await ShutdownAsync();
    }
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
            notice.Message = $"更新ファイル（約{release.Size / 1048576d:F0}MB）をダウンロードできます。";
            if (settings.AutoDownloadUpdates) await DownloadUpdateAsync(service, release, notice);
            else UpdateAction(notice, "ダウンロード", async () =>
            {
                if (updateBusy) return;
                updateBusy = true;
                try { updateOperation = DownloadUpdateAsync(service, release, notice); await updateOperation; }
                finally { updateBusy = false; }
            });
            if (!settings.AutoDownloadUpdates && await ApproveUpdateAsync(notice, $"Locaptio {release.Version} が公開されています", "更新ファイルをダウンロードしますか？インストールの前にも確認します。", "ダウンロード"))
                await DownloadUpdateAsync(service, release, notice);
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
        notice.Message = "更新ファイルをダウンロード中: 0%";
        bool finished = false;
        string path = await service.DownloadAsync(release, directory, p => DispatcherQueue.TryEnqueue(() =>
        {
            if (!closing && !finished) notice.Message = p < 100 ? $"更新ファイルをダウンロード中: {p}%" : "ダウンロード完了・SHA-256確認済み";
        }), timeout.Token);
        finished = true;
        if (closing) return;
        notice.IsOpen = true; notice.Severity = InfoBarSeverity.Success;
        notice.Title = $"Locaptio {release.Version} のダウンロード完了";
        bool setup = Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase);
        notice.Message = setup ? $"SHA-256確認済み。セットアップを実行して更新できます。保存先: {path}" : $"SHA-256確認済み。ZIPを別フォルダーに展開して起動してください。保存先: {path}";
        UpdateAction(notice, setup ? "セットアップを実行" : "保存先を開く", async () =>
        {
            if (setup)
            {
                await ConfirmInstallUpdateAsync(path, release, notice);
            }
            else await Windows.System.Launcher.LaunchFolderPathAsync(directory);
        });
        if (setup) DispatcherQueue.TryEnqueue(async () =>
        {
            try { await ConfirmInstallUpdateAsync(path, release, notice); }
            catch (Exception ex) { if (!closing) { notice.Severity = InfoBarSeverity.Warning; notice.Message = ex.Message; } }
        });
    }
}
