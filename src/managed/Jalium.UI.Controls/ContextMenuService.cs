using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;

namespace Jalium.UI.Controls;

/// <summary>
/// Provides static methods and attached properties to manage context menus.
/// </summary>
public static class ContextMenuService
{
    private sealed record ContextMenuOpenState(UIElement Owner, bool HasPosition, Point Position);
    private static readonly Dictionary<ContextMenu, ContextMenuOpenState> s_openStates = [];

    static ContextMenuService()
    {
        EventManager.RegisterClassHandler(
            typeof(UIElement),
            UIElement.MouseUpEvent,
            new RoutedEventHandler(OnMouseUpClassHandler),
            handledEventsToo: true);

        EventManager.RegisterClassHandler(
            typeof(UIElement),
            UIElement.KeyDownEvent,
            new RoutedEventHandler(OnKeyDownClassHandler),
            handledEventsToo: true);
    }

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

    #region Routed Events

    /// <summary>
    /// Identifies the ContextMenuOpening routed event.
    /// </summary>
    public static readonly RoutedEvent ContextMenuOpeningEvent =
        EventManager.RegisterRoutedEvent(
            "ContextMenuOpening",
            RoutingStrategy.Bubble,
            typeof(ContextMenuEventHandler),
            typeof(ContextMenuService));

    /// <summary>
    /// Identifies the ContextMenuClosing routed event.
    /// </summary>
    public static readonly RoutedEvent ContextMenuClosingEvent =
        EventManager.RegisterRoutedEvent(
            "ContextMenuClosing",
            RoutingStrategy.Bubble,
            typeof(ContextMenuEventHandler),
            typeof(ContextMenuService));

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

    #region Routed Event Handlers

    /// <summary>
    /// Adds a handler for the <see cref="ContextMenuOpeningEvent"/> routed event.
    /// </summary>
    public static void AddContextMenuOpeningHandler(DependencyObject element, ContextMenuEventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(handler);
        if (element is UIElement uiElement)
        {
            uiElement.AddHandler(ContextMenuOpeningEvent, handler);
        }
    }

    /// <summary>
    /// Removes a handler for the <see cref="ContextMenuOpeningEvent"/> routed event.
    /// </summary>
    public static void RemoveContextMenuOpeningHandler(DependencyObject element, ContextMenuEventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(handler);
        if (element is UIElement uiElement)
        {
            uiElement.RemoveHandler(ContextMenuOpeningEvent, handler);
        }
    }

    /// <summary>
    /// Adds a handler for the <see cref="ContextMenuClosingEvent"/> routed event.
    /// </summary>
    public static void AddContextMenuClosingHandler(DependencyObject element, ContextMenuEventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(handler);
        if (element is UIElement uiElement)
        {
            uiElement.AddHandler(ContextMenuClosingEvent, handler);
        }
    }

    /// <summary>
    /// Removes a handler for the <see cref="ContextMenuClosingEvent"/> routed event.
    /// </summary>
    public static void RemoveContextMenuClosingHandler(DependencyObject element, ContextMenuEventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(handler);
        if (element is UIElement uiElement)
        {
            uiElement.RemoveHandler(ContextMenuClosingEvent, handler);
        }
    }

    #endregion

    #region Open APIs

    /// <summary>
    /// Tries to open the attached context menu for <paramref name="owner"/> at the specified pointer position.
    /// </summary>
    /// <returns><c>true</c> if the menu was opened; otherwise <c>false</c>.</returns>
    public static bool TryOpen(UIElement owner, Point position)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var menu = GetContextMenu(owner);
        if (menu == null)
        {
            return false;
        }

        return TryOpenCore(owner, menu, isKeyboardRequest: false, position);
    }

    /// <summary>
    /// Opens <paramref name="menu"/> for <paramref name="owner"/> at the specified pointer position.
    /// </summary>
    public static void Open(UIElement owner, ContextMenu menu, Point position)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(menu);
        TryOpenCore(owner, menu, isKeyboardRequest: false, position);
    }

    /// <summary>
    /// Opens <paramref name="menu"/> for <paramref name="owner"/> using keyboard semantics.
    /// </summary>
    public static void Open(UIElement owner, ContextMenu menu)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(menu);
        TryOpenCore(owner, menu, isKeyboardRequest: true, position: null);
    }

    #endregion

    #region Internal Open Pipeline

    private static void OnMouseUpClassHandler(object sender, RoutedEventArgs e)
    {
        if (e.Handled || sender is not UIElement owner || e is not MouseButtonEventArgs mouseArgs)
        {
            return;
        }

        if (mouseArgs.ChangedButton != MouseButton.Right)
        {
            return;
        }

        if (TryOpen(owner, mouseArgs.GetPosition(null)))
        {
            e.Handled = true;
        }
    }

    private static void OnKeyDownClassHandler(object sender, RoutedEventArgs e)
    {
        if (e.Handled || sender is not UIElement owner || e is not KeyEventArgs keyArgs)
        {
            return;
        }

        if (keyArgs.Key != Key.F10 || keyArgs.KeyboardModifiers != ModifierKeys.Shift)
        {
            return;
        }

        if (TryOpenAttachedForKeyboard(owner))
        {
            e.Handled = true;
        }
    }

    private static bool TryOpenAttachedForKeyboard(UIElement owner)
    {
        var menu = GetContextMenu(owner);
        if (menu == null)
        {
            return false;
        }

        return TryOpenCore(owner, menu, isKeyboardRequest: true, position: null);
    }

    private static bool TryOpenCore(UIElement owner, ContextMenu menu, bool isKeyboardRequest, Point? position)
    {
        if (!CanOpen(owner))
        {
            return false;
        }

        var openingArgs = position is Point point
            ? new ContextMenuEventArgs(owner, opening: true, point.X, point.Y)
            : new ContextMenuEventArgs(owner, opening: true);
        openingArgs.RoutedEvent = ContextMenuOpeningEvent;
        owner.RaiseEvent(openingArgs);
        if (openingArgs.Handled)
        {
            return false;
        }

        ApplyAttachedSettings(owner, menu, isKeyboardRequest);

        // Popup light-dismiss can leave internal popup state stale when reopening quickly.
        if (menu.IsOpen)
        {
            menu.Close();
        }

        TrackMenuState(menu, owner, position);

        if (position is Point explicitPosition)
        {
            menu.Open(explicitPosition);
        }
        else
        {
            menu.IsOpen = true;
        }

        return true;
    }

    private static bool CanOpen(UIElement owner)
    {
        if (!GetIsEnabled(owner))
        {
            return false;
        }

        if (owner.IsEnabled)
        {
            return true;
        }

        return GetShowOnDisabled(owner);
    }

    private static void ApplyAttachedSettings(UIElement owner, ContextMenu menu, bool isKeyboardRequest)
    {
        menu.PlacementTarget = GetPlacementTarget(owner) ?? owner;

        var placement = GetPlacement(owner);
        if (isKeyboardRequest &&
            (placement == PlacementMode.Mouse || placement == PlacementMode.MousePoint))
        {
            placement = PlacementMode.Bottom;
        }

        menu.Placement = placement;
        menu.HorizontalOffset = GetHorizontalOffset(owner);
        menu.VerticalOffset = GetVerticalOffset(owner);
    }

    private static void TrackMenuState(ContextMenu menu, UIElement owner, Point? position)
    {
        menu.Closed -= OnTrackedMenuClosed;
        menu.Closed += OnTrackedMenuClosed;
        s_openStates[menu] = position is Point p
            ? new ContextMenuOpenState(owner, HasPosition: true, p)
            : new ContextMenuOpenState(owner, HasPosition: false, Point.Zero);
    }

    private static void OnTrackedMenuClosed(object? sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu || !s_openStates.TryGetValue(menu, out var state))
        {
            return;
        }

        s_openStates.Remove(menu);

        var args = state.HasPosition
            ? new ContextMenuEventArgs(state.Owner, opening: false, state.Position.X, state.Position.Y)
            : new ContextMenuEventArgs(state.Owner, opening: false);
        args.RoutedEvent = ContextMenuClosingEvent;
        state.Owner.RaiseEvent(args);
    }

    #endregion
}
