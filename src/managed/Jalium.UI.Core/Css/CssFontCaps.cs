using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal enum CssCapsVariant
{
    Normal, SmallCaps, AllSmallCaps, PetiteCaps, AllPetiteCaps, Unicase, TitlingCaps,
}

internal static class CssFontCaps
{
    internal static readonly DependencyProperty SynthesisProperty = DependencyProperty.RegisterAttached(
        "CssSmallCapsSynthesis", typeof(bool), typeof(CssFontCaps),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits |
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            static (owner, _) => FontWidthRenderingSource.Invalidate(CssNode.Get(owner))));
    internal static readonly DependencyProperty VariantProperty = DependencyProperty.RegisterAttached(
        "CssCapsVariant", typeof(CssCapsVariant), typeof(CssFontCaps),
        new FrameworkPropertyMetadata(CssCapsVariant.Normal, FrameworkPropertyMetadataOptions.Inherits |
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            static (owner, _) => FontWidthRenderingSource.Invalidate(CssNode.Get(owner))));

    internal static CssCompiledValue? Parse(ref CssTokenReader reader)
    {
        if (!reader.TryReadIdent(out var word) || !reader.AtEnd) return null;
        CssCapsVariant? variant = word.ToString().ToLowerInvariant() switch
        {
            "normal" => CssCapsVariant.Normal,
            "small-caps" => CssCapsVariant.SmallCaps,
            "all-small-caps" => CssCapsVariant.AllSmallCaps,
            "petite-caps" => CssCapsVariant.PetiteCaps,
            "all-petite-caps" => CssCapsVariant.AllPetiteCaps,
            "unicase" => CssCapsVariant.Unicase,
            "titling-caps" => CssCapsVariant.TitlingCaps,
            _ => null,
        };
        return variant is { } value ? new CssImmediateValue(VariantProperty, value) : null;
    }

    internal static FontFeature[] Features(DependencyObject owner)
        => Features((CssCapsVariant)owner.GetValue(VariantProperty)!);

    internal static FontFeature[] Features(CssCapsVariant variant)
        => variant switch
        {
            CssCapsVariant.SmallCaps => [Feature("smcp")],
            CssCapsVariant.AllSmallCaps => [Feature("smcp"), Feature("c2sc")],
            CssCapsVariant.PetiteCaps => [Feature("pcap")],
            CssCapsVariant.AllPetiteCaps => [Feature("pcap"), Feature("c2pc")],
            CssCapsVariant.Unicase => [Feature("unic")],
            CssCapsVariant.TitlingCaps => [Feature("titl")],
            _ => [],
        };

    internal static FontFeature[] Resolve(CssCapsVariant variant, bool petiteLower, bool petiteUpper)
        => variant switch
        {
            CssCapsVariant.PetiteCaps when !petiteLower =>
                [new(FontFeature.TagOf("pcap"), 0), Feature("smcp")],
            CssCapsVariant.AllPetiteCaps when !petiteLower || !petiteUpper =>
                [new(FontFeature.TagOf("pcap"), 0), new(FontFeature.TagOf("c2pc"), 0), Feature("smcp"), Feature("c2sc")],
            _ => Features(variant),
        };

    private static FontFeature Feature(string tag) => new(FontFeature.TagOf(tag), 1);
}
