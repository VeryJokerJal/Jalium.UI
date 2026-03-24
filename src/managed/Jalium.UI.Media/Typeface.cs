namespace Jalium.UI.Media;

/// <summary>
/// Represents a combination of FontFamily, FontWeight, FontStyle, and FontStretch.
/// </summary>
public sealed class Typeface
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Typeface"/> class from a typeface name string.
    /// </summary>
    /// <param name="typefaceName">The name of the typeface (e.g., "Arial Bold Italic").</param>
    public Typeface(string typefaceName)
    {
        FontFamily = new FontFamily(typefaceName);
        Style = FontStyles.Normal;
        Weight = FontWeights.Normal;
        Stretch = FontStretches.Normal;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Typeface"/> class with normal stretch.
    /// </summary>
    /// <param name="fontFamily">The font family.</param>
    /// <param name="weight">The font weight.</param>
    /// <param name="style">The font style.</param>
    public Typeface(FontFamily fontFamily, FontWeight weight, FontStyle style)
        : this(fontFamily, style, weight, FontStretches.Normal)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Typeface"/> class.
    /// </summary>
    /// <param name="fontFamily">The font family.</param>
    /// <param name="style">The font style.</param>
    /// <param name="weight">The font weight.</param>
    /// <param name="stretch">The font stretch.</param>
    public Typeface(FontFamily fontFamily, FontStyle style, FontWeight weight, FontStretch stretch)
    {
        FontFamily = fontFamily;
        Style = style;
        Weight = weight;
        Stretch = stretch;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Typeface"/> class with a fallback font family.
    /// </summary>
    /// <param name="fontFamily">The font family.</param>
    /// <param name="style">The font style.</param>
    /// <param name="weight">The font weight.</param>
    /// <param name="stretch">The font stretch.</param>
    /// <param name="fallbackFontFamily">The fallback font family.</param>
    public Typeface(FontFamily fontFamily, FontStyle style, FontWeight weight, FontStretch stretch, FontFamily? fallbackFontFamily)
    {
        FontFamily = fontFamily;
        Style = style;
        Weight = weight;
        Stretch = stretch;
        FallbackFontFamily = fallbackFontFamily;
    }

    /// <summary>
    /// Gets the font family for this typeface.
    /// </summary>
    public FontFamily FontFamily { get; }

    /// <summary>
    /// Gets the style of the typeface.
    /// </summary>
    public FontStyle Style { get; }

    /// <summary>
    /// Gets the weight of the typeface.
    /// </summary>
    public FontWeight Weight { get; }

    /// <summary>
    /// Gets the stretch of the typeface.
    /// </summary>
    public FontStretch Stretch { get; }

    /// <summary>
    /// Gets the fallback font family for this typeface.
    /// </summary>
    public FontFamily? FallbackFontFamily { get; }

    /// <summary>
    /// Gets a value indicating whether the oblique style is simulated.
    /// </summary>
    public bool IsObliqueSimulated => false;

    /// <summary>
    /// Gets a value indicating whether the bold style is simulated.
    /// </summary>
    public bool IsBoldSimulated => false;

    /// <summary>
    /// Attempts to get the GlyphTypeface for this typeface.
    /// </summary>
    /// <param name="glyphTypeface">The resulting GlyphTypeface.</param>
    /// <returns>True if a GlyphTypeface was found; otherwise, false.</returns>
    public bool TryGetGlyphTypeface(out GlyphTypeface? glyphTypeface)
    {
        try
        {
            glyphTypeface = new GlyphTypeface(new Uri(FontFamily.Source, UriKind.RelativeOrAbsolute));
            return true;
        }
        catch
        {
            glyphTypeface = null;
            return false;
        }
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        if (obj is not Typeface other)
            return false;

        return FontFamily.Source == other.FontFamily.Source
            && Style == other.Style
            && Weight == other.Weight
            && Stretch == other.Stretch;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(FontFamily.Source, Style, Weight, Stretch);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{FontFamily.Source} {Weight} {Style} {Stretch}";
    }
}
