using Jalium.UI.Media;

namespace Jalium.UI.Styling;

[Flags]
internal enum CssEastAsianVariant
{
    Normal = 0, Jis78 = 1, Jis83 = 2, Jis90 = 4, Jis04 = 8,
    Simplified = 16, Traditional = 32, FullWidth = 64, ProportionalWidth = 128, Ruby = 256,
}

internal static class CssFontEastAsian
{
    internal static readonly DependencyProperty VariantProperty = DependencyProperty.RegisterAttached(
        "CssEastAsianVariant", typeof(CssEastAsianVariant), typeof(CssFontEastAsian),
        new FrameworkPropertyMetadata(CssEastAsianVariant.Normal, FrameworkPropertyMetadataOptions.Inherits |
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            static (owner, _) => FontWidthRenderingSource.Invalidate(CssNode.Get(owner))));

    internal static CssCompiledValue? Parse(ref CssTokenReader reader)
    {
        CssEastAsianVariant flags = 0;
        while (reader.TryReadIdent(out var word))
        {
            CssEastAsianVariant? flag = word.ToString().ToLowerInvariant() switch
            {
                "normal" => CssEastAsianVariant.Normal,
                "jis78" => CssEastAsianVariant.Jis78, "jis83" => CssEastAsianVariant.Jis83,
                "jis90" => CssEastAsianVariant.Jis90, "jis04" => CssEastAsianVariant.Jis04,
                "simplified" => CssEastAsianVariant.Simplified, "traditional" => CssEastAsianVariant.Traditional,
                "full-width" => CssEastAsianVariant.FullWidth, "proportional-width" => CssEastAsianVariant.ProportionalWidth,
                "ruby" => CssEastAsianVariant.Ruby, _ => null,
            };
            if (flag is not { } value) return null;
            if (value == 0) return flags == 0 && reader.AtEnd ? new CssImmediateValue(VariantProperty, value) : null;
            var group = (value & (CssEastAsianVariant)63) != 0 ? (CssEastAsianVariant)63
                : value is CssEastAsianVariant.FullWidth or CssEastAsianVariant.ProportionalWidth ? (CssEastAsianVariant)192 : value;
            if ((flags & group) != 0) return null;
            flags |= value;
        }
        return flags != 0 && reader.AtEnd ? new CssImmediateValue(VariantProperty, flags) : null;
    }

    internal static FontFeature[] Features(DependencyObject owner)
    {
        var flags = (CssEastAsianVariant)owner.GetValue(VariantProperty)!;
        if (flags == 0) return [];
        var result = new List<FontFeature>(3);
        Add(CssEastAsianVariant.Jis78, "jp78"); Add(CssEastAsianVariant.Jis83, "jp83");
        Add(CssEastAsianVariant.Jis90, "jp90"); Add(CssEastAsianVariant.Jis04, "jp04");
        Add(CssEastAsianVariant.Simplified, "smpl"); Add(CssEastAsianVariant.Traditional, "trad");
        Add(CssEastAsianVariant.FullWidth, "fwid"); Add(CssEastAsianVariant.ProportionalWidth, "pwid"); Add(CssEastAsianVariant.Ruby, "ruby");
        return result.ToArray();
        void Add(CssEastAsianVariant flag, string tag) { if ((flags & flag) != 0) result.Add(new(FontFeature.TagOf(tag), 1)); }
    }
}
