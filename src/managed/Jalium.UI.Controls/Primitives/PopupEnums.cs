namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Describes how a <see cref="Popup"/> control animates when it opens.
/// </summary>
public enum PopupAnimation
{
    /// <summary>The Popup appears without animation.</summary>
    None,

    /// <summary>The Popup fades in.</summary>
    Fade,

    /// <summary>The Popup slides into view.</summary>
    Slide,

    /// <summary>The Popup scrolls into view.</summary>
    Scroll
}

/// <summary>
/// Describes the primary axis used to position a popup relative to its target.
/// </summary>
public enum PopupPrimaryAxis
{
    /// <summary>No primary axis.</summary>
    None,

    /// <summary>Horizontal axis.</summary>
    Horizontal,

    /// <summary>Vertical axis.</summary>
    Vertical
}

/// <summary>
/// Represents a point and primary axis for custom popup placement.
/// </summary>
public struct CustomPopupPlacement
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CustomPopupPlacement"/> structure.
    /// </summary>
    public CustomPopupPlacement(Point point, PopupPrimaryAxis primaryAxis)
    {
        Point = point;
        PrimaryAxis = primaryAxis;
    }

    /// <summary>Gets or sets the point relative to the target object where the popup is placed.</summary>
    public Point Point { get; set; }

    /// <summary>Gets or sets the primary axis along which the popup adjusts when obstructed.</summary>
    public PopupPrimaryAxis PrimaryAxis { get; set; }
}

/// <summary>
/// Represents the method that provides custom popup placement.
/// </summary>
public delegate CustomPopupPlacement[] CustomPopupPlacementCallback(Size popupSize, Size targetSize, Point offset);
