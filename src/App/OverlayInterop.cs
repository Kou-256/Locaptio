using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Locaptio.App;
internal static class OverlayInterop
{
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(nint window, int index, int value);
    [DllImport("user32.dll")] private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
    public static bool IsTopmost(Window window) => (GetWindowLong(new WindowInteropHelper(window).Handle, -20) & 0x8) != 0;
    public static int ResizeHitTest(Window window)
    {
        var point = window.PointToScreen(new Point(window.ActualWidth - 3, window.ActualHeight / 2));
        int packed = ((int)point.Y << 16) | ((int)point.X & 0xffff);
        return (int)SendMessage(new WindowInteropHelper(window).Handle, 0x84, 0, packed);
    }
    // Future UI must provide a global hotkey to disable this before exposing it.
    public static void SetClickThrough(Window window, bool enabled)
    {
        nint handle = new WindowInteropHelper(window).Handle;
        int style = GetWindowLong(handle, -20);
        SetWindowLong(handle, -20, enabled ? style | 0x20 : style & ~0x20);
    }
}
