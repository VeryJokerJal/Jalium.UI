using Jalium.UI.Media;

namespace Jalium.UI.Input;

/// <summary>
/// Maintains logical focus independently for each focus scope.
/// </summary>
public static class FocusManager
{
    /// <summary>Identifies the attached property that marks a focus scope.</summary>
    public static readonly DependencyProperty IsFocusScopeProperty =
        DependencyProperty.RegisterAttached(
            "IsFocusScope",
            typeof(bool),
            typeof(FocusManager),
            new PropertyMetadata(false));

    /// <summary>Identifies the logical focused element attached property.</summary>
    public static readonly DependencyProperty FocusedElementProperty =
        DependencyProperty.RegisterAttached(
            "FocusedElement",
            typeof(IInputElement),
            typeof(FocusManager),
            new PropertyMetadata(null));

    public static bool GetIsFocusScope(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)(element.GetValue(IsFocusScopeProperty) ?? false);
    }

    public static void SetIsFocusScope(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsFocusScopeProperty, value);
    }

    public static IInputElement? GetFocusedElement(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(FocusedElementProperty) as IInputElement;
    }

    public static void SetFocusedElement(DependencyObject element, IInputElement? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(FocusedElementProperty, value);
    }

    /// <summary>Finds the nearest focus scope that contains an element.</summary>
    public static DependencyObject GetFocusScope(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);

        DependencyObject? current = element;
        DependencyObject root = element;
        while (current is not null)
        {
            root = current;
            if (GetIsFocusScope(current))
                return current;

            current = GetParent(current);
        }

        return root;
    }

    internal static void OnKeyboardFocusChanged(IInputElement? focusedElement)
    {
        if (focusedElement is not DependencyObject dependencyObject)
            return;

        SetFocusedElement(GetFocusScope(dependencyObject), focusedElement);
    }

    private static DependencyObject? GetParent(DependencyObject element)
    {
        if (element is FrameworkElement frameworkElement && frameworkElement.Parent is not null)
            return frameworkElement.Parent;
        return VisualTreeHelper.GetParent(element);
    }
}
