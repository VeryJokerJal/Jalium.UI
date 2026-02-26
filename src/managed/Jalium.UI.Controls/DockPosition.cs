namespace Jalium.UI.Controls;

/// <summary>
/// Specifies the dock position when dropping a panel onto a dock target.
/// </summary>
public enum DockPosition
{
    /// <summary>No dock position (not over any indicator).</summary>
    None,

    /// <summary>Add as a new tab in the target panel.</summary>
    Center,

    /// <summary>Split and dock to the left of the target panel.</summary>
    Left,

    /// <summary>Split and dock to the right of the target panel.</summary>
    Right,

    /// <summary>Split and dock above the target panel.</summary>
    Top,

    /// <summary>Split and dock below the target panel.</summary>
    Bottom,

    /// <summary>Dock to the left edge of the root layout.</summary>
    EdgeLeft,

    /// <summary>Dock to the right edge of the root layout.</summary>
    EdgeRight,

    /// <summary>Dock to the top edge of the root layout.</summary>
    EdgeTop,

    /// <summary>Dock to the bottom edge of the root layout.</summary>
    EdgeBottom,
}
