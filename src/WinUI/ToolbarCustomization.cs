using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Locaptio.WinUI;

/// <summary>
/// The main toolbar's order and hidden items (Settings → 本体の表示 → ツールバー, or right-click the toolbar).
/// Hidden items stay available from the menu bar and can be shown again at any time; 開始 and 停止 cannot be hidden.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly string[] AlwaysShown = ["StartButton", "StopButton"];
    private string[]? toolbarDefaultOrder;

    private static string ToolbarLabel(FrameworkElement item) => item.Name switch
    {
        "SearchButton" => "録音内の検索",
        _ => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(item) is { Length: > 0 } name ? name : item.Name
    };
    private FrameworkElement[] ToolbarItems => MainToolbar.Children.OfType<FrameworkElement>().Where(c => c.Name.Length > 0).ToArray();

    /// <summary>Applies the saved order and hidden items. Unknown or new items keep their default position.</summary>
    private void ApplyToolbarLayout()
    {
        toolbarDefaultOrder ??= ToolbarItems.Select(i => i.Name).ToArray();
        var items = toolbarDefaultOrder.Select(name => ToolbarItems.First(i => i.Name == name)).ToList();
        var ordered = (settings.ToolbarOrder ?? []).Where(n => items.Any(i => i.Name == n)).Distinct()
            .Concat(toolbarDefaultOrder.Where(n => !(settings.ToolbarOrder ?? []).Contains(n))).ToList();
        // Items not named in the saved order go back next to their default neighbour.
        foreach (string name in toolbarDefaultOrder.Where(n => !(settings.ToolbarOrder ?? []).Contains(n)))
        {
            ordered.Remove(name);
            int defaultIndex = Array.IndexOf(toolbarDefaultOrder, name);
            string? before = toolbarDefaultOrder.Take(defaultIndex).LastOrDefault(ordered.Contains);
            ordered.Insert(before is null ? 0 : ordered.IndexOf(before) + 1, name);
        }
        for (int target = 0; target < ordered.Count; target++)
        {
            var item = items.First(i => i.Name == ordered[target]);
            int current = MainToolbar.Children.IndexOf(item);
            if (current != target) { MainToolbar.Children.RemoveAt(current); MainToolbar.Children.Insert(target, item); }
        }
        MainToolbar.Hidden.Clear();
        foreach (var item in items)
        {
            bool hidden = (settings.ToolbarHidden ?? []).Contains(item.Name) && !AlwaysShown.Contains(item.Name);
            if (hidden) MainToolbar.Hidden.Add(item);
            if (item is Control control) control.IsTabStop = !hidden;
        }
        MainToolbar.InvalidateMeasure(); MainToolbar.InvalidateArrange();
    }

    private string[] CurrentToolbarOrder() => MainToolbar.Children.OfType<FrameworkElement>().Where(c => c.Name.Length > 0).Select(c => c.Name).ToArray();
    private void SetToolbarHidden(string name, bool hidden)
    {
        var set = new HashSet<string>(settings.ToolbarHidden ?? []);
        if (hidden && !AlwaysShown.Contains(name)) set.Add(name); else set.Remove(name);
        settings.ToolbarHidden = set.ToArray(); settings.Save(); ApplyToolbarLayout();
    }
    private void MoveToolbarItem(string name, int offset)
    {
        var order = CurrentToolbarOrder().ToList(); int index = order.IndexOf(name), target = index + offset;
        if (index < 0 || target < 0 || target >= order.Count) return;
        order.RemoveAt(index); order.Insert(target, name);
        settings.ToolbarOrder = order.ToArray(); settings.Save(); ApplyToolbarLayout();
    }
    /// <summary>Self-check: puts the controls back in default order without touching the saved settings.</summary>
    private void ResetToolbarOrderForVerification(string[] order)
    {
        for (int target = 0; target < order.Length; target++)
        {
            var item = ToolbarItems.First(i => i.Name == order[target]); int current = MainToolbar.Children.IndexOf(item);
            if (current != target) { MainToolbar.Children.RemoveAt(current); MainToolbar.Children.Insert(target, item); }
        }
        MainToolbar.Hidden.Clear();
    }
    private void ResetToolbar() { settings.ToolbarOrder = []; settings.ToolbarHidden = []; settings.Save(); ApplyToolbarLayout(); }

    /// <summary>Right-click on the toolbar: hide that item, show hidden ones again, or open the settings.</summary>
    private void ToolbarRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var item = (e.OriginalSource as DependencyObject) is { } source ? ToolbarItems.FirstOrDefault(i => IsInside(source, i)) : null;
        var menu = new MenuFlyout();
        if (item is not null && !AlwaysShown.Contains(item.Name))
        {
            var hide = new MenuFlyoutItem { Text = $"「{ToolbarLabel(item)}」を隠す" }; string name = item.Name;
            hide.Click += (_, _) => { SetToolbarHidden(name, true); Status($"「{ToolbarLabel(item)}」を隠しました。右クリック →「隠した項目を表示」か、設定の「本体の表示」で戻せます。"); };
            menu.Items.Add(hide);
            var left = new MenuFlyoutItem { Text = "左へ移動" }; left.Click += (_, _) => MoveToolbarItem(name, -1); menu.Items.Add(left);
            var right = new MenuFlyoutItem { Text = "右へ移動" }; right.Click += (_, _) => MoveToolbarItem(name, 1); menu.Items.Add(right);
        }
        if (MainToolbar.Hidden.Count > 0)
        {
            var shown = new MenuFlyoutSubItem { Text = "隠した項目を表示" };
            foreach (var hidden in ToolbarItems.Where(MainToolbar.Hidden.Contains))
            {
                var restore = new MenuFlyoutItem { Text = ToolbarLabel(hidden) }; string name = hidden.Name;
                restore.Click += (_, _) => SetToolbarHidden(name, false); shown.Items.Add(restore);
            }
            menu.Items.Add(shown);
        }
        if (menu.Items.Count > 0) menu.Items.Add(new MenuFlyoutSeparator());
        var customize = new MenuFlyoutItem { Text = "ツールバーの並び順・表示を設定…" };
        customize.Click += (_, _) => { SettingsClick(this, new RoutedEventArgs()); settingsNavigation?.Select("本体の表示"); };
        menu.Items.Add(customize);
        var reset = new MenuFlyoutItem { Text = "ツールバーを既定に戻す" }; reset.Click += (_, _) => ResetToolbar(); menu.Items.Add(reset);
        menu.ShowAt(MainToolbar, e.GetPosition(MainToolbar));
        e.Handled = true;
    }
    private static bool IsInside(DependencyObject source, DependencyObject ancestor)
    {
        for (var node = source; node is not null; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node)) if (node == ancestor) return true;
        return false;
    }

    /// <summary>Settings: one row per toolbar item with a show checkbox and up/down buttons.</summary>
    private void AddToolbarSettings(StackPanel content)
    {
        content.Children.Add(new TextBlock { Text = "ツールバー（並び順・表示）", Margin = new Thickness(0, 8, 0, 0) });
        content.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = .8, Text = "チェックを外した項目はツールバーから隠れます（メニューバーからは使えます）。↑↓で並び順を変えます。ツールバーを右クリックしても隠す・戻す・移動ができます。「開始」「停止」は隠せません。" });
        var list = new StackPanel { Spacing = 2 };
        void Render()
        {
            list.Children.Clear();
            var order = CurrentToolbarOrder();
            for (int i = 0; i < order.Length; i++)
            {
                var item = ToolbarItems.First(c => c.Name == order[i]); string name = item.Name;
                var row = new Grid { ColumnSpacing = 4 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var show = new CheckBox { Content = ToolbarLabel(item), IsChecked = !MainToolbar.Hidden.Contains(item), IsEnabled = !AlwaysShown.Contains(name) };
                CheckBoxGlyph.SetStatic(show, true);
                show.Click += (_, _) => { SetToolbarHidden(name, show.IsChecked != true); };
                var up = new Button { Content = new FontIcon { Glyph = "", FontSize = 12 }, IsEnabled = i > 0, Padding = new Thickness(8, 4, 8, 4) };
                var down = new Button { Content = new FontIcon { Glyph = "", FontSize = 12 }, IsEnabled = i < order.Length - 1, Padding = new Thickness(8, 4, 8, 4) };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(up, ToolbarLabel(item) + "を上へ"); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(down, ToolbarLabel(item) + "を下へ");
                up.Click += (_, _) => { MoveToolbarItem(name, -1); Render(); }; down.Click += (_, _) => { MoveToolbarItem(name, 1); Render(); };
                Grid.SetColumn(up, 1); Grid.SetColumn(down, 2);
                row.Children.Add(show); row.Children.Add(up); row.Children.Add(down);
                list.Children.Add(row);
            }
        }
        Render();
        content.Children.Add(list);
        var reset = new Button { Content = "ツールバーを既定に戻す" }; reset.Click += (_, _) => { ResetToolbar(); Render(); };
        content.Children.Add(reset);
    }
}
