namespace Jalium.UI.Controls;

/// <summary>
/// Specifies how a <see cref="DiffViewer"/> displays the differences between two texts.
/// </summary>
public enum DiffViewMode
{
    /// <summary>
    /// Shows original and modified text in side-by-side panels.
    /// </summary>
    SideBySide,

    /// <summary>
    /// Shows original and modified text in a single unified view with +/- indicators.
    /// </summary>
    Unified
}
