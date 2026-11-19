using System.Diagnostics;
using Locaptio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Locaptio.WinUI;

// --verify-appearance-stress: changes caption and window appearance continuously from the Settings window while
// captions keep arriving (main list and caption window), the way a user adjusts the look during transcription.
// A crash ends the process; App writes logs/crash.txt.
public sealed partial class MainWindow
{
    private async Task VerifyAppearanceStressAsync()
    {
        string report = Path.Combine(root, "artifacts", "appearance-stress.txt");
        string saved = System.Text.Json.JsonSerializer.Serialize(settings);
        var log = new List<string>(); int updates = 0, changes = 0;
        try
        {
            string early = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--part="))?[7..] ?? "all";
            if (early.StartsWith("bare"))
            {
                // Theme switches with nothing else going on; "bare-rows" adds transcript rows first.
                string bareTrail = Path.Combine(root, "artifacts", "appearance-stress-trail.txt"); File.WriteAllText(bareTrail, "");
                if (early == "bare-rows") { captionStop = new(); for (int i = 1; i <= 20; i++) UpdateCaption(new CaptionUpdate(i, 1, "Row " + i, "行 " + i, true, Stopwatch.GetTimestamp(), 0, 0, "en")); captionStop.Dispose(); captionStop = null; }
                for (int i = 0; i < 60; i++) { File.AppendAllText(bareTrail, "theme " + i + Environment.NewLine); WindowAppearance.SetTheme(Root, i % 2 == 0 ? ElementTheme.Light : ElementTheme.Dark); await Task.Delay(60); }
                log.Add("PASS: bare theme switching"); return;
            }
            ClearCaptions(); settings.CaptionMode = "en>ja"; ApplySettings();
            captionStop = new();
            CaptionClick(this, new RoutedEventArgs()); await Task.Delay(500);
            SettingsClick(this, new RoutedEventArgs()); await Task.Delay(600);
            settingsNavigation!.Select("字幕の見た目"); await Task.Delay(400);
            var page = settingsNavigation.Page("字幕の見た目");
            var sliders = page.Children.OfType<Slider>().ToArray();
            var selectors = page.Children.OfType<DownwardSelector>().ToArray();
            log.Add($"sliders={sliders.Length}, selectors={selectors.Length}");
            var random = new Random(7); long id = 0;
            var feed = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
            feed.Tick += (_, _) =>
            {
                id++; updates++;
                bool final = id % 3 == 0;
                UpdateCaption(new CaptionUpdate(id / 3 + 1, (int)(id % 3) + 1, $"Sentence {id / 3} is still being recognized, word {id % 3}.", final ? $"文 {id / 3} の訳です。" : null, final, Stopwatch.GetTimestamp(), 0, 0, "en", TranslationRefined: id % 6 == 0));
            };
            feed.Start();
            var clock = Stopwatch.StartNew();
            string trail = Path.Combine(root, "artifacts", "appearance-stress-trail.txt"); File.WriteAllText(trail, "");
            string part = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--part="))?[7..] ?? "all";
            void Mark(string what) { File.AppendAllText(trail, what + Environment.NewLine); }
            while (clock.Elapsed < TimeSpan.FromSeconds(25) && !closing)
            {
                // Drag-like slider changes, palette and background switches, a layout switch now and then.
                if (part is "all" or "sliders") foreach (var slider in sliders) { Mark("slider " + Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(slider) + " " + Array.IndexOf(sliders, slider)); slider.Value = slider.Minimum + random.NextDouble() * (slider.Maximum - slider.Minimum); changes++; await Task.Delay(15); }
                if (part is "all" or "selectors") foreach (var selector in selectors) { Mark("selector " + Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(selector)); int count = ((System.Collections.IEnumerable)selector.ItemsSource!).Cast<object>().Count(); selector.SelectedIndex = random.Next(count); changes++; await Task.Delay(25); }
                if (part is "theme" or "theme-nofeed") { Mark("theme"); WindowAppearance.SetTheme(Root, Root.RequestedTheme == ElementTheme.Light ? ElementTheme.Dark : ElementTheme.Light); changes++; }
                if (part == "theme-nofeed") feed.Stop();
                if (part == "caption-theme") { Mark("caption window only"); settings.CaptionBackground = settings.CaptionBackground == "light" ? "live" : "light"; captionWindow?.ApplyAppearance(); changes++; }
                if (part is "all" or "background") { Mark("background"); settings.CaptionBackground = random.Next(3) == 0 ? "light" : "live"; ApplySettings(); changes++; }
                await Task.Delay(20);
            }
            feed.Stop();
            log.Add($"PASS: {changes} appearance changes during {updates} caption updates without a crash");
        }
        catch (Exception ex) { log.Add("FAIL: " + ex); }
        finally
        {
            captionStop?.Dispose(); captionStop = null;
            if (System.Text.Json.JsonSerializer.Deserialize<Locaptio.App.Settings>(saved) is { } original)
                foreach (var property in typeof(Locaptio.App.Settings).GetProperties().Where(p => p.CanWrite)) property.SetValue(settings, property.GetValue(original));
            settings.Save();
            File.WriteAllText(report, string.Join(Environment.NewLine, log));
            Environment.ExitCode = log.Any(l => l.StartsWith("FAIL")) ? 1 : 0;
            await ShutdownAsync();
        }
    }
}
