namespace Jalium.UI.Input;

/// <summary>
/// Abstract class that describes an input device.
/// </summary>
public abstract class InputDevice
{
    /// <summary>
    /// Gets the element that receives input from this device.
    /// </summary>
    public abstract UIElement? Target { get; }

    /// <summary>
    /// Gets the PresentationSource that reports input for this device.
    /// </summary>
    public abstract object? ActiveSource { get; }
}

/// <summary>
/// Represents the keyboard device.
/// </summary>
public abstract class KeyboardDevice : InputDevice
{
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
            if (IsKeyDown(Key.Alt))
                modifiers |= ModifierKeys.Alt;
            if (IsKeyDown(Key.Ctrl))
                modifiers |= ModifierKeys.Control;
            if (IsKeyDown(Key.Shift))
                modifiers |= ModifierKeys.Shift;
            return modifiers;
        }
    }

    /// <summary>
    /// Gets the element that has keyboard focus.
    /// </summary>
    public UIElement? FocusedElement { get; internal set; }

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
    public UIElement? DirectlyOver { get; internal set; }

    /// <summary>
    /// Gets the element that has captured the mouse.
    /// </summary>
    public UIElement? Captured { get; internal set; }

    /// <summary>
    /// Gets the position of the mouse relative to a specified element.
    /// </summary>
    public Point GetPosition(UIElement? relativeTo)
    {
        return GetPositionCore(relativeTo);
    }

    /// <summary>
    /// Captures the mouse to the specified element.
    /// </summary>
    public bool Capture(UIElement? element)
    {
        Captured = element;
        return true;
    }

    /// <summary>
    /// When implemented, gets the button state from the system.
    /// </summary>
    protected abstract MouseButtonState GetButtonState(MouseButton mouseButton);

    /// <summary>
    /// When implemented, gets the position from the system.
    /// </summary>
    protected abstract Point GetPositionCore(UIElement? relativeTo);
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
