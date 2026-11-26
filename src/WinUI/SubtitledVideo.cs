using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Locaptio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Media.Editing;
using Windows.Storage;

namespace Locaptio.WinUI;

// Burns the transcript into a copy of a video as subtitles, using the Windows media composition
// API (no external tools). Each subtitle cue becomes a transparent caption image shown for the
// cue's time; the original video is never modified.
public sealed partial class MainWindow
{
    private Func<Task<(string Content, string Size, bool Top, bool Srt, string Path)?>>? burnChoiceForVerification;

    /// <summary>Draws one caption as a PNG sized to its text, with a translucent rounded background.</summary>
    internal static (int Width, int Height) DrawCaption(string text, string file, int videoWidth, int videoHeight, string size)
    {
        float fontPixels = Math.Max(16, videoHeight * (size == "large" ? .058f : size == "small" ? .036f : .046f));
        int maxWidth = (int)(videoWidth * .88), padX = (int)(fontPixels * .6), padY = (int)(fontPixels * .35);
        using var font = new Font(FontFamily.Families.Any(f => f.Name == "Yu Gothic UI") ? "Yu Gothic UI" : "Meiryo", fontPixels, FontStyle.Bold, GraphicsUnit.Pixel);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        SizeF measured;
        using (var probe = new Bitmap(1, 1)) using (var graphics = Graphics.FromImage(probe))
        { graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit; measured = graphics.MeasureString(text, font, maxWidth - padX * 2, format); }
        int width = Math.Min(maxWidth, (int)Math.Ceiling(measured.Width) + padX * 2), height = (int)Math.Ceiling(measured.Height) + padY * 2;
        width += width % 2; height += height % 2;
        using var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias; graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit; graphics.Clear(Color.Transparent);
            float radius = fontPixels * .35f;
            using var path = new GraphicsPath();
            path.AddArc(0, 0, radius * 2, radius * 2, 180, 90); path.AddArc(width - radius * 2 - 1, 0, radius * 2, radius * 2, 270, 90);
            path.AddArc(width - radius * 2 - 1, height - radius * 2 - 1, radius * 2, radius * 2, 0, 90); path.AddArc(0, height - radius * 2 - 1, radius * 2, radius * 2, 90, 90); path.CloseFigure();
            using var background = new SolidBrush(Color.FromArgb(200, 24, 24, 24));
            graphics.FillPath(background, path);
            graphics.DrawString(text, font, Brushes.White, new RectangleF(padX, padY, width - padX * 2, height - padY * 2), format);
        }
        bitmap.Save(file, System.Drawing.Imaging.ImageFormat.Png);
        return (width, height);
    }

    /// <summary>Renders <paramref name="video"/> with the document's subtitles into <paramref name="output"/>.</summary>
    internal static async Task BurnSubtitlesAsync(string video, string output, RecordingDocument document, string content, string size, bool top, IProgress<double>? progress, CancellationToken token)
    {
        string work = Path.Combine(Path.GetTempPath(), "Locaptio-burn-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work);
        try
        {
            var composition = new MediaComposition();
            var clip = await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(video));
            composition.Clips.Add(clip);
            var properties = clip.GetVideoEncodingProperties();
            int videoWidth = (int)properties.Width, videoHeight = (int)properties.Height;
            if (videoWidth <= 0 || videoHeight <= 0) throw new InvalidDataException("動画の映像を読み取れません。");
            var layer = new MediaOverlayLayer(); int index = 0;
            var cues = SubtitleExport.Cues(document, content).Where(c => c.Start < clip.OriginalDuration.TotalSeconds).ToList();
            foreach (var cue in cues)
            {
                token.ThrowIfCancellationRequested();
                string file = Path.Combine(work, $"cue{index++:D5}.png");
                var (width, height) = DrawCaption(cue.Text, file, videoWidth, videoHeight, size);
                double end = Math.Min(cue.End, clip.OriginalDuration.TotalSeconds);
                if (end - cue.Start < .05) continue;
                var image = await MediaClip.CreateFromImageFileAsync(await StorageFile.GetFileFromPathAsync(file), TimeSpan.FromSeconds(end - cue.Start));
                double margin = videoHeight * .06;
                layer.Overlays.Add(new MediaOverlay(image)
                {
                    Position = new Windows.Foundation.Rect((videoWidth - width) / 2.0, top ? margin : videoHeight - height - margin, width, height),
                    Delay = TimeSpan.FromSeconds(cue.Start), Opacity = 1
                });
                progress?.Report(10.0 * index / Math.Max(1, cues.Count));
            }
            composition.OverlayLayers.Add(layer);
            if (File.Exists(output)) File.Delete(output);
            await File.WriteAllBytesAsync(output, [], token);
            // The frame rate the video was recorded at (a still screen records only a few frames; 30 then).
            double fps = properties.FrameRate.Denominator > 0 ? properties.FrameRate.Numerator / (double)properties.FrameRate.Denominator : 0;
            var profile = EncodingProfile(videoWidth, videoHeight, fps >= 10 ? (int)Math.Round(fps) : 30, clip.EmbeddedAudioTracks.Count > 0);
            var render = composition.RenderToFileAsync(await StorageFile.GetFileFromPathAsync(output), MediaTrimmingPreference.Precise, profile);
            render.Progress = (_, percent) => progress?.Report(10 + percent * .9);
            using (token.Register(() => { try { render.Cancel(); } catch (Exception) { } }))
            {
                var result = await render;
                token.ThrowIfCancellationRequested();
                if (result != Windows.Media.Transcoding.TranscodeFailureReason.None) throw new IOException("動画の書き出しに失敗しました: " + result);
            }
        }
        catch { try { if (File.Exists(output) && new FileInfo(output).Length == 0) File.Delete(output); } catch (IOException) { } throw; }
        finally { try { Directory.Delete(work, true); } catch (IOException) { } }
    }

    private async void BurnSubtitlesClick(object sender, RoutedEventArgs e)
    {
        if (dialogOpen || closing || !RequireOpenRecording("字幕付き動画の書き出し")) return;
        if (!PlaybackClock.IsVideo(playbackPath)) { Status("字幕付き動画は、録画や読み込んだ動画を開いているときに書き出せます。"); return; }
        var document = playbackDocument!; string video = playbackPath;
        if (document.Phrases.Length == 0) { Status("字幕にする文字起こしがありません。"); return; }
        (string Content, string Size, bool Top, bool Srt, string Path)? choice;
        if (burnChoiceForVerification is not null) choice = await burnChoiceForVerification();
        else
        {
            dialogOpen = true;
            try
            {
                bool translated = document.Phrases.Any(p => !string.IsNullOrWhiteSpace(p.Translation));
                var text = new RadioButtons { Header = "字幕にする文章", SelectedIndex = settings.SubtitleBurnContent == "translation" && translated ? 1 : settings.SubtitleBurnContent == "both" && translated ? 2 : 0 };
                text.Items.Add(translated ? "原文" : "文字起こし"); if (translated) { text.Items.Add("日本語訳"); text.Items.Add("原文と日本語訳（2段）"); }
                var size = new RadioButtons { Header = "文字の大きさ", SelectedIndex = settings.SubtitleBurnSize == "small" ? 0 : settings.SubtitleBurnSize == "large" ? 2 : 1, MaxColumns = 3 };
                foreach (string label in new[] { "小", "中", "大" }) size.Items.Add(label);
                var position = new RadioButtons { Header = "位置", SelectedIndex = 0, MaxColumns = 2 }; position.Items.Add("下"); position.Items.Add("上");
                var srt = new CheckBox { Content = "同じ名前の字幕ファイル（SRT）も保存（プレーヤーで字幕の表示・非表示を切り替えられます）", IsChecked = true };
                var content = new StackPanel { Spacing = 10 };
                content.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Text = "文字起こしを映像に焼き込んだ新しいMP4を作成します。元の動画は変更しません。編集・AI補正・用語辞書・同期の改善を反映した、いま表示している文章を使います。書き出しには動画の長さと同じくらいの時間がかかることがあります。" });
                content.Children.Add(text); content.Children.Add(size); content.Children.Add(position); content.Children.Add(srt);
                var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "字幕付き動画を書き出す", Content = content, PrimaryButtonText = "保存先を選ぶ…", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Primary, RequestedTheme = Root.RequestedTheme };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary || closing) return;
                string contentMode = text.SelectedIndex == 1 ? "translation" : text.SelectedIndex == 2 ? "both" : "source", sizeMode = size.SelectedIndex == 0 ? "small" : size.SelectedIndex == 2 ? "large" : "medium";
                settings.SubtitleBurnContent = contentMode; settings.SubtitleBurnSize = sizeMode; settings.Save();
                var picker = new Microsoft.Windows.Storage.Pickers.FileSavePicker(AppWindow.Id) { Title = "字幕付き動画を保存", SuggestedFileName = Path.GetFileNameWithoutExtension(video) + "_字幕付き" };
                picker.FileTypeChoices.Add("MP4動画", new List<string> { ".mp4" });
                if (Directory.Exists(Path.GetDirectoryName(video))) picker.SuggestedFolder = Path.GetDirectoryName(video)!;
                var file = await picker.PickSaveFileAsync(); if (file is null || closing) return;
                if (Path.GetFullPath(file.Path).Equals(Path.GetFullPath(video), StringComparison.OrdinalIgnoreCase)) { Status("元の動画とは別の名前で保存してください。"); return; }
                choice = (contentMode, sizeMode, position.SelectedIndex == 1, srt.IsChecked == true, file.Path);
            }
            finally { dialogOpen = false; }
        }
        if (choice is not { } c || closing) return;
        ClosePlayback();
        bool done = await RunExclusiveAsync("字幕付き動画の書き出し", async token =>
        {
            var progress = new Progress<double>(percent => ReportTask(token, $"字幕付き動画の書き出し：{percent:F0}%", percent));
            await BurnSubtitlesAsync(video, c.Path, document, c.Content, c.Size, c.Top, progress, token);
            if (c.Srt) await File.WriteAllTextAsync(Path.ChangeExtension(c.Path, ".srt"), SubtitleExport.Srt(document, c.Content), new System.Text.UTF8Encoding(true), token);
        });
        if (closing) return;
        EnsurePlayback();
        if (done)
        {
            Status("字幕付き動画を保存しました: " + c.Path + (c.Srt ? "（同じ名前のSRTも保存）" : ""));
            if (!VerificationRun) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", c.Path }, UseShellExecute = false });
        }
    }
}
