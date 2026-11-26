using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Locaptio.WinUI;

/// <summary>A display or a top-level window that can be recorded.</summary>
internal sealed record CaptureTarget(bool IsDisplay, string DeviceName, nint Window, string Title, int Width, int Height)
{
    public string Kind => IsDisplay ? "画面全体" : "ウィンドウ";
    public string Label => IsDisplay ? $"画面全体（{Title}）" : Title;
    public string Size => $"{Width}×{Height}";
}

/// <summary>
/// Lists recordable displays and windows and takes their still previews with GDI. Previews are
/// small copies taken locally for the picker; nothing is saved or sent anywhere.
/// </summary>
internal static class CaptureTargets
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx { public int Size; public Rect Monitor, Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapHeader { public int Size, Width, Height; public short Planes, BitCount; public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant; }
    private delegate bool MonitorProc(nint monitor, nint dc, ref Rect rect, nint data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorProc proc, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfoEx info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern bool PrintWindow(nint window, nint dc, uint flags);
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleBitmap(nint dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint item);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(nint target, int x, int y, int width, int height, nint source, int sourceX, int sourceY, int operation);
    [DllImport("gdi32.dll")] private static extern bool StretchBlt(nint target, int x, int y, int width, int height, nint source, int sourceX, int sourceY, int sourceWidth, int sourceHeight, int operation);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(nint dc, int mode);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(nint dc, nint bitmap, uint start, uint lines, byte[] bits, ref BitmapHeader header, uint usage);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint item);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);

    private static Dictionary<string, Rect> Monitors()
    {
        var result = new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        EnumDisplayMonitors(0, 0, (nint monitor, nint _, ref Rect _, nint _) =>
        {
            var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
            if (GetMonitorInfoW(monitor, ref info)) result[info.Device] = info.Monitor;
            return true;
        }, 0);
        return result;
    }

    /// <summary>A display's position on the desktop and size, in pixels.</summary>
    public static (int Left, int Top, int Width, int Height)? DisplayBounds(string deviceName) =>
        Monitors().TryGetValue(deviceName, out var rect) ? (rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top) : null;

    /// <summary>Displays first, then visible, non-minimized windows of other apps.</summary>
    public static List<CaptureTarget> List()
    {
        var targets = new List<CaptureTarget>(); var monitors = Monitors(); int number = 0;
        foreach (var display in ScreenRecorderLib.Recorder.GetDisplays())
        {
            number++;
            var bounds = monitors.TryGetValue(display.DeviceName, out var rect) ? rect : default;
            string name = string.IsNullOrWhiteSpace(display.FriendlyName) ? $"ディスプレイ{number}" : $"ディスプレイ{number}・{display.FriendlyName}";
            targets.Add(new(true, display.DeviceName, 0, name, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top));
        }
        uint self = (uint)Environment.ProcessId;
        foreach (var window in ScreenRecorderLib.Recorder.GetWindows())
        {
            try
            {
                if (!window.IsValidWindow() || window.IsMinmimized() || string.IsNullOrWhiteSpace(window.Title) || !IsWindowVisible(window.Handle)) continue;
                GetWindowThreadProcessId(window.Handle, out uint process);
                // Locaptio's own windows are recorded through the display instead.
                if (process == self) continue;
                // Cloaked windows (other virtual desktops, suspended UWP frames) cannot be captured.
                if (DwmGetWindowAttribute(window.Handle, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) continue;
                GetWindowRect(window.Handle, out var rect);
                if (rect.Right - rect.Left < 80 || rect.Bottom - rect.Top < 60) continue;
                targets.Add(new(false, "", window.Handle, window.Title.Trim(), rect.Right - rect.Left, rect.Bottom - rect.Top));
            }
            catch (Exception) { }
        }
        return targets;
    }

    /// <summary>BGRA pixels of the target scaled to <paramref name="maxWidth"/>, or null when it cannot be captured.</summary>
    public static (byte[] Pixels, int Width, int Height)? Snapshot(CaptureTarget target, int maxWidth)
    {
        nint screen = GetDC(0), source = CreateCompatibleDC(screen), small = CreateCompatibleDC(screen), full = 0, scaled = 0;
        try
        {
            int width, height, x = 0, y = 0;
            if (target.IsDisplay)
            {
                if (!Monitors().TryGetValue(target.DeviceName, out var rect)) return null;
                x = rect.Left; y = rect.Top; width = rect.Right - rect.Left; height = rect.Bottom - rect.Top;
            }
            else
            {
                if (IsIconic(target.Window) || !GetWindowRect(target.Window, out var rect)) return null;
                width = rect.Right - rect.Left; height = rect.Bottom - rect.Top;
            }
            if (width <= 0 || height <= 0) return null;
            full = CreateCompatibleBitmap(screen, width, height); var oldFull = SelectObject(source, full);
            // PrintWindow with full content renders the window even when other windows cover it.
            bool ok = target.IsDisplay ? BitBlt(source, 0, 0, width, height, screen, x, y, 0x00CC0020 | 0x40000000) : PrintWindow(target.Window, source, 2);
            if (!ok) { SelectObject(source, oldFull); return null; }
            int outWidth = Math.Min(maxWidth, width), outHeight = Math.Max(1, (int)Math.Round(height * (double)outWidth / width));
            scaled = CreateCompatibleBitmap(screen, outWidth, outHeight); var oldScaled = SelectObject(small, scaled);
            SetStretchBltMode(small, 4);
            StretchBlt(small, 0, 0, outWidth, outHeight, source, 0, 0, width, height, 0x00CC0020);
            SelectObject(small, oldScaled); SelectObject(source, oldFull);
            var pixels = new byte[outWidth * outHeight * 4];
            var header = new BitmapHeader { Size = Marshal.SizeOf<BitmapHeader>(), Width = outWidth, Height = -outHeight, Planes = 1, BitCount = 32 };
            if (GetDIBits(small, scaled, 0, (uint)outHeight, pixels, ref header, 0) == 0) return null;
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            return (pixels, outWidth, outHeight);
        }
        finally
        {
            if (full != 0) DeleteObject(full); if (scaled != 0) DeleteObject(scaled);
            DeleteDC(source); DeleteDC(small); ReleaseDC(0, screen);
        }
    }

    public static WriteableBitmap ToBitmap((byte[] Pixels, int Width, int Height) image, WriteableBitmap? reuse = null)
    {
        var bitmap = reuse is not null && reuse.PixelWidth == image.Width && reuse.PixelHeight == image.Height ? reuse : new WriteableBitmap(image.Width, image.Height);
        using (var stream = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsStream(bitmap.PixelBuffer)) stream.Write(image.Pixels, 0, Math.Min(image.Pixels.Length, (int)stream.Length));
        bitmap.Invalidate();
        return bitmap;
    }
}
