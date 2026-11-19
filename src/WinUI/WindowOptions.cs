using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Graphics;

namespace Locaptio.WinUI;

/// <summary>
/// "常に手前に表示" (Settings, the 設定 menu, or a system-wide shortcut such as Ctrl+Alt+T), and the summary pane's
/// width and separate window. The caption overlay always stays in front; this setting is for the main window.
/// </summary>
public sealed partial class MainWindow
{
    // ---- Keep on top ------------------------------------------------------------------------
    public static readonly (string Key, string Label, uint Modifiers, uint Key2)[] KeepOnTopHotkeys =
    [
        ("ctrl-alt-t", "Ctrl+Alt+T", ModControl | ModAlt, 0x54),
        ("ctrl-shift-t", "Ctrl+Shift+T", ModControl | ModShift, 0x54),
        ("ctrl-alt-f12", "Ctrl+Alt+F12", ModControl | ModAlt, 0x7B),
        ("none", "使わない", 0, 0),
    ];
    private const uint ModAlt = 1, ModControl = 2, ModShift = 4, ModNoRepeat = 0x4000;
    private const int KeepOnTopHotkeyId = 0x4C43;
    private SubclassProc? hotkeyProcedure;
    private bool hotkeyRegistered;
    private string KeepOnTopHotkeyLabel => KeepOnTopHotkeys.FirstOrDefault(h => h.Key == settings.KeepOnTopHotkey).Label ?? "使わない";

    private void InitializeKeepOnTop()
    {
        hotkeyProcedure = (window, message, wParam, lParam, _, _) =>
        {
            if (message == 0x0312 && wParam == KeepOnTopHotkeyId) { DispatcherQueue.TryEnqueue(() => SetKeepOnTop(!settings.KeepOnTop, fromShortcut: true)); return 0; }
            return DefSubclassProc(window, message, wParam, lParam);
        };
        SetWindowSubclass(hwnd, hotkeyProcedure, 0x4C43, 0);
        RegisterKeepOnTopHotkey();
        Closed += (_, _) => { summaryWindow?.Close(); if (hotkeyRegistered) UnregisterHotKey(hwnd, KeepOnTopHotkeyId); RemoveWindowSubclass(hwnd, hotkeyProcedure, 0x4C43); };
        KeepOnTopMenuItem.IsChecked = settings.KeepOnTop;
        UpdateKeepOnTopMenuText();
    }
    /// <summary>Registers the chosen system-wide shortcut; returns false when another app already uses it.</summary>
    private bool RegisterKeepOnTopHotkey()
    {
        if (hotkeyRegistered) { UnregisterHotKey(hwnd, KeepOnTopHotkeyId); hotkeyRegistered = false; }
        var hotkey = KeepOnTopHotkeys.FirstOrDefault(h => h.Key == settings.KeepOnTopHotkey);
        if (hotkey.Key is null or "none") return true;
        hotkeyRegistered = RegisterHotKey(hwnd, KeepOnTopHotkeyId, hotkey.Modifiers | ModNoRepeat, hotkey.Key2);
        if (!hotkeyRegistered) Status($"ショートカット {hotkey.Label} はほかのアプリが使用中のため登録できませんでした。設定の「本体の表示」で別のキーを選んでください。");
        return hotkeyRegistered;
    }
    private void UpdateKeepOnTopMenuText() => KeepOnTopMenuItem.Text = "常に手前に表示" + (settings.KeepOnTopHotkey == "none" ? "" : $"（{KeepOnTopHotkeyLabel}）");
    private void KeepOnTopMenuClick(object sender, RoutedEventArgs e) => SetKeepOnTop(KeepOnTopMenuItem.IsChecked);
    private ToggleSwitch? settingsKeepOnTopToggle;
    private void SetKeepOnTop(bool on, bool fromShortcut = false)
    {
        if (closing) return;
        settings.KeepOnTop = on; settings.Save();
        ((OverlappedPresenter)AppWindow.Presenter).IsAlwaysOnTop = on;
        if (settingsWindow?.AppWindow.Presenter is OverlappedPresenter settingsPresenter) settingsPresenter.IsAlwaysOnTop = on;
        KeepOnTopMenuItem.IsChecked = on;
        if (settingsKeepOnTopToggle is not null && settingsKeepOnTopToggle.IsOn != on) settingsKeepOnTopToggle.IsOn = on;
        // Turning it on from anywhere brings Locaptio forward, so the shortcut also finds a hidden window.
        if (on && fromShortcut) { AppWindow.Show(); Activate(); }
        Status(on ? "常に手前に表示をONにしました。" + (settings.KeepOnTopHotkey == "none" ? "" : $"{KeepOnTopHotkeyLabel} でOFFにできます。")
                  : "常に手前に表示をOFFにしました。ほかのウィンドウの後ろに回ります。" + (settings.KeepOnTopHotkey == "none" ? "" : $"{KeepOnTopHotkeyLabel} でONに戻せます。"));
    }
    private void AddKeepOnTopSettings(StackPanel content, Func<string, string[], int, Action<int>, DownwardSelector> choice)
    {
        content.Children.Add(new TextBlock { Text = "ウィンドウの重なり", Margin = new Thickness(0, 8, 0, 0) });
        settingsKeepOnTopToggle = new ToggleSwitch { Header = "本体を常に手前に表示する（ほかのウィンドウより前に出す）", IsOn = settings.KeepOnTop };
        settingsKeepOnTopToggle.Toggled += (_, _) => { if (settingsKeepOnTopToggle.IsOn != settings.KeepOnTop) SetKeepOnTop(settingsKeepOnTopToggle.IsOn); };
        content.Children.Add(settingsKeepOnTopToggle);
        choice("ON/OFFを切り替えるショートカット（ほかのアプリを使っていても有効）", KeepOnTopHotkeys.Select(h => h.Label).ToArray(),
            Math.Max(0, Array.FindIndex(KeepOnTopHotkeys, h => h.Key == settings.KeepOnTopHotkey)),
            i => { settings.KeepOnTopHotkey = KeepOnTopHotkeys[i].Key; settings.Save(); RegisterKeepOnTopHotkey(); UpdateKeepOnTopMenuText(); });
        content.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = .8, Text = "メニューの「設定」→「常に手前に表示」でも切り替えられます。字幕ウィンドウは常に手前に表示します。" });
    }

    // ---- Summary pane width and separate window ---------------------------------------------
    private Window? summaryWindow;
    private void SummaryResizeDrag(object sender, DragDeltaEventArgs e)
    {
        // The grip is on the pane's left edge: dragging left widens it.
        double width = Math.Clamp(SummaryPane.Width - e.HorizontalChange, 260, Math.Max(260, Root.ActualWidth * .7));
        settings.SummaryPaneWidth = width;
        ShowSummaryPane(true);
    }
    private void SummaryResizeCompleted(object sender, DragCompletedEventArgs e) => settings.Save();

    private void SummaryPopOutClick(object sender, RoutedEventArgs e)
    {
        if (summaryWindow is not null) { DockSummary(); return; }
        if (SummaryPane.Child is not FrameworkElement inner) return;
        ShowSummaryPane(false);
        SummaryPane.Child = null;
        var host = new Grid { Padding = new Thickness(12, 10, 8, 10), Background = CaptionWindow.SurfaceBrush(settings.MainView()), RequestedTheme = settings.MainBackground == "light" ? ElementTheme.Light : ElementTheme.Dark };
        host.Children.Add(inner);
        var window = new Window { Title = "AI要約 — Locaptio", Content = host, SystemBackdrop = null };
        summaryWindow = window;
        window.AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Locaptio.ico"));
        double scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(window)) / 96.0;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        int width = Math.Min((int)(Math.Max(420, settings.SummaryPaneWidth) * scale), area.Width), height = Math.Min((int)(720 * scale), area.Height);
        // Next to the main window when there is room, otherwise over its right side.
        var main = AppWindow.Position; var mainSize = AppWindow.Size;
        int x = main.X + mainSize.Width + width <= area.X + area.Width ? main.X + mainSize.Width : Math.Max(area.X, main.X + mainSize.Width - width);
        window.AppWindow.MoveAndResize(new RectInt32(x, Math.Clamp(main.Y, area.Y, area.Y + area.Height - height), width, height));
        ((OverlappedPresenter)window.AppWindow.Presenter).IsAlwaysOnTop = settings.SummaryWindowOnTop;
        SummaryOnTopButton.Visibility = Visibility.Visible; SummaryOnTopButton.IsChecked = settings.SummaryWindowOnTop;
        SummaryPopOutIcon.Glyph = ""; ToolTipService.SetToolTip(SummaryPopOutButton, "本体の要約欄に戻す");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SummaryPopOutButton, "要約を本体に戻す");
        // Closing the window puts the content back into the (hidden) pane.
        window.Closed += (_, _) => { if (ReferenceEquals(summaryWindow, window)) Reattach(window, show: false); };
        window.Activate();
        Status("AI要約を別ウィンドウで開きました。大きさを自由に変えられます。");
    }
    /// <summary>The separate summary window's own "always on top" (off by default, independent of the main window).</summary>
    private void SummaryOnTopClick(object sender, RoutedEventArgs e) => SetSummaryWindowOnTop(SummaryOnTopButton.IsChecked == true);
    private void SetSummaryWindowOnTop(bool on)
    {
        settings.SummaryWindowOnTop = on; settings.Save(); SummaryOnTopButton.IsChecked = on;
        if (summaryWindow?.AppWindow.Presenter is OverlappedPresenter presenter) presenter.IsAlwaysOnTop = on;
        Status(on ? "要約ウィンドウを常に手前に表示します。" : "要約ウィンドウの常に手前を解除しました。");
    }
    private void DockSummary()
    {
        if (summaryWindow is not { } window) return;
        Reattach(window, show: true); window.Close();
    }
    private void Reattach(Window window, bool show)
    {
        summaryWindow = null;
        if (window.Content is Grid host && host.Children.FirstOrDefault() is UIElement inner) { host.Children.Remove(inner); SummaryPane.Child = inner; }
        SummaryOnTopButton.Visibility = Visibility.Collapsed;
        SummaryPopOutIcon.Glyph = ""; ToolTipService.SetToolTip(SummaryPopOutButton, "別ウィンドウで開く（大きさを自由に変えられます）");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SummaryPopOutButton, "要約を別ウィンドウで開く");
        if (!closing) { ShowSummaryPane(show); UpdateCommandStates(); }
    }

    private delegate nint SubclassProc(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint hwnd, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hwnd, int id);
}
