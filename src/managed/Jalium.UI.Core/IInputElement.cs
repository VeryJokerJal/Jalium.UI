namespace Jalium.UI;

/// <summary>
/// Interface for elements that can receive input and focus.
/// </summary>
public interface IInputElement
{
    /// <summary>
    /// Gets a value indicating whether this element can receive focus.
    /// </summary>
    bool Focusable { get; }

    /// <summary>
    /// Gets a value indicating whether this element is enabled.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Gets a value indicating whether this element has keyboard focus.
    /// </summary>
    bool IsKeyboardFocused { get; }

    /// <summary>
    /// Gets a value indicating whether keyboard focus is within this element.
    /// </summary>
    bool IsKeyboardFocusWithin { get; }

    /// <summary>
    /// Attempts to set focus to this element.
    /// </summary>
    /// <returns>True if focus was set; otherwise, false.</returns>
    bool Focus();
}

/// <summary>
/// Specifies the direction of focus navigation.
/// </summary>
public enum FocusNavigationDirection
{
    /// <summary>
    /// Move to the next element in the tab order.
    /// </summary>
    Next,

    /// <summary>
    /// Move to the previous element in the tab order.
    /// </summary>
    Previous,

    /// <summary>
    /// Move to the first element.
    /// </summary>
    First,

    /// <summary>
    /// Move to the last element.
    /// </summary>
    Last,

    /// <summary>
    /// Move to the element on the left.
    /// </summary>
    Left,

    /// <summary>
    /// Move to the element on the right.
    /// </summary>
    Right,

    /// <summary>
    /// Move to the element above.
    /// </summary>
    Up,

    /// <summary>
    /// Move to the element below.
    /// </summary>
    Down
}

/// <summary>
/// Delegate for keyboard focus changed event handlers.
/// </summary>
public delegate void KeyboardFocusChangedEventHandler(object sender, KeyboardFocusChangedEventArgs e);

/// <summary>
/// Provides data for keyboard focus changed events.
/// </summary>
public sealed class KeyboardFocusChangedEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Gets the element that previously had keyboard focus.
    /// </summary>
    public IInputElement? OldFocus { get; }

    /// <summary>
    /// Gets the element that now has keyboard focus.
    /// </summary>
    public IInputElement? NewFocus { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyboardFocusChangedEventArgs"/> class.
    /// </summary>
    public KeyboardFocusChangedEventArgs(RoutedEvent routedEvent, IInputElement? oldFocus, IInputElement? newFocus)
        : base(routedEvent)
    {
        OldFocus = oldFocus;
        NewFocus = newFocus;
    }
}
