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
            // An "on" switch has a white knob in both themes (the dark theme's default knob is black).
            static Border? Knob(DependencyObject node)
            {
                if (node is Border { Name: "SwitchKnobOn" } knob) return knob;
                for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node); i++)
                    if (Knob(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i)) is { } found) return found;
                return null;
            }
            var originalTheme = Root.RequestedTheme; var knobColors = new List<string>();
            foreach (var theme in new[] { ElementTheme.Dark, ElementTheme.Light })
            {
                WindowAppearance.SetTheme(Root, theme); await Task.Delay(250);
                knobColors.Add(Knob(AiCorrectionToggle.Switch)?.Background is Microsoft.UI.Xaml.Media.SolidColorBrush brush ? brush.Color.ToString() : "none");
                if (theme == ElementTheme.Dark) await CaptureComposedAsync(Path.Combine(shots, "toolbar-switches-dark.png"));
            }
            WindowAppearance.SetTheme(Root, originalTheme); await Task.Delay(250);
            Check(knobColors.All(c => c == "#FFFFFFFF"), "switch knob is white when on, dark and light: " + string.Join(", ", knobColors));
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

            // Crash report: written with the trail of recent actions, shown once on the next launch (the user's own report is kept).
            string existing = File.Exists(CrashReport.FilePath) ? File.ReadAllText(CrashReport.FilePath) : "";
            try
            {
                Status("見た目を変更しました（テスト）");
                await Task.Delay(100);
                CrashReport.Write(new System.Runtime.InteropServices.COMException("テストの例外", unchecked((int)0x8000FFFF)), "UI");
                string written = File.ReadAllText(CrashReport.FilePath);
                Check(written.Contains("0x8000FFFF") && written.Contains("見た目を変更しました（テスト）") && written.Contains("Version:"), "crash report has the error, version and recent actions");
                Check(CrashReport.Unseen() is not null, "a new report is shown on the next launch");
                CrashReport.MarkSeen();
                Check(CrashReport.Unseen() is null, "and only once");
            }
            finally { if (existing.Length > 0) File.WriteAllText(CrashReport.FilePath, existing); else File.Delete(CrashReport.FilePath); CrashReport.MarkSeen(); }
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
