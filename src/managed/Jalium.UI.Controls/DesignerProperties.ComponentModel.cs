using Jalium.UI;

namespace System.ComponentModel;

/// <summary>
/// Provides attached properties used to communicate with a designer.
/// </summary>
public static class DesignerProperties
{
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsInDesignModeProperty =
        DependencyProperty.RegisterAttached(
            "IsInDesignMode",
            typeof(bool),
            typeof(DesignerProperties),
            new FrameworkPropertyMetadata(
                false,
                FrameworkPropertyMetadataOptions.Inherits |
                FrameworkPropertyMetadataOptions.OverridesInheritanceBehavior));

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static bool GetIsInDesignMode(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)(element.GetValue(IsInDesignModeProperty) ?? false);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static void SetIsInDesignMode(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsInDesignModeProperty, value);
    }
}
