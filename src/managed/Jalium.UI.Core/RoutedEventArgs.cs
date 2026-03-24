namespace Jalium.UI;

/// <summary>
/// Base class for routed event arguments.
/// </summary>
public class RoutedEventArgs : EventArgs
{
    /// <summary>
    /// Gets or sets the routed event associated with this instance.
    /// </summary>
    public RoutedEvent? RoutedEvent { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this event has been handled.
    /// </summary>
    public bool Handled { get; set; }

    /// <summary>
    /// Gets or sets the source element that raised the event.
    /// </summary>
    public object? Source { get; set; }

    /// <summary>
    /// Gets the original source element that first raised the event.
    /// </summary>
    public object? OriginalSource { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="RoutedEventArgs"/> class.
    /// </summary>
    public RoutedEventArgs()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RoutedEventArgs"/> class.
    /// </summary>
    /// <param name="routedEvent">The routed event.</param>
    public RoutedEventArgs(RoutedEvent routedEvent)
    {
        RoutedEvent = routedEvent;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RoutedEventArgs"/> class.
    /// </summary>
    /// <param name="routedEvent">The routed event.</param>
    /// <param name="source">The source element.</param>
    public RoutedEventArgs(RoutedEvent routedEvent, object? source)
    {
        RoutedEvent = routedEvent;
        Source = source;
        OriginalSource = source;
    }

    /// <summary>
    /// Sets the original source of the event. Can only be set once.
    /// </summary>
    internal void SetOriginalSource(object? originalSource)
    {
        OriginalSource ??= originalSource;
    }

    /// <summary>
    /// Called before the event is raised. Override to perform validation or preparation.
    /// </summary>
    protected virtual void OnSetSource(object? source)
    {
    }

    /// <summary>
    /// Invokes the event handler with the appropriate arguments.
    /// </summary>
    internal virtual void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is RoutedEventHandler routedHandler)
        {
            routedHandler(target, this);
        }
        else
        {
            handler.DynamicInvoke(target, this);
        }
    }
}

/// <summary>
/// Delegate for handling routed events.
/// </summary>
/// <param name="sender">The sender of the event.</param>
/// <param name="e">The event arguments.</param>
public delegate void RoutedEventHandler(object sender, RoutedEventArgs e);
