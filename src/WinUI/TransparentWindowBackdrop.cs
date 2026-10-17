using System.Runtime.InteropServices;
using ICompositionSupportsSystemBackdrop = Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.Composition;

namespace Locaptio.WinUI;

// Per-pixel composition: changing a background brush never fades the foreground.
internal sealed class TransparentWindowBackdrop : SystemBackdrop
{
    private static Windows.System.DispatcherQueueController? queue;
    private static Compositor? compositor;
    private CompositionColorBrush? brush;
    private nint window;
    private readonly SubclassProc procedure;
    public TransparentWindowBackdrop() { procedure = WindowProcedure; }
    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        if (Windows.System.DispatcherQueue.GetForCurrentThread() is null && queue is null)
        {
            var options = new QueueOptions { Size = Marshal.SizeOf<QueueOptions>(), ThreadType = 2, ApartmentType = 2 };
            Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out nint controller));
            queue = WinRT.MarshalInterface<Windows.System.DispatcherQueueController>.FromAbi(controller);
            Marshal.Release(controller);
        }
        compositor ??= new Compositor();
        brush = compositor.CreateColorBrush(Color.FromArgb(0, 0, 0, 0));
        target.SystemBackdrop = brush;
        window = Microsoft.UI.Win32Interop.GetWindowFromWindowId(xamlRoot.ContentIslandEnvironment.AppWindowId);
        SetWindowSubclass(window, procedure, 103, 0);
        ConfigureTransparency();
    }
    private void ConfigureTransparency()
    {
        nint region = CreateRectRgn(-2, -2, -1, -1);
        try
        {
            var blur = new BlurBehind { Flags = 3, Enable = true, Region = region };
            Marshal.ThrowExceptionForHR(DwmEnableBlurBehindWindow(window, ref blur));
        }
        finally { DeleteObject(region); }
    }
    private nint WindowProcedure(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == 0x0014) // WM_ERASEBKGND: a black GDI surface leaves composition alpha intact.
        {
            GetClientRect(hwnd, out var rect); FillRect(wParam, ref rect, GetStockObject(4)); return 1;
        }
        if (message == 0x031E) ConfigureTransparency();
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }
    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        RemoveWindowSubclass(window, procedure, 103);
        target.SystemBackdrop = null; brush?.Dispose(); brush = null; window = 0;
    }
    [StructLayout(LayoutKind.Sequential)] private struct QueueOptions { public int Size, ThreadType, ApartmentType; }
    [StructLayout(LayoutKind.Sequential)] private struct BlurBehind { public uint Flags; [MarshalAs(UnmanagedType.Bool)] public bool Enable; public nint Region; [MarshalAs(UnmanagedType.Bool)] public bool Transition; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    private delegate nint SubclassProc(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nuint data);
    [DllImport("CoreMessaging.dll")] private static extern int CreateDispatcherQueueController(QueueOptions options, out nint controller);
    [DllImport("dwmapi.dll")] private static extern int DwmEnableBlurBehindWindow(nint hwnd, ref BlurBehind blur);
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll")] private static extern nint GetStockObject(int index);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern int FillRect(nint hdc, ref Rect rect, nint brush);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint hwnd, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint message, nint wParam, nint lParam);
}
