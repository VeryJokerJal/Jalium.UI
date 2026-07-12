namespace Jalium.UI.Media;

/// <summary>Specifies the formatting method used for text metrics.</summary>
public enum TextFormattingMode
{
    /// <summary>Uses resolution-independent ideal glyph metrics.</summary>
    Ideal = 0,

    /// <summary>Uses glyph metrics snapped for display rendering.</summary>
    Display = 1,
}

/// <summary>Specifies the anti-aliasing mode used to render text.</summary>
public enum TextRenderingMode
{
    /// <summary>Selects the most appropriate rendering mode automatically.</summary>
    Auto = 0,

    /// <summary>Uses bilevel rendering.</summary>
    Aliased = 1,

    /// <summary>Uses grayscale anti-aliasing.</summary>
    Grayscale = 2,

    /// <summary>Uses ClearType subpixel rendering.</summary>
    ClearType = 3,
}

/// <summary>Specifies how glyph hinting behaves while text is animated.</summary>
public enum TextHintingMode
{
    /// <summary>Selects the hinting mode automatically.</summary>
    Auto = 0,

    /// <summary>Uses fixed hinting values.</summary>
    Fixed = 1,

    /// <summary>Uses animation-friendly hinting values.</summary>
    Animated = 2,
}
