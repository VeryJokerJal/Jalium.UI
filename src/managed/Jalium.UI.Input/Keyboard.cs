namespace Jalium.UI.Input;

/// <summary>
/// Represents the keyboard input device and provides keyboard focus management.
/// </summary>
public static partial class Keyboard
{
    private static readonly KeyboardFocusProvider _provider = new();
    private static readonly SystemKeyboardDevice _primaryDevice = new();

    static Keyboard()
    {
        Initialize();
        InputManager.Current.RegisterPrimaryKeyboardDevice(_primaryDevice);
    }

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

    internal static void UpdatePrimaryFocusedElement(IInputElement? element) =>
        _primaryDevice.FocusedElement = element;

    /// <summary>Gets the primary logical keyboard device.</summary>
    public static KeyboardDevice PrimaryDevice => _primaryDevice;

    /// <summary>
    /// Sets keyboard focus to the specified element.
    /// </summary>
    /// <param name="element">The element to receive keyboard focus.</param>
    /// <returns>The element that received focus, or null if focus could not be set.</returns>
    public static IInputElement? Focus(IInputElement? element) => PrimaryDevice.Focus(element);

    /// <summary>
    /// Clears keyboard focus.
    /// </summary>
    public static void ClearFocus() => PrimaryDevice.ClearFocus();

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

    /// <summary>Identifies the PreviewKeyDown routed event.</summary>
    public static readonly RoutedEvent PreviewKeyDownEvent = UIElement.PreviewKeyDownEvent;

    /// <summary>Identifies the KeyDown routed event.</summary>
    public static readonly RoutedEvent KeyDownEvent = UIElement.KeyDownEvent;

    /// <summary>Identifies the PreviewKeyUp routed event.</summary>
    public static readonly RoutedEvent PreviewKeyUpEvent = UIElement.PreviewKeyUpEvent;

    /// <summary>Identifies the KeyUp routed event.</summary>
    public static readonly RoutedEvent KeyUpEvent = UIElement.KeyUpEvent;

    /// <summary>
    /// Identifies the tunneling notification raised while an input provider is
    /// acquiring native keyboard focus.
    /// </summary>
    public static readonly RoutedEvent PreviewKeyboardInputProviderAcquireFocusEvent =
        EventManager.RegisterRoutedEvent(
            "PreviewKeyboardInputProviderAcquireFocus",
            RoutingStrategy.Tunnel,
            typeof(KeyboardInputProviderAcquireFocusEventHandler),
            typeof(Keyboard));

    /// <summary>
    /// Identifies the bubbling notification raised after an input provider has
    /// attempted to acquire native keyboard focus.
    /// </summary>
    public static readonly RoutedEvent KeyboardInputProviderAcquireFocusEvent =
        EventManager.RegisterRoutedEvent(
            "KeyboardInputProviderAcquireFocus",
            RoutingStrategy.Bubble,
            typeof(KeyboardInputProviderAcquireFocusEventHandler),
            typeof(Keyboard));

    #endregion

    #region Attached routed-event handlers

    public static void AddPreviewKeyDownHandler(DependencyObject element, KeyEventHandler handler) =>
        AddHandler(element, PreviewKeyDownEvent, handler);

    public static void RemovePreviewKeyDownHandler(DependencyObject element, KeyEventHandler handler) =>
        RemoveHandler(element, PreviewKeyDownEvent, handler);

    public static void AddKeyDownHandler(DependencyObject element, KeyEventHandler handler) =>
        AddHandler(element, KeyDownEvent, handler);

    public static void RemoveKeyDownHandler(DependencyObject element, KeyEventHandler handler) =>
        RemoveHandler(element, KeyDownEvent, handler);

    public static void AddPreviewKeyUpHandler(DependencyObject element, KeyEventHandler handler) =>
        AddHandler(element, PreviewKeyUpEvent, handler);

    public static void RemovePreviewKeyUpHandler(DependencyObject element, KeyEventHandler handler) =>
        RemoveHandler(element, PreviewKeyUpEvent, handler);

    public static void AddKeyUpHandler(DependencyObject element, KeyEventHandler handler) =>
        AddHandler(element, KeyUpEvent, handler);

    public static void RemoveKeyUpHandler(DependencyObject element, KeyEventHandler handler) =>
        RemoveHandler(element, KeyUpEvent, handler);

    public static void AddPreviewGotKeyboardFocusHandler(
        DependencyObject element,
        KeyboardFocusChangedEventHandler handler) =>
        AddHandler(element, PreviewGotKeyboardFocusEvent, handler);

    public static void RemovePreviewGotKeyboardFocusHandler(
        DependencyObject element,
        KeyboardFocusChangedEventHandler handler) =>
        RemoveHandler(element, PreviewGotKeyboardFocusEvent, handler);

    public static void AddGotKeyboardFocusHandler(
        DependencyObject element,
        KeyboardFocusChangedEventHandler handler) =>
        AddHandler(element, GotKeyboardFocusEvent, handler);

    public static void RemoveGotKeyboardFocusHandler(
        DependencyObject element,
        KeyboardFocusChangedEventHandler handler) =>
        RemoveHandler(element, GotKeyboardFocusEvent, handler);

    public static void AddPreviewLostKeyboardFocusHandler(
        DependencyObject element,
        KeyboardFocusChangedEventHandler handler) =>
        AddHandler(element, PreviewLostKeyboardFocusEvent, handler);

    public static void RemovePreviewLostKeyboardFocusHandler(
        DependencyObject element,
        KeyboardFocusChangedEventHandler handler) =>
        RemoveHandler(element, PreviewLostKeyboardFocusEvent, handler);

    public static void AddLostKeyboardFocusHandler(
        DependencyObject element,
        KeyboardFocusChangedEventHandler handler) =>
        AddHandler(element, LostKeyboardFocusEvent, handler);

    public static void RemoveLostKeyboardFocusHandler(
        DependencyObject element,
        KeyboardFocusChangedEventHandler handler) =>
        RemoveHandler(element, LostKeyboardFocusEvent, handler);

    public static void AddPreviewKeyboardInputProviderAcquireFocusHandler(
        DependencyObject element,
        KeyboardInputProviderAcquireFocusEventHandler handler) =>
        AddHandler(element, PreviewKeyboardInputProviderAcquireFocusEvent, handler);

    public static void RemovePreviewKeyboardInputProviderAcquireFocusHandler(
        DependencyObject element,
        KeyboardInputProviderAcquireFocusEventHandler handler) =>
        RemoveHandler(element, PreviewKeyboardInputProviderAcquireFocusEvent, handler);

    public static void AddKeyboardInputProviderAcquireFocusHandler(
        DependencyObject element,
        KeyboardInputProviderAcquireFocusEventHandler handler) =>
        AddHandler(element, KeyboardInputProviderAcquireFocusEvent, handler);

    public static void RemoveKeyboardInputProviderAcquireFocusHandler(
        DependencyObject element,
        KeyboardInputProviderAcquireFocusEventHandler handler) =>
        RemoveHandler(element, KeyboardInputProviderAcquireFocusEvent, handler);

    private static void AddHandler(DependencyObject element, RoutedEvent routedEvent, Delegate handler)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(handler);
        if (element is not IInputElement inputElement)
            throw new ArgumentException("The element must implement IInputElement.", nameof(element));

        inputElement.AddHandler(routedEvent, handler);
    }

    private static void RemoveHandler(DependencyObject element, RoutedEvent routedEvent, Delegate handler)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(handler);
        if (element is not IInputElement inputElement)
            throw new ArgumentException("The element must implement IInputElement.", nameof(element));

        inputElement.RemoveHandler(routedEvent, handler);
    }

    #endregion

    #region Modifier Keys

    /// <summary>
    /// Gets the current modifier key states.
    /// </summary>
    public static ModifierKeys Modifiers => PrimaryDevice.Modifiers;

    /// <summary>Gets or sets the default keyboard-focus restoration mode.</summary>
    public static RestoreFocusMode DefaultRestoreFocusMode
    {
        get => PrimaryDevice.DefaultRestoreFocusMode;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new System.ComponentModel.InvalidEnumArgumentException(
                    nameof(value),
                    (int)value,
                    typeof(RestoreFocusMode));
            }

            PrimaryDevice.DefaultRestoreFocusMode = value;
        }
    }

    /// <summary>
    /// Determines whether a specific key is currently pressed.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <returns>True if the key is pressed; otherwise, false.</returns>
    public static bool IsKeyDown(Key key)
        => PrimaryDevice.IsKeyDown(key);

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
        => PrimaryDevice.IsKeyToggled(key);

    /// <summary>Gets the complete state flags for a logical key.</summary>
    public static KeyStates GetKeyStates(Key key) => PrimaryDevice.GetKeyStates(key);

    #endregion

    internal static partial class NativeMethods
    {
        private static readonly bool s_isWindows =
            System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows);

        public static short GetKeyState(int vKey)
        {
            try
            {
                if (s_isWindows)
                    return GetKeyStateWindows(vKey);

                // Cross-platform native backends expose Win32-compatible high
                // (down) and low (toggle) state bits for normalized VK codes.
                return InputGetKeyState(vKey);
            }
            catch (DllNotFoundException)
            {
                return 0;
            }
            catch (EntryPointNotFoundException)
            {
                return 0;
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetKeyState")]
        private static extern short GetKeyStateWindows(int vKey);

        [System.Runtime.InteropServices.LibraryImport("jalium.native.platform",
            EntryPoint = "jalium_input_get_key_state")]
        private static partial short InputGetKeyState(int virtualKey);
    }

    private sealed class SystemKeyboardDevice : KeyboardDevice
    {
        public override IInputElement? Target => FocusedElement;

        public override PresentationSource? ActiveSource => null;

        protected override KeyStates GetKeyStatesFromSystem(Key key)
        {
            if (!Enum.IsDefined(key))
            {
                throw new System.ComponentModel.InvalidEnumArgumentException(
                    nameof(key),
                    (int)key,
                    typeof(Key));
            }

            int virtualKey = KeyInterop.VirtualKeyFromKey(key);
            if (virtualKey == 0)
                return KeyStates.None;

            short nativeState = NativeMethods.GetKeyState(virtualKey);
            var result = KeyStates.None;
            if ((nativeState & unchecked((short)0x8000)) != 0)
                result |= KeyStates.Down;
            if ((nativeState & 0x0001) != 0)
                result |= KeyStates.Toggled;
            return result;
        }
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
            Keyboard.UpdatePrimaryFocusedElement(element);
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
                    Keyboard.UpdatePrimaryFocusedElement(pending);
                    RaiseFocusChangedEvents(currentFocus, pending);
                }
            }
        }
        finally
        {
            _isChangingFocus = false;
        }

        // Report that the requested element accepted focus even if an event handler
        // synchronously queued a subsequent focus transfer. This keeps
        // IInputElement.Focus truthful for the completed request while FocusedElement
        // still exposes the final element after re-entrant processing.
        return element;
    }

    public void ClearFocus()
    {
        Focus(null);
    }

    public bool MoveFocus(UIElement element, FocusNavigationDirection direction)
    {
        return KeyboardNavigation.MoveFocus(element, direction);
    }

    public DependencyObject? PredictFocus(UIElement element, FocusNavigationDirection direction)
    {
        return KeyboardNavigation.PredictFocus(element, direction);
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
