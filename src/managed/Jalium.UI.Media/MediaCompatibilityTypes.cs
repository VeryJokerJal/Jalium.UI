using System.ComponentModel;
using System.Globalization;

namespace Jalium.UI.Media;

/// <summary>Converts cache modes to and from their XAML names.</summary>
public sealed class CacheModeConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string text && string.Equals(text.Trim(), nameof(BitmapCache), StringComparison.OrdinalIgnoreCase))
        {
            return new BitmapCache();
        }

        if (value is string invalid)
        {
            throw new FormatException($"'{invalid}' is not a valid cache mode.");
        }

        return base.ConvertFrom(context, culture, value)!;
    }

    public override object? ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType)
    {
        if (destinationType == typeof(string) && value is BitmapCache)
        {
            return nameof(BitmapCache);
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }
}

/// <summary>Records the glyphs used from each physical font URI for font embedding.</summary>
public class FontEmbeddingManager
{
    private readonly object _gate = new();
    private readonly Dictionary<Uri, HashSet<ushort>> _usage = new();

    /// <summary>Gets the font URIs for which glyph usage has been recorded.</summary>
    public ICollection<Uri> GlyphTypefaceUris
    {
        get
        {
            lock (_gate)
            {
                return Array.AsReadOnly(_usage.Keys.ToArray());
            }
        }
    }

    /// <summary>Adds a glyph run's indices to the usage set for its physical typeface.</summary>
    public void RecordUsage(GlyphRun glyphRun)
    {
        ArgumentNullException.ThrowIfNull(glyphRun);

        Uri? fontUri = glyphRun.GlyphTypeface?.FontUri;
        IList<ushort>? glyphIndices = glyphRun.GlyphIndices;
        if (fontUri is null || glyphIndices is null)
        {
            throw new InvalidOperationException("The glyph run must have a glyph typeface and glyph indices.");
        }

        lock (_gate)
        {
            if (!_usage.TryGetValue(fontUri, out HashSet<ushort>? usedGlyphs))
            {
                usedGlyphs = new HashSet<ushort>();
                _usage.Add(fontUri, usedGlyphs);
            }

            foreach (ushort glyphIndex in glyphIndices)
            {
                usedGlyphs.Add(glyphIndex);
            }
        }
    }

    /// <summary>Gets the unique glyph indices recorded for a font URI.</summary>
    public ICollection<ushort> GetUsedGlyphs(Uri glyphTypefaceUri)
    {
        ArgumentNullException.ThrowIfNull(glyphTypefaceUri);
        lock (_gate)
        {
            if (!_usage.TryGetValue(glyphTypefaceUri, out HashSet<ushort>? usedGlyphs))
            {
                throw new KeyNotFoundException($"No glyph usage was recorded for '{glyphTypefaceUri}'.");
            }

            ushort[] result = usedGlyphs.ToArray();
            Array.Sort(result);
            return Array.AsReadOnly(result);
        }
    }
}
