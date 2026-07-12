namespace Jalium.UI.Media;

/// <summary>Provides data for media and bitmap failure events.</summary>
public sealed class ExceptionEventArgs : EventArgs
{
    /// <summary>Initializes a new instance of the <see cref="ExceptionEventArgs"/> class.</summary>
    public ExceptionEventArgs(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ErrorException = exception;
    }

    /// <summary>Gets the exception that caused the failure.</summary>
    public Exception ErrorException { get; }
}
