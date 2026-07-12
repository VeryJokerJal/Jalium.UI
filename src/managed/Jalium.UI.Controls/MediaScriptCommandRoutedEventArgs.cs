namespace Jalium.UI;

/// <summary>
/// Provides data for the MediaElement.ScriptCommand event.
/// </summary>
public sealed class MediaScriptCommandRoutedEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MediaScriptCommandRoutedEventArgs"/> class.
    /// </summary>
    internal MediaScriptCommandRoutedEventArgs(
        RoutedEvent routedEvent,
        object source,
        string parameterType,
        string parameterValue)
        : base(routedEvent, source)
    {
        ParameterType = parameterType;
        ParameterValue = parameterValue;
    }

    /// <summary>
    /// Gets the type of the script command.
    /// </summary>
    public string ParameterType { get; }

    /// <summary>
    /// Gets the value of the script command.
    /// </summary>
    public string ParameterValue { get; }
}
