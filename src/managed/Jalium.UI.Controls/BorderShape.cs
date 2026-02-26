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
    /// Superellipse (squircle) shape with smooth continuous curvature.
    /// The exponent is controlled by SuperEllipseN (default 4, iOS-style).
    /// </summary>
    SuperEllipse = 1,
}
