namespace Jalium.UI.Styling;

/// <summary>
/// Font capabilities used by both @font-face source hints and @supports queries.
/// Keep these answers tied to the formats and technologies the text pipeline can use.
/// </summary>
internal static class CssFontCapabilities
{
    internal static bool SupportsFormat(string format) => format.ToLowerInvariant() is
        "truetype" or "opentype" or "woff" or "woff2" or "collection";

    // The native shapers use OpenType GSUB/GPOS features. Variation selection,
    // palette control and the individual color-font technologies are not yet
    // guaranteed across all rendering backends.
    internal static bool SupportsTechnology(string technology) =>
        technology.Equals("features-opentype", StringComparison.OrdinalIgnoreCase);

    internal static bool SupportsSource(CssFontSource source) =>
        (source.Format is null || SupportsFormat(source.Format)) &&
        source.Technologies.All(SupportsTechnology);
}
