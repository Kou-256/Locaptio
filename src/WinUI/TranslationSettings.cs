using Locaptio.Core.AI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Locaptio.WinUI;

/// <summary>
/// Translation directions. Modes: "translate" (English speech → chosen language, Japanese by default),
/// "japanese" (Japanese transcription only) and "ja-translate" (Japanese speech → chosen language, English by
/// default). English → Japanese uses the built-in model; other directions use packs downloaded in Settings.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly string[] Modes = ["translate", "japanese", "ja-translate"];
    private void RefreshModeLabels()
    {
        bool previous = modeChanging; modeChanging = true;
        int index = ModeIndex(settings.CaptionMode);
        ModeSelector.ItemsSource = new[] { $"英語 → {TranslationPacks.Name(settings.EnglishTranslationTarget)} 翻訳", "日本語の文字起こし", $"日本語 → {TranslationPacks.Name(settings.JapaneseTranslationTarget)} 翻訳" };
        ModeSelector.SelectedIndex = index; modeChanging = previous;
    }
    private static int ModeIndex(string mode) => Math.Max(0, Array.IndexOf(Modes, mode));
    private static string ModeAt(int index) => Modes[Math.Clamp(index, 0, Modes.Length - 1)];

    /// <summary>The speech is Japanese (transcription or Japanese → translation).</summary>
    private bool SpeechJapanese => settings.CaptionMode is "japanese" or "ja-translate";
    /// <summary>What the current mode translates; null when only transcribing.</summary>
    private TranslationRoute? Route => settings.CaptionMode switch
    {
        "translate" => TranslationPacks.Route("en", settings.EnglishTranslationTarget),
        "ja-translate" => TranslationPacks.Route("ja", settings.JapaneseTranslationTarget),
        _ => null
    };
    /// <summary>Explains which pack to download when the current mode needs one that is missing.</summary>
    private string? MissingTranslationPack(string folder)
        => Route is { } route && folder.Length > 0 && TranslationPacks.Missing(folder, route) is { } pack
            ? $"{TranslationPacks.Name(route.Source)}→{TranslationPacks.Name(route.Target)}の翻訳には「{pack.Name}」の翻訳モデル（約{pack.Gigabytes:0.##}GB）が必要です。設定の「翻訳・ローカルLLM」→「翻訳の言語」でダウンロードしてください。"
            : null;

    private void AddTranslationSettings(StackPanel content, Window owner)
    {
        content.Children.Add(new TextBlock { Text = "翻訳の言語", FontSize = 18, Margin = new Thickness(0, 12, 0, 0) });
        content.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Text = "本体の上部で「英語 → 翻訳」「日本語の文字起こし」「日本語 → 翻訳」を切り替えます。翻訳先の言語はここで選びます。英語→日本語は標準で使えます。それ以外の言語は、下の翻訳モデルを一度ダウンロードしてから使います（PC内で翻訳し、文章を外部へ送信しません）。" });
        var languages = TranslationPacks.Languages;
        DownwardSelector Target(string title, string source, string current, Action<string> set)
        {
            var choices = languages.Where(l => l.Code != source).ToArray();
            content.Children.Add(new TextBlock { Text = title });
            var selector = new DownwardSelector { OwnerWindow = owner, HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = choices.Select(l => l.Name).ToArray(), SelectedIndex = Math.Max(0, Array.FindIndex(choices, l => l.Code == current)) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(selector, title);
            selector.SelectionChanged += (_, _) => { set(choices[Math.Max(0, selector.SelectedIndex)].Code); settings.Save(); ApplySettings(); };
            content.Children.Add(selector);
            return selector;
        }
        Target("英語の音声を翻訳する言語（「英語 → 翻訳」）", "en", settings.EnglishTranslationTarget, v => settings.EnglishTranslationTarget = v);
        Target("日本語の音声を翻訳する言語（「日本語 → 翻訳」）", "ja", settings.JapaneseTranslationTarget, v => settings.JapaneseTranslationTarget = v);
        content.Children.Add(new TextBlock { Text = "翻訳モデル（あとからダウンロード）", Margin = new Thickness(0, 8, 0, 0) });
        foreach (var pack in TranslationPacks.Downloadable)
        {
            var state = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
            var download = new Button();
            var progress = new ProgressBar { Maximum = 100, Visibility = Visibility.Collapsed };
            void Refresh()
            {
                string folder = FindRoot(); bool there = folder.Length > 0 && pack.IsInstalled(folder);
                state.Text = $"{pack.Name}：{pack.Use}　約{pack.Gigabytes:0.##}GB・{pack.License}　" + (there ? "✓ダウンロード済み" : folder.Length == 0 ? "（モデル・推論環境のフォルダーを指定してください）" : "未ダウンロード");
                download.Content = there ? "ダウンロード済み" : "ダウンロード"; download.IsEnabled = !there && folder.Length > 0;
            }
            download.Click += async (_, _) =>
            {
                string folder = FindRoot();
                if (captionStop is not null || recordingStop is not null) { state.Text = "文字起こし・録音を停止してからダウンロードしてください。"; return; }
                download.IsEnabled = false; progress.Visibility = Visibility.Visible; progress.IsIndeterminate = true;
                using var stop = new CancellationTokenSource(); owner.Closed += (_, _) => stop.Cancel();
                try
                {
                    await TranslationPacks.EnsureAsync(folder, pack.Key, p => DispatcherQueue.TryEnqueue(() => { progress.IsIndeterminate = p.Total <= 0; if (p.Total > 0) progress.Value = p.Completed * 100.0 / p.Total; state.Text = pack.Name + "：" + p.Stage; }), stop.Token);
                    Refresh(); Status($"翻訳モデル「{pack.Name}」の準備が完了しました。");
                }
                catch (OperationCanceledException) { Refresh(); }
                catch (Exception ex) { Refresh(); state.Text = "ダウンロードに失敗しました：" + ex.Message; }
                finally { progress.Visibility = Visibility.Collapsed; }
            };
            Refresh();
            var row = new StackPanel { Spacing = 4 }; row.Children.Add(state); row.Children.Add(download); row.Children.Add(progress);
            content.Children.Add(row);
        }
        content.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = .8, Text = "・ローカルLLMによる文脈を踏まえた翻訳の仕上げは、英語→日本語で使います。その他の言語は翻訳モデルだけで訳します。\n・日本語→英語は「日本語 → 英語」のモデルがあればそれを、なければ多言語モデルを使います。" });
    }
}
