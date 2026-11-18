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
            settings.EnglishTranslationTarget = "ja"; settings.JapaneseTranslationTarget = "en"; ApplySettings();
            var labels = ((System.Collections.IEnumerable)ModeSelector.ItemsSource!).Cast<object>().Select(o => o.ToString()).ToArray();
            Check(labels.SequenceEqual(["英語 → 日本語 翻訳", "日本語の文字起こし", "日本語 → 英語 翻訳"]), "three modes: " + string.Join(" / ", labels));

            modeChanging = true; settings.CaptionMode = "ja-translate"; ModeSelector.SelectedIndex = 2; modeChanging = false; ApplySettings(); RenderCaptions();
            Check(SpeechJapanese && Route is { Source: "ja", Target: "en", Pack: "ja-en" }, "日本語 → 英語: Japanese speech, Japanese → English route");
            Check(EnglishHeader.Text == "日本語" && JapaneseHeader.Text == "英語", $"column headers name the languages ({EnglishHeader.Text} / {JapaneseHeader.Text})");
            bool packHere = TranslationPacks.Find("ja-en")!.IsInstalled(root);
            Check(packHere ? MissingTranslationPack(root) is null : MissingTranslationPack(root)?.Contains("日本語 → 英語") == true, "missing pack explained (or none missing): " + (MissingTranslationPack(root) ?? "installed"));

            settings.JapaneseTranslationTarget = "zh"; ApplySettings();
            labels = ((System.Collections.IEnumerable)ModeSelector.ItemsSource!).Cast<object>().Select(o => o.ToString()).ToArray();
            Check(labels[2] == "日本語 → 中国語 翻訳" && Route is { Pack: "multi", Target: "zh" } && ModeSelector.SelectedIndex == 2 && settings.CaptionMode == "ja-translate", "Chinese target uses the multilingual pack; relabeling keeps the mode");
            modeChanging = true; settings.CaptionMode = "translate"; ModeSelector.SelectedIndex = 0; modeChanging = false; settings.EnglishTranslationTarget = "ko"; ApplySettings();
            Check(!SpeechJapanese && Route is { Source: "en", Target: "ko", Pack: "multi" }, "English → Korean uses the multilingual pack");
            settings.EnglishTranslationTarget = "ja"; ApplySettings();
            Check(Route is { BuiltIn: true }, "English → Japanese stays built in");

            SettingsClick(this, new RoutedEventArgs()); await Task.Delay(400);
            settingsNavigation!.Select("翻訳・ローカルLLM"); await Task.Delay(300);
            var page = settingsNavigation.Page("翻訳・ローカルLLM");
            Check(page.Children.OfType<TextBlock>().Any(t => t.Text == "翻訳の言語") && TranslationPacks.Downloadable.All(pack => page.Children.OfType<StackPanel>().Any(p => p.Children.OfType<TextBlock>().FirstOrDefault()?.Text.StartsWith(pack.Name + "：") == true && p.Children.OfType<Button>().Any())), "settings list the target languages and two downloadable packs");
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
