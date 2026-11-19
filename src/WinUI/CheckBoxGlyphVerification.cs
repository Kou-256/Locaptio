using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Locaptio.WinUI;

// --verify-checkbox-glyph: feature on/off controls are switches (toolbar and settings), item lists keep checkboxes
// (toolbar items), and changing the light/dark theme with both on screen does not close the app.
public sealed partial class MainWindow
{
    private async Task VerifyCheckBoxGlyphAsync()
    {
        string shots = Path.Combine(root, "artifacts", "checkbox-glyph"); Directory.CreateDirectory(shots);
        string report = Path.Combine(root, "artifacts", "checkbox-glyph-verification.txt");
        bool correction = settings.CorrectTranscriptWithAi; string background = settings.CaptionBackground;
        var log = new List<string>(); var failures = new List<string>();
        void Check(bool condition, string name) { log.Add((condition ? "ok   " : "FAIL ") + name); if (!condition) failures.Add(name); }
        try
        {
            await Task.Delay(300);
            Check(AiCorrectionToggle is FeatureSwitch && LiveSummaryToggle is FeatureSwitch, "toolbar AI補正 and ライブ要約 are switches");
            for (int i = 0; i < 6; i++) { AiCorrectionToggle.IsChecked = i % 2 == 0; await Task.Delay(30); }
            AiCorrectionToggle.IsChecked = true; await Task.Delay(300);
            Check(AiCorrectionToggle.Switch.IsOn, "the switch follows the setting after rapid changes");
            await CaptureComposedAsync(Path.Combine(shots, "toolbar-switches.png"));

            SettingsClick(this, new RoutedEventArgs()); await Task.Delay(400);
            settingsNavigation!.Select("AI文字補正"); await Task.Delay(300);
            var page = settingsNavigation.Page("AI文字補正");
            Check(page.Children.OfType<FeatureSwitch>().Count() >= 3 && !page.Children.OfType<CheckBox>().Any(), $"settings features are switches ({page.Children.OfType<FeatureSwitch>().Count()})");
            await settingsNavigation.CaptureAsync(Path.Combine(shots, "settings.png"));
            settingsNavigation.Select("本体の表示"); await Task.Delay(300);
            var toolbarList = settingsNavigation.Page("本体の表示").Children.OfType<StackPanel>().SelectMany(p => p.Children.OfType<Grid>()).SelectMany(g => g.Children.OfType<CheckBox>()).ToArray();
            Check(toolbarList.Length > 5, $"the toolbar item list keeps checkboxes ({toolbarList.Length})");

            // Light caption background switches the whole window to the light theme and back (this used to close the app).
            for (int i = 0; i < 6; i++) { settings.CaptionBackground = i % 2 == 0 ? "light" : "live"; ApplySettings(); await Task.Delay(150); }
            Check(!closing, "light/dark theme changes with switches and checkboxes on screen");
            settingsWindow?.Close(); await Task.Delay(300);
        }
        catch (Exception ex) { failures.Add(ex.Message); log.Add(ex.ToString()); }
        finally
        {
            settings.CorrectTranscriptWithAi = correction; AiCorrectionToggle.IsChecked = correction; settings.CaptionBackground = background; ApplySettings(); settings.Save();
            File.WriteAllText(report, (failures.Count == 0 ? "PASS" : "FAIL: " + string.Join("; ", failures)) + Environment.NewLine + string.Join(Environment.NewLine, log));
            Environment.ExitCode = failures.Count == 0 ? 0 : 1;
            await ShutdownAsync();
        }
    }
}
