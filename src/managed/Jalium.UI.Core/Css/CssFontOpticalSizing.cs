using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal static class CssFontOpticalSizing
{
    internal static readonly DependencyProperty AutoProperty = DependencyProperty.RegisterAttached(
        "CssFontOpticalSizing", typeof(bool), typeof(CssFontOpticalSizing),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits |
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            static (owner, _) => FontWidthRenderingSource.Invalidate(CssNode.Get(owner))));

    internal static CssCompiledValue? Parse(ref CssTokenReader reader)
        => reader.TryReadIdent(out var keyword) && reader.AtEnd
            ? keyword.Equals("auto", StringComparison.OrdinalIgnoreCase) ? new CssImmediateValue(AutoProperty, true)
            : keyword.Equals("none", StringComparison.OrdinalIgnoreCase) ? new CssImmediateValue(AutoProperty, false) : null : null;

    // An internal zero mode keeps standalone FormattedText/native callers on
    // their existing platform policy. CSS defaults to automatic optical sizing.
    internal static byte ForOwner(DependencyObject owner) => (bool)owner.GetValue(AutoProperty)! ? (byte)1 : (byte)2;

    internal static FontVariation[] Compose(byte mode, FontVariation[] descriptor, FontVariation[] property)
    {
        // auto follows the used text size and overrides descriptor opsz. none
        // disables automatic sizing, retaining an explicitly authored descriptor.
        if (mode == 1) descriptor = descriptor.Where(axis => axis.Tag != 0x6f70737a).ToArray();
        return CssFontVariationSettings.Merge(descriptor, property);
    }
}
