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

/// <summary>
/// Enumeration of modifier keys.
/// </summary>
[Flags]
public enum ModifierKeys
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8
}

/// <summary>
/// Enumeration of keyboard keys.
/// </summary>
public enum Key
{
    None = 0,
    Back = 8,
    Tab = 9,
    Enter = 13,
    Shift = 16,
    Ctrl = 17,
    Alt = 18,
    Pause = 19,
    CapsLock = 20,
    Escape = 27,
    Space = 32,
    PageUp = 33,
    PageDown = 34,
    End = 35,
    Home = 36,
    Left = 37,
    Up = 38,
    Right = 39,
    Down = 40,
    Insert = 45,
    Delete = 46,

    // Numbers
    D0 = 48, D1, D2, D3, D4, D5, D6, D7, D8, D9,

    // Letters
    A = 65, B, C, D, E, F, G, H, I, J, K, L, M,
    N, O, P, Q, R, S, T, U, V, W, X, Y, Z,

    // Numpad
    NumPad0 = 96, NumPad1, NumPad2, NumPad3, NumPad4,
    NumPad5, NumPad6, NumPad7, NumPad8, NumPad9,
    Multiply = 106,
    Add = 107,
    Subtract = 109,
    Decimal = 110,
    Divide = 111,

    // Function keys
    F1 = 112, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,

    // OEM keys
    OemSemicolon = 186,
    OemPlus = 187,
    OemComma = 188,
    OemMinus = 189,
    OemPeriod = 190,
    OemQuestion = 191,
    OemTilde = 192,
    OemOpenBrackets = 219,
    OemPipe = 220,
    OemCloseBrackets = 221,
    OemQuotes = 222
}
