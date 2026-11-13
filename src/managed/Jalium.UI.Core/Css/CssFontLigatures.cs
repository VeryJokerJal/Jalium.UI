using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal enum CssFontKerning { Auto, Normal, None }

[Flags]
internal enum CssLigatureVariant
{
    Normal = 0,
    Common = 1, NoCommon = 2,
    Discretionary = 4, NoDiscretionary = 8,
    Historical = 16, NoHistorical = 32,
    Contextual = 64, NoContextual = 128,
    None = 256,
}

internal static class CssFontLigatures
{
    internal static readonly DependencyProperty KerningProperty = DependencyProperty.RegisterAttached(
        "CssFontKerning", typeof(CssFontKerning), typeof(CssFontLigatures),
        new FrameworkPropertyMetadata(CssFontKerning.Auto, FrameworkPropertyMetadataOptions.Inherits |
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            static (owner, _) => FontWidthRenderingSource.Invalidate(CssNode.Get(owner))));

    internal static readonly DependencyProperty VariantProperty = DependencyProperty.RegisterAttached(
        "CssLigatureVariant", typeof(CssLigatureVariant), typeof(CssFontLigatures),
        new FrameworkPropertyMetadata(CssLigatureVariant.Normal, FrameworkPropertyMetadataOptions.Inherits |
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            static (owner, _) => FontWidthRenderingSource.Invalidate(CssNode.Get(owner))));

    internal static CssCompiledValue? ParseKerning(ref CssTokenReader reader)
    {
        if (!reader.TryReadIdent(out var word) || !reader.AtEnd) return null;
        CssFontKerning? value = word.ToString().ToLowerInvariant() switch
        {
            "auto" => CssFontKerning.Auto, "normal" => CssFontKerning.Normal,
            "none" => CssFontKerning.None, _ => null,
        };
        return value is { } parsed ? new CssImmediateValue(KerningProperty, parsed) : null;
    }

    internal static CssCompiledValue? ParseVariant(ref CssTokenReader reader)
    {
        CssLigatureVariant flags = 0;
        while (reader.TryReadIdent(out var word))
        {
            CssLigatureVariant? flag = word.ToString().ToLowerInvariant() switch
            {
                "normal" => CssLigatureVariant.Normal, "none" => CssLigatureVariant.None,
                "common-ligatures" => CssLigatureVariant.Common, "no-common-ligatures" => CssLigatureVariant.NoCommon,
                "discretionary-ligatures" => CssLigatureVariant.Discretionary, "no-discretionary-ligatures" => CssLigatureVariant.NoDiscretionary,
                "historical-ligatures" => CssLigatureVariant.Historical, "no-historical-ligatures" => CssLigatureVariant.NoHistorical,
                "contextual" => CssLigatureVariant.Contextual, "no-contextual" => CssLigatureVariant.NoContextual,
                _ => null,
            };
            if (flag is not { } value) return null;
            if (value is CssLigatureVariant.Normal or CssLigatureVariant.None)
                return flags == 0 && reader.AtEnd ? new CssImmediateValue(VariantProperty, value) : null;
            var group = value is CssLigatureVariant.Common or CssLigatureVariant.NoCommon ? (CssLigatureVariant)3
                : value is CssLigatureVariant.Discretionary or CssLigatureVariant.NoDiscretionary ? (CssLigatureVariant)12
                : value is CssLigatureVariant.Historical or CssLigatureVariant.NoHistorical ? (CssLigatureVariant)48 : (CssLigatureVariant)192;
            if ((flags & group) != 0) return null;
            flags |= value;
        }
        return flags != 0 && reader.AtEnd ? new CssImmediateValue(VariantProperty, flags) : null;
    }

    internal static FontFeature[] Features(DependencyObject owner)
    {
        var result = new List<FontFeature>(6);
        var kerning = (CssFontKerning)owner.GetValue(KerningProperty)!;
        if (kerning != CssFontKerning.Auto) result.Add(new(FontFeature.TagOf("kern"), kerning == CssFontKerning.None ? 0u : 1u));
        var flags = (CssLigatureVariant)owner.GetValue(VariantProperty)!;
        Pair(CssLigatureVariant.Common, CssLigatureVariant.NoCommon, "liga", "clig");
        Pair(CssLigatureVariant.Discretionary, CssLigatureVariant.NoDiscretionary, "dlig");
        Pair(CssLigatureVariant.Historical, CssLigatureVariant.NoHistorical, "hlig");
        Pair(CssLigatureVariant.Contextual, CssLigatureVariant.NoContextual, "calt");
        return result.ToArray();

        void Pair(CssLigatureVariant enabled, CssLigatureVariant disabled, params string[] tags)
        {
            if (flags != CssLigatureVariant.None && (flags & (enabled | disabled)) == 0) return;
            uint value = flags == CssLigatureVariant.None || (flags & disabled) != 0 ? 0u : 1u;
            foreach (string tag in tags) result.Add(new(FontFeature.TagOf(tag), value));
        }
    }

    // CSS Text suppresses optional ligatures for nonzero tracking. Required
    // ligatures and contextual alternates remain available; explicit low-level
    // settings are merged later and can opt an optional ligature back in.
    internal static FontFeature[] SpacingFeatures(DependencyObject owner)
        => CssTextSpacing.Letter(owner) != 0
            ? [new(FontFeature.TagOf("liga"), 0), new(FontFeature.TagOf("clig"), 0),
                new(FontFeature.TagOf("dlig"), 0), new(FontFeature.TagOf("hlig"), 0)] : [];
}
