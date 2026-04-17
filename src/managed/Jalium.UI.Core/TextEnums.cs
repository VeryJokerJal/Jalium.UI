namespace Jalium.UI;

/// <summary>
/// Specifies whether text in the object is left-aligned, right-aligned, centered, or justified.
/// </summary>
public enum TextAlignment
{
    /// <summary>
    /// In horizontal inline progression, the text is aligned on the left.
    /// </summary>
    Left,

    /// <summary>
    /// In horizontal inline progression, the text is aligned on the right.
    /// </summary>
    Right,

    /// <summary>
    /// The text is center aligned.
    /// </summary>
    Center,

    /// <summary>
    /// The text is justified.
    /// </summary>
    Justify
}

/// <summary>
/// Specifies whether text wraps when it reaches the edge of the containing box.
/// </summary>
public enum TextWrapping
{
    /// <summary>
    /// Line-breaking occurs if the line overflows the available block width.
    /// However, a line may overflow the block width if the line breaking algorithm
    /// cannot determine a break opportunity.
    /// </summary>
    WrapWithOverflow,

    /// <summary>
    /// No line wrapping is performed.
    /// </summary>
    NoWrap,

    /// <summary>
    /// Line-breaking occurs if the line overflows the available block width,
    /// even if the standard line breaking algorithm cannot determine any break opportunity.
    /// </summary>
    Wrap
}
