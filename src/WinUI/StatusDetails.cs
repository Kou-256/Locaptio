using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace Locaptio.WinUI;

public sealed partial class MainWindow
{
    private bool positioningStatusDetails;
    private void InitializeStatusDetails()
    {
        Root.SizeChanged += (_, _) => { if (StatusDetailsPanel.Visibility == Visibility.Visible) PositionStatusDetails(); };
        StatusDetailsPanel.SizeChanged += (_, _) => { if (StatusDetailsPanel.Visibility == Visibility.Visible) PositionStatusDetails(); };
        Root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape && StatusDetailsPanel.Visibility == Visibility.Visible)
            { HideStatusDetails(); e.Handled = true; }
        }), true);
    }
    private void PositionStatusDetails()
    {
        if (positioningStatusDetails) return;
        positioningStatusDetails = true;
        try
        {
            var anchor = StatusDetailsButton.TransformToVisual(StatusOverlay).TransformPoint(new Point());
            double maxWidth = Math.Min(360, Math.Max(100, StatusOverlay.ActualWidth));
            double maxHeight = Math.Min(220, Math.Max(40, anchor.Y - 16));
            StatusDetailsPanel.Width = double.NaN;
            StatusDetailsPanel.MaxWidth = maxWidth; StatusDetailsPanel.MaxHeight = maxHeight;
            StatusDetailsPanel.Measure(new Size(maxWidth, maxHeight));
            var size = StatusDetailsPanel.DesiredSize;
            Canvas.SetLeft(StatusDetailsPanel, Math.Max(0, anchor.X + StatusDetailsButton.ActualWidth - size.Width));
            Canvas.SetTop(StatusDetailsPanel, Math.Max(0, anchor.Y - size.Height - 6));
        }
        finally { positioningStatusDetails = false; }
    }
    private void ShowStatusDetails(bool pin = false)
    {
        StatusDetailsPanel.Visibility = Visibility.Visible;
        PositionStatusDetails();
    }
    private void HideStatusDetails()
    {
        StatusDetailsPanel.Visibility = Visibility.Collapsed;
    }
    private void StatusDetailsClick(object sender, RoutedEventArgs e)
    {
        if (StatusDetailsPanel.Visibility == Visibility.Visible) HideStatusDetails(); else ShowStatusDetails();
    }
}
