namespace Jalium.UI;

/// <summary>Specifies how text is trimmed when it exceeds its layout box.</summary>
public enum TextTrimming
{
    None,
    CharacterEllipsis,
    WordEllipsis,
}

/// <summary>Specifies an inline element's vertical position relative to the text baseline.</summary>
public enum BaselineAlignment
{
    Top,
    Center,
    Bottom,
    Baseline,
    TextTop,
    TextBottom,
    Subscript,
    Superscript,
}

/// <summary>Specifies numeral glyph forms.</summary>
public enum FontNumeralStyle
{
    Normal,
    Lining,
    OldStyle,
}

/// <summary>Specifies proportional or tabular numeral spacing.</summary>
public enum FontNumeralAlignment
{
    Normal,
    Proportional,
    Tabular,
}

/// <summary>Specifies typographic variants such as superscript and ordinal forms.</summary>
public enum FontVariants
{
    Normal,
    Superscript,
    Subscript,
    Ordinal,
    Inferior,
    Ruby,
}

/// <summary>Specifies capitalization glyph variants.</summary>
public enum FontCapitals
{
    Normal,
    AllSmallCaps,
    SmallCaps,
    AllPetiteCaps,
    PetiteCaps,
    Unicase,
    Titling,
}
