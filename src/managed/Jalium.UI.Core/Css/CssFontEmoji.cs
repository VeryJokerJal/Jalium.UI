using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal enum CssEmojiVariant { Normal, Text, Emoji, Unicode }

internal static class CssFontEmoji
{
    internal static readonly DependencyProperty VariantProperty = DependencyProperty.RegisterAttached(
        "CssEmojiVariant", typeof(CssEmojiVariant), typeof(CssFontEmoji),
        new FrameworkPropertyMetadata(CssEmojiVariant.Normal, FrameworkPropertyMetadataOptions.Inherits |
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            static (owner, _) => FontWidthRenderingSource.Invalidate(CssNode.Get(owner))));
    internal static CssCompiledValue? Parse(ref CssTokenReader reader)
    {
        if (!reader.TryReadIdent(out var word) || !reader.AtEnd) return null;
        CssEmojiVariant? value=word.ToString().ToLowerInvariant() switch {
            "normal"=>CssEmojiVariant.Normal,"text"=>CssEmojiVariant.Text,"emoji"=>CssEmojiVariant.Emoji,
            "unicode"=>CssEmojiVariant.Unicode,_=>null,
        };
        return value is {} variant ? new CssImmediateValue(VariantProperty,variant) : null;
    }
}
