using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Locaptio.WinUI;

/// <summary>
/// Folding the language/input selectors and the main toolbar away (the ⌃ button at the right, 設定 → ボタンの列を折りたたむ,
/// or Ctrl+Shift+M). A slim bar with 開始・停止 and the current choices remains; the menu bar keeps every command.
/// </summary>
public sealed partial class MainWindow
{
    private void InitializeToolbarCollapse()
    {
        StartButton.IsEnabledChanged += (_, _) => CollapsedStartButton.IsEnabled = StartButton.IsEnabled;
        StopButton.IsEnabledChanged += (_, _) => CollapsedStopButton.IsEnabled = StopButton.IsEnabled;
        CollapsedStartButton.IsEnabled = StartButton.IsEnabled; CollapsedStopButton.IsEnabled = StopButton.IsEnabled;
        foreach (var selector in new[] { ModeSelector, TargetSelector, AudioSelector }) selector.SelectionChanged += (_, _) => UpdateCollapsedToolbarText();
        var shortcut = new KeyboardAccelerator { Key = VirtualKey.M, Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift };
        shortcut.Invoked += (_, e) => { e.Handled = true; SetToolbarCollapsed(!settings.ToolbarCollapsed); };
        Root.KeyboardAccelerators.Add(shortcut);
        ApplyToolbarCollapse();
    }
    private void ToolbarCollapseClick(object sender, RoutedEventArgs e) => SetToolbarCollapsed(!settings.ToolbarCollapsed);
    private void ToolbarCollapsedMenuClick(object sender, RoutedEventArgs e) => SetToolbarCollapsed(ToolbarCollapsedMenuItem.IsChecked);
    private void SetToolbarCollapsed(bool collapsed)
    {
        settings.ToolbarCollapsed = collapsed; settings.Save(); ApplyToolbarCollapse();
    }
    private void ApplyToolbarCollapse()
    {
        bool collapsed = settings.ToolbarCollapsed;
        ToolbarArea.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        CollapsedToolbar.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
        ToolbarCollapsedMenuItem.IsChecked = collapsed;
        ToolbarCollapseGlyph.Glyph = collapsed ? "\uE70D" : "\uE70E";
        string name = collapsed ? "メニューを展開" : "メニューを折りたたむ";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ToolbarCollapseButton, name);
        ToolTipService.SetToolTip(ToolbarCollapseButton, collapsed ? "言語・音声の選択とボタンの列を表示します（Ctrl+Shift+M）" : "言語・音声の選択とボタンの列を折りたたみます（Ctrl+Shift+M）");
        UpdateCollapsedToolbarText();
    }
    private void UpdateCollapsedToolbarText() =>
        CollapsedToolbarText.Text = string.Join("・", new[] { ModeSelector.Text, TargetSelector.Text, AudioSelector.Text }.Where(t => t.Length > 0));
}
