using Jalium.UI.Media;

namespace Jalium.UI.Styling;

[Flags]
internal enum CssNumericVariant
{
    Normal = 0, Lining = 1, OldStyle = 2, Proportional = 4, Tabular = 8,
    DiagonalFractions = 16, StackedFractions = 32, Ordinal = 64, SlashedZero = 128,
}

internal static class CssFontNumeric
{
    internal static readonly DependencyProperty VariantProperty = DependencyProperty.RegisterAttached(
        "CssNumericVariant", typeof(CssNumericVariant), typeof(CssFontNumeric),
        new FrameworkPropertyMetadata(CssNumericVariant.Normal, FrameworkPropertyMetadataOptions.Inherits |
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            static (owner, _) => FontWidthRenderingSource.Invalidate(CssNode.Get(owner))));

    internal static CssCompiledValue? Parse(ref CssTokenReader reader)
    {
        CssNumericVariant flags = 0;
        while (reader.TryReadIdent(out var word))
        {
            var flag = word.ToString().ToLowerInvariant() switch
            {
                "normal" => CssNumericVariant.Normal,
                "lining-nums" => CssNumericVariant.Lining,
                "oldstyle-nums" => CssNumericVariant.OldStyle,
                "proportional-nums" => CssNumericVariant.Proportional,
                "tabular-nums" => CssNumericVariant.Tabular,
                "diagonal-fractions" => CssNumericVariant.DiagonalFractions,
                "stacked-fractions" => CssNumericVariant.StackedFractions,
                "ordinal" => CssNumericVariant.Ordinal,
                "slashed-zero" => CssNumericVariant.SlashedZero,
                _ => (CssNumericVariant)(-1),
            };
            if (flag == (CssNumericVariant)(-1)) return null;
            if (flag == 0) return flags == 0 && reader.AtEnd
                ? new CssImmediateValue(VariantProperty, CssNumericVariant.Normal) : null;
            var group = flag is CssNumericVariant.Lining or CssNumericVariant.OldStyle ? (CssNumericVariant)3
                : flag is CssNumericVariant.Proportional or CssNumericVariant.Tabular ? (CssNumericVariant)12
                : flag is CssNumericVariant.DiagonalFractions or CssNumericVariant.StackedFractions ? (CssNumericVariant)48 : flag;
            if ((flags & group) != 0) return null;
            flags |= flag;
        }
        return flags != 0 && reader.AtEnd ? new CssImmediateValue(VariantProperty, flags) : null;
    }

    internal static FontFeature[] Features(DependencyObject owner)
    {
        var flags = (CssNumericVariant)owner.GetValue(VariantProperty)!;
        if (flags == 0) return [];
        var result = new List<FontFeature>(5);
        Add(CssNumericVariant.Lining, "lnum"); Add(CssNumericVariant.OldStyle, "onum");
        Add(CssNumericVariant.Proportional, "pnum"); Add(CssNumericVariant.Tabular, "tnum");
        Add(CssNumericVariant.DiagonalFractions, "frac"); Add(CssNumericVariant.StackedFractions, "afrc");
        Add(CssNumericVariant.Ordinal, "ordn"); Add(CssNumericVariant.SlashedZero, "zero");
        return result.ToArray();
        void Add(CssNumericVariant flag, string tag) { if ((flags & flag) != 0) result.Add(new(FontFeature.TagOf(tag), 1)); }
    }
}
