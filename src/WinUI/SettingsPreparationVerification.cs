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
            var navigation = settingsNavigation!;
            var scroll = navigation.Scroll;
            string[] names = ["本体の表示", "字幕の見た目", "音声認識・Windows連携", "AI文字補正", "話者識別・色分け", "翻訳・ローカルLLM", "保存先・録音", "アプリの更新", "診断"];
            var sections = ((StackPanel)scroll.Content).Children.OfType<StackPanel>().ToArray();
            bool categorized = navigation.Categories.SequenceEqual(names) && navigation.Selected == names[0]
                && sections.Count(p => p.Visibility == Visibility.Visible) == 1;
            navigation.Select(names[2]); await Task.Delay(200);
            var speech = navigation.Page(names[2]);
            bool modelControls = speech.Children.OfType<DownwardSelector>().Count() >= 4 && navigation.Selected == names[2];
            bool aiGroup = navigation.Page(names[3]).Children.Contains(settingsCorrectionToggle!);
            bool gutter = ((StackPanel)scroll.Content).Padding.Right >= 32 && speech.ActualWidth < scroll.ActualWidth;
            bool svgIcons = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Assets", "Settings"), "*.svg").Length == 9;
            for (int i = 0; i < names.Length; i++)
            {
                navigation.Select(names[i]); await Task.Delay(20);
                categorized &= sections.Count(p => p.Visibility == Visibility.Visible) == 1 && sections[i].Visibility == Visibility.Visible;
            }
            categorized &= svgIcons;
            navigation.Select(names[1]); await Task.Delay(150);
            await navigation.CaptureAsync(Path.Combine(root, "artifacts", "settings-navigation-1.0.36.png"));
            passed = halfway && complete && waitsForTranslation && translationCompleteHidden && staleCompletionIgnored && failureVisible && categorized && modelControls && aiGroup && gutter;
            report = $"PASS={passed}; remainingPercent={halfway}; completionHidden={complete}; waitsForTranslationAndAi={waitsForTranslation}; translationCompleteHidden={translationCompleteHidden}; staleCompletionIgnored={staleCompletionIgnored}; failureVisible={failureVisible}; categories={categorized}; modelsGrouped={modelControls}; aiGrouped={aiGroup}; scrollbarGutter={gutter}; count={sections.Length}";
            if (Environment.GetCommandLineArgs().Contains("--verify-update-location"))
            {
                navigation.Select("アプリの更新"); await Task.Delay(150);
                var page = navigation.Page("アプリの更新");
                bool versionShown = page.Children.OfType<TextBlock>().Any(t => t.Text == "現在のバージョン: " + CurrentAppVersion);
                var notice = page.Children.OfType<InfoBar>().Single();
                var button = page.Children.OfType<Button>().Single(b => (string)b.Content == "アプリの更新を確認");
                var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button);
                ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                await Task.Delay(200);
                if (updateOperation is not null) await updateOperation;
                await Task.Delay(150);
                bool localResult = notice.IsOpen && notice.Title == "更新の確認が完了しました" && notice.Message.Contains(CurrentAppVersion) && !UpdateNotice.IsOpen;
                passed &= versionShown && localResult;
                report += $"; updateVersionShown={versionShown}; resultInSettingsOnly={localResult}; title={notice.Title}; message={notice.Message}";
                if (!passed) report = "PASS=False; " + report;
                await navigation.CaptureAsync(Path.Combine(root, "artifacts", "settings-update-1.0.37.png"));
            }
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
