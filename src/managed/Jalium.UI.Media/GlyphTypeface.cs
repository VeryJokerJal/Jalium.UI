namespace Jalium.UI.Media;

/// <summary>
/// Specifies a physical font face that corresponds to a font file on the disk.
/// </summary>
public sealed class GlyphTypeface
{
    /// <summary>
    /// Initializes a new instance of the GlyphTypeface class.
    /// </summary>
    public GlyphTypeface()
    {
    }

    /// <summary>
    /// Initializes a new instance of the GlyphTypeface class with the specified font URI.
    /// </summary>
    public GlyphTypeface(Uri typefaceSource)
    {
        FontUri = typefaceSource;
    }

    /// <summary>
    /// Gets or sets the URI for the font file.
    /// </summary>
    public Uri? FontUri { get; set; }

    /// <summary>
    /// Gets the font face name.
    /// </summary>
    public string FaceName { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets the font family name.
    /// </summary>
    public string FamilyName { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets the font weight.
    /// </summary>
    public FontWeight Weight { get; internal set; } = FontWeights.Normal;

    /// <summary>
    /// Gets the font style.
    /// </summary>
    public FontStyle Style { get; internal set; } = FontStyles.Normal;

    /// <summary>
    /// Gets the font stretch.
    /// </summary>
    public FontStretch Stretch { get; internal set; } = FontStretches.Normal;

    /// <summary>
    /// Gets the em size of the font in design units.
    /// </summary>
    public double Height { get; internal set; }

    /// <summary>
    /// Gets the baseline value for the font.
    /// </summary>
    public double Baseline { get; internal set; }

    /// <summary>
    /// Gets the distance from the cell top to the English capital letter top.
    /// </summary>
    public double CapsHeight { get; internal set; }

    /// <summary>
    /// Gets the distance from the baseline to the top of an English lowercase letter.
    /// </summary>
    public double XHeight { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the font is a symbol font.
    /// </summary>
    public bool Symbol { get; internal set; }

    /// <summary>
    /// Gets the width of the specified character.
    /// </summary>
    public double GetAdvanceWidth(int glyphIndex) => 0.0;

    /// <summary>
    /// Gets the glyph index for the specified Unicode code point.
    /// </summary>
    public bool TryGetGlyphIndex(int unicodeValue, out int glyphIndex)
    {
        glyphIndex = 0;
        return false;
    }
}
