namespace Jalium.UI;

/// <summary>
/// Specifies the line-breaking condition adjacent to an inline object.
/// </summary>
public enum LineBreakCondition
{
    /// <summary>Break if the condition on the other side does not prohibit it.</summary>
    BreakDesired = 0,

    /// <summary>Break if the condition on the other side allows it.</summary>
    BreakPossible = 1,

    /// <summary>Do not break unless the condition on the other side always permits a break.</summary>
    BreakRestrained = 2,

    /// <summary>Always permit a break.</summary>
    BreakAlways = 3,
}

/// <summary>
/// Specifies the typographic treatment of fractions.
/// </summary>
public enum FontFraction
{
    /// <summary>Use the font's default fraction treatment.</summary>
    Normal = 0,

    /// <summary>Use slashed fractions.</summary>
    Slashed = 1,

    /// <summary>Use vertically stacked fractions.</summary>
    Stacked = 2,
}

/// <summary>
/// Specifies East Asian glyph-width variants.
/// </summary>
public enum FontEastAsianWidths
{
    /// <summary>Use the font's default East Asian width.</summary>
    Normal = 0,

    /// <summary>Use proportional-width glyphs.</summary>
    Proportional = 1,

    /// <summary>Use full-width glyphs.</summary>
    Full = 2,

    /// <summary>Use half-width glyphs.</summary>
    Half = 3,

    /// <summary>Use third-width glyphs.</summary>
    Third = 4,

    /// <summary>Use quarter-width glyphs.</summary>
    Quarter = 5,
}

/// <summary>
/// Specifies East Asian language-specific glyph forms.
/// </summary>
public enum FontEastAsianLanguage
{
    /// <summary>Use the font's default East Asian language forms.</summary>
    Normal = 0,

    /// <summary>Use JIS 1978 forms.</summary>
    Jis78 = 1,

    /// <summary>Use JIS 1983 forms.</summary>
    Jis83 = 2,

    /// <summary>Use JIS 1990 forms.</summary>
    Jis90 = 3,

    /// <summary>Use JIS 2004 forms.</summary>
    Jis04 = 4,

    /// <summary>Use Hojo Kanji forms.</summary>
    HojoKanji = 5,

    /// <summary>Use National Language Council Kanji forms.</summary>
    NlcKanji = 6,

    /// <summary>Use simplified forms.</summary>
    Simplified = 7,

    /// <summary>Use traditional forms.</summary>
    Traditional = 8,

    /// <summary>Use traditional name forms.</summary>
    TraditionalNames = 9,
}
