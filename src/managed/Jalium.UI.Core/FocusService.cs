using Jalium.UI.Input;

namespace Jalium.UI;

/// <summary>
/// Provides focus management services. This class acts as an abstraction
/// that can be configured by the Input layer.
/// </summary>
public static class FocusService
{
    private static IFocusProvider? _provider;

    /// <summary>
    /// Gets or sets the focus provider implementation.
    /// </summary>
    public static IFocusProvider? Provider
    {
        get => _provider;
        set => _provider = value;
    }

    /// <summary>
    /// Gets the element that currently has keyboard focus.
    /// </summary>
    public static IInputElement? FocusedElement => _provider?.FocusedElement;

    /// <summary>
    /// Attempts to set keyboard focus to the specified element.
    /// </summary>
    public static IInputElement? Focus(IInputElement? element)
    {
        var focused = _provider?.Focus(element);
        Input.FocusManager.OnKeyboardFocusChanged(focused);
        return focused;
    }

    /// <summary>
    /// Clears keyboard focus.
    /// </summary>
    public static void ClearFocus()
    {
        _provider?.ClearFocus();
    }

    /// <summary>
    /// Moves focus from the specified element in the given direction.
    /// </summary>
    public static bool MoveFocus(UIElement element, FocusNavigationDirection direction)
    {
        return _provider?.MoveFocus(element, direction) ?? false;
    }

    /// <summary>
    /// Gets the element that would receive focus for a traversal request without
    /// changing the current keyboard focus.
    /// </summary>
    public static DependencyObject? PredictFocus(UIElement element, FocusNavigationDirection direction)
    {
        return _provider?.PredictFocus(element, direction);
    }

    #region Routed Events

    /// <summary>
    /// Identifies the PreviewGotKeyboardFocus routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewGotKeyboardFocusEvent =
        EventManager.RegisterRoutedEvent("PreviewGotKeyboardFocus", RoutingStrategy.Tunnel, typeof(KeyboardFocusChangedEventHandler), typeof(FocusService));

    /// <summary>
    /// Identifies the GotKeyboardFocus routed event.
    /// </summary>
    public static readonly RoutedEvent GotKeyboardFocusEvent =
        EventManager.RegisterRoutedEvent("GotKeyboardFocus", RoutingStrategy.Bubble, typeof(KeyboardFocusChangedEventHandler), typeof(FocusService));

    /// <summary>
    /// Identifies the PreviewLostKeyboardFocus routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewLostKeyboardFocusEvent =
        EventManager.RegisterRoutedEvent("PreviewLostKeyboardFocus", RoutingStrategy.Tunnel, typeof(KeyboardFocusChangedEventHandler), typeof(FocusService));

    /// <summary>
    /// Identifies the LostKeyboardFocus routed event.
    /// </summary>
    public static readonly RoutedEvent LostKeyboardFocusEvent =
        EventManager.RegisterRoutedEvent("LostKeyboardFocus", RoutingStrategy.Bubble, typeof(KeyboardFocusChangedEventHandler), typeof(FocusService));

    /// <summary>
    /// Identifies the GotFocus routed event.
    /// </summary>
    public static readonly RoutedEvent GotFocusEvent =
        EventManager.RegisterRoutedEvent("GotFocus", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(FocusService));

    /// <summary>
    /// Identifies the LostFocus routed event.
    /// </summary>
    public static readonly RoutedEvent LostFocusEvent =
        EventManager.RegisterRoutedEvent("LostFocus", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(FocusService));

    #endregion
}

/// <summary>
/// Interface for focus provider implementations.
/// </summary>
public interface IFocusProvider
{
    /// <summary>
    /// Gets the element that currently has keyboard focus.
    /// </summary>
    IInputElement? FocusedElement { get; }

    /// <summary>
    /// Attempts to set keyboard focus to the specified element.
    /// </summary>
    IInputElement? Focus(IInputElement? element);

    /// <summary>
    /// Clears keyboard focus.
    /// </summary>
    void ClearFocus();

    /// <summary>
    /// Moves focus from the specified element in the given direction.
    /// </summary>
    bool MoveFocus(UIElement element, FocusNavigationDirection direction);

    /// <summary>Predicts the next focus target without moving focus.</summary>
    DependencyObject? PredictFocus(UIElement element, FocusNavigationDirection direction) => null;
}
