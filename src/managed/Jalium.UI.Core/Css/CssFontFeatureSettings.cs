using System.Globalization;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

// The computed value is a whole map. Canonical equality avoids invalidating a
// document again when a stylesheet spells the same map in a different order.
internal sealed class CssFontFeatureSettings : IEquatable<CssFontFeatureSettings>
{
    private const int MaximumLayerFeatures = 4096;
    internal static readonly CssFontFeatureSettings Normal = new([]);
    internal static readonly DependencyProperty SettingsProperty = DependencyProperty.RegisterAttached(
        "CssFontFeatureSettings", typeof(CssFontFeatureSettings), typeof(CssFontFeatureSettings),
        new FrameworkPropertyMetadata(Normal, FrameworkPropertyMetadataOptions.Inherits |
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            static (owner, _) => FontWidthRenderingSource.Invalidate(CssNode.Get(owner))));

    internal FontFeature[] Features { get; }
    private readonly string _key;
    private CssFontFeatureSettings(FontFeature[] features)
    {
        Features = features;
        _key = string.Join(',', features.Select(f => f.Tag.ToString("X8", CultureInfo.InvariantCulture) + "=" + f.Value.ToString(CultureInfo.InvariantCulture)));
    }

    internal static CssCompiledValue? Parse(ref CssTokenReader reader)
        => Read(ref reader) is { } value ? new CssImmediateValue(SettingsProperty, value) : null;

    internal static CssFontFeatureSettings? Read(ref CssTokenReader reader)
    {
        var probe = reader;
        if (probe.TryReadIdent(out var word) && word.Equals("normal", StringComparison.OrdinalIgnoreCase) && probe.AtEnd)
        { reader = probe; return Normal; }
        var values = new Dictionary<uint, uint>();
        while (reader.TryReadString(out var tag))
        {
            if (tag.Length != 4 || tag.Any(c => c is < ' ' or > '~')) return null;
            uint value = 1;
            probe = reader;
            if (probe.TryReadIdent(out word))
            {
                if (word.Equals("on", StringComparison.OrdinalIgnoreCase)) value = 1;
                else if (word.Equals("off", StringComparison.OrdinalIgnoreCase)) value = 0;
                else return null;
                reader = probe;
            }
            else if (probe.TryReadInteger(out var number, 0)) { value = (uint)number; reader = probe; }
            values[FontFeature.TagOf(tag)] = value;
            if (values.Count > MaximumLayerFeatures) return null;
            if (reader.AtEnd) return new(values.OrderBy(p => p.Key).Select(p => new FontFeature(p.Key, p.Value)).ToArray());
            if (!reader.TryReadComma() || reader.AtEnd) return null;
        }
        return null;
    }

    internal static FontFeature[] ForOwner(DependencyObject owner)
        => Compose(owner, CssFontCaps.Features(owner));

    // Caps need the selected native face before choosing petite/small fallback.
    // Keep explicit low-level settings here so they remain the final layer.
    internal static FontFeature[] ForRendering(DependencyObject owner) => Compose(owner, []);

    private static FontFeature[] Compose(DependencyObject owner, FontFeature[] caps)
    {
        var features = FontFeatureRenderingSource.Merge(CssFontNumeric.Features(owner), caps);
        features = FontFeatureRenderingSource.Merge(features, CssFontAlternates.GlobalFeatures(owner));
        features = FontFeatureRenderingSource.Merge(features, CssFontEastAsian.Features(owner));
        features = FontFeatureRenderingSource.Merge(features, CssFontLigatures.Features(owner));
        features = FontFeatureRenderingSource.Merge(features, CssFontLigatures.SpacingFeatures(owner));
        return FontFeatureRenderingSource.Merge(features, ((CssFontFeatureSettings)owner.GetValue(SettingsProperty)!).Features);
    }

    internal string ToCss() => Features.Length == 0 ? "normal" : string.Join(", ", Features.Select(feature =>
        "\"" + new string([(char)(feature.Tag >> 24), (char)(feature.Tag >> 16 & 255),
            (char)(feature.Tag >> 8 & 255), (char)(feature.Tag & 255)]).Replace("\\", "\\\\").Replace("\"", "\\\"") +
        "\" " + feature.Value.ToString(CultureInfo.InvariantCulture)));

    public bool Equals(CssFontFeatureSettings? other) => other is not null && _key == other._key;
    public override bool Equals(object? other) => other is CssFontFeatureSettings value && Equals(value);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_key);
}
