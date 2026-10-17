using System.Runtime.InteropServices;
using Locaptio.App;
using Locaptio.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;

namespace Locaptio.WinUI;

public sealed class CaptionWindow : Window
{
    private readonly Settings settings;
    private readonly Grid root = new() { RequestedTheme = ElementTheme.Dark, Padding = new Thickness(8, 4, 8, 4) };
    private readonly StackPanel lines = new() { Spacing = 2 };
    private readonly List<Border> bubbles = [];
    private string regionGeometry = "";
    private readonly ScrollViewer scroll;
    private readonly nint hwnd;
    private CaptionUpdate[] captions = [];
    private readonly DispatcherTimer idleTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private long lastRecognition;
    public CaptionWindow(Settings settings)
    {
        this.settings = settings; Title = "Locaptio — 字幕";
        scroll = new ScrollViewer { Content = lines, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        root.Children.Add(scroll); Content = root;
        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false); presenter.IsAlwaysOnTop = true; presenter.IsMaximizable = false; presenter.IsMinimizable = false; presenter.IsResizable = false;
        SystemBackdrop = new TransparentWindowBackdrop();
        RemoveNativeFrame();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Locaptio.ico"));
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        double scale = GetDpiForWindow(hwnd) / 96.0;
        int width = Math.Clamp(settings.CaptionWindowWidth > 0 ? settings.CaptionWindowWidth : (int)(960 * scale), Math.Min((int)(360 * scale), work.Width), work.Width);
        int height = Math.Clamp(settings.CaptionWindowHeight > 0 ? settings.CaptionWindowHeight : (int)(260 * scale), Math.Min((int)(120 * scale), work.Height), work.Height);
        int left = settings.CaptionWindowLeft < 0 ? work.X + (work.Width - width) / 2 : settings.CaptionWindowLeft;
        int top = settings.CaptionWindowTop < 0 ? work.Y + work.Height - height - (int)(60 * scale) : settings.CaptionWindowTop;
        AppWindow.MoveAndResize(new RectInt32(Math.Clamp(left, work.X, work.X + work.Width - width), Math.Clamp(top, work.Y, work.Y + work.Height - height), width, height));
        root.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(root).Properties.IsLeftButtonPressed) return;
            e.Handled = true; ReleaseCapture(); SendMessageW(hwnd, 0x00A1, 2, 0);
        };
        root.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Escape) { e.Handled = true; Close(); } };
        AppWindow.Changed += (_, args) =>
        {
            if ((args.DidPositionChange || args.DidSizeChange) && presenter.State == OverlappedPresenterState.Restored) RememberBounds();
        };
        Closed += (_, _) => { idleTimer.Stop(); settings.Save(); };
        idleTimer.Tick += (_, _) => HideIfIdle(); idleTimer.Start();
        root.Loaded += (_, _) => { RemoveNativeFrame(); scroll.Focus(FocusState.Programmatic); };
        Activated += (_, _) => RemoveNativeFrame();
        root.LayoutUpdated += (_, _) => UpdateWindowShape();
        scroll.ViewChanged += (_, _) => UpdateWindowShape();
        ApplyAppearance();
    }
    private void RememberBounds()
    {
        settings.CaptionWindowLeft = AppWindow.Position.X; settings.CaptionWindowTop = AppWindow.Position.Y;
        settings.CaptionWindowWidth = AppWindow.Size.Width; settings.CaptionWindowHeight = AppWindow.Size.Height;
    }
    private void RemoveNativeFrame()
    {
        // A borderless OverlappedPresenter can retain the Windows 11 resize frame.
        // Remove the native chrome as well as the XAML title bar, only for captions.
        const int frame = 0x00C00000 | 0x00040000 | 0x00030000;
        int style = GetWindowLongW(hwnd, -16);
        int desired = (style & ~frame) | unchecked((int)0x80000000); // WS_POPUP
        int extended = GetWindowLongW(hwnd, -20);
        int desiredExtended = (extended & ~(0x00000100 | 0x00000200 | 0x00020000 | 0x00000001 | 0x00040000)) | 0x00000080; // tool window, no edges
        if (style != desired) SetWindowLongW(hwnd, -16, desired);
        if (extended != desiredExtended) SetWindowLongW(hwnd, -20, desiredExtended);
        int disabled = 1, noBorder = unchecked((int)0xFFFFFFFE), noRounding = 1, noBackdrop = 1;
        DwmSetWindowAttribute(hwnd, 2, ref disabled, sizeof(int)); // DWMNCRP_DISABLED
        DwmSetWindowAttribute(hwnd, 33, ref noRounding, sizeof(int));
        DwmSetWindowAttribute(hwnd, 34, ref noBorder, sizeof(int));
        DwmSetWindowAttribute(hwnd, 38, ref noBackdrop, sizeof(int));
        if (style != desired || extended != desiredExtended)
        {
            SetWindowPos(hwnd, 0, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020);
            regionGeometry = "";
        }
    }
    public void Update(IEnumerable<CaptionUpdate> updates)
    {
        var next = updates.TakeLast(settings.CaptionRows).ToArray();
        // Delayed translations and AI corrections do not revive expired subtitles.
        bool recognition = next.Any(c => !c.SourceCorrected && (c.Japanese is null || captions.Any(old => old.Id == c.Id && old.English != c.English)) && !string.IsNullOrWhiteSpace(c.English) && !captions.Any(old => old.Id == c.Id && old.Revision >= c.Revision));
        captions = next; ApplyAppearance();
        if (recognition) { lastRecognition = System.Diagnostics.Stopwatch.GetTimestamp(); AppWindow.Show(false); RemoveNativeFrame(); UpdateWindowShape(); }
        HideIfIdle();
    }
    public void HideIfIdle()
    {
        if (captions.Length == 0 || lastRecognition == 0 || System.Diagnostics.Stopwatch.GetElapsedTime(lastRecognition).TotalSeconds >= settings.CaptionHideSeconds) AppWindow.Hide();
    }
    public void ApplyAppearance()
    {
        bool light = settings.CaptionBackground == "light";
        root.RequestedTheme = light ? ElementTheme.Light : ElementTheme.Dark;
        root.Background = null;
        lines.Spacing = settings.CaptionLineGap;
        WindowAppearance.SetOpacity(hwnd, 1);
        lines.Children.Clear(); bubbles.Clear();
        foreach (var caption in captions.TakeLast(settings.CaptionRows))
        {
            bool japanese = settings.CaptionMode == "japanese";
            uint enColor = OrdinaryColor(settings, false);
            uint jaColor = OrdinaryColor(settings, true);
            TextBlock Text(string text, bool ja) => new()
            {
                Text = text, FontSize = ja ? settings.CaptionJapaneseSize : settings.CaptionEnglishSize,
                LineHeight = (ja ? settings.CaptionJapaneseSize : settings.CaptionEnglishSize) * settings.CaptionLineHeight,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                TextWrapping = TextWrapping.Wrap, Opacity = settings.CaptionTextOpacity, Foreground = Brush(ja ? jaColor : enColor),
                TextAlignment = settings.CaptionAlignment == "left" ? TextAlignment.Left : settings.CaptionAlignment == "right" ? TextAlignment.Right : TextAlignment.Center,
                FontWeight = settings.CaptionBold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal
            };
            var source = Text(caption.English, japanese);
            var translation = Text(caption.Japanese ?? "", true);
            if (!settings.CaptionCorrectedSameColor && caption.SourceCorrected) source.Foreground = CorrectedBrush(settings);
            if (!settings.CaptionCorrectedSameColor && (caption.TranslationRefined || caption.SourceCorrected)) translation.Foreground = CorrectedBrush(settings);
            Border Bubble(TextBlock block)
            {
                var bubble = new Border { Child = block, Padding = new Thickness(8, 1, 8, 1), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
                    Background = BackgroundBrush(settings, settings.CaptionOpacity), BorderBrush = new SolidColorBrush(Brush(ParseColor(settings.CaptionBorderColor, 0x858585)).Color) { Opacity = settings.CaptionOpacity },
                    HorizontalAlignment = settings.CaptionAlignment == "left" ? HorizontalAlignment.Left : settings.CaptionAlignment == "right" ? HorizontalAlignment.Right : HorizontalAlignment.Center };
                bubbles.Add(bubble); return bubble;
            }
            var pair = new Grid { RowSpacing = settings.CaptionLineGap, ColumnSpacing = 12 };
            bool side = settings.CaptionLayout == "side-by-side" && !japanese && settings.English && settings.Japanese;
            if (side)
            {
                pair.ColumnDefinitions.Add(new ColumnDefinition()); pair.ColumnDefinitions.Add(new ColumnDefinition());
                var sourceBubble = Bubble(source); pair.Children.Add(sourceBubble);
                if (!string.IsNullOrWhiteSpace(translation.Text)) { var translatedBubble = Bubble(translation); Grid.SetColumn(translatedBubble, 1); pair.Children.Add(translatedBubble); }
            }
            else
            {
                pair.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); pair.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                bool reverse = settings.CaptionLayout == "japanese-top";
                int row = 0;
                foreach (var block in reverse ? new[] { translation, source } : new[] { source, translation })
                {
                    if (block == translation && (japanese || !settings.Japanese) || block == source && !japanese && !settings.English) continue;
                    if (string.IsNullOrWhiteSpace(block.Text)) continue;
                    var bubble = Bubble(block); Grid.SetRow(bubble, row++); pair.Children.Add(bubble);
                }
            }
            lines.Children.Add(pair);
        }
        scroll.UpdateLayout(); scroll.ChangeView(null, scroll.ScrollableHeight, null, true);
        UpdateWindowShape();
    }
    public static SolidColorBrush CorrectedBrush(Settings settings)
    {
        string hex = settings.CaptionCorrectedColor.Trim().TrimStart('#');
        return Brush(hex.Length == 6 && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint rgb) ? rgb : 0x70E1C4u);
    }
    private void UpdateWindowShape()
    {
        // The native region clips both rendering and hit testing outside the text bubbles.
        if (root.XamlRoot is null || root.ActualWidth <= 0) return;
        double scale = root.XamlRoot.RasterizationScale;
        GetWindowRect(hwnd, out var window); var client = new NativePoint(); ClientToScreen(hwnd, ref client);
        var bounds = bubbles.Where(b => b.ActualWidth > 0 && b.ActualHeight > 0).Select(b =>
        {
            var p = b.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
            return new NativeRect { Left = (int)Math.Floor(p.X * scale) + client.X - window.Left, Top = (int)Math.Floor(p.Y * scale) + client.Y - window.Top,
                Right = (int)Math.Ceiling((p.X + b.ActualWidth) * scale) + client.X - window.Left, Bottom = (int)Math.Ceiling((p.Y + b.ActualHeight) * scale) + client.Y - window.Top };
        }).ToArray();
        var viewportOrigin = scroll.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
        int viewportLeft = (int)Math.Ceiling(viewportOrigin.X * scale) + client.X - window.Left;
        int viewportTop = (int)Math.Ceiling(viewportOrigin.Y * scale) + client.Y - window.Top;
        int viewportRight = (int)Math.Floor((viewportOrigin.X + scroll.ActualWidth) * scale) + client.X - window.Left;
        int viewportBottom = (int)Math.Floor((viewportOrigin.Y + scroll.ActualHeight) * scale) + client.Y - window.Top;
        string geometry = $"{viewportLeft},{viewportTop},{viewportRight},{viewportBottom}:" + string.Join(';', bounds.Select(b => $"{b.Left},{b.Top},{b.Right},{b.Bottom}"));
        if (geometry == regionGeometry) return;
        regionGeometry = geometry;
        nint region = CreateRectRgn(0, 0, 0, 0);
        foreach (var b in bounds)
        {
            nint part = CreateRoundRectRgn(b.Left, b.Top, b.Right + 1, b.Bottom + 1, (int)(16 * scale), (int)(16 * scale));
            CombineRgn(region, region, part, 2); DeleteObject(part);
        }
        nint viewport = CreateRectRgn(viewportLeft, viewportTop, viewportRight, viewportBottom);
        CombineRgn(region, region, viewport, 1); DeleteObject(viewport);
        if (SetWindowRgn(hwnd, region, true) == 0) { DeleteObject(region); regionGeometry = ""; } // Success transfers ownership to Windows.
    }
    private static SolidColorBrush Brush(uint rgb) => new(Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
    internal (string Text, double X, double Y)[] RenderedPositions() => bubbles.Select(b => (TextBlock)b.Child).Select(block =>
    {
        var position = block.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(0, 0));
        return (block.Text, position.X, position.Y);
    }).ToArray();
    internal bool HasTextOnlyShape()
    {
        nint region = CreateRectRgn(0, 0, 0, 0);
        try
        {
            if (GetWindowRgn(hwnd, region) <= 1 || bubbles.Count == 0) return false;
            GetWindowRect(hwnd, out var window);
            int width = window.Right - window.Left, height = window.Bottom - window.Top;
            // Check the top bar and the empty space below the captions, not just one corner.
            return !PtInRegion(region, 0, 0) && !PtInRegion(region, width / 2, 0)
                && !PtInRegion(region, width / 2, height - 1) && !PtInRegion(region, width - 1, height - 1);
        }
        finally { DeleteObject(region); }
    }
    internal static uint ParseColor(string hex, uint fallback) => uint.TryParse(hex.Trim().TrimStart('#'), System.Globalization.NumberStyles.HexNumber, null, out uint rgb) && hex.Trim().TrimStart('#').Length == 6 ? rgb : fallback;
    internal static uint OrdinaryColor(Settings value, bool japanese) => value.CaptionPalette == "custom" ? ParseColor(value.CaptionTextColor, 0xFFFFFF)
        : value.CaptionBackground == "light" ? 0x162030u : japanese ? value.CaptionPalette == "white-yellow" ? 0xFFE794u : 0xFFFFFFu : value.CaptionPalette == "cyan-white" ? 0x7DE3FFu : 0xFFFFFFu;
    internal static SolidColorBrush OrdinaryBrush(Settings value, bool japanese) => Brush(OrdinaryColor(value, japanese));
    internal static SolidColorBrush SurfaceBrush(Settings value) => Brush(value.CaptionBackground switch { "light" => 0xF5F7FAu, "black" => 0u, "dark" => 0x141A24u, _ => ParseColor(value.CaptionSurfaceColor, 0x363636) });
    internal static SolidColorBrush BackgroundBrush(Settings settings, double opacity) => new(SurfaceBrush(settings).Color) { Opacity = Math.Clamp(opacity, 0, 1) };
    internal bool HasIndependentOpacity() => bubbles.All(b => Math.Abs(b.Background.Opacity - settings.CaptionOpacity) < .001 && Math.Abs(((TextBlock)b.Child).Opacity - settings.CaptionTextOpacity) < .001);
    internal bool UsesOrdinaryColors() => bubbles.All(b => { var text = (TextBlock)b.Child; bool japanese = settings.CaptionMode == "japanese" || captions.Any(c => c.Japanese == text.Text); return ((SolidColorBrush)text.Foreground).Color == OrdinaryBrush(settings, japanese).Color; });
    internal bool UsesCorrectionColor() => bubbles.Select(b => (TextBlock)b.Child).Any(t => ((SolidColorBrush)t.Foreground).Color == CorrectedBrush(settings).Color && !t.Text.Contains("AI補正"));
    internal bool HasNoNativeFrame()
    {
        GetWindowRect(hwnd, out var window); GetClientRect(hwnd, out var client);
        int result = DwmGetWindowAttribute(hwnd, 1, out int rendering, sizeof(int));
        return (GetWindowLongW(hwnd, -16) & (0x00C00000 | 0x00040000)) == 0 && result == 0 && rendering == 0
            && window.Right - window.Left == client.Right && window.Bottom - window.Top == client.Bottom;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint hwnd, ref NativePoint point);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint hwnd, nint region, bool redraw);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(nint hwnd, nint region);
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(nint dest, nint source1, nint source2, int mode);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] private static extern bool PtInRegion(nint region, int x, int y);
    internal async Task CaptureAsync(string path)
    {
        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap(); await bitmap.RenderAsync(root);
        var pixels = await bitmap.GetPixelsAsync();
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(pixels));
        await encoder.FlushAsync(); stream.Seek(0);
        using var file = File.Create(path); await System.IO.WindowsRuntimeStreamExtensions.AsStreamForRead(stream).CopyToAsync(file);
    }
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] private static extern int GetWindowLongW(nint window, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLongW(nint window, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(nint window, uint color, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern nint SendMessageW(nint window, uint message, nint wParam, nint lParam);
}
