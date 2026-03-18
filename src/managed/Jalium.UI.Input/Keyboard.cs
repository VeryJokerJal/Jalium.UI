namespace Jalium.UI.Input;

/// <summary>
/// Represents the keyboard input device and provides keyboard focus management.
/// </summary>
public static class Keyboard
{
    private static readonly KeyboardFocusProvider _provider = new();

    /// <summary>
    /// Initializes the keyboard focus system by registering the focus provider.
    /// Call this method at application startup.
    /// </summary>
    public static void Initialize()
    {
        FocusService.Provider = _provider;
    }

    #region Focus

    /// <summary>
    /// Gets the element that has keyboard focus.
    /// </summary>
    public static IInputElement? FocusedElement => _provider.FocusedElement;

    /// <summary>
    /// Sets keyboard focus to the specified element.
    /// </summary>
    /// <param name="element">The element to receive keyboard focus.</param>
    /// <returns>The element that received focus, or null if focus could not be set.</returns>
    public static IInputElement? Focus(IInputElement? element) => _provider.Focus(element);

    /// <summary>
    /// Clears keyboard focus.
    /// </summary>
    public static void ClearFocus() => _provider.ClearFocus();

    #endregion

    #region Routed Events (aliases to FocusService events)

    /// <summary>
    /// Identifies the PreviewGotKeyboardFocus routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewGotKeyboardFocusEvent = FocusService.PreviewGotKeyboardFocusEvent;

    /// <summary>
    /// Identifies the GotKeyboardFocus routed event.
    /// </summary>
    public static readonly RoutedEvent GotKeyboardFocusEvent = FocusService.GotKeyboardFocusEvent;

    /// <summary>
    /// Identifies the PreviewLostKeyboardFocus routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewLostKeyboardFocusEvent = FocusService.PreviewLostKeyboardFocusEvent;

    /// <summary>
    /// Identifies the LostKeyboardFocus routed event.
    /// </summary>
    public static readonly RoutedEvent LostKeyboardFocusEvent = FocusService.LostKeyboardFocusEvent;

    #endregion

    #region Modifier Keys

    /// <summary>
    /// Gets the current modifier key states.
    /// </summary>
    public static ModifierKeys Modifiers { get; internal set; }

    /// <summary>
    /// Determines whether a specific key is currently pressed.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <returns>True if the key is pressed; otherwise, false.</returns>
    public static bool IsKeyDown(Key key)
    {
        short state = NativeMethods.GetAsyncKeyState((int)key);
        return (state & 0x8000) != 0;
    }

    /// <summary>
    /// Determines whether a specific key is currently released.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <returns>True if the key is released; otherwise, false.</returns>
    public static bool IsKeyUp(Key key) => !IsKeyDown(key);

    /// <summary>
    /// Determines whether the toggled state of a key is on.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <returns>True if the key is toggled on; otherwise, false.</returns>
    public static bool IsKeyToggled(Key key)
    {
        short state = NativeMethods.GetAsyncKeyState((int)key);
        return (state & 0x0001) != 0;
    }

    #endregion

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);
    }
}

/// <summary>
/// Internal implementation of the focus provider.
/// </summary>
internal sealed class KeyboardFocusProvider : IFocusProvider
{
    private IInputElement? _focusedElement;
    private bool _isChangingFocus;
    private IInputElement? _pendingFocusElement;
    private bool _hasPendingFocus;

    public IInputElement? FocusedElement => _focusedElement;

    public IInputElement? Focus(IInputElement? element)
    {
        // Check if element can receive focus
        if (element != null)
        {
            if (!element.Focusable || !element.IsEnabled)
            {
                return null;
            }
        }

        var oldFocus = _focusedElement;
        if (oldFocus == element)
            return element;

        // Handle re-entrancy: if we're already changing focus, queue this request
        if (_isChangingFocus)
        {
            _pendingFocusElement = element;
            _hasPendingFocus = true;
            return element;
        }

        _isChangingFocus = true;
        try
        {
            _focusedElement = element;
            RaiseFocusChangedEvents(oldFocus, element);

            // Process any pending focus change that was requested during event handling
            while (_hasPendingFocus)
            {
                var pending = _pendingFocusElement;
                _hasPendingFocus = false;
                _pendingFocusElement = null;

                if (pending != _focusedElement)
                {
                    var currentFocus = _focusedElement;
                    _focusedElement = pending;
                    RaiseFocusChangedEvents(currentFocus, pending);
                }
            }
        }
        finally
        {
            _isChangingFocus = false;
        }

        return _focusedElement;
    }

    public void ClearFocus()
    {
        Focus(null);
    }

    public bool MoveFocus(UIElement element, FocusNavigationDirection direction)
    {
        return KeyboardNavigation.MoveFocus(element, direction);
    }

    private void RaiseFocusChangedEvents(IInputElement? oldFocus, IInputElement? newFocus)
    {
        // Raise PreviewLostKeyboardFocus and LostKeyboardFocus on old element
        if (oldFocus is UIElement oldUIElement)
        {
            var lostFocusArgs = new KeyboardFocusChangedEventArgs(FocusService.PreviewLostKeyboardFocusEvent, oldFocus, newFocus);
            oldUIElement.RaiseEvent(lostFocusArgs);

            lostFocusArgs = new KeyboardFocusChangedEventArgs(FocusService.LostKeyboardFocusEvent, oldFocus, newFocus);
            oldUIElement.RaiseEvent(lostFocusArgs);

            oldUIElement.UpdateIsKeyboardFocused(false);
        }

        // Raise PreviewGotKeyboardFocus and GotKeyboardFocus on new element
        if (newFocus is UIElement newUIElement)
        {
            var gotFocusArgs = new KeyboardFocusChangedEventArgs(FocusService.PreviewGotKeyboardFocusEvent, oldFocus, newFocus);
            newUIElement.RaiseEvent(gotFocusArgs);

            gotFocusArgs = new KeyboardFocusChangedEventArgs(FocusService.GotKeyboardFocusEvent, oldFocus, newFocus);
            newUIElement.RaiseEvent(gotFocusArgs);

            newUIElement.UpdateIsKeyboardFocused(true);
        }

        // Update IsKeyboardFocusWithin for all ancestors
        UpdateIsKeyboardFocusWithin(oldFocus as UIElement, newFocus as UIElement);
    }

    private void UpdateIsKeyboardFocusWithin(UIElement? oldFocus, UIElement? newFocus)
    {
        var oldChain = GetVisualAncestorChain(oldFocus);
        var newChain = GetVisualAncestorChain(newFocus);
        var newSet = new HashSet<UIElement>(newChain);
        var oldSet = new HashSet<UIElement>(oldChain);

        // Clear IsKeyboardFocusWithin only for ancestors that no longer contain the focused element.
        foreach (var element in oldChain)
        {
            if (!newSet.Contains(element))
            {
                element.UpdateIsKeyboardFocusWithin(false);
            }
        }

        // Set IsKeyboardFocusWithin only for ancestors newly entered by the focused element.
        foreach (var element in newChain)
        {
            if (!oldSet.Contains(element))
            {
                element.UpdateIsKeyboardFocusWithin(true);
            }
        }
    }

    private static List<UIElement> GetVisualAncestorChain(UIElement? element)
    {
        var chain = new List<UIElement>();
        var current = element;
        while (current != null)
        {
            chain.Add(current);
            current = current.VisualParent as UIElement;
        }

        return chain;
    }
}
// Key and ModifierKeys enums moved to Jalium.UI.Core/Input/KeyboardEnums.cs
