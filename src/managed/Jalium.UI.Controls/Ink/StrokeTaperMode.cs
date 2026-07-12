namespace Jalium.UI.Ink;

/// <summary>
/// Specifies the taper mode for stroke rendering.
/// </summary>
public enum StrokeTaperMode
{
    /// <summary>
    /// No taper - stroke is rendered at constant width.
    /// </summary>
    None,

    /// <summary>
    /// Tapered start - stroke starts thin and grows to full width.
    /// </summary>
    TaperedStart,

    /// <summary>
    /// Tapered end - stroke starts at full width and tapers to thin.
    /// </summary>
    TaperedEnd
}
