namespace Jalium.UI;

/// <summary>
/// Provides exception information for a routed failure event.
/// </summary>
public sealed class ExceptionRoutedEventArgs : RoutedEventArgs
{
    internal ExceptionRoutedEventArgs(Exception errorException)
    {
        ArgumentNullException.ThrowIfNull(errorException);
        ErrorException = errorException;
    }

    internal ExceptionRoutedEventArgs(RoutedEvent routedEvent, object source, Exception errorException)
        : base(routedEvent, source)
    {
        ArgumentNullException.ThrowIfNull(errorException);
        ErrorException = errorException;
    }

    /// <summary>Gets the exception that caused the failure.</summary>
    public Exception ErrorException { get; }
}
