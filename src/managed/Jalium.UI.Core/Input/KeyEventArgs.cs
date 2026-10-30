namespace Jalium.UI.Input;

/// <summary>
/// Provides data for keyboard events.
/// </summary>
public class KeyEventArgs : KeyboardEventArgs
{
    private readonly Key _realKey;

    /// <summary>
    /// Gets the key that was pressed or released.
    /// </summary>
    public Key Key => _realKey;

    /// <summary>
    /// Gets the system key if this is a system key event.
    /// </summary>
    public Key SystemKey => _realKey == Key.System ? _realKey : Key.None;

    /// <summary>Gets the key processed by an input method editor.</summary>
    public Key ImeProcessedKey => _realKey == Key.ImeProcessed ? _realKey : Key.None;

    /// <summary>Gets the key processed as a dead character.</summary>
    public Key DeadCharProcessedKey =>
        _realKey == Key.DeadCharProcessed ? _realKey : Key.None;

    /// <summary>Gets the presentation source that reported the key.</summary>
    public PresentationSource InputSource { get; } = null!;

    /// <summary>Gets the complete state of the key.</summary>
    public KeyStates KeyStates { get; }

    /// <summary>Gets whether the key is toggled.</summary>
    public bool IsToggled => (KeyStates & KeyStates.Toggled) != 0;

    /// <summary>
    /// Gets the modifier keys that were pressed during the event.
    /// </summary>
    public ModifierKeys KeyboardModifiers { get; }

    /// <summary>
    /// Gets the physical modifiers captured by a platform input provider, before
    /// compatibility mapping. Windows represents Command on macOS. A null value
    /// means the event was created without a physical modifier snapshot.
    /// </summary>
    public ModifierKeys? PhysicalModifiers { get; internal init; }

    /// <summary>Gets whether a platform event has the macOS Command key held.</summary>
    public bool IsCommandDown => OperatingSystem.IsMacOS() &&
        (PhysicalModifiers.GetValueOrDefault() & ModifierKeys.Windows) != 0;

    /// <summary>
    /// Gets a value indicating whether this is a repeated key event.
    /// </summary>
    public bool IsRepeat { get; }

    /// <summary>
    /// Gets a value indicating whether the key is currently down.
    /// </summary>
    public bool IsDown => (KeyStates & KeyStates.Down) != 0;

    /// <summary>
    /// Gets a value indicating whether the key is currently up.
    /// </summary>
    public bool IsUp => !IsDown;

    /// <summary>
    /// Gets a value indicating whether the Alt key was pressed.
    /// </summary>
    public bool IsAltDown => (KeyboardModifiers & ModifierKeys.Alt) != 0;

    /// <summary>
    /// Gets a value indicating whether the compatible Control command modifier
    /// was pressed. Platform input also maps macOS Command to this modifier;
    /// use <see cref="PhysicalModifiers"/> to distinguish the physical keys.
    /// </summary>
    public bool IsControlDown => (KeyboardModifiers & ModifierKeys.Control) != 0;

    /// <summary>
    /// Gets a value indicating whether the Shift key was pressed.
    /// </summary>
    public bool IsShiftDown => (KeyboardModifiers & ModifierKeys.Shift) != 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyEventArgs"/> class.
    /// </summary>
    internal KeyEventArgs(RoutedEvent routedEvent, Key key, ModifierKeys modifiers, bool isDown, bool isRepeat, int timestamp)
        : base(InputManager.Current.PrimaryKeyboardDevice, timestamp)
    {
        RoutedEvent = routedEvent ?? throw new ArgumentNullException(nameof(routedEvent));
        _realKey = key;
        KeyboardModifiers = modifiers;
        KeyStates = isDown ? KeyStates.Down : KeyStates.None;
        IsRepeat = isRepeat;
    }

    /// <summary>Initializes device-backed keyboard event data.</summary>
    public KeyEventArgs(
        KeyboardDevice keyboard,
        PresentationSource inputSource,
        int timestamp,
        Key key)
        : base(keyboard ?? throw new ArgumentNullException(nameof(keyboard)), timestamp)
    {
        InputSource = inputSource ?? throw new ArgumentNullException(nameof(inputSource));
        if (!Enum.IsDefined(key))
            throw new System.ComponentModel.InvalidEnumArgumentException(
                nameof(key), (int)key, typeof(Key));
        _realKey = key;
        KeyStates = keyboard.GetKeyStates(key);
        KeyboardModifiers = keyboard.Modifiers;
    }

    /// <inheritdoc />
    protected override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is KeyEventHandler keyHandler)
        {
            keyHandler(target, this);
        }
        else
        {
            base.InvokeEventHandler(handler, target);
        }
    }
}

/// <summary>
/// Delegate for handling keyboard events.
/// </summary>
public delegate void KeyEventHandler(object sender, KeyEventArgs e);
