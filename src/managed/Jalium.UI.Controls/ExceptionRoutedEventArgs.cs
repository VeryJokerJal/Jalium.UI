namespace Jalium.UI.Controls;

/// <summary>
/// Provides data about an exception that was raised as a routed event.
/// </summary>
public sealed class ExceptionRoutedEventArgs : RoutedEventArgs
{
    internal ExceptionRoutedEventArgs(Exception errorException)
    {
        ErrorException = errorException;
    }

    /// <summary>Gets the exception that caused the error.</summary>
    public Exception ErrorException { get; }
}
