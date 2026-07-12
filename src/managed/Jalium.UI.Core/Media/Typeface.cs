namespace Jalium.UI.Media;

/// <summary>
/// Represents a combination of FontFamily, FontWeight, FontStyle, and FontStretch.
/// </summary>
public sealed class Typeface
{
    private readonly FamilyTypeface? _familyTypeface;
    private readonly LanguageSpecificStringDictionary _faceNames;

    /// <summary>
    /// Initializes a new instance of the <see cref="Typeface"/> class from a typeface name string.
    /// </summary>
    /// <param name="typefaceName">The name of the typeface (e.g., "Arial Bold Italic").</param>
    public Typeface(string typefaceName)
        : this(new FontFamily(typefaceName), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal, null)
    {
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
        : this(fontFamily, style, weight, stretch, null)
    {
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
        ArgumentNullException.ThrowIfNull(fontFamily);
        FontFamily = fontFamily;
        Style = style;
        Weight = weight;
        Stretch = stretch;
        FallbackFontFamily = fallbackFontFamily;
        _familyTypeface = fontFamily.FamilyTypefaces.Find(weight, style, stretch);
        _faceNames = CreateFaceNames(_familyTypeface);
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

    /// <summary>Gets the height of capital letters relative to the em size.</summary>
    public double CapsHeight => _familyTypeface?.CapsHeight ?? 0.7;

    /// <summary>Gets the localized face names for this typeface.</summary>
    public LanguageSpecificStringDictionary FaceNames => _faceNames;

    /// <summary>Gets the recommended strikethrough position relative to the em size.</summary>
    public double StrikethroughPosition => _familyTypeface?.StrikethroughPosition ?? 0.3;

    /// <summary>Gets the recommended strikethrough thickness relative to the em size.</summary>
    public double StrikethroughThickness => _familyTypeface?.StrikethroughThickness ?? 0.05;

    /// <summary>Gets the recommended underline position relative to the em size.</summary>
    public double UnderlinePosition => _familyTypeface?.UnderlinePosition ?? -0.1;

    /// <summary>Gets the recommended underline thickness relative to the em size.</summary>
    public double UnderlineThickness => _familyTypeface?.UnderlineThickness ?? 0.05;

    /// <summary>Gets the x-height relative to the em size.</summary>
    public double XHeight => _familyTypeface?.XHeight ?? 0.5;

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

    private static LanguageSpecificStringDictionary CreateFaceNames(FamilyTypeface? familyTypeface)
    {
        var names = new Dictionary<Jalium.UI.Markup.XmlLanguage, string>();
        if (familyTypeface is not null)
        {
            foreach (KeyValuePair<Jalium.UI.Markup.XmlLanguage, string> entry in familyTypeface.AdjustedFaceNames)
            {
                names[entry.Key] = entry.Value;
            }
        }

        if (names.Count == 0)
        {
            names[Jalium.UI.Markup.XmlLanguage.GetLanguage("en-us")] = "Regular";
        }

        return new LanguageSpecificStringDictionary(names);
    }
}
