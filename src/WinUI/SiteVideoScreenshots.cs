using System.Drawing;
using System.Drawing.Imaging;
using Locaptio.Core;
using Microsoft.UI.Xaml;
using Windows.Media.Editing;
using Windows.Storage;

namespace Locaptio.WinUI;

// Website screenshots of the recording features. Recording targets and the recorded video use
// drawn sample screens (slides, a browser), so no real screen or window content is shown.
public sealed partial class MainWindow
{
    private static bool IsBlack(string png)
    {
        using var image = new Bitmap(png);
        for (int y = 0; y < image.Height; y += Math.Max(1, image.Height / 40))
            for (int x = 0; x < image.Width; x += Math.Max(1, image.Width / 40)) { var c = image.GetPixel(x, y); if (c.R + c.G + c.B > 30) return false; }
        return true;
    }
    private static async Task<Bitmap> RenderElementAsync(Microsoft.UI.Xaml.UIElement element)
    {
        var target = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap(); await target.RenderAsync(element);
        var buffer = await target.GetPixelsAsync();
        var pixels = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(buffer);
        var bitmap = new Bitmap(target.PixelWidth, target.PixelHeight, PixelFormat.Format32bppPArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        for (int y = 0; y < bitmap.Height; y++) System.Runtime.InteropServices.Marshal.Copy(pixels, y * bitmap.Width * 4, data.Scan0 + y * data.Stride, bitmap.Width * 4);
        bitmap.UnlockBits(data);
        return bitmap;
    }
    /// <summary>The window drawn by XAML, with open dialogs and menus layered where they appear on screen.</summary>
    private async Task CaptureComposedAsync(string file)
    {
        using var window = await RenderElementAsync(Root);
        double scale = window.Width / Math.Max(1, Root.ActualWidth);
        using var composed = new Bitmap(window.Width, window.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(composed))
        {
            g.Clear(System.Drawing.Color.FromArgb(54, 54, 54)); g.DrawImage(window, 0, 0, window.Width, window.Height);
            foreach (var popup in Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot))
            {
                if (popup.Child is not Microsoft.UI.Xaml.FrameworkElement child || child.ActualWidth <= 0) continue;
                var origin = child.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point());
                using var layer = await RenderElementAsync(child);
                g.DrawImage(layer, (float)(origin.X * scale), (float)(origin.Y * scale), layer.Width, layer.Height);
            }
        }
        composed.Save(file, ImageFormat.Png);
    }
    /// <summary>A sample screen drawn with GDI+: "slide", "browser" or "desktop".</summary>
    private static Bitmap SampleScreen(string kind, int width, int height, string title)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        float u = height / 100f;
        using var heading = new Font("Yu Gothic UI", 7 * u, FontStyle.Bold, GraphicsUnit.Pixel);
        using var body = new Font("Yu Gothic UI", 4 * u, FontStyle.Regular, GraphicsUnit.Pixel);
        using var accent = new SolidBrush(Color.FromArgb(22, 123, 119));
        using var soft = new SolidBrush(Color.FromArgb(220, 236, 232));
        if (kind == "desktop")
        {
            using var sky = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, width, height), Color.FromArgb(40, 96, 140), Color.FromArgb(120, 190, 210), 60f);
            g.FillRectangle(sky, 0, 0, width, height);
            using var slide = SampleScreen("slide", width / 2, height / 2, title);
            g.FillRectangle(Brushes.Black, width / 8 - 3, height / 8 - 3, width / 2 + 6, height / 2 + 6); g.DrawImage(slide, width / 8, height / 8);
            using var browser = SampleScreen("browser", width / 3, height / 3, "オンライン講義");
            g.DrawImage(browser, width * 9 / 16, height * 7 / 12);
            g.FillRectangle(new SolidBrush(Color.FromArgb(230, 32, 32, 32)), 0, height - 6 * u, width, 6 * u);
            return bitmap;
        }
        if (kind == "browser")
        {
            g.Clear(Color.FromArgb(245, 247, 250));
            g.FillRectangle(new SolidBrush(Color.FromArgb(222, 226, 232)), 0, 0, width, 9 * u);
            g.FillEllipse(Brushes.IndianRed, 2 * u, 3 * u, 3 * u, 3 * u);
            g.FillRectangle(Brushes.White, 12 * u, 2 * u, width - 24 * u, 5 * u);
            g.FillRectangle(new SolidBrush(Color.FromArgb(30, 30, 30)), 6 * u, 14 * u, width - 12 * u, 60 * u);
            var play = new[] { new PointF(width / 2f - 5 * u, 36 * u), new PointF(width / 2f - 5 * u, 52 * u), new PointF(width / 2f + 7 * u, 44 * u) };
            g.FillPolygon(Brushes.White, play);
            g.DrawString(title, heading, Brushes.Black, 6 * u, 78 * u);
            return bitmap;
        }
        g.Clear(Color.White);
        g.FillRectangle(accent, 0, 0, width, 3 * u);
        g.DrawString(title, heading, new SolidBrush(Color.FromArgb(24, 55, 59)), 6 * u, 8 * u);
        string[] lines = ["自動保存と編集はほぼ完成", "要約機能は社内テスト中", "リリースは来月15日"];
        for (int i = 0; i < lines.Length; i++)
        {
            g.FillEllipse(accent, 7 * u, (27 + i * 11) * u, 2.5f * u, 2.5f * u);
            g.DrawString(lines[i], body, new SolidBrush(Color.FromArgb(60, 80, 82)), 11 * u, (25.5f + i * 11) * u);
        }
        float[] bars = [.45f, .7f, .55f, .9f];
        for (int i = 0; i < bars.Length; i++) g.FillRectangle(i == 3 ? accent : soft, width * .62f + i * 9 * u, (85 - 50 * bars[i]) * u, 6 * u, 50 * bars[i] * u);
        return bitmap;
    }
    private static (byte[] Pixels, int Width, int Height) Pixels(Bitmap bitmap)
    {
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new byte[bitmap.Width * bitmap.Height * 4];
            for (int y = 0; y < bitmap.Height; y++) System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * bitmap.Width * 4, bitmap.Width * 4);
            return (pixels, bitmap.Width, bitmap.Height);
        }
        finally { bitmap.UnlockBits(data); }
    }

    private async Task CaptureVideoScreensAsync(string work, Func<string, int, Task> shot, Func<string, Func<Task>, Task> dialogShot, Action<string> reset, Action<IEnumerable<(string, string?)>, string> feed)
    {
        // Recording target picker with sample targets.
        var display = new CaptureTarget(true, "SAMPLE", 0, "ディスプレイ1", 1920, 1080);
        var slides = new CaptureTarget(false, "", 1, "定例会議資料 - プレゼンテーション", 1600, 900);
        var browser = new CaptureTarget(false, "", 2, "オンライン講義 - ブラウザー", 1280, 800);
        var notes = new CaptureTarget(false, "", 3, "議事メモ - メモ帳", 900, 700);
        targetsForVerification = [display, slides, browser, notes];
        snapshotForVerification = (target, width) =>
        {
            int height = (int)(width * (double)target.Height / target.Width);
            using var image = SampleScreen(target == display ? "desktop" : target == browser ? "browser" : "slide", width, height, target == notes ? "議事メモ" : "定例会議：リリース日程");
            return Pixels(image);
        };
        reset("japanese");
        await dialogShot("record-picker", async () => await ChooseCaptureAsync());

        // A recording in progress, with its live preview and controls.
        reset("japanese"); feed(SiteMeeting.Take(5).Select(t => (t, (string?)null)), "ja");
        VideoToggle.IsChecked = true; VideoToggleText.Text = "録画中"; RecordToggle.IsEnabled = false; StopButton.IsEnabled = true; StartButton.IsEnabled = false;
        RecordingIndicator.Visibility = LivePreviewFrame.Visibility = VideoControls.Visibility = Visibility.Visible;
        RecordingLabel.Text = "● 録画中"; RecordingClock.Text = "00:08:15"; RecordingText.Text = @"C:\Users\…\Music\Locaptio\定例会議-20261117\定例会議.mp4";
        LivePreview.Source = CaptureTargets.ToBitmap(snapshotForVerification(slides, 320)!.Value);
        SessionTitle.Text = "新規録画中：定例会議-20261117（定例会議資料 - プレゼンテーション）";
        StatusText.Text = "録画を開始しました：定例会議資料 - プレゼンテーション。文字起こしも同時に行います。";
        await shot("video-recording", 600);
        VideoToggle.IsChecked = false; VideoToggleText.Text = "新規録画"; RecordToggle.IsEnabled = true; RecordingIndicator.Visibility = LivePreviewFrame.Visibility = VideoControls.Visibility = Visibility.Collapsed; RecordingLabel.Text = "● 録音中";
        targetsForVerification = null; snapshotForVerification = null;

        // A recorded video (made from sample slides) replayed with its transcript and caption.
        string folder = Path.Combine(work, "定例会議-録画"); Directory.CreateDirectory(folder);
        string video = Path.Combine(folder, "定例会議.mp4");
        var composition = new MediaComposition();
        foreach (var (title, seconds) in new[] { ("定例会議：リリース日程", 32.0), ("TODOと担当", 34.0) })
        {
            string image = Path.Combine(work, Guid.NewGuid().ToString("N") + ".png");
            using (var bitmap = SampleScreen("slide", 1280, 720, title)) bitmap.Save(image, ImageFormat.Png);
            composition.Clips.Add(await MediaClip.CreateFromImageFileAsync(await StorageFile.GetFileFromPathAsync(image), TimeSpan.FromSeconds(seconds)));
        }
        await File.WriteAllBytesAsync(video, []);
        await composition.RenderToFileAsync(await StorageFile.GetFileFromPathAsync(video), MediaTrimmingPreference.Fast);
        double[] starts = SiteMeeting.Select((_, i) => 1.0 + i * 6.4).ToArray();
        var phrases = SiteMeeting.Select((text, i) => new RecordingPhrase(i + 1, i + 1, starts[i], text, null, 1, "ja", starts[i] + 5.6,
            PlaybackHighlight.Tokens(text).Select(t => new RecordingWord(t.Index, t.Length, starts[i] + 5.6 * t.Index / text.Length, starts[i] + 5.6 * (t.Index + t.Length) / text.Length)).ToArray())).ToArray();
        await RecordingTranscript.SaveAsync(video, new(1, "定例会議.mp4", DateTimeOffset.Now, false, phrases, [new(starts[3] + .2, "重要", 4)], null, "large-v3-turbo"));
        reset("japanese"); await LoadRecordingAsync(video);
        for (int i = 0; i < 30 && playbackReader is VideoPlaybackClock { TotalTime.TotalSeconds: < 1 }; i++) await Task.Delay(100);
        if (playbackReader is not null) { playbackReader.CurrentTime = TimeSpan.FromSeconds(starts[3] + 2); await Task.Delay(600); UpdatePlaybackPosition(); }
        StatusText.Text = "文字や検索結果をクリックすると、その発話から再生します。";
        using (var frame = SampleScreen("slide", 1280, 720, "定例会議：リリース日程")) VideoStill.Source = CaptureTargets.ToBitmap(Pixels(frame));
        VideoStill.Visibility = Visibility.Visible; UpdateCommandStates();
        await shot("video-review", 900);
        VideoStill.Visibility = Visibility.Collapsed; VideoStill.Source = null;
        await dialogShot("video-burn", async () => { BurnSubtitlesClick(this, new RoutedEventArgs()); await Task.Delay(1500); });
        ClosePlayback();
    }
}
