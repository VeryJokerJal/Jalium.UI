namespace Jalium.UI;

/// <summary>
/// Specifies which edges of an element participate in bounds clipping.
/// </summary>
[Flags]
public enum ClipEdges
{
    /// <summary>
    /// No edge clips content.
    /// </summary>
    None = 0,

    /// <summary>
    /// Content is clipped at the left edge.
    /// </summary>
    Left = 1 << 0,

    /// <summary>
    /// Content is clipped at the top edge.
    /// </summary>
    Top = 1 << 1,

    /// <summary>
    /// Content is clipped at the right edge.
    /// </summary>
    Right = 1 << 2,

    /// <summary>
    /// Content is clipped at the bottom edge.
    /// </summary>
    Bottom = 1 << 3,

    /// <summary>
    /// Content is clipped at every edge.
    /// </summary>
    All = Left | Top | Right | Bottom
}
