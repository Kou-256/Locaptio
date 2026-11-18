using Locaptio.Core;
using Locaptio.Core.AI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Locaptio.WinUI;

/// <summary>
/// Caption mode: the spoken language (Japanese, English, Italian or Spanish) and either transcription only or a
/// translation into one of the other three (CaptionModes: "ja", "en>ja", "it>es" ...). English → Japanese uses the
/// built-in model; other directions use packs downloaded in Settings (Japanese → English pack, or multilingual).
/// </summary>
public sealed partial class MainWindow
{
    private (string Source, string? Target) CurrentMode => CaptionModes.Parse(settings.CaptionMode, settings.EnglishTranslationTarget, settings.JapaneseTranslationTarget);
    /// <summary>Spoken language code: ja, en, it or es.</summary>
    private string SpeechLanguage => CurrentMode.Source;
    private bool TranscriptionOnly => CurrentMode.Target is null;
    private static string LanguageName(string code) => TranslationPacks.Name(code);
    /// <summary>Target choices for a spoken language: index 0 is "transcription only".</summary>
    private static string?[] TargetChoices(string source) => [null, .. CaptionModes.Languages.Where(l => l != source)];
    private void RefreshModeLabels()
    {
        bool previous = modeChanging; modeChanging = true;
        var (source, target) = CurrentMode;
        ModeSelector.ItemsSource = CaptionModes.Languages.Select(l => LanguageName(l) + "を話す").ToArray();
        ModeSelector.SelectedIndex = ModeIndex(settings.CaptionMode);
        var targets = TargetChoices(source);
        TargetSelector.ItemsSource = targets.Select(t => t is null ? "文字起こしのみ" : "→ " + LanguageName(t) + "に翻訳").ToArray();
        TargetSelector.SelectedIndex = Math.Max(0, Array.IndexOf(targets, target));
        modeChanging = previous;
    }
    /// <summary>Index of the spoken language in <see cref="ModeSelector"/>.</summary>
    private int ModeIndex(string mode) => Math.Max(0, Array.IndexOf(CaptionModes.Languages, CaptionModes.Parse(mode, settings.EnglishTranslationTarget, settings.JapaneseTranslationTarget).Source));
    /// <summary>The mode the two selectors show; a target equal to a newly chosen spoken language becomes "transcription only".</summary>
    private string SelectedMode()
    {
        string source = CaptionModes.Languages[Math.Clamp(ModeSelector.SelectedIndex, 0, CaptionModes.Languages.Length - 1)];
        var (oldSource, oldTarget) = CurrentMode;
        string? target = source == oldSource ? TargetChoices(source)[Math.Clamp(TargetSelector.SelectedIndex, 0, CaptionModes.Languages.Length - 1)] : oldTarget;
        return CaptionModes.Format(source, target == source ? null : target);
    }

    /// <summary>The speech is Japanese (affects segmentation, models and AI prompts).</summary>
    private bool SpeechJapanese => SpeechLanguage == "ja";
    /// <summary>What the current mode translates; null when only transcribing.</summary>
    private TranslationRoute? Route => CurrentMode is (var source, { } target) ? TranslationPacks.Route(source, target) : null;
    /// <summary>Explains which pack to download when the current mode needs one that is missing.</summary>
    private string? MissingTranslationPack(string folder)
        => Route is { } route && folder.Length > 0 && TranslationPacks.Missing(folder, route) is { } pack
            ? $"{TranslationPacks.Name(route.Source)}→{TranslationPacks.Name(route.Target)}の翻訳には「{pack.Name}」の翻訳モデル（約{pack.Gigabytes:0.##}GB）が必要です。設定の「翻訳・ローカルLLM」→「翻訳の言語」からダウンロードしてください（初回のみ）。"
            : null;

    private void AddTranslationSettings(StackPanel content, Window owner)
    {
        content.Children.Add(new TextBlock { Text = "翻訳の言語", FontSize = 18, Margin = new Thickness(0, 12, 0, 0) });
        content.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Text = "本体の上部で、話す言語（日本語・英語・イタリア語・スペイン語）と「文字起こしのみ」または翻訳先の言語を選びます。英語→日本語は標準で使えます。日本語→英語は「日本語 → 英語」、それ以外の組み合わせは「多言語」の翻訳モデルを一度ダウンロードしてから使います（PC内で翻訳し、文章を外部へ送信しません）。" });
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
