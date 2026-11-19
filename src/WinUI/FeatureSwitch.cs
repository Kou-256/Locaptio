using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Locaptio.WinUI;

/// <summary>
/// A toggle switch for turning a feature on or off (it takes effect immediately). Checkboxes stay for choosing
/// items from a list (for example which toolbar items to show). It keeps a CheckBox-like IsChecked/Click
/// surface so callers did not change: Click is raised only when the user flips it, not when code sets it.
/// (ToggleSwitch itself is sealed, so this hosts one.)
/// </summary>
public sealed class FeatureSwitch : UserControl
{
    private readonly ToggleSwitch toggle = new() { MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
    private bool settingFromCode;
    public event RoutedEventHandler? Click;
    public FeatureSwitch()
    {
        Content = toggle; IsTabStop = false;
        toggle.Toggled += (_, _) => { if (!settingFromCode) Click?.Invoke(this, new RoutedEventArgs()); };
        RegisterPropertyChangedCallback(Microsoft.UI.Xaml.Automation.AutomationProperties.NameProperty,
            (_, _) => Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(this)));
    }
    /// <summary>The hosted switch (self-checks and styling).</summary>
    public ToggleSwitch Switch => toggle;
    public bool IsOn { get => toggle.IsOn; set => IsChecked = value; }
    public bool? IsChecked
    {
        get => toggle.IsOn;
        set { bool on = value == true; if (toggle.IsOn == on) return; settingFromCode = true; try { toggle.IsOn = on; } finally { settingFromCode = false; } }
    }
    public object? Header { get => toggle.Header; set => toggle.Header = value; }
    public object? OnContent { get => toggle.OnContent; set => toggle.OnContent = value; }
    public object? OffContent { get => toggle.OffContent; set => toggle.OffContent = value; }
    /// <summary>A compact switch for the toolbar: the label sits beside it whether on or off.</summary>
    public string Label
    {
        get => toggle.OnContent as string ?? "";
        set { toggle.OnContent = toggle.OffContent = value; if (string.IsNullOrEmpty(Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(this))) Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, value); }
    }
}
