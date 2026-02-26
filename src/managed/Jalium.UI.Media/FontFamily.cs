using System.Collections;
using System.Collections.ObjectModel;

namespace Jalium.UI.Media;

/// <summary>
/// Represents a family of related fonts.
/// </summary>
public sealed class FontFamily
{
    private readonly string _source;
    private readonly FamilyTypefaceCollection _typefaces;

    /// <summary>
    /// Initializes a new instance of the <see cref="FontFamily"/> class using the default font family.
    /// </summary>
    public FontFamily() : this("Segoe UI")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FontFamily"/> class from the specified font family name.
    /// </summary>
    /// <param name="familyName">The font family name or names.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="familyName"/> is null.</exception>
    public FontFamily(string familyName)
    {
        ArgumentNullException.ThrowIfNull(familyName);
        _source = familyName;
        _typefaces = new FamilyTypefaceCollection();
    }

    /// <summary>
    /// Gets the string used to construct the <see cref="FontFamily"/> object.
    /// </summary>
    public string Source => _source;

    /// <summary>
    /// Gets the distance from the baseline to the top of the character cell for the font family.
    /// </summary>
    /// <remarks>
    /// The baseline value is expressed as a proportion of the font em size.
    /// A typical value for Western fonts is approximately 0.8.
    /// </remarks>
    public double Baseline => 0.8;

    /// <summary>
    /// Gets the line spacing value for the <see cref="FontFamily"/> object.
    /// </summary>
    /// <remarks>
    /// The line spacing is the recommended baseline-to-baseline distance for the text in this font,
    /// expressed as a proportion of the font em size.
    /// </remarks>
    public double LineSpacing => 1.2;

    /// <summary>
    /// Gets the collection of typefaces that make up this font family.
    /// </summary>
    public FamilyTypefaceCollection FamilyTypefaces => _typefaces;

    /// <summary>
    /// Returns a string representation of the font family.
    /// </summary>
    /// <returns>The font family source name.</returns>
    public override string ToString() => _source;

    /// <summary>
    /// Determines whether the specified object is equal to the current object.
    /// </summary>
    public override bool Equals(object? obj)
    {
        if (obj is FontFamily other)
        {
            return string.Equals(_source, other._source, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    /// <summary>
    /// Returns the hash code for this font family.
    /// </summary>
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(_source);

    /// <summary>
    /// Implicit conversion from string to FontFamily.
    /// </summary>
    public static implicit operator FontFamily(string familyName) => new(familyName);

    /// <summary>
    /// Implicit conversion from FontFamily to string.
    /// </summary>
    public static implicit operator string(FontFamily family) => family?._source ?? string.Empty;
}

/// <summary>
/// Represents a typeface supported by a <see cref="FontFamily"/>.
/// </summary>
public sealed class FamilyTypeface
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FamilyTypeface"/> class.
    /// </summary>
    public FamilyTypeface()
    {
        Weight = FontWeights.Normal;
        Style = FontStyles.Normal;
        Stretch = FontStretches.Normal;
    }

    /// <summary>
    /// Gets or sets the font weight for the typeface.
    /// </summary>
    public FontWeight Weight { get; set; }

    /// <summary>
    /// Gets or sets the font style for the typeface.
    /// </summary>
    public FontStyle Style { get; set; }

    /// <summary>
    /// Gets or sets the font stretch for the typeface.
    /// </summary>
    public FontStretch Stretch { get; set; }

    /// <summary>
    /// Gets or sets the adjusted face names for the typeface.
    /// </summary>
    public IDictionary<string, string>? AdjustedFaceNames { get; set; }

    /// <summary>
    /// Gets or sets the device font name.
    /// </summary>
    public string? DeviceFontName { get; set; }

    /// <summary>
    /// Returns a string representation of this typeface.
    /// </summary>
    public override string ToString() => $"{Weight} {Style} {Stretch}";
}

/// <summary>
/// Represents a collection of <see cref="FamilyTypeface"/> objects.
/// </summary>
public sealed class FamilyTypefaceCollection : Collection<FamilyTypeface>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FamilyTypefaceCollection"/> class.
    /// </summary>
    public FamilyTypefaceCollection()
    {
    }

    /// <summary>
    /// Finds a typeface matching the specified weight, style, and stretch.
    /// </summary>
    /// <param name="weight">The desired font weight.</param>
    /// <param name="style">The desired font style.</param>
    /// <param name="stretch">The desired font stretch.</param>
    /// <returns>The matching typeface, or null if not found.</returns>
    public FamilyTypeface? Find(FontWeight weight, FontStyle style, FontStretch stretch)
    {
        foreach (var typeface in this)
        {
            if (typeface.Weight == weight && typeface.Style == style && typeface.Stretch == stretch)
            {
                return typeface;
            }
        }
        return null;
    }
}
