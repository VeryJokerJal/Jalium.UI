using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal enum CssPositionVariant { Normal, Sub, Super }

internal static class CssFontPosition
{
    internal static readonly DependencyProperty VariantProperty = DependencyProperty.RegisterAttached(
        "CssPositionVariant", typeof(CssPositionVariant), typeof(CssFontPosition),
        new FrameworkPropertyMetadata(CssPositionVariant.Normal, FrameworkPropertyMetadataOptions.Inherits |
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            static (owner, _) => FontWidthRenderingSource.Invalidate(CssNode.Get(owner))));
    internal static readonly DependencyProperty SynthesisProperty = DependencyProperty.RegisterAttached(
        "CssPositionSynthesis", typeof(bool), typeof(CssFontPosition),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits |
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            static (owner, _) => FontWidthRenderingSource.Invalidate(CssNode.Get(owner))));

    internal static CssCompiledValue? Parse(ref CssTokenReader reader)
    {
        if (!reader.TryReadIdent(out var word) || !reader.AtEnd) return null;
        CssPositionVariant? value = word.ToString().ToLowerInvariant() switch
        {
            "normal" => CssPositionVariant.Normal, "sub" => CssPositionVariant.Sub,
            "super" => CssPositionVariant.Super, _ => null,
        };
        return value is { } position ? new CssImmediateValue(VariantProperty, position) : null;
    }
}
