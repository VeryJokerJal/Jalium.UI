using Jalium.UI;

namespace Jalium.UI.Input;

/// <summary>
/// Abstract class that describes an input device.
/// </summary>
public abstract class InputDevice
{
    /// <summary>
    /// Gets the element that receives input from this device.
    /// </summary>
    public abstract IInputElement? Target { get; }

    /// <summary>
    /// Gets the PresentationSource that reports input for this device.
    /// </summary>
    public abstract PresentationSource? ActiveSource { get; }
}

/// <summary>
/// Represents the keyboard device.
/// </summary>
public abstract class KeyboardDevice : InputDevice
{
    private RestoreFocusMode _defaultRestoreFocusMode = RestoreFocusMode.Auto;

    /// <summary>Initializes a keyboard device owned by an input manager.</summary>
    protected KeyboardDevice(InputManager inputManager)
    {
        InputManager = inputManager ?? throw new ArgumentNullException(nameof(inputManager));
    }

    /// <summary>Initializes a keyboard device for the current input manager.</summary>
    protected KeyboardDevice()
        : this(InputManager.Current)
    {
    }

    internal InputManager InputManager { get; }

    /// <summary>
    /// Gets the set of key states for the specified key.
    /// </summary>
    public KeyStates GetKeyStates(Key key)
    {
        return GetKeyStatesFromSystem(key);
    }

    /// <summary>
    /// Gets a value indicating whether the specified key is pressed.
    /// </summary>
    public bool IsKeyDown(Key key) => (GetKeyStates(key) & KeyStates.Down) == KeyStates.Down;

    /// <summary>
    /// Gets a value indicating whether the specified key is released.
    /// </summary>
    public bool IsKeyUp(Key key) => (GetKeyStates(key) & KeyStates.Down) != KeyStates.Down;

    /// <summary>
    /// Gets a value indicating whether the specified key has been toggled.
    /// </summary>
    public bool IsKeyToggled(Key key) => (GetKeyStates(key) & KeyStates.Toggled) == KeyStates.Toggled;

    /// <summary>
    /// Gets the set of ModifierKeys currently pressed.
    /// </summary>
    public ModifierKeys Modifiers
    {
        get
        {
            var modifiers = ModifierKeys.None;
            if (IsKeyDown(Key.LeftAlt) || IsKeyDown(Key.RightAlt))
                modifiers |= ModifierKeys.Alt;
            if (IsKeyDown(Key.LeftCtrl) || IsKeyDown(Key.RightCtrl))
                modifiers |= ModifierKeys.Control;
            if (IsKeyDown(Key.LeftShift) || IsKeyDown(Key.RightShift))
                modifiers |= ModifierKeys.Shift;
            if (IsKeyDown(Key.LWin) || IsKeyDown(Key.RWin))
                modifiers |= ModifierKeys.Windows;
            return modifiers;
        }
    }

    /// <summary>Gets or sets the mode used when native keyboard focus returns.</summary>
    public RestoreFocusMode DefaultRestoreFocusMode
    {
        get => _defaultRestoreFocusMode;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new System.ComponentModel.InvalidEnumArgumentException(
                    nameof(value),
                    (int)value,
                    typeof(RestoreFocusMode));
            }

            _defaultRestoreFocusMode = value;
        }
    }

    /// <summary>
    /// Gets the element that has keyboard focus.
    /// </summary>
    public IInputElement? FocusedElement { get; internal set; }

    /// <summary>Clears logical keyboard focus.</summary>
    public void ClearFocus() => FocusService.ClearFocus();

    /// <summary>Attempts to move logical keyboard focus to an input element.</summary>
    public IInputElement? Focus(IInputElement? element) => FocusService.Focus(element);

    /// <summary>
    /// When implemented, gets the key states from the system.
    /// </summary>
    protected abstract KeyStates GetKeyStatesFromSystem(Key key);
}

/// <summary>
/// Represents the mouse device.
/// </summary>
public abstract class MouseDevice : InputDevice
{
    private Cursor? _overrideCursor;

    /// <summary>Initializes a mouse device owned by an input manager.</summary>
    protected MouseDevice(InputManager inputManager)
    {
        InputManager = inputManager ?? throw new ArgumentNullException(nameof(inputManager));
    }

    /// <summary>Initializes a mouse device for the current input manager.</summary>
    protected MouseDevice()
        : this(InputManager.Current)
    {
    }

    internal InputManager InputManager { get; }

    /// <summary>
    /// Gets the state of the left button.
    /// </summary>
    public MouseButtonState LeftButton => GetButtonState(MouseButton.Left);

    /// <summary>
    /// Gets the state of the right button.
    /// </summary>
    public MouseButtonState RightButton => GetButtonState(MouseButton.Right);

    /// <summary>
    /// Gets the state of the middle button.
    /// </summary>
    public MouseButtonState MiddleButton => GetButtonState(MouseButton.Middle);

    /// <summary>
    /// Gets the state of the first extended button.
    /// </summary>
    public MouseButtonState XButton1 => GetButtonState(MouseButton.XButton1);

    /// <summary>
    /// Gets the state of the second extended button.
    /// </summary>
    public MouseButtonState XButton2 => GetButtonState(MouseButton.XButton2);

    /// <summary>
    /// Gets the element that the mouse is directly over.
    /// </summary>
    public IInputElement? DirectlyOver { get; internal set; }

    /// <summary>
    /// Gets the element that has captured the mouse.
    /// </summary>
    public IInputElement? Captured { get; internal set; }

    /// <summary>Gets or sets the cursor that overrides element cursor selection.</summary>
    public Cursor? OverrideCursor
    {
        get => _overrideCursor;
        set
        {
            _overrideCursor = value;
            UpdateCursor();
        }
    }

    /// <summary>
    /// Gets the position of the mouse relative to a specified element.
    /// </summary>
    public Point GetPosition(IInputElement? relativeTo)
    {
        return GetPositionCore(relativeTo);
    }

    /// <summary>
    /// Captures the mouse to the specified element.
    /// </summary>
    public bool Capture(IInputElement? element)
        => Capture(element, CaptureMode.Element);

    /// <summary>Captures the mouse using the specified capture mode.</summary>
    public bool Capture(IInputElement? element, CaptureMode captureMode)
    {
        if (!Enum.IsDefined(captureMode))
            throw new System.ComponentModel.InvalidEnumArgumentException(
                nameof(captureMode), (int)captureMode, typeof(CaptureMode));
        if (element is not null && captureMode == CaptureMode.None)
            throw new ArgumentException("A non-null element requires an active capture mode.", nameof(captureMode));
        Captured = element;
        return true;
    }

    /// <summary>Sets the cursor used by this device.</summary>
    public bool SetCursor(Cursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        _overrideCursor = cursor;
        return true;
    }

    /// <summary>Synchronizes cached mouse state with the platform device.</summary>
    public void Synchronize() => InputManager.InvalidateHitTest();

    /// <summary>Refreshes cursor selection for the current mouse target.</summary>
    public void UpdateCursor() => InputManager.InvalidateHitTest();

    /// <summary>Gets the current client position.</summary>
    protected Point GetClientPosition() => GetPositionCore(null);

    /// <summary>Gets the current position relative to a presentation source.</summary>
    protected Point GetClientPosition(PresentationSource presentationSource)
    {
        ArgumentNullException.ThrowIfNull(presentationSource);
        return GetPositionCore(presentationSource.RootVisual as IInputElement);
    }

    /// <summary>Gets the current screen position.</summary>
    protected Point GetScreenPosition() => GetPositionCore(null);

    /// <summary>
    /// When implemented, gets the button state from the system.
    /// </summary>
    protected abstract MouseButtonState GetButtonState(MouseButton mouseButton);

    /// <summary>
    /// When implemented, gets the position from the system.
    /// </summary>
    protected abstract Point GetPositionCore(IInputElement? relativeTo);
}

/// <summary>
/// Specifies the possible key states.
/// </summary>
[Flags]
public enum KeyStates
{
    /// <summary>The key is not pressed.</summary>
    None = 0,

    /// <summary>The key is pressed.</summary>
    Down = 1,

    /// <summary>The key is toggled.</summary>
    Toggled = 2
}

/// <summary>
/// Specifies the capture mode for mouse input.
/// </summary>
public enum CaptureMode
{
    /// <summary>No capture.</summary>
    None,

    /// <summary>Mouse is captured to a single element.</summary>
    Element,

    /// <summary>Mouse is captured to a subtree of elements.</summary>
    SubTree
}

/// <summary>
/// Specifies the type of input event.
/// </summary>
public enum InputType
{
    /// <summary>Keyboard input.</summary>
    Keyboard,

    /// <summary>Mouse input.</summary>
    Mouse,

    /// <summary>Stylus input.</summary>
    Stylus,

    /// <summary>HID input.</summary>
    Hid,

    /// <summary>Text input.</summary>
    Text,

    /// <summary>Command input.</summary>
    Command
}

/// <summary>
/// Specifies the input processing mode.
/// </summary>
public enum InputMode
{
    /// <summary>Input is in foreground mode.</summary>
    Foreground,

    /// <summary>Input is in sink mode.</summary>
    Sink
}
