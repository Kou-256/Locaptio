using Locaptio.Core.AI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace Locaptio.WinUI;

public sealed partial class MainWindow
{
    private CancellationTokenSource? modelPreparationStop;
    private void AddModelDownloads(StackPanel content, Window owner)
    {
        content.Children.Add(new TextBlock { Text = "モデルの事前ダウンロード", FontSize = 18 });
        content.Children.Add(new TextBlock { Text = "録音や文字起こしを始めずに準備できます。初回はネット接続と数GBの空き容量が必要です。認識モデルは「音声認識・Windows連携」で選んだもの、推論デバイスも現在の設定を使います。", TextWrapping = TextWrapping.Wrap });
        var kind = new DownwardSelector { OwnerWindow = owner, ItemsSource = new[] { "音声認識（選択中のWhisperモデル）", "音声認識＋英日翻訳", "ローカルLLM（「翻訳・ローカルLLM」で選んだモデル）" }, SelectedIndex = 0 };
        var start = new Button { Content = "モデルをダウンロード・準備" };
        var cancel = new Button { Content = "中止", IsEnabled = false };
        var progress = new ProgressBar { IsIndeterminate = false, Maximum = 100, Visibility = Visibility.Collapsed };
        var result = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        CancellationTokenSource? stop = null;
        owner.Closed += (_, _) => stop?.Cancel();
        cancel.Click += (_, _) => stop?.Cancel();
        start.Click += async (_, _) =>
        {
            if (captionStop is not null || recordingStop is not null || dialogOpen)
            { result.Text = "文字起こし・録音・ファイル読み込みを停止してから準備してください。"; return; }
            string folder = FindRoot();
            if (folder.Length == 0) { result.Text = "「保存先・録音」でモデル・推論環境のフォルダーを指定してください。"; return; }
            prepared?.Dispose(); prepared = null;
            using var request = new CancellationTokenSource(); stop = modelPreparationStop = request; bool finished = false;
            start.IsEnabled = kind.IsEnabled = false; cancel.IsEnabled = true; progress.Visibility = Visibility.Visible; progress.IsIndeterminate = true;
            int choice = kind.SelectedIndex; string model = settings.SpeechModel, device = settings.CpuOnly ? "cpu" : settings.InferenceDevice;
            try
            {
                void Report(PreparationProgress value) => DispatcherQueue.TryEnqueue(() =>
                {
                    if (finished || request.IsCancellationRequested) return;
                    progress.IsIndeterminate = value.Total <= 0;
                    if (value.Total > 0) progress.Value = value.Completed * 100.0 / value.Total;
                    result.Text = value.Component + "：" + value.Stage;
                });
                result.Text = "準備しています…";
                if (choice == 2) await LocalLlmPreparation.EnsureAsync(folder, LocalLlmModels.Resolve(folder, settings.LlmModel, settings.LlmModelPath), settings.CpuOnly || device == "cpu", Report, request.Token);
                else await SpeechPreparation.EnsureAsync(folder, model, choice == 1, device, Report, request.Token);
                result.Text = "モデルのダウンロード・準備が完了しました。";
            }
            catch (OperationCanceledException) { result.Text = "準備を中止しました。ダウンロード済みのデータは残します。"; }
            catch (Exception ex) { result.Text = "モデルの準備に失敗しました：" + ex.Message; }
            finally { finished = true; stop = modelPreparationStop = null; start.IsEnabled = kind.IsEnabled = true; cancel.IsEnabled = false; progress.Visibility = Visibility.Collapsed; }
        };
        content.Children.Add(kind); var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; actions.Children.Add(start); actions.Children.Add(cancel);
        content.Children.Add(actions); content.Children.Add(progress); content.Children.Add(result);
    }
}
