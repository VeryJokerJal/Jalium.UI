using System.Globalization;

namespace Jalium.UI.Media;

/// <summary>
/// Refers to the density of a typeface, in terms of the lightness or heaviness of the strokes.
/// </summary>
public readonly struct FontWeight : IEquatable<FontWeight>, IFormattable
{
    private readonly int _weight;

    /// <summary>
    /// Initializes a new instance of <see cref="FontWeight"/> with the specified weight value.
    /// </summary>
    /// <param name="weight">The OpenType weight value (1-999).</param>
    internal FontWeight(int weight)
    {
        _weight = Math.Clamp(weight, 1, 999);
    }

    /// <summary>
    /// Creates a new instance of <see cref="FontWeight"/> that corresponds to the OpenType usWeightClass value.
    /// </summary>
    /// <param name="weightValue">An integer value between 1 and 999 that corresponds to the usWeightClass definition in the OpenType specification.</param>
    /// <returns>A new instance of <see cref="FontWeight"/>.</returns>
    public static FontWeight FromOpenTypeWeight(int weightValue)
    {
        return new FontWeight(weightValue);
    }

    /// <summary>
    /// Returns a value that represents the OpenType usWeightClass for this <see cref="FontWeight"/> object.
    /// </summary>
    /// <returns>An integer value between 1 and 999 that corresponds to the usWeightClass definition in the OpenType specification.</returns>
    public int ToOpenTypeWeight() => _weight;

    /// <summary>
    /// Compares two instances of <see cref="FontWeight"/>.
    /// </summary>
    /// <param name="left">The first <see cref="FontWeight"/> object to compare.</param>
    /// <param name="right">The second <see cref="FontWeight"/> object to compare.</param>
    /// <returns>An <see cref="int"/> value that indicates the relationship between the two instances.</returns>
    public static int Compare(FontWeight left, FontWeight right)
    {
        return left._weight - right._weight;
    }

    /// <inheritdoc />
    public bool Equals(FontWeight other) => _weight == other._weight;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is FontWeight other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _weight.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => ToString(null, null);

    /// <inheritdoc />
    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        return _weight switch
        {
            100 => "Thin",
            200 => "ExtraLight",
            300 => "Light",
            400 => "Normal",
            500 => "Medium",
            600 => "SemiBold",
            700 => "Bold",
            800 => "ExtraBold",
            900 => "Black",
            _ => _weight.ToString(formatProvider)
        };
    }

    /// <summary>Compares two instances of <see cref="FontWeight"/> for equality.</summary>
    public static bool operator ==(FontWeight left, FontWeight right) => left._weight == right._weight;

    /// <summary>Evaluates two instances of <see cref="FontWeight"/> to determine inequality.</summary>
    public static bool operator !=(FontWeight left, FontWeight right) => left._weight != right._weight;

    /// <summary>Evaluates two instances of <see cref="FontWeight"/> to determine whether one is less than the other.</summary>
    public static bool operator <(FontWeight left, FontWeight right) => left._weight < right._weight;

    /// <summary>Evaluates two instances of <see cref="FontWeight"/> to determine whether one is greater than the other.</summary>
    public static bool operator >(FontWeight left, FontWeight right) => left._weight > right._weight;

    /// <summary>Evaluates two instances of <see cref="FontWeight"/> to determine whether one is less than or equal to the other.</summary>
    public static bool operator <=(FontWeight left, FontWeight right) => left._weight <= right._weight;

    /// <summary>Evaluates two instances of <see cref="FontWeight"/> to determine whether one is greater than or equal to the other.</summary>
    public static bool operator >=(FontWeight left, FontWeight right) => left._weight >= right._weight;
}

/// <summary>
/// Provides a set of predefined <see cref="FontWeight"/> values.
/// </summary>
public static class FontWeights
{
    /// <summary>Specifies a "Thin" font weight (100).</summary>
    public static FontWeight Thin => new(100);

    /// <summary>Specifies an "ExtraLight" (UltraLight) font weight (200).</summary>
    public static FontWeight ExtraLight => new(200);

    /// <summary>Specifies an "UltraLight" font weight (200).</summary>
    public static FontWeight UltraLight => new(200);

    /// <summary>Specifies a "Light" font weight (300).</summary>
    public static FontWeight Light => new(300);

    /// <summary>Specifies a "Normal" (Regular) font weight (400).</summary>
    public static FontWeight Normal => new(400);

    /// <summary>Specifies a "Regular" font weight (400).</summary>
    public static FontWeight Regular => new(400);

    /// <summary>Specifies a "Medium" font weight (500).</summary>
    public static FontWeight Medium => new(500);

    /// <summary>Specifies a "DemiBold" (SemiBold) font weight (600).</summary>
    public static FontWeight DemiBold => new(600);

    /// <summary>Specifies a "SemiBold" font weight (600).</summary>
    public static FontWeight SemiBold => new(600);

    /// <summary>Specifies a "Bold" font weight (700).</summary>
    public static FontWeight Bold => new(700);

    /// <summary>Specifies an "ExtraBold" (UltraBold) font weight (800).</summary>
    public static FontWeight ExtraBold => new(800);

    /// <summary>Specifies an "UltraBold" font weight (800).</summary>
    public static FontWeight UltraBold => new(800);

    /// <summary>Specifies a "Black" (Heavy) font weight (900).</summary>
    public static FontWeight Black => new(900);

    /// <summary>Specifies a "Heavy" font weight (900).</summary>
    public static FontWeight Heavy => new(900);
}

/// <summary>
/// Defines the style of a font face as normal, italic, or oblique.
/// </summary>
public readonly struct FontStyle : IEquatable<FontStyle>, IFormattable
{
    private readonly int _style;

    /// <summary>
    /// Initializes a new instance of <see cref="FontStyle"/> with the specified style value.
    /// </summary>
    /// <param name="style">The style value (0 = Normal, 1 = Oblique, 2 = Italic).</param>
    internal FontStyle(int style)
    {
        _style = Math.Clamp(style, 0, 2);
    }

    /// <summary>
    /// Returns the OpenType font style value.
    /// </summary>
    /// <returns>An integer value (0 = Normal, 1 = Oblique, 2 = Italic).</returns>
    public int ToOpenTypeStyle() => _style;

    /// <summary>
    /// Creates a new instance of <see cref="FontStyle"/> from an OpenType style value.
    /// </summary>
    /// <param name="styleValue">The OpenType style value.</param>
    /// <returns>A new instance of <see cref="FontStyle"/>.</returns>
    public static FontStyle FromOpenTypeStyle(int styleValue)
    {
        return new FontStyle(styleValue);
    }

    /// <summary>
    /// Compares two instances of <see cref="FontStyle"/>.
    /// </summary>
    public static int Compare(FontStyle left, FontStyle right)
    {
        return left._style - right._style;
    }

    /// <inheritdoc />
    public bool Equals(FontStyle other) => _style == other._style;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is FontStyle other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _style.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => ToString(null, null);

    /// <inheritdoc />
    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        return _style switch
        {
            0 => "Normal",
            1 => "Oblique",
            2 => "Italic",
            _ => _style.ToString(formatProvider)
        };
    }

    /// <summary>Compares two instances of <see cref="FontStyle"/> for equality.</summary>
    public static bool operator ==(FontStyle left, FontStyle right) => left._style == right._style;

    /// <summary>Evaluates two instances of <see cref="FontStyle"/> to determine inequality.</summary>
    public static bool operator !=(FontStyle left, FontStyle right) => left._style != right._style;
}

/// <summary>
/// Provides a set of predefined <see cref="FontStyle"/> values.
/// </summary>
public static class FontStyles
{
    /// <summary>Specifies a normal (upright) font style.</summary>
    public static FontStyle Normal => new(0);

    /// <summary>Specifies an oblique font style (slanted version of normal).</summary>
    public static FontStyle Oblique => new(1);

    /// <summary>Specifies an italic font style.</summary>
    public static FontStyle Italic => new(2);
}

/// <summary>
/// Describes the degree to which a font has been stretched compared to the normal aspect ratio of that font.
/// </summary>
public readonly struct FontStretch : IEquatable<FontStretch>, IFormattable
{
    private readonly int _stretch;

    /// <summary>
    /// Initializes a new instance of <see cref="FontStretch"/> with the specified stretch value.
    /// </summary>
    /// <param name="stretch">The OpenType stretch value (1-9).</param>
    internal FontStretch(int stretch)
    {
        _stretch = Math.Clamp(stretch, 1, 9);
    }

    /// <summary>
    /// Creates a new instance of <see cref="FontStretch"/> that corresponds to the OpenType usStretchClass value.
    /// </summary>
    /// <param name="stretchValue">An integer value between 1 and 9 that corresponds to the usStretchClass definition in the OpenType specification.</param>
    /// <returns>A new instance of <see cref="FontStretch"/>.</returns>
    public static FontStretch FromOpenTypeStretch(int stretchValue)
    {
        return new FontStretch(stretchValue);
    }

    /// <summary>
    /// Returns a value that represents the OpenType usStretchClass for this <see cref="FontStretch"/> object.
    /// </summary>
    /// <returns>An integer value between 1 and 9 that corresponds to the usStretchClass definition in the OpenType specification.</returns>
    public int ToOpenTypeStretch() => _stretch;

    /// <summary>
    /// Compares two instances of <see cref="FontStretch"/>.
    /// </summary>
    /// <param name="left">The first <see cref="FontStretch"/> object to compare.</param>
    /// <param name="right">The second <see cref="FontStretch"/> object to compare.</param>
    /// <returns>An <see cref="int"/> value that indicates the relationship between the two instances.</returns>
    public static int Compare(FontStretch left, FontStretch right)
    {
        return left._stretch - right._stretch;
    }

    /// <inheritdoc />
    public bool Equals(FontStretch other) => _stretch == other._stretch;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is FontStretch other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _stretch.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => ToString(null, null);

    /// <inheritdoc />
    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        return _stretch switch
        {
            1 => "UltraCondensed",
            2 => "ExtraCondensed",
            3 => "Condensed",
            4 => "SemiCondensed",
            5 => "Normal",
            6 => "SemiExpanded",
            7 => "Expanded",
            8 => "ExtraExpanded",
            9 => "UltraExpanded",
            _ => _stretch.ToString(formatProvider)
        };
    }

    /// <summary>Compares two instances of <see cref="FontStretch"/> for equality.</summary>
    public static bool operator ==(FontStretch left, FontStretch right) => left._stretch == right._stretch;

    /// <summary>Evaluates two instances of <see cref="FontStretch"/> to determine inequality.</summary>
    public static bool operator !=(FontStretch left, FontStretch right) => left._stretch != right._stretch;

    /// <summary>Evaluates two instances of <see cref="FontStretch"/> to determine whether one is less than the other.</summary>
    public static bool operator <(FontStretch left, FontStretch right) => left._stretch < right._stretch;

    /// <summary>Evaluates two instances of <see cref="FontStretch"/> to determine whether one is greater than the other.</summary>
    public static bool operator >(FontStretch left, FontStretch right) => left._stretch > right._stretch;

    /// <summary>Evaluates two instances of <see cref="FontStretch"/> to determine whether one is less than or equal to the other.</summary>
    public static bool operator <=(FontStretch left, FontStretch right) => left._stretch <= right._stretch;

    /// <summary>Evaluates two instances of <see cref="FontStretch"/> to determine whether one is greater than or equal to the other.</summary>
    public static bool operator >=(FontStretch left, FontStretch right) => left._stretch >= right._stretch;
}

/// <summary>
/// Provides a set of predefined <see cref="FontStretch"/> values.
/// </summary>
public static class FontStretches
{
    /// <summary>Specifies an ultra-condensed font stretch (50% of normal).</summary>
    public static FontStretch UltraCondensed => new(1);

    /// <summary>Specifies an extra-condensed font stretch (62.5% of normal).</summary>
    public static FontStretch ExtraCondensed => new(2);

    /// <summary>Specifies a condensed font stretch (75% of normal).</summary>
    public static FontStretch Condensed => new(3);

    /// <summary>Specifies a semi-condensed font stretch (87.5% of normal).</summary>
    public static FontStretch SemiCondensed => new(4);

    /// <summary>Specifies a normal (medium) font stretch (100%).</summary>
    public static FontStretch Normal => new(5);

    /// <summary>Specifies a medium font stretch (100%).</summary>
    public static FontStretch Medium => new(5);

    /// <summary>Specifies a semi-expanded font stretch (112.5% of normal).</summary>
    public static FontStretch SemiExpanded => new(6);

    /// <summary>Specifies an expanded font stretch (125% of normal).</summary>
    public static FontStretch Expanded => new(7);

    /// <summary>Specifies an extra-expanded font stretch (150% of normal).</summary>
    public static FontStretch ExtraExpanded => new(8);

    /// <summary>Specifies an ultra-expanded font stretch (200% of normal).</summary>
    public static FontStretch UltraExpanded => new(9);
}
