using System.Collections;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;

namespace Locaptio.WinUI;

// Explicit popup coordinates keep the menu below its button, including when
// another row is selected. A short menu scrolls instead of flipping upwards.
public sealed class DownwardSelector : UserControl
{
    private readonly Button button;
    private readonly TextBlock label = new() { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly Popup popup = new() { IsLightDismissEnabled = true, ShouldConstrainToRootBounds = false };
    private readonly ListView list = new() { IsItemClickEnabled = true, SelectionMode = ListViewSelectionMode.Single };
    private readonly Border menu;
    private AppWindow? subscribedWindow;
    private ScrollViewer? parentScroll;
    private object[] items = [];
    private int selectedIndex = -1;
    public Window? OwnerWindow { get; set; }
    public event EventHandler? SelectionChanged;
    public event EventHandler? DropDownOpened;
    public IEnumerable? ItemsSource
    {
        get => items;
        set
        {
            var previous = SelectedItem;
            items = value?.Cast<object>().ToArray() ?? [];
            selectedIndex = Array.IndexOf(items, previous);
            UpdateLabel();
        }
    }
    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            var next = value >= 0 && value < items.Length ? value : -1;
            if (selectedIndex == next) return;
            selectedIndex = next; UpdateLabel(); SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public object? SelectedItem
    {
        get => selectedIndex >= 0 ? items[selectedIndex] : null;
        set => SelectedIndex = Array.IndexOf(items, value);
    }
    public DownwardSelector()
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(label);
        var arrow = new FontIcon { Glyph = "\uE70D", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(arrow, 1); row.Children.Add(arrow);
        button = new Button { Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, MinHeight = 40 };
        Content = button;
        menu = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 54, 54, 54)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 133, 133, 133)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(4), Child = list,
            RequestedTheme = ElementTheme.Dark
        };
        popup.Child = menu;
        button.Click += (_, _) => { if (popup.IsOpen) popup.IsOpen = false; else Open(); };
        button.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Down) { Open(); e.Handled = true; } };
        list.ItemClick += (_, e) => { SelectedItem = e.ClickedItem; popup.IsOpen = false; button.Focus(FocusState.Programmatic); };
        list.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape) { popup.IsOpen = false; button.Focus(FocusState.Programmatic); e.Handled = true; }
            else if (e.Key == Windows.System.VirtualKey.Enter || e.Key == Windows.System.VirtualKey.Space)
            { SelectedItem = list.SelectedItem; popup.IsOpen = false; button.Focus(FocusState.Programmatic); e.Handled = true; }
        };
        Loaded += (_, _) =>
        {
            AutomationProperties.SetName(button, AutomationProperties.GetName(this));
            subscribedWindow = OwnerWindow?.AppWindow;
            if (subscribedWindow is not null) subscribedWindow.Changed += WindowChanged;
            for (DependencyObject? parent = VisualTreeHelper.GetParent(this); parent is not null; parent = VisualTreeHelper.GetParent(parent))
                if (parent is ScrollViewer scroll) { parentScroll = scroll; scroll.ViewChanged += ParentScrolled; break; }
        };
        Unloaded += (_, _) =>
        {
            popup.IsOpen = false;
            if (subscribedWindow is not null) subscribedWindow.Changed -= WindowChanged;
            if (parentScroll is not null) parentScroll.ViewChanged -= ParentScrolled;
            subscribedWindow = null; parentScroll = null;
        };
    }
    private void UpdateLabel() => label.Text = SelectedItem?.ToString() ?? "選択してください";
    private void WindowChanged(AppWindow sender, AppWindowChangedEventArgs args) => popup.IsOpen = false;
    private void ParentScrolled(object? sender, ScrollViewerViewChangedEventArgs args) => popup.IsOpen = false;
    private void Open()
    {
        DropDownOpened?.Invoke(this, EventArgs.Empty);
        if (items.Length == 0 || XamlRoot is null || OwnerWindow is null) return;
        var anchor = button.TransformToVisual(OwnerWindow.Content).TransformPoint(new Point(0, button.ActualHeight));
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(OwnerWindow);
        double scale = GetDpiForWindow(handle) / 96.0;
        var screen = new NativePoint { X = (int)(anchor.X * scale), Y = (int)(anchor.Y * scale) };
        ClientToScreen(handle, ref screen);
        var work = DisplayArea.GetFromWindowId(OwnerWindow.AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        double available = (work.Y + work.Height - screen.Y) / scale - 8;
        if (available < 24) return;
        if (OwnerWindow.Content is Panel panel) { menu.Background = panel.Background; menu.RequestedTheme = panel.RequestedTheme; }
        popup.XamlRoot = XamlRoot;
        popup.HorizontalOffset = anchor.X;
        popup.VerticalOffset = anchor.Y + 4;
        menu.Width = Math.Max(ActualWidth, 180);
        list.MaxHeight = Math.Min(340, available - 10);
        list.ItemsSource = items; list.SelectedItem = SelectedItem;
        popup.IsOpen = true;
        list.Focus(FocusState.Programmatic);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint window, ref NativePoint point);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
}
