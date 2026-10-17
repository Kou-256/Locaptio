using System.Diagnostics;
using Locaptio.App;
using Locaptio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace Locaptio.WinUI;
public sealed partial class MainWindow
{
    private async Task VerifyOpacityAsync()
    {
        bool passed = false; string report = "";
        var original = (settings.BackgroundOpacity, settings.TextOpacity, settings.CaptionOpacity, settings.CaptionTextOpacity, settings.CaptionMode, settings.CaptionPalette, settings.CaptionBackground, settings.CaptionCorrectedSameColor, settings.CaptionWindowLeft, settings.CaptionWindowTop, settings.CaptionWindowWidth, settings.CaptionWindowHeight);
        Window? underlay = null;
        try
        {
            settings.CaptionMode = "translate"; settings.CaptionPalette = "white"; settings.CaptionBackground = "live"; settings.CaptionCorrectedSameColor = true;
            settings.BackgroundOpacity = settings.CaptionOpacity = .3; settings.TextOpacity = settings.CaptionTextOpacity = 1;
            settings.CaptionWindowLeft = AppWindow.Position.X; settings.CaptionWindowTop = AppWindow.Position.Y + 200;
            settings.CaptionWindowWidth = AppWindow.Size.Width; settings.CaptionWindowHeight = 350;
            underlay = new Window { Title = "Transparency test backdrop", Content = new Grid { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 100, 220)) } };
            underlay.AppWindow.MoveAndResize(new RectInt32(AppWindow.Position.X - 20, AppWindow.Position.Y - 20, AppWindow.Size.Width + 40, AppWindow.Size.Height + 40));
            underlay.Activate(); Activate();
            ClearCaptions();
            var sample = new CaptionUpdate(1, 1, "White text remains fully opaque.", "背景だけを透過し、白い文字は不透明に保ちます。", true, Stopwatch.GetTimestamp(), 0, 0);
            UpdateHistory(sample); captions[sample.Id] = sample; RenderCaptions();
            CaptionClick(CaptionButton, new RoutedEventArgs()); ((OverlappedPresenter)AppWindow.Presenter).Restore(); Activate();
            captionWindow!.Update([sample]); await Task.Delay(250);
            bool defaults = new Settings().BackgroundOpacity == .3 && new Settings().CaptionOpacity == .3 && new Settings().TextOpacity == 1 && new Settings().CaptionTextOpacity == 1 && new Settings().CaptionTextColor == "#FFFFFF";
            bool independent = Math.Abs(Root.Background.Opacity - .3) < .001 && RealizedCaptionRows.SelectMany(g => g.Children.OfType<TextBlock>()).All(t => t.Opacity == 1) && captionWindow.HasIndependentOpacity();
            settings.BackgroundOpacity = .65; settings.TextOpacity = .4; settings.CaptionOpacity = .8; settings.CaptionTextOpacity = .55; ApplySettings();
            bool adjusted = Math.Abs(Root.Background.Opacity - .65) < .001 && RealizedCaptionRows.SelectMany(g => g.Children.OfType<TextBlock>()).All(t => Math.Abs(t.Opacity - .4) < .001) && captionWindow.HasIndependentOpacity();
            var reloaded = Settings.Load(); bool persisted = reloaded.BackgroundOpacity == .65 && reloaded.TextOpacity == .4 && reloaded.CaptionOpacity == .8 && reloaded.CaptionTextOpacity == .55;
            settings.BackgroundOpacity = settings.CaptionOpacity = .3; settings.TextOpacity = settings.CaptionTextOpacity = 1; ApplySettings();
            SettingsClick(this, new RoutedEventArgs()); await Task.Delay(100);
            var controls = (StackPanel)((Grid)settingsWindow!.Content).Children.OfType<ScrollViewer>().Single().Content;
            string labels = string.Join("|", controls.Children.OfType<TextBlock>().Select(t => t.Text));
            bool sliders = new[] { "本体：背景の透過率", "本体：文字の不透明度", "字幕：背景の透過率", "字幕：文字の不透明度" }.All(labels.Contains);
            settingsWindow.Close();
            passed = defaults && independent && adjusted && persisted && sliders && SystemBackdrop is TransparentWindowBackdrop && WindowAppearance.IsOpaque(hwnd) && captionWindow.HasNoNativeFrame() && captionWindow.HasTextOnlyShape();
            report = $"PASS={passed}; default70PercentTransparentWhiteOpaque={defaults}; backgroundAndTextIndependent={independent}; fourValuesAdjustable={adjusted}; persisted={persisted}; fourSliders={sliders}; perPixelComposition={SystemBackdrop is TransparentWindowBackdrop}; noWholeWindowAlpha={WindowAppearance.IsOpaque(hwnd)}; noCaptionFrame={captionWindow.HasNoNativeFrame()}";
            if (Environment.GetCommandLineArgs().Contains("--preview-opacity"))
            {
                Title = "Locaptio — Transparency preview";
                for (int i = 0; i < 60; i++) { captionWindow.Update([sample with { Revision = i + 2 }]); await Task.Delay(1000); }
            }
        }
        catch (Exception ex) { report = ex.ToString(); }
        finally
        {
            (settings.BackgroundOpacity, settings.TextOpacity, settings.CaptionOpacity, settings.CaptionTextOpacity, settings.CaptionMode, settings.CaptionPalette, settings.CaptionBackground, settings.CaptionCorrectedSameColor, settings.CaptionWindowLeft, settings.CaptionWindowTop, settings.CaptionWindowWidth, settings.CaptionWindowHeight) = original;
            underlay?.Close();
            File.WriteAllText(Path.Combine(root, "artifacts", "opacity-verification.txt"), report);
            Environment.ExitCode = passed ? 0 : 1; await ShutdownAsync();
        }
    }
}
