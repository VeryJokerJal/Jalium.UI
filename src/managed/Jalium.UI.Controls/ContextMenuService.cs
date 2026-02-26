using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Controls;

/// <summary>
/// Provides static methods and attached properties to manage context menus.
/// </summary>
public static class ContextMenuService
{
    #region Attached Properties

    /// <summary>
    /// Identifies the ContextMenu attached dependency property.
    /// </summary>
    public static readonly DependencyProperty ContextMenuProperty =
        DependencyProperty.RegisterAttached("ContextMenu", typeof(ContextMenu), typeof(ContextMenuService),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the IsEnabled attached dependency property.
    /// </summary>
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(ContextMenuService),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the Placement attached dependency property.
    /// </summary>
    public static readonly DependencyProperty PlacementProperty =
        DependencyProperty.RegisterAttached("Placement", typeof(PlacementMode), typeof(ContextMenuService),
            new PropertyMetadata(PlacementMode.MousePoint));

    /// <summary>
    /// Identifies the PlacementTarget attached dependency property.
    /// </summary>
    public static readonly DependencyProperty PlacementTargetProperty =
        DependencyProperty.RegisterAttached("PlacementTarget", typeof(UIElement), typeof(ContextMenuService),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the HorizontalOffset attached dependency property.
    /// </summary>
    public static readonly DependencyProperty HorizontalOffsetProperty =
        DependencyProperty.RegisterAttached("HorizontalOffset", typeof(double), typeof(ContextMenuService),
            new PropertyMetadata(0.0));

    /// <summary>
    /// Identifies the VerticalOffset attached dependency property.
    /// </summary>
    public static readonly DependencyProperty VerticalOffsetProperty =
        DependencyProperty.RegisterAttached("VerticalOffset", typeof(double), typeof(ContextMenuService),
            new PropertyMetadata(0.0));

    /// <summary>
    /// Identifies the HasDropShadow attached dependency property.
    /// </summary>
    public static readonly DependencyProperty HasDropShadowProperty =
        DependencyProperty.RegisterAttached("HasDropShadow", typeof(bool), typeof(ContextMenuService),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the ShowOnDisabled attached dependency property.
    /// </summary>
    public static readonly DependencyProperty ShowOnDisabledProperty =
        DependencyProperty.RegisterAttached("ShowOnDisabled", typeof(bool), typeof(ContextMenuService),
            new PropertyMetadata(false));

    #endregion

    #region Getters and Setters

    /// <summary>
    /// Gets the context menu for the specified element.
    /// </summary>
    public static ContextMenu? GetContextMenu(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (ContextMenu?)element.GetValue(ContextMenuProperty);
    }

    /// <summary>
    /// Sets the context menu for the specified element.
    /// </summary>
    public static void SetContextMenu(DependencyObject element, ContextMenu? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(ContextMenuProperty, value);
    }

    /// <summary>
    /// Gets whether context menu is enabled for the specified element.
    /// </summary>
    public static bool GetIsEnabled(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(IsEnabledProperty);
    }

    /// <summary>
    /// Sets whether context menu is enabled for the specified element.
    /// </summary>
    public static void SetIsEnabled(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsEnabledProperty, value);
    }

    /// <summary>
    /// Gets the placement mode for the context menu.
    /// </summary>
    public static PlacementMode GetPlacement(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (PlacementMode)element.GetValue(PlacementProperty);
    }

    /// <summary>
    /// Sets the placement mode for the context menu.
    /// </summary>
    public static void SetPlacement(DependencyObject element, PlacementMode value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(PlacementProperty, value);
    }

    /// <summary>
    /// Gets the placement target for the context menu.
    /// </summary>
    public static UIElement? GetPlacementTarget(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (UIElement?)element.GetValue(PlacementTargetProperty);
    }

    /// <summary>
    /// Sets the placement target for the context menu.
    /// </summary>
    public static void SetPlacementTarget(DependencyObject element, UIElement? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(PlacementTargetProperty, value);
    }

    /// <summary>
    /// Gets the horizontal offset for the context menu.
    /// </summary>
    public static double GetHorizontalOffset(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (double)element.GetValue(HorizontalOffsetProperty);
    }

    /// <summary>
    /// Sets the horizontal offset for the context menu.
    /// </summary>
    public static void SetHorizontalOffset(DependencyObject element, double value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(HorizontalOffsetProperty, value);
    }

    /// <summary>
    /// Gets the vertical offset for the context menu.
    /// </summary>
    public static double GetVerticalOffset(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (double)element.GetValue(VerticalOffsetProperty);
    }

    /// <summary>
    /// Sets the vertical offset for the context menu.
    /// </summary>
    public static void SetVerticalOffset(DependencyObject element, double value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(VerticalOffsetProperty, value);
    }

    /// <summary>
    /// Gets whether the context menu has a drop shadow.
    /// </summary>
    public static bool GetHasDropShadow(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(HasDropShadowProperty);
    }

    /// <summary>
    /// Sets whether the context menu has a drop shadow.
    /// </summary>
    public static void SetHasDropShadow(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(HasDropShadowProperty, value);
    }

    /// <summary>
    /// Gets whether the context menu should show on disabled elements.
    /// </summary>
    public static bool GetShowOnDisabled(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(ShowOnDisabledProperty);
    }

    /// <summary>
    /// Sets whether the context menu should show on disabled elements.
    /// </summary>
    public static void SetShowOnDisabled(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(ShowOnDisabledProperty, value);
    }

    #endregion
}
