using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private FlowDirection CssLineDirection(int source)
    {
        if (UsesUprightGlyphForms) return FlowDirection.LeftToRight;
        if (this is InlineSourceText || HasCssInlineFragments ||
            CssUnicodeBidiProperties.Value(this) != CssUnicodeBidi.Plaintext) return FlowDirection;
        _cssBidiDirectionProjection ??= CreateCssBidiProjection(_visualText, CssUnicodeBidi.Plaintext, softBreaks: false);
        return _cssBidiDirectionProjection.LineDirection(source, FlowDirection);
    }

    private TextAlignment CssLineTextAlignment(in TextLayoutLine line, bool isLast)
    {
        if (_cssRubyJustify) return TextAlignment.Justify;
        if (CssUnicodeBidiProperties.Value(this) != CssUnicodeBidi.Plaintext ||
            HasLocalOrAnimatedValue(TextAlignmentProperty))
            return CssFlowProperties.NativeLineTextAlignment(this, TextAlignmentProperty, isLast);
        return CssFlowProperties.ToNativeTextAlignment(
            CssFlowProperties.LineAlignment(this, isLast, TextAlignmentProperty),
            CssLineDirection(line.StartIndex) == FlowDirection.RightToLeft);
    }
}
