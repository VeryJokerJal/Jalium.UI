namespace Jalium.UI.Media;

/// <summary>
/// Provides data for a script command reported by a media source.
/// </summary>
public sealed class MediaScriptCommandEventArgs : EventArgs
{
    internal MediaScriptCommandEventArgs(string parameterType, string parameterValue)
    {
        ParameterType = parameterType;
        ParameterValue = parameterValue;
    }

    /// <summary>Gets the command parameter type.</summary>
    public string ParameterType { get; }

    /// <summary>Gets the command parameter value.</summary>
    public string ParameterValue { get; }
}
