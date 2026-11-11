using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Locaptio.WinUI;

internal sealed class SettingsNavigation
{
    private readonly StackPanel[] pages;
    internal readonly ListView Menu = new() { SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    internal readonly ScrollViewer Scroll;
    internal Grid Root { get; }
    internal string[] Categories => pages.Select(p => (string)p.Tag).ToArray();
    internal StackPanel Page(string title) => pages.Single(p => (string)p.Tag == title).Children.OfType<StackPanel>().Single();
    internal void Select(string title) => Menu.SelectedIndex = Array.IndexOf(Categories, title);
    internal string Selected => Menu.SelectedIndex >= 0 ? Categories[Menu.SelectedIndex] : "";
    internal SettingsNavigation(StackPanel content, ElementTheme theme, Brush background)
    {
        pages = content.Children.OfType<StackPanel>().ToArray();
        Root = new Grid { RequestedTheme = theme, Background = background, Padding = new Thickness(20), RowSpacing = 18, ColumnSpacing = 24 };
        Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Root.RowDefinitions.Add(new RowDefinition());
        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) }); Root.ColumnDefinitions.Add(new ColumnDefinition());
        var heading = new TextBlock { Text = "設定", FontSize = 26, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        Grid.SetColumnSpan(heading, 2); Root.Children.Add(heading);
        var style = new Style { TargetType = typeof(ListViewItem) };
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 10, 8, 10)));
        style.Setters.Add(new Setter(Control.MarginProperty, new Thickness(0, 2, 0, 2)));
        style.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(8)));
        Menu.ItemContainerStyle = style;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(Menu, "設定カテゴリ");
        string[] icons = ["display", "captions", "speech", "correction", "speakers", "translation", "save", "update", "diagnostics"];
        for (int i = 0; i < pages.Length; i++)
        {
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition());
            var icon = new Image { Width = 20, Height = 20, Source = new SvgImageSource(new Uri($"ms-appx:///Assets/Settings/{icons[i]}.svg")) };
            var badge = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 60, 103, 214)), Child = icon };
            row.Children.Add(badge);
            var label = new TextBlock { Text = (string)pages[i].Tag, FontSize = 14, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, 1); row.Children.Add(label);
            var item = new ListViewItem { Content = row };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, label.Text); Menu.Items.Add(item);
        }
        Scroll = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Menu.SelectionChanged += (_, _) =>
        {
            for (int i = 0; i < pages.Length; i++) pages[i].Visibility = i == Menu.SelectedIndex ? Visibility.Visible : Visibility.Collapsed;
            Scroll.ChangeView(null, 0, null, true);
        };
        Menu.ItemClick += (_, e) => Menu.SelectedItem = e.ClickedItem;
        var rail = new Border { BorderThickness = new Thickness(0, 0, 1, 0), BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(90, 133, 133, 133)), Padding = new Thickness(0, 0, 12, 0), Child = Menu };
        Grid.SetRow(rail, 1); Root.Children.Add(rail);
        Grid.SetRow(Scroll, 1); Grid.SetColumn(Scroll, 1); Root.Children.Add(Scroll);
        Menu.SelectedIndex = 0;
    }
    internal async Task CaptureAsync(string path)
    {
        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap(); await bitmap.RenderAsync(Root);
        var pixels = await bitmap.GetPixelsAsync();
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(pixels));
        await encoder.FlushAsync(); stream.Seek(0);
        using var file = File.Create(path); await System.IO.WindowsRuntimeStreamExtensions.AsStreamForRead(stream).CopyToAsync(file);
    }
}
