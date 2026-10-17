using System.Runtime.InteropServices;

namespace Locaptio.WinUI;
internal static class WindowAppearance
{
    [DllImport("user32.dll")] private static extern int GetWindowLongW(nint window, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLongW(nint window, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(nint window, uint color, byte alpha, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
    internal static bool IsOpaque(nint hwnd) => (GetWindowLongW(hwnd, -20) & 0x80000) == 0;
    internal static void SetOpacity(nint hwnd, double opacity)
    {
        // Keep opaque WinUI windows on the ordinary DWM composition path.
        int style = GetWindowLongW(hwnd, -20);
        if (opacity >= 1) { if ((style & 0x80000) != 0) SetWindowLongW(hwnd, -20, style & ~0x80000); }
        else { SetWindowLongW(hwnd, -20, style | 0x80000); SetLayeredWindowAttributes(hwnd, 0, (byte)(Math.Clamp(opacity, .25, 1) * 255), 2); }
    }
    internal static void SetBorder(nint hwnd, string color)
    {
        uint rgb = CaptionWindow.ParseColor(color, 0x858585);
        int colorRef = (int)((rgb & 255) << 16 | (rgb & 0xFF00) | (rgb >> 16));
        DwmSetWindowAttribute(hwnd, 34, ref colorRef, sizeof(int));
    }
}
