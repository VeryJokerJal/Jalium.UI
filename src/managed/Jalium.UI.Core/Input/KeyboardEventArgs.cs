namespace Jalium.UI.Input;

/// <summary>
/// Provides the logical keyboard device for device-level keyboard events.
/// </summary>
public class KeyboardEventArgs : InputEventArgs
{
    protected KeyboardEventArgs(RoutedEvent routedEvent)
        : base(routedEvent)
    {
    }

    public KeyboardEventArgs(KeyboardDevice keyboard, int timestamp)
        : base(keyboard ?? throw new ArgumentNullException(nameof(keyboard)), timestamp)
    {
    }

    /// <summary>Gets the keyboard device associated with this event.</summary>
    public KeyboardDevice KeyboardDevice => (KeyboardDevice)Device;

    protected override void InvokeEventHandler(Delegate genericHandler, object genericTarget)
    {
        if (genericHandler is KeyboardEventHandler handler)
        {
            handler(genericTarget, this);
            return;
        }

        base.InvokeEventHandler(genericHandler, genericTarget);
    }
}

/// <summary>Handles logical keyboard-device events.</summary>
public delegate void KeyboardEventHandler(object sender, KeyboardEventArgs e);

/// <summary>
/// Reports an input provider's attempt to acquire native keyboard focus.
/// </summary>
public class KeyboardInputProviderAcquireFocusEventArgs : KeyboardEventArgs
{
    public KeyboardInputProviderAcquireFocusEventArgs(
        KeyboardDevice keyboard,
        int timestamp,
        bool focusAcquired)
        : base(keyboard, timestamp)
    {
        FocusAcquired = focusAcquired;
    }

    /// <summary>Gets whether the input provider acquired native focus.</summary>
    public bool FocusAcquired { get; }

    protected override void InvokeEventHandler(Delegate genericHandler, object genericTarget)
    {
        if (genericHandler is KeyboardInputProviderAcquireFocusEventHandler handler)
        {
            handler(genericTarget, this);
            return;
        }

        base.InvokeEventHandler(genericHandler, genericTarget);
    }
}

/// <summary>Handles keyboard-provider focus-acquisition notifications.</summary>
public delegate void KeyboardInputProviderAcquireFocusEventHandler(
    object sender,
    KeyboardInputProviderAcquireFocusEventArgs e);
