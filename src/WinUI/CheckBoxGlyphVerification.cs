using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Locaptio.WinUI;

// --verify-checkbox-glyph: every kind of checkbox (main toolbar, settings window, dialog) uses the static
// check mark rather than the animation that can stay on its dash frame, and still draws checked/unchecked.
public sealed partial class MainWindow
{
    private async Task VerifyCheckBoxGlyphAsync()
    {
        string shots = Path.Combine(root, "artifacts", "checkbox-glyph"); Directory.CreateDirectory(shots);
        string report = Path.Combine(root, "artifacts", "checkbox-glyph-verification.txt");
        bool correction = settings.CorrectTranscriptWithAi;
        var log = new List<string>(); var failures = new List<string>();
        void Check(bool condition, string name) { log.Add((condition ? "ok   " : "FAIL ") + name); if (!condition) failures.Add(name); }
        try
        {
            await Task.Delay(300);
            Check(CheckBoxGlyph.IsStatic(AiCorrectionToggle), "toolbar AI補正 uses the static check mark");
            for (int i = 0; i < 6; i++) { AiCorrectionToggle.IsChecked = i % 2 == 0; await Task.Delay(30); }
            AiCorrectionToggle.IsChecked = true; await Task.Delay(300);
            await CaptureComposedAsync(Path.Combine(shots, "toolbar-checked.png"));
            Check(CheckBoxGlyph.IsStatic(AiCorrectionToggle) && AiCorrectionToggle.IsChecked == true, "still static after rapid toggling");

            SettingsClick(this, new RoutedEventArgs()); await Task.Delay(400);
            settingsNavigation!.Select("AI文字補正"); await Task.Delay(300);
            var boxes = settingsNavigation.Page("AI文字補正").Children.OfType<CheckBox>().ToArray();
            Check(boxes.Length > 0 && boxes.All(CheckBoxGlyph.IsStatic), $"settings checkboxes use the static check mark ({boxes.Length})");
            await settingsNavigation.CaptureAsync(Path.Combine(shots, "settings.png"));
            settingsWindow?.Close(); await Task.Delay(300);

            var inDialog = new CheckBox { Content = "ダイアログのチェックボックス", IsChecked = true };
            var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "確認", Content = inDialog, CloseButtonText = "閉じる", RequestedTheme = Root.RequestedTheme };
            var showing = dialog.ShowAsync(); await Task.Delay(500);
            Check(CheckBoxGlyph.IsStatic(inDialog), "dialog checkboxes use the static check mark");
            await CaptureComposedAsync(Path.Combine(shots, "dialog.png"));
            dialog.Hide(); try { await showing; } catch { }
        }
        catch (Exception ex) { failures.Add(ex.Message); log.Add(ex.ToString()); }
        finally
        {
            settings.CorrectTranscriptWithAi = correction; AiCorrectionToggle.IsChecked = correction; settings.Save();
            File.WriteAllText(report, (failures.Count == 0 ? "PASS" : "FAIL: " + string.Join("; ", failures)) + Environment.NewLine + string.Join(Environment.NewLine, log));
            Environment.ExitCode = failures.Count == 0 ? 0 : 1;
            await ShutdownAsync();
        }
    }
}
