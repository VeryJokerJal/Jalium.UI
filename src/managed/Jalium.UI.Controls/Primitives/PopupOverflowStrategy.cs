namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Specifies how a Popup handles content that would overflow the window bounds.
/// </summary>
public enum PopupOverflowStrategy
{
    /// <summary>
    /// Content is constrained to the window bounds and clipped if it overflows.
    /// </summary>
    ConstrainToWindow,

    /// <summary>
    /// When content would overflow, automatically flip the placement direction.
    /// For example, Bottom flips to Top if there is insufficient space below.
    /// Content remains within window bounds.
    /// </summary>
    AutoFlip
}
