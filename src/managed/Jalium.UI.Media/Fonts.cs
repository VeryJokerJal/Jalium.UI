using System.Collections.ObjectModel;

namespace Jalium.UI.Media;

/// <summary>
/// Provides enumeration support for FontFamily and Typeface objects.
/// </summary>
public static class Fonts
{
    /// <summary>
    /// Returns the collection of FontFamily objects from the default system font location.
    /// </summary>
    public static ICollection<FontFamily> SystemFontFamilies { get; } = GetSystemFontFamilies();

    /// <summary>
    /// Returns the collection of Typeface objects from the default system font location.
    /// </summary>
    public static ICollection<Typeface> SystemTypefaces { get; } = GetSystemTypefaces();

    /// <summary>
    /// Returns the collection of FontFamily objects from a specified font location.
    /// </summary>
    public static ICollection<FontFamily> GetFontFamilies(string location)
    {
        return new ReadOnlyCollection<FontFamily>(new[] { new FontFamily(location) });
    }

    /// <summary>
    /// Returns the collection of FontFamily objects from a specified URI.
    /// </summary>
    public static ICollection<FontFamily> GetFontFamilies(Uri baseUri)
    {
        return GetFontFamilies(baseUri.ToString());
    }

    public static ICollection<FontFamily> GetFontFamilies(Uri baseUri, string location)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        ArgumentNullException.ThrowIfNull(location);
        return new ReadOnlyCollection<FontFamily>(new[] { new FontFamily(baseUri, location) });
    }

    /// <summary>
    /// Returns the collection of Typeface objects from a specified font location.
    /// </summary>
    public static ICollection<Typeface> GetTypefaces(string location)
    {
        return new ReadOnlyCollection<Typeface>(Array.Empty<Typeface>());
    }

    /// <summary>
    /// Returns the collection of Typeface objects from a specified URI.
    /// </summary>
    public static ICollection<Typeface> GetTypefaces(Uri baseUri)
    {
        return GetTypefaces(baseUri.ToString());
    }

    public static ICollection<Typeface> GetTypefaces(Uri baseUri, string location)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        ArgumentNullException.ThrowIfNull(location);
        var family = new FontFamily(baseUri, location);
        ICollection<Typeface> typefaces = family.GetTypefaces();
        return typefaces.Count != 0
            ? new ReadOnlyCollection<Typeface>(typefaces.ToArray())
            : new ReadOnlyCollection<Typeface>(new[] { new Typeface(family) });
    }

    private static ICollection<FontFamily> GetSystemFontFamilies()
    {
        var families = new List<FontFamily>();
        // Common system fonts
        families.Add(new FontFamily(Jalium.UI.FrameworkElement.DefaultFontFamilyName));
        families.Add(new FontFamily("Arial"));
        families.Add(new FontFamily("Times New Roman"));
        families.Add(new FontFamily("Consolas"));
        families.Add(new FontFamily("Courier New"));
        return new ReadOnlyCollection<FontFamily>(families);
    }

    private static ICollection<Typeface> GetSystemTypefaces()
    {
        return new ReadOnlyCollection<Typeface>(Array.Empty<Typeface>());
    }
}

/// <summary>
/// Provides metadata for an image.
/// </summary>
public abstract class ImageMetadata : Freezable
{
    /// <summary>
    /// Creates a modifiable clone of this ImageMetadata.
    /// </summary>
    public new virtual ImageMetadata Clone() => (ImageMetadata)base.Clone();

    /// <summary>Creates a modifiable copy using current property values.</summary>
    public new virtual ImageMetadata CloneCurrentValue() => (ImageMetadata)base.CloneCurrentValue();
}

/// <summary>
/// Provides metadata for bitmap images.
/// </summary>
public class BitmapMetadataBase : ImageMetadata
{
    protected override Freezable CreateInstanceCore() => new BitmapMetadataBase();
}

/// <summary>
/// Represents metric information for a single character in a device font.
/// </summary>
public sealed class CharacterMetrics
{
    /// <summary>Gets or sets all metrics as a comma-separated invariant string.</summary>
    public string Metrics
    {
        get => ToString();
        set => ApplyMetrics(value);
    }

    /// <summary>
    /// Gets or sets the width of the black box for the character.
    /// </summary>
    public double BlackBoxWidth { get; set; }

    /// <summary>
    /// Gets or sets the height of the black box for the character.
    /// </summary>
    public double BlackBoxHeight { get; set; }

    /// <summary>
    /// Gets or sets the baseline value for the character.
    /// </summary>
    public double Baseline { get; set; }

    /// <summary>
    /// Gets or sets the recommended white space to the left of the black box.
    /// </summary>
    public double LeftSideBearing { get; set; }

    /// <summary>
    /// Gets or sets the recommended white space to the right of the black box.
    /// </summary>
    public double RightSideBearing { get; set; }

    /// <summary>
    /// Gets or sets the recommended white space above the black box.
    /// </summary>
    public double TopSideBearing { get; set; }

    /// <summary>
    /// Gets or sets the recommended white space below the black box.
    /// </summary>
    public double BottomSideBearing { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CharacterMetrics"/> class.
    /// </summary>
    public CharacterMetrics()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CharacterMetrics"/> class with
    /// metric values specified as a string.
    /// </summary>
    /// <param name="metrics">A comma-separated string of metric values:
    /// BlackBoxWidth,BlackBoxHeight,Baseline,LeftSideBearing,RightSideBearing,TopSideBearing,BottomSideBearing.</param>
    public CharacterMetrics(string metrics)
    {
        ApplyMetrics(metrics);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        if (obj is not CharacterMetrics other)
            return false;

        return BlackBoxWidth == other.BlackBoxWidth &&
               BlackBoxHeight == other.BlackBoxHeight &&
               Baseline == other.Baseline &&
               LeftSideBearing == other.LeftSideBearing &&
               RightSideBearing == other.RightSideBearing &&
               TopSideBearing == other.TopSideBearing &&
               BottomSideBearing == other.BottomSideBearing;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(BlackBoxWidth, BlackBoxHeight, Baseline,
            LeftSideBearing, RightSideBearing, TopSideBearing, BottomSideBearing);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return FormattableString.Invariant(
            $"{BlackBoxWidth},{BlackBoxHeight},{Baseline},{LeftSideBearing},{RightSideBearing},{TopSideBearing},{BottomSideBearing}");
    }

    private void ApplyMetrics(string metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        string[] parts = metrics.Split(',');
        if (parts.Length is < 2 or > 7 ||
            string.IsNullOrWhiteSpace(parts[0]) ||
            string.IsNullOrWhiteSpace(parts[1]))
        {
            throw new FormatException("Character metrics require black-box width and height and allow up to seven values.");
        }

        var values = new double[7];
        for (int index = 0; index < parts.Length; index++)
        {
            values[index] = string.IsNullOrWhiteSpace(parts[index])
                ? 0
                : double.Parse(parts[index], System.Globalization.CultureInfo.InvariantCulture);
        }

        BlackBoxWidth = values[0];
        BlackBoxHeight = values[1];
        Baseline = values[2];
        LeftSideBearing = values[3];
        RightSideBearing = values[4];
        TopSideBearing = values[5];
        BottomSideBearing = values[6];
    }
}

/// <summary>
/// Represents a dictionary of <see cref="CharacterMetrics"/> objects, keyed by Unicode scalar values.
/// </summary>
public sealed class CharacterMetricsDictionary : IDictionary<int, CharacterMetrics>
{
    private readonly Dictionary<int, CharacterMetrics> _dict = new();

    /// <inheritdoc />
    public CharacterMetrics this[int key]
    {
        get => _dict[key];
        set => _dict[key] = value;
    }

    /// <inheritdoc />
    public ICollection<int> Keys => _dict.Keys;

    /// <inheritdoc />
    public ICollection<CharacterMetrics> Values => _dict.Values;

    /// <inheritdoc />
    public int Count => _dict.Count;

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <inheritdoc />
    public void Add(int key, CharacterMetrics value)
    {
        _dict.Add(key, value);
    }

    /// <inheritdoc />
    public void Add(KeyValuePair<int, CharacterMetrics> item)
    {
        ((ICollection<KeyValuePair<int, CharacterMetrics>>)_dict).Add(item);
    }

    /// <inheritdoc />
    public void Clear()
    {
        _dict.Clear();
    }

    /// <inheritdoc />
    public bool Contains(KeyValuePair<int, CharacterMetrics> item)
    {
        return ((ICollection<KeyValuePair<int, CharacterMetrics>>)_dict).Contains(item);
    }

    /// <inheritdoc />
    public bool ContainsKey(int key)
    {
        return _dict.ContainsKey(key);
    }

    /// <inheritdoc />
    public void CopyTo(KeyValuePair<int, CharacterMetrics>[] array, int arrayIndex)
    {
        ((ICollection<KeyValuePair<int, CharacterMetrics>>)_dict).CopyTo(array, arrayIndex);
    }

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<int, CharacterMetrics>> GetEnumerator()
    {
        return _dict.GetEnumerator();
    }

    /// <inheritdoc />
    public bool Remove(int key)
    {
        return _dict.Remove(key);
    }

    /// <inheritdoc />
    public bool Remove(KeyValuePair<int, CharacterMetrics> item)
    {
        return ((ICollection<KeyValuePair<int, CharacterMetrics>>)_dict).Remove(item);
    }

    /// <inheritdoc />
    public bool TryGetValue(int key, out CharacterMetrics value)
    {
        return _dict.TryGetValue(key, out value!);
    }

    /// <inheritdoc />
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
    {
        return _dict.GetEnumerator();
    }
}

/// <summary>
/// Describes a mapping between a Unicode code-point range and the target FontFamily
/// to use for that range within a composite font.
/// </summary>
public sealed class FamilyMap
{
    /// <summary>
    /// Gets or sets the target font family name for this mapping.
    /// </summary>
    public string? Target { get; set; }

    /// <summary>
    /// Gets or sets the Unicode range for this mapping (e.g., "0000-00FF").
    /// </summary>
    public string? Unicode { get; set; }

    /// <summary>
    /// Gets or sets the font scale factor for this mapping.
    /// </summary>
    public double Scale { get; set; } = 1.0;

    /// <summary>
    /// Gets or sets the language tag for this mapping.
    /// </summary>
    public string? Language { get; set; }
}

/// <summary>
/// Represents an ordered collection of <see cref="FamilyMap"/> objects.
/// </summary>
public sealed class FamilyMapCollection : Collection<FamilyMap>
{
}

/// <summary>
/// Describes font embedding permissions specified in an OpenType font file.
/// </summary>
public enum FontEmbeddingRight
{
    /// <summary>Fonts can be embedded and permanently installed on the remote system.</summary>
    Installable = 0,

    /// <summary>Fonts can be embedded and permanently installed but must not be subsetted.</summary>
    InstallableButNoSubsetting = 1,

    /// <summary>Fonts can be embedded and permanently installed with bitmaps only.</summary>
    InstallableButWithBitmapsOnly = 2,

    /// <summary>Fonts can be embedded and permanently installed with bitmaps only and must not be subsetted.</summary>
    InstallableButNoSubsettingAndWithBitmapsOnly = 3,

    /// <summary>Fonts may not be embedded or temporarily loaded.</summary>
    RestrictedLicense = 4,

    /// <summary>Fonts can be embedded for preview and print only.</summary>
    PreviewAndPrint = 5,

    /// <summary>Fonts can be embedded for preview and print only but must not be subsetted.</summary>
    PreviewAndPrintButNoSubsetting = 6,

    /// <summary>Fonts can be embedded for preview and print only with bitmaps only.</summary>
    PreviewAndPrintButWithBitmapsOnly = 7,

    /// <summary>Fonts can be embedded for preview and print only with bitmaps only and must not be subsetted.</summary>
    PreviewAndPrintButNoSubsettingAndWithBitmapsOnly = 8,

    /// <summary>Fonts can be embedded but must only be installed temporarily.</summary>
    Editable = 9,

    /// <summary>Fonts can be embedded temporarily but must not be subsetted.</summary>
    EditableButNoSubsetting = 10,

    /// <summary>Fonts can be embedded temporarily with bitmaps only.</summary>
    EditableButWithBitmapsOnly = 11,

    /// <summary>Fonts can be embedded temporarily with bitmaps only and must not be subsetted.</summary>
    EditableButNoSubsettingAndWithBitmapsOnly = 12,
}
