using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Locaptio.WinUI;

/// <summary>
/// WinUI draws the check mark as an animation that morphs a dash into a check. When the animation is
/// interrupted (the window not drawing during a state change), it can stay on the dash frame, so a checked
/// box shows "—". Set through the CheckBox styles, this makes each box use the static check mark WinUI
/// keeps as the animation's fallback.
/// </summary>
public static class CheckBoxGlyph
{
    public static readonly DependencyProperty StaticProperty = DependencyProperty.RegisterAttached(
        "Static", typeof(bool), typeof(CheckBoxGlyph), new PropertyMetadata(false, (d, e) =>
        {
            if (d is not CheckBox box || e.NewValue is not true) return;
            // A box on a hidden page gets its template only when shown, after Loaded; try again on layout until done.
            EventHandler<object>? layout = null;
            layout = (_, _) => { if (UseStaticGlyph(box)) box.LayoutUpdated -= layout; };
            box.Loaded += (_, _) => { if (!UseStaticGlyph(box)) { box.LayoutUpdated -= layout; box.LayoutUpdated += layout; } };
            if (box.IsLoaded && !UseStaticGlyph(box)) box.LayoutUpdated += layout;
        }));
    public static bool GetStatic(DependencyObject element) => (bool)element.GetValue(StaticProperty);
    public static void SetStatic(DependencyObject element, bool value) => element.SetValue(StaticProperty, value);

    /// <summary>True when the box's check mark is the static glyph (for self-checks).</summary>
    public static bool IsStatic(CheckBox box) => Find(box) is { Source: null };

    /// <returns>True once the box has its template and uses the static glyph.</returns>
    private static bool UseStaticGlyph(CheckBox box)
    {
        // With no animated source, AnimatedIcon shows its FallbackIconSource: the font check mark.
        if (Find(box) is not { } icon) return false;
        if (icon.Source is not null) icon.Source = null;
        return true;
    }
    private static AnimatedIcon? Find(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is AnimatedIcon { Name: "CheckGlyph" } icon) return icon;
            if (Find(child) is { } found) return found;
        }
        return null;
    }
}
