using System.ComponentModel;

namespace Jalium.UI.Input;

/// <summary>Specifies the direction in which focus is traversed.</summary>
public enum FocusNavigationDirection
{
    Next,
    Previous,
    First,
    Last,
    Left,
    Right,
    Up,
    Down,
}

/// <summary>Describes a keyboard-focus traversal request.</summary>
public class TraversalRequest
{
    public TraversalRequest(FocusNavigationDirection focusNavigationDirection)
    {
        if (!Enum.IsDefined(focusNavigationDirection))
        {
            throw new InvalidEnumArgumentException(
                nameof(focusNavigationDirection),
                (int)focusNavigationDirection,
                typeof(FocusNavigationDirection));
        }

        FocusNavigationDirection = focusNavigationDirection;
    }

    public FocusNavigationDirection FocusNavigationDirection { get; }

    public bool Wrapped { get; set; }
}

/// <summary>Handles keyboard-focus transition routed events.</summary>
public delegate void KeyboardFocusChangedEventHandler(object sender, KeyboardFocusChangedEventArgs e);

/// <summary>Provides data for a keyboard-focus transition.</summary>
public class KeyboardFocusChangedEventArgs : KeyboardEventArgs
{
    public KeyboardFocusChangedEventArgs(
        KeyboardDevice keyboard,
        int timestamp,
        IInputElement? oldFocus,
        IInputElement? newFocus)
        : base(keyboard, timestamp)
    {
        ValidateFocusElement(oldFocus, nameof(oldFocus));
        ValidateFocusElement(newFocus, nameof(newFocus));

        OldFocus = oldFocus;
        NewFocus = newFocus;
    }

    public KeyboardFocusChangedEventArgs(
        RoutedEvent routedEvent,
        IInputElement? oldFocus,
        IInputElement? newFocus)
        : base(routedEvent)
    {
        OldFocus = oldFocus;
        NewFocus = newFocus;
    }

    public IInputElement? OldFocus { get; }

    public IInputElement? NewFocus { get; }

    protected override void InvokeEventHandler(Delegate genericHandler, object genericTarget)
    {
        if (genericHandler is KeyboardFocusChangedEventHandler handler)
        {
            handler(genericTarget, this);
            return;
        }

        base.InvokeEventHandler(genericHandler, genericTarget);
    }

    private static void ValidateFocusElement(IInputElement? element, string parameterName)
    {
        if (element is not null && element is not DependencyObject)
        {
            throw new InvalidOperationException(
                $"The {parameterName} value must be a Jalium input element backed by a DependencyObject.");
        }
    }
}
