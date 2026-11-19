using Microsoft.UI.Xaml;

namespace Locaptio.WinUI;

/// <summary>
/// Formerly removed the CheckBox's animated check mark to avoid a stuck "−" glyph. Without its animated source,
/// WinUI failed with 0x8000FFFF and closed the app when the light/dark theme changed (choosing a light caption
/// background while transcribing). Feature on/off controls are switches now (<see cref="FeatureSwitch"/>), and
/// checkboxes keep WinUI's own glyph. The attached property stays so existing styles still load.
/// </summary>
public static class CheckBoxGlyph
{
    public static readonly DependencyProperty StaticProperty = DependencyProperty.RegisterAttached("Static", typeof(bool), typeof(CheckBoxGlyph), new PropertyMetadata(false));
    public static bool GetStatic(DependencyObject element) => (bool)element.GetValue(StaticProperty);
    public static void SetStatic(DependencyObject element, bool value) => element.SetValue(StaticProperty, value);
    /// <summary>Changes an element's light/dark theme only when it differs.</summary>
    public static void SetTheme(FrameworkElement element, ElementTheme theme) { if (element.RequestedTheme != theme) element.RequestedTheme = theme; }
}
