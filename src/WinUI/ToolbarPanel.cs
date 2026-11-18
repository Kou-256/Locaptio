using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Locaptio.WinUI;
public sealed class ToolbarPanel : Panel
{
    /// <summary>Items the user hid; kept apart from Visibility, which the app also sets by state.</summary>
    public HashSet<UIElement> Hidden { get; } = new();
    private bool Shown(UIElement child) => child.Visibility == Visibility.Visible && !Hidden.Contains(child);
    protected override Size MeasureOverride(Size availableSize)
    {
        double x = 0, y = 0, row = 0, width = 0;
        foreach (var child in Children.Where(Shown))
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > availableSize.Width) { width = Math.Max(width, x - 8); y += row + 6; x = row = 0; }
            x += size.Width + 8; row = Math.Max(row, size.Height);
        }
        return new Size(Math.Max(width, Math.Max(0, x - 8)), y + row);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0, y = 0, row = 0;
        foreach (var child in Children.Where(c => !Shown(c))) child.Arrange(new Rect(0, 0, 0, 0));
        foreach (var child in Children.Where(Shown))
        {
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > finalSize.Width) { y += row + 6; x = row = 0; }
            child.Arrange(new Rect(x, y, size.Width, size.Height)); x += size.Width + 8; row = Math.Max(row, size.Height);
        }
        return finalSize;
    }
}
