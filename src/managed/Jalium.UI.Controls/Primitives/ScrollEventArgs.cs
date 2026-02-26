namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Specifies the type of scroll action that occurred.
/// </summary>
public enum ScrollEventType
{
    /// <summary>
    /// The Thumb was dragged to a new position and is now being held.
    /// </summary>
    EndScroll,

    /// <summary>
    /// The Thumb moved to the Minimum position of the ScrollBar.
    /// </summary>
    First,

    /// <summary>
    /// The Thumb moved a large distance. The user clicked the scroll bar on the side of the Thumb.
    /// </summary>
    LargeDecrement,

    /// <summary>
    /// The Thumb moved a large distance. The user clicked the scroll bar on the side of the Thumb.
    /// </summary>
    LargeIncrement,

    /// <summary>
    /// The Thumb moved to the Maximum position of the ScrollBar.
    /// </summary>
    Last,

    /// <summary>
    /// The Thumb moved a small distance. The user clicked the arrow at the start of the ScrollBar.
    /// </summary>
    SmallDecrement,

    /// <summary>
    /// The Thumb moved a small distance. The user clicked the arrow at the end of the ScrollBar.
    /// </summary>
    SmallIncrement,

    /// <summary>
    /// The Thumb was moved to a new position because the user selected Scroll Here in the shortcut menu.
    /// </summary>
    ThumbPosition,

    /// <summary>
    /// The Thumb was dragged and caused a RoutedEvent.
    /// </summary>
    ThumbTrack
}

/// <summary>
/// Provides data for a Scroll event that occurs when the Thumb of a ScrollBar moves.
/// </summary>
public sealed class ScrollEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Gets the new value of the ScrollBar after the scroll action.
    /// </summary>
    public double NewValue { get; }

    /// <summary>
    /// Gets the type of scroll action that occurred.
    /// </summary>
    public ScrollEventType ScrollEventType { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ScrollEventArgs"/> class.
    /// </summary>
    /// <param name="scrollEventType">The type of scroll action.</param>
    /// <param name="newValue">The new value of the ScrollBar.</param>
    public ScrollEventArgs(ScrollEventType scrollEventType, double newValue)
    {
        ScrollEventType = scrollEventType;
        NewValue = newValue;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ScrollEventArgs"/> class with a routed event.
    /// </summary>
    /// <param name="routedEvent">The routed event identifier.</param>
    /// <param name="scrollEventType">The type of scroll action.</param>
    /// <param name="newValue">The new value of the ScrollBar.</param>
    public ScrollEventArgs(RoutedEvent routedEvent, ScrollEventType scrollEventType, double newValue)
    {
        RoutedEvent = routedEvent;
        ScrollEventType = scrollEventType;
        NewValue = newValue;
    }
}

/// <summary>
/// Represents the method that will handle the Scroll event of a ScrollBar.
/// </summary>
/// <param name="sender">The source of the event.</param>
/// <param name="e">The event data.</param>
public delegate void ScrollEventHandler(object sender, ScrollEventArgs e);
