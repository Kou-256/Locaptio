using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace Locaptio.WinUI;

/// <summary>
/// A dialog is drawn inside the main window, so a small window cuts it off. While a large dialog is open the window
/// grows (within the screen's work area) until the dialog's content fits, and goes back to its size when it closes.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Self-check: the capture dialog's scroll area (it should not need to scroll).</summary>
    private ScrollViewer? captureDialogScrollForVerification;

    /// <summary>Room the dialog needs around its content: padding, title and the button row (in DIPs).</summary>
    private const double DialogChromeWidth = 120, DialogChromeHeight = 250;

    private void FitWindowToDialog(ContentDialog dialog, FrameworkElement content)
    {
        (PointInt32 Position, SizeInt32 Size)? previous = null;
        dialog.Opened += (_, _) =>
        {
            if (AppWindow.Presenter is OverlappedPresenter { State: not OverlappedPresenterState.Restored }) return;
            content.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            double scale = Root.XamlRoot?.RasterizationScale ?? 1;
            var size = AppWindow.Size; var client = AppWindow.ClientSize;
            var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
            int width = Math.Min(work.Width, Math.Max(size.Width, (int)Math.Ceiling((content.DesiredSize.Width + DialogChromeWidth) * scale) + size.Width - client.Width));
            int height = Math.Min(work.Height, Math.Max(size.Height, (int)Math.Ceiling((content.DesiredSize.Height + DialogChromeHeight) * scale) + size.Height - client.Height));
            if (width == size.Width && height == size.Height) return;
            previous = (AppWindow.Position, size);
            // Grow around the window's centre, kept on the screen.
            int x = Math.Clamp(AppWindow.Position.X - (width - size.Width) / 2, work.X, work.X + work.Width - width);
            int y = Math.Clamp(AppWindow.Position.Y - (height - size.Height) / 2, work.Y, work.Y + work.Height - height);
            AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
        };
        dialog.Closed += (_, _) =>
        {
            if (previous is not { } bounds) return;
            previous = null;
            AppWindow.MoveAndResize(new RectInt32(bounds.Position.X, bounds.Position.Y, bounds.Size.Width, bounds.Size.Height));
        };
    }
}
