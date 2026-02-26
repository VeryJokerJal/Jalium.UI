namespace Jalium.UI.Controls;

/// <summary>
/// Provides attached properties and methods for the designer.
/// </summary>
public static class DesignerProperties
{
    /// <summary>
    /// Identifies the IsInDesignMode attached property.
    /// </summary>
    public static readonly DependencyProperty IsInDesignModeProperty =
        DependencyProperty.RegisterAttached("IsInDesignMode", typeof(bool), typeof(DesignerProperties),
            new PropertyMetadata(false));

    /// <summary>
    /// Gets the value of the IsInDesignMode attached property.
    /// </summary>
    public static bool GetIsInDesignMode(DependencyObject element)
    {
        return (bool)(element.GetValue(IsInDesignModeProperty) ?? false);
    }

    /// <summary>
    /// Sets the value of the IsInDesignMode attached property.
    /// </summary>
    public static void SetIsInDesignMode(DependencyObject element, bool value)
    {
        element.SetValue(IsInDesignModeProperty, value);
    }
}
