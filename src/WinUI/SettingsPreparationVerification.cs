using Locaptio.Core.AI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Locaptio.WinUI;

public sealed partial class MainWindow
{
    private async Task VerifySettingsPreparationAsync()
    {
        bool passed = false; string report = "";
        try
        {
            var progress = BeginPreparation();
            progress(new("AMD NPU", 3, 6, "encoderのコンパイル中")); await Task.Delay(150);
            bool halfway = PreparationPanel.Visibility == Visibility.Visible && PreparationBar.Value == 50 && PreparationText.Text.Contains("残り工程 50%") && PreparationText.Text.Contains("3/6");
            progress(new("AMD NPU", 6, 6, "準備完了")); await Task.Delay(100);
            bool complete = PreparationBar.Value == 100 && PreparationText.Text.Contains("準備完了") && PreparationPanel.Visibility == Visibility.Collapsed;
            // English -> Japanese initializes recognition, translation and optional AI independently.
            var translationProgress = BeginPreparation();
            translationProgress(new("音声認識", 0, 3, "準備中"));
            translationProgress(new("翻訳モデル", 0, 3, "準備中"));
            translationProgress(new("文脈AI", 0, 2, "準備中"));
            translationProgress(new("音声認識", 3, 3, "準備完了")); await Task.Delay(100);
            bool waitsForTranslation = PreparationPanel.Visibility == Visibility.Visible && PreparationBar.Value < 100;
            translationProgress(new("翻訳モデル", 3, 3, "準備完了")); await Task.Delay(100);
            waitsForTranslation &= PreparationPanel.Visibility == Visibility.Visible && PreparationBar.Value < 100;
            translationProgress(new("文脈AI", 2, 2, "準備完了")); await Task.Delay(100);
            bool translationCompleteHidden = PreparationBar.Value == 100 && PreparationPanel.Visibility == Visibility.Collapsed;
            UpdatePreparationDisplay();
            translationCompleteHidden &= PreparationPanel.Visibility == Visibility.Collapsed;
            var restartedProgress = BeginPreparation();
            restartedProgress(new("音声認識", 1, 2, "準備中"));
            translationProgress(new("文脈AI", 2, 2, "遅れて届いた完了")); await Task.Delay(100);
            bool staleCompletionIgnored = PreparationBar.Value == 50 && PreparationPanel.Visibility == Visibility.Visible;
            restartedProgress(new("音声認識", 1, 2, "検証失敗", true)); await Task.Delay(100);
            bool failureVisible = PreparationPanel.Visibility == Visibility.Visible && PreparationText.Text.Contains("失敗");
            BeginPreparation();
            SettingsClick(this, new RoutedEventArgs()); await Task.Delay(150);
            var panel = (Grid)settingsWindow!.Content;
            var scroll = panel.Children.OfType<ScrollViewer>().Single();
            var sections = ((StackPanel)scroll.Content).Children.OfType<Expander>().ToArray();
            string[] names = ["本体の表示", "字幕の見た目", "音声認識・Windows連携", "AI文字補正", "話者識別・色分け", "翻訳・ローカルLLM", "保存先・録音", "診断"];
            bool categorized = sections.Select(e => (string)e.Header).SequenceEqual(names) && sections.All(e => !e.IsExpanded);
            var speech = sections[2]; speech.IsExpanded = true; await Task.Delay(200);
            var speechControls = ((StackPanel)speech.Content).Children;
            bool modelControls = speechControls.OfType<DownwardSelector>().Count() >= 4;
            bool aiGroup = ((StackPanel)sections[3].Content).Children.Contains(settingsCorrectionToggle!);
            bool gutter = ((StackPanel)scroll.Content).Padding.Right >= 32 && ((StackPanel)speech.Content).ActualWidth < scroll.ActualWidth;
            passed = halfway && complete && waitsForTranslation && translationCompleteHidden && staleCompletionIgnored && failureVisible && categorized && modelControls && aiGroup && gutter;
            report = $"PASS={passed}; remainingPercent={halfway}; completionHidden={complete}; waitsForTranslationAndAi={waitsForTranslation}; translationCompleteHidden={translationCompleteHidden}; staleCompletionIgnored={staleCompletionIgnored}; failureVisible={failureVisible}; categories={categorized}; modelsGrouped={modelControls}; aiGrouped={aiGroup}; scrollbarGutter={gutter}; count={sections.Length}";
            if (Environment.GetCommandLineArgs().Contains("--preview-settings-preparation")) await Task.Delay(20000);
        }
        catch (Exception ex) { report = ex.ToString(); }
        finally
        {
            File.WriteAllText(Path.Combine(root, "artifacts", "settings-preparation-verification.txt"), report);
            Environment.ExitCode = passed ? 0 : 1; await ShutdownAsync();
        }
    }
}
