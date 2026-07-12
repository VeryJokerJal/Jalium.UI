namespace Jalium.UI.Controls;

/// <summary>
/// Provides attached properties and methods for the designer.
/// </summary>
public static class DesignerProperties
{
    /// <summary>
    /// Identifies the IsInDesignMode attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsInDesignModeProperty =
        System.ComponentModel.DesignerProperties.IsInDesignModeProperty;

    /// <summary>
    /// Gets the value of the IsInDesignMode attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static bool GetIsInDesignMode(DependencyObject element)
    {
        return System.ComponentModel.DesignerProperties.GetIsInDesignMode(element);
    }

    /// <summary>
    /// Sets the value of the IsInDesignMode attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static void SetIsInDesignMode(DependencyObject element, bool value)
    {
        System.ComponentModel.DesignerProperties.SetIsInDesignMode(element, value);
    }
}
