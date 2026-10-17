using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.IO;
using Microsoft.Win32;

namespace Locaptio.App;
public sealed class SettingsWindow : Window
{
    public SettingsWindow(Settings settings, Action changed)
    {
        Title = "Locaptio · Settings"; Width = 550; Height = 700; ResizeMode = ResizeMode.CanResize;
        MinWidth = 440; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = new SolidColorBrush(Color.FromRgb(20, 26, 36)); Foreground = Brushes.White; Topmost = true;
        var panel = new StackPanel { Margin = new Thickness(24) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = "字幕の表示", FontSize = 20, Margin = new Thickness(0, 0, 0, 18) });
        void SliderSetting(string label, double min, double max, double value, Action<double> set)
        {
            var caption = new TextBlock { Text = $"{label}: {value:F0}", Margin = new Thickness(0, 8, 0, 6) }; panel.Children.Add(caption);
            var slider = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = 1, IsSnapToTickEnabled = true };
            slider.ValueChanged += (_, _) => { set(slider.Value); caption.Text = $"{label}: {slider.Value:F0}"; changed(); }; panel.Children.Add(slider);
        }
        CheckBox Toggle(string label, bool value, Action<bool> set)
        {
            var check = new CheckBox { Content = label, IsChecked = value }; check.Click += (_, _) => { set(check.IsChecked == true); changed(); }; panel.Children.Add(check); return check;
        }
        SliderSetting("フォントサイズ", 14, 40, settings.FontSize, v => settings.FontSize = v);
        SliderSetting("背景不透明度 (%)", 25, 100, settings.BackgroundOpacity * 100, v => settings.BackgroundOpacity = v / 100);
        SliderSetting("表示履歴の行数", 1, 30, settings.Rows, v => settings.Rows = (int)v);
        CheckBox? en = null, ja = null;
        en = Toggle("英語を表示", settings.English, v => { settings.English = v; if (!v && !settings.Japanese) { settings.Japanese = true; if (ja is not null) ja.IsChecked = true; } });
        ja = Toggle("日本語を表示", settings.Japanese, v => { settings.Japanese = v; if (!v && !settings.English) { settings.English = true; en.IsChecked = true; } });
        Toggle("CPUを使用（次回Startから）", settings.CpuOnly, v => settings.CpuOnly = v);
        Toggle("診断情報を表示", settings.Diagnostics, v => settings.Diagnostics = v);
        panel.Children.Add(new TextBlock { Text = "保存先フォルダー", FontSize = 20, Margin = new Thickness(0, 20, 0, 8) });
        void FolderSetting(string label, string value, Action<string> set)
        {
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 6) });
            var path = new TextBox { Text = value, IsReadOnly = true, ToolTip = value, Margin = new Thickness(0, 0, 0, 6) };
            panel.Children.Add(path);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            var browse = new Button { Content = "フォルダーを選択…", Margin = new Thickness(0, 0, 8, 0) };
            browse.Click += (_, _) =>
            {
                var dialog = new OpenFolderDialog { Title = label, Multiselect = false };
                if (Directory.Exists(path.Text)) dialog.InitialDirectory = path.Text;
                if (dialog.ShowDialog(this) != true) return;
                path.Text = dialog.FolderName; path.ToolTip = dialog.FolderName; set(dialog.FolderName); changed();
            };
            var reset = new Button { Content = "指定を解除" };
            reset.Click += (_, _) => { path.Text = ""; path.ToolTip = null; set(""); changed(); };
            buttons.Children.Add(browse); buttons.Children.Add(reset); panel.Children.Add(buttons);
        }
        FolderSetting("テキストの保存先", settings.TextSaveDirectory, v => settings.TextSaveDirectory = v);
        FolderSetting("録音の保存先", settings.RecordingSaveDirectory, v => settings.RecordingSaveDirectory = v);
        panel.Children.Add(new TextBlock { Text = "保存時のダイアログを、指定したフォルダーで開きます。ファイル名や保存先は、その都度変更できます。未指定・フォルダーが利用できない場合は標準の保存先を使います。", Foreground = Brushes.LightSlateGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 6) });
        panel.Children.Add(new TextBlock { Text = "設定は自動で記憶します。音声・字幕は保存ボタンを操作したときだけ保存します。", Foreground = Brushes.LightSlateGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 12) });
        var close = new Button { Content = "閉じる", HorizontalAlignment = HorizontalAlignment.Right }; close.Click += (_, _) => Close(); panel.Children.Add(close);
    }
}
