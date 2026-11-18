using Locaptio.Core.AI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;

namespace Locaptio.WinUI;

/// <summary>Settings → 翻訳・ローカルLLM: which local LLM to use, and downloading it ahead of time.</summary>
public sealed partial class MainWindow
{
    /// <summary>The GGUF file the AI correction, translation and summaries load.</summary>
    private string LlmModelFile => LocalLlmModels.Resolve(root, settings.LlmModel, settings.LlmModelPath);
    /// <summary>The download last started from Settings (self-checks wait for it).</summary>
    private Task? llmDownloadTask;

    private void AddLlmModelSettings(StackPanel content, Window owner)
    {
        content.Children.Add(new TextBlock { Text = "ローカルLLMのモデル（AI補正・文脈翻訳・AI要約）", Margin = new Thickness(0, 8, 0, 0) });
        string Label(LocalLlmModel m, string folder) => $"{m.Name}（約{m.Gigabytes:0.#}GB）— {m.Use}" + (folder.Length > 0 && m.IsDownloaded(folder) ? "　✓ダウンロード済み" : "");
        var models = LocalLlmModels.All;
        bool refreshing = false;
        var selector = new DownwardSelector { OwnerWindow = owner, HorizontalAlignment = HorizontalAlignment.Stretch };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(selector, "ローカルLLMのモデル");
        var state = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        var download = new Button { Content = "このモデルを今ダウンロード" };
        var cancel = new Button { Content = "中止", IsEnabled = false };
        var progress = new ProgressBar { Maximum = 100, Visibility = Visibility.Collapsed };
        var custom = new TextBox { Text = settings.LlmModelPath, PlaceholderText = "例: D:\\models\\my-model.gguf", Header = "使用するGGUFファイル" };
        var browse = new Button { Content = "GGUFファイルを選択…" };
        var customPanel = new StackPanel { Spacing = 6 }; customPanel.Children.Add(custom); customPanel.Children.Add(browse);
        CancellationTokenSource? stop = null;
        owner.Closed += (_, _) => stop?.Cancel();

        void Refresh()
        {
            string folder = FindRoot();
            refreshing = true;
            selector.ItemsSource = models.Select(m => Label(m, folder)).Append("自分のGGUFファイルを使う").ToArray();
            int index = Array.FindIndex(models, m => m.Key == settings.LlmModel);
            selector.SelectedIndex = settings.LlmModel == LocalLlmModels.Custom ? models.Length : Math.Max(0, index);
            refreshing = false;
            var chosen = LocalLlmModels.Find(settings.LlmModel);
            customPanel.Visibility = chosen is null ? Visibility.Visible : Visibility.Collapsed;
            download.Visibility = chosen is null ? Visibility.Collapsed : Visibility.Visible;
            if (stop is not null) return;
            bool there = chosen is not null && folder.Length > 0 && chosen.IsDownloaded(folder);
            download.Content = there ? "ダウンロード済み" : "このモデルを今ダウンロード"; download.IsEnabled = !there && folder.Length > 0;
            state.Text = chosen is null
                ? (File.Exists(settings.LlmModelPath) ? "指定したGGUFファイルを使います。" : "GGUFファイルを指定してください。指定がない間は Qwen3 4B を使います。")
                : folder.Length == 0 ? "「保存先・録音」でモデル・推論環境のフォルダーを指定すると、ダウンロードできます。"
                : chosen.IsDownloaded(folder) ? $"ダウンロード済みです（約{chosen.Gigabytes:0.#}GB）。次回のStart・AI要約からこのモデルを使います。"
                : $"まだダウンロードしていません（約{chosen.Gigabytes:0.#}GB）。今ダウンロードしておくか、初めて使うときに自動でダウンロードします。";
        }
        selector.SelectionChanged += (_, _) =>
        {
            if (refreshing || selector.SelectedIndex < 0) return;
            settings.LlmModel = selector.SelectedIndex < models.Length ? models[selector.SelectedIndex].Key : LocalLlmModels.Custom;
            settings.Save(); Refresh();
        };
        custom.TextChanged += (_, _) => { settings.LlmModelPath = custom.Text; settings.Save(); if (stop is null) Refresh(); };
        browse.Click += async (_, _) =>
        {
            try { var picker = new FileOpenPicker(owner.AppWindow.Id); picker.FileTypeFilter.Add(".gguf"); var file = await picker.PickSingleFileAsync(); if (file is not null) custom.Text = file.Path; }
            catch (Exception ex) { state.Text = ex.Message; }
        };
        cancel.Click += (_, _) => stop?.Cancel();
        download.Click += (_, _) => llmDownloadTask = DownloadAsync();
        async Task DownloadAsync()
        {
            var chosen = LocalLlmModels.Find(settings.LlmModel);
            string folder = FindRoot();
            if (chosen is null || folder.Length == 0) { Refresh(); return; }
            if (captionStop is not null || recordingStop is not null) { state.Text = "文字起こし・録音を停止してからダウンロードしてください。"; return; }
            using var request = new CancellationTokenSource(); stop = request; bool finished = false;
            download.IsEnabled = selector.IsEnabled = false; cancel.IsEnabled = true; progress.Visibility = Visibility.Visible; progress.IsIndeterminate = true;
            state.Text = $"{chosen.Name} を準備しています…";
            try
            {
                void Report(PreparationProgress value) => DispatcherQueue.TryEnqueue(() =>
                {
                    if (finished || request.IsCancellationRequested) return;
                    progress.IsIndeterminate = value.Total <= 0;
                    if (value.Total > 0) progress.Value = value.Completed * 100.0 / value.Total;
                    state.Text = value.Stage;
                });
                await LocalLlmPreparation.EnsureAsync(folder, chosen.PathIn(folder), settings.CpuOnly || settings.InferenceDevice == "cpu", Report, request.Token);
                Finish($"{chosen.Name} のダウンロードが完了しました。次回のStart・AI要約からこのモデルを使います。");
            }
            catch (OperationCanceledException) { Finish("ダウンロードを中止しました。途中までのデータは残し、次回は続きからダウンロードします。"); }
            catch (Exception ex) { Finish("ダウンロードに失敗しました：" + ex.Message); }
            void Finish(string message)
            {
                finished = true; stop = null; selector.IsEnabled = true; cancel.IsEnabled = false; progress.Visibility = Visibility.Collapsed;
                Refresh(); state.Text = message;
            }
        }

        Refresh();
        content.Children.Add(selector); content.Children.Add(state);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; actions.Children.Add(download); actions.Children.Add(cancel);
        content.Children.Add(actions); content.Children.Add(progress); content.Children.Add(customPanel);
        content.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Text =
            "いずれも Qwen3 の公式 GGUF（Apache-2.0）です。大きいモデルほど補正・翻訳・要約の精度が上がりますが、メモリを多く使い、処理に時間がかかります。モデルはPC内に保存し、文章を外部へ送信しません。切り替えは次回のStart・AI要約から反映します。" });
    }
}
