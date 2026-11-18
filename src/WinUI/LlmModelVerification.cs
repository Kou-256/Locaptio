using Locaptio.Core.AI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Locaptio.WinUI;

// --verify-llm-models: Settings → 翻訳・ローカルLLM. Choosing a model changes the file every AI feature loads,
// the downloaded state is shown per model, the user's own GGUF file can be chosen, and "download now" on a
// model that is already there finishes without downloading. Run from a runtime folder with Qwen3 4B prepared.
// Nothing is downloaded; the original choice is restored.
public sealed partial class MainWindow
{
    private async Task VerifyLlmModelsAsync()
    {
        string report = Path.Combine(root, "artifacts", "llm-models-verification.txt");
        string originalModel = settings.LlmModel, originalPath = settings.LlmModelPath;
        var log = new List<string>(); var failures = new List<string>();
        void Check(bool condition, string name) { log.Add((condition ? "ok   " : "FAIL ") + name); if (!condition) failures.Add(name); }
        string fake = Path.Combine(Path.GetTempPath(), "Locaptio-own-model-" + Environment.ProcessId + ".gguf");
        try
        {
            settings.LlmModel = LocalLlmModels.Default; settings.LlmModelPath = "";
            SettingsClick(this, new RoutedEventArgs()); await Task.Delay(300);
            var navigation = settingsNavigation!;
            navigation.Select("翻訳・ローカルLLM"); await Task.Delay(300);
            var page = navigation.Page("翻訳・ローカルLLM");
            var selector = page.Children.OfType<DownwardSelector>().Single(s => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(s) == "ローカルLLMのモデル");
            int index = page.Children.IndexOf(selector);
            var state = (TextBlock)page.Children[index + 1];
            var actions = (StackPanel)page.Children[index + 2];
            var download = (Button)actions.Children[0];
            var customPanel = (StackPanel)page.Children[index + 4];
            var items = ((System.Collections.IEnumerable)selector.ItemsSource!).Cast<object>().Select(o => o.ToString() ?? "").ToArray();
            log.Add("items: " + string.Join(" | ", items));
            Check(items.Length == LocalLlmModels.All.Length + 1 && selector.SelectedIndex == 1, "four models and your own file, Qwen3 4B selected");
            Check(items[1].Contains("✓") && state.Text.Contains("ダウンロード済み") && !download.IsEnabled && (string)download.Content == "ダウンロード済み", "Qwen3 4B shown as downloaded: " + state.Text);
            await navigation.CaptureAsync(Path.Combine(root, "artifacts", "settings-llm-models.png"));

            selector.SelectedIndex = 2; await Task.Delay(100);
            Check(settings.LlmModel == "qwen3-8b" && LlmModelFile.EndsWith("Qwen3-8B-Q4_K_M.gguf"), "choosing Qwen3 8B changes the model file: " + LlmModelFile);
            bool eightThere = LocalLlmModels.Find("qwen3-8b")!.IsDownloaded(root);
            Check(eightThere ? state.Text.Contains("ダウンロード済み") : state.Text.Contains("まだダウンロードしていません") && download.Visibility == Visibility.Visible && download.IsEnabled, "8B download state: " + state.Text);

            selector.SelectedIndex = LocalLlmModels.All.Length; await Task.Delay(100);
            Check(settings.LlmModel == LocalLlmModels.Custom && customPanel.Visibility == Visibility.Visible && download.Visibility == Visibility.Collapsed, "your own file: path box shown, download hidden");
            Check(LlmModelFile == LocalLlmModels.Find(LocalLlmModels.Default)!.PathIn(root), "no file chosen yet: Qwen3 4B is used");
            await File.WriteAllTextAsync(fake, "not a real model");
            ((TextBox)customPanel.Children[0]).Text = fake; await Task.Delay(100);
            Check(LlmModelFile == fake && state.Text.Contains("指定したGGUFファイル"), "your own file is used: " + LlmModelFile);
            await navigation.CaptureAsync(Path.Combine(root, "artifacts", "settings-llm-models-custom.png"));

            selector.SelectedIndex = 1; await Task.Delay(100);
            Check(settings.LlmModel == LocalLlmModels.Default && !download.IsEnabled, "back to Qwen3 4B; nothing to download");
            Check(Settings_Reloaded().LlmModel == LocalLlmModels.Default, "choice saved to settings");
        }
        catch (Exception ex) { failures.Add(ex.Message); log.Add(ex.ToString()); }
        finally
        {
            settings.LlmModel = originalModel; settings.LlmModelPath = originalPath; settings.Save();
            try { File.Delete(fake); } catch (IOException) { }
            File.WriteAllText(report, (failures.Count == 0 ? "PASS" : "FAIL: " + string.Join("; ", failures)) + Environment.NewLine + string.Join(Environment.NewLine, log));
            Environment.ExitCode = failures.Count == 0 ? 0 : 1;
            await ShutdownAsync();
        }
    }
    private static Locaptio.App.Settings Settings_Reloaded() => Locaptio.App.Settings.Load();
}
