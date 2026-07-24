namespace Jalium.UI.Controls;

/// <summary>
/// Defines the shape of a Border element.
/// </summary>
public enum BorderShape
{
    /// <summary>
    /// Standard rectangle with circular arc corners (controlled by CornerRadius).
    /// </summary>
    RoundedRectangle = 0,

    /// <summary>
    /// Continuous-corner rectangle whose four local corner patches use the
    /// superellipse exponent. CornerRadius bounds each patch; the sides between
    /// patches remain straight. SuperEllipseN defaults to 4 (iOS-style).
    /// </summary>
    SuperEllipse = 1,
}
