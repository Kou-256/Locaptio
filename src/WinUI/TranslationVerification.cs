using Locaptio.Core;
using Locaptio.Core.AI;
using Locaptio.Core.Audio;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Locaptio.WinUI;

// --verify-translation-modes: the three modes, routes for the chosen target languages, the message when a pack
// is missing, column headers and the settings section. Run from a runtime folder with the Japanese → English
// pack and artifacts/test-ja.wav, it also recognizes real Japanese speech and translates it into English
// through the same path file import uses.
public sealed partial class MainWindow
{
    private async Task VerifyTranslationModesAsync()
    {
        string report = Path.Combine(root, "artifacts", "translation-modes-verification.txt");
        string mode = settings.CaptionMode, en = settings.EnglishTranslationTarget, ja = settings.JapaneseTranslationTarget;
        var log = new List<string>(); var failures = new List<string>();
        void Check(bool condition, string name) { log.Add((condition ? "ok   " : "FAIL ") + name); if (!condition) failures.Add(name); }
        try
        {
            string[] Items(DownwardSelector selector) => ((System.Collections.IEnumerable)selector.ItemsSource!).Cast<object>().Select(o => o.ToString()!).ToArray();
            async Task Choose(DownwardSelector selector, int index) { selector.SelectedIndex = index; for (int i = 0; i < 40 && modeChanging; i++) await Task.Delay(50); await Task.Delay(150); }
            ClearCaptions(); modeChanging = true; settings.CaptionMode = "ja"; modeChanging = false; ApplySettings();
            Check(Items(ModeSelector).SequenceEqual(["日本語を話す", "英語を話す", "イタリア語を話す", "スペイン語を話す"]), "four spoken languages: " + string.Join(" / ", Items(ModeSelector)));
            Check(Items(TargetSelector).SequenceEqual(["文字起こしのみ", "→ 英語に翻訳", "→ イタリア語に翻訳", "→ スペイン語に翻訳"]) && TranscriptionOnly, "Japanese: transcription only or one of the other three: " + string.Join(" / ", Items(TargetSelector)));

            await Choose(ModeSelector, 2); RenderCaptions();
            Check(settings.CaptionMode == "it" && SpeechLanguage == "it" && TranscriptionOnly && Route is null && EnglishHeader.Text == "イタリア語", $"Italian transcription chosen from the selector ({settings.CaptionMode}, header {EnglishHeader.Text})");
            Check(Items(TargetSelector)[1] == "→ 日本語に翻訳" && Items(TargetSelector).Length == 4, "Italian offers Japanese, English and Spanish targets");
            await Choose(TargetSelector, 3); RenderCaptions();
            Check(settings.CaptionMode == "it>es" && Route is { Source: "it", Target: "es", Pack: "multi" } && JapaneseHeader.Text == "スペイン語", $"Italian → Spanish uses the multilingual pack ({settings.CaptionMode})");
            await Choose(ModeSelector, 3);
            Check(settings.CaptionMode == "es" && TranscriptionOnly && TargetSelector.SelectedIndex == 0, $"switching to Spanish speech drops the Spanish target ({settings.CaptionMode})");
            await Choose(ModeSelector, 1); await Choose(TargetSelector, 1);
            Check(settings.CaptionMode == "en>ja" && Route is { BuiltIn: true }, "English → Japanese stays built in");
            await Choose(ModeSelector, 0); await Choose(TargetSelector, 1); RenderCaptions();
            Check(settings.CaptionMode == "ja>en" && SpeechJapanese && Route is { Source: "ja", Target: "en", Pack: "ja-en" } && EnglishHeader.Text == "日本語" && JapaneseHeader.Text == "英語", $"Japanese → English route and headers ({EnglishHeader.Text} / {JapaneseHeader.Text})");
            // Caption colors follow the role (original / translation / transcription only), not the language.
            string savedPalette = settings.CaptionPalette, savedTranscript = settings.CaptionTranscriptColor, savedBackground = settings.CaptionBackground;
            try
            {
                settings.CaptionPalette = "cyan-white"; settings.CaptionTranscriptColor = "white"; settings.CaptionBackground = "live";
                Windows.UI.Color RowColor(int child, CaptionUpdate caption)
                {
                    var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition());
                    row.Children.Add(new TextBlock()); row.Children.Add(new TextBlock()); StyleHistoryRow(row, caption);
                    return ((Microsoft.UI.Xaml.Media.SolidColorBrush)((TextBlock)row.Children[child]).Foreground).Color;
                }
                var spoken = new CaptionUpdate(1, 1, "今日の会議を始めます。", "Let's start today's meeting.", true, 0, 0, 0, "ja");
                var cyan = Windows.UI.Color.FromArgb(255, 0x7D, 0xE3, 0xFF); var white = Windows.UI.Color.FromArgb(255, 255, 255, 255); var yellow = Windows.UI.Color.FromArgb(255, 0xFF, 0xE7, 0x94);
                Check(RowColor(0, spoken) == cyan && RowColor(1, spoken) == white, "Japanese → English: the original (Japanese) uses the original color, the English translation the translation color");
                modeChanging = true; settings.CaptionMode = "ja"; modeChanging = false; ApplySettings();
                Check(RowColor(0, spoken with { Japanese = null }) == white, "transcription only uses its own color (white by default)");
                settings.CaptionTranscriptColor = "yellow";
                Check(RowColor(0, spoken with { Japanese = null }) == yellow, "the transcription-only color can be changed separately");
                modeChanging = true; settings.CaptionMode = "it>es"; modeChanging = false; ApplySettings();
                Check(RowColor(0, spoken) == cyan, "translation colors stay as chosen for other languages");
            }
            finally { settings.CaptionPalette = savedPalette; settings.CaptionTranscriptColor = savedTranscript; settings.CaptionBackground = savedBackground; modeChanging = true; settings.CaptionMode = "ja>en"; modeChanging = false; ApplySettings(); }
            bool packHere = TranslationPacks.Find("ja-en")!.IsInstalled(root);
            Check(packHere ? MissingTranslationPack(root) is null : MissingTranslationPack(root)?.Contains("日本語 → 英語") == true, "missing pack explained (or none missing): " + (MissingTranslationPack(root) ?? "installed"));
            Check(CaptionModes.Normalize("translate", "zh") == "en>ja" && CaptionModes.Normalize("translate", "es") == "en>es" && CaptionModes.Normalize("ja-translate", "ja", "en") == "ja>en"
                && CaptionModes.Normalize("japanese") == "ja" && CaptionModes.Normalize("fr>de") == "ja" && CaptionModes.WindowsLanguage("it") == "it-IT" && CaptionModes.WindowsLanguage("es") == "es-ES",
                "older settings are carried over; Windows captions languages it-IT / es-ES");

            SettingsClick(this, new RoutedEventArgs()); await Task.Delay(400);
            settingsNavigation!.Select("翻訳・ローカルLLM"); await Task.Delay(300);
            var page = settingsNavigation.Page("翻訳・ローカルLLM");
            Check(page.Children.OfType<TextBlock>().Any(t => t.Text == "翻訳の言語") && TranslationPacks.Downloadable.All(pack => page.Children.OfType<StackPanel>().Any(p => p.Children.OfType<TextBlock>().FirstOrDefault()?.Text.StartsWith(pack.Name + "：") == true && p.Children.OfType<Button>().Any())), "settings explain the languages and list two downloadable packs");
            await settingsNavigation.CaptureAsync(Path.Combine(root, "artifacts", "settings-translation.png"));
            settingsWindow?.Close(); await Task.Delay(200);

            // Real speech: Japanese recognized and translated into English (when the pack is here).
            string audio = Path.Combine(root, "artifacts", "test-ja.wav");
            if (packHere && File.Exists(audio) && File.Exists(Path.Combine(root, "models", "speech-small", "model.bin")))
            {
                var route = TranslationPacks.Route("ja", "en", root)!;
                using var models = new PreparedInference(root, true, "small", "", "cpu", true, false, false, LlmModelFile, _ => { }, route: route);
                await models.Ready.WaitAsync(TimeSpan.FromMinutes(5));
                var phrases = new List<RecordingPhrase>();
                await Task.Run(() => FileTranscription.RunAsync(audio, models.Speech, "ja", p => { phrases.Add(p); return Task.CompletedTask; }, _ => { }, CancellationToken.None, models.Translation));
                log.Add("speech: " + string.Join(" / ", phrases.Select(p => p.Text + " → " + p.Translation)));
                Check(phrases.Count > 0 && phrases.All(p => !string.IsNullOrWhiteSpace(p.Translation) && !System.Text.RegularExpressions.Regex.IsMatch(p.Translation!, "[ぁ-んァ-ヶ]")), "real Japanese speech translated into English");
            }
            else log.Add("skip real speech: run from a runtime folder with the Japanese → English pack, speech-small and artifacts/test-ja.wav");
            // Real speech: English transcription only (no translation worker).
            string english = Path.Combine(root, "artifacts", File.Exists(Path.Combine(root, "artifacts", "test.wav")) ? "test.wav" : "test-translation.wav");
            if (File.Exists(english) && File.Exists(Path.Combine(root, "models", "speech-small", "model.bin")))
            {
                using var models = new PreparedInference(root, false, "small", "", "cpu", true, false, false, LlmModelFile, _ => { }, language: "en");
                await models.Ready.WaitAsync(TimeSpan.FromMinutes(5));
                var phrases = new List<RecordingPhrase>();
                await Task.Run(() => FileTranscription.RunAsync(english, models.Speech, "en", p => { phrases.Add(p); return Task.CompletedTask; }, _ => { }, CancellationToken.None, models.Translation));
                log.Add("english: " + string.Join(" / ", phrases.Select(p => p.Text)));
                Check(models.Translation is null && phrases.Count > 0 && phrases.All(p => p.Language == "en" && string.IsNullOrWhiteSpace(p.Translation) && System.Text.RegularExpressions.Regex.IsMatch(p.Text, "[A-Za-z]")), "real English speech transcribed in English without translation");
            }
            else log.Add("skip English speech: needs speech-small and artifacts/test.wav");
        }
        catch (Exception ex) { failures.Add(ex.Message); log.Add(ex.ToString()); }
        finally
        {
            modeChanging = true; settings.CaptionMode = mode; settings.EnglishTranslationTarget = en; settings.JapaneseTranslationTarget = ja; ModeSelector.SelectedIndex = ModeIndex(mode); modeChanging = false;
            settings.Save();
            File.WriteAllText(report, (failures.Count == 0 ? "PASS" : "FAIL: " + string.Join("; ", failures)) + Environment.NewLine + string.Join(Environment.NewLine, log));
            Environment.ExitCode = failures.Count == 0 ? 0 : 1;
            await ShutdownAsync();
        }
    }
}
