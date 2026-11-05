using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private readonly Dictionary<TextLayoutLine, NativeTextParagraph?> _nativeBidiLines = [];
    private Color _nativeBidiColor;
    private double _nativeBidiOpacity;

    private void ClearNativeBidiLines()
    {
        ClearCssBidiLines();
        foreach (var paragraph in _nativeBidiLines.Values) paragraph?.Dispose();
        _nativeBidiLines.Clear();
        ClearShapedSpacing();
    }

    // Use the existing native paragraph for the base-direction-dependent order
    // of simple CSS lines. The same shaped object supplies drawing, ranges and
    // caret hits; source strings and their UTF-16 positions remain unchanged.
    private bool TryNativeBidiLine(in TextLayoutLine line, out NativeTextParagraph paragraph)
    {
        paragraph = null!;
        int visibleEnd = line.StartIndex + line.Length;
        if (HasAuthoredWhiteSpace() && _whiteSpaceMode is CssWhiteSpaceCollapse.Collapse or CssWhiteSpaceCollapse.PreserveBreaks)
            while (visibleEnd > line.StartIndex && _visualText[visibleEnd - 1] is ' ' or '\t') visibleEnd--;
        if (WantsShapedJustification)
        {
            TryGetJustifiedLine(line, GetContentWidth(TextContentSize.Width), out var justified);
            if (justified.Shaped is { } shaped) { paragraph = shaped; return true; }
        }
        if ((_hasLetterSpacing || _hasWordSpacing || _usesWordSpacingShaping || _usesCapsShaping || _usesAutospaceShaping || WantsShapedJustification) && line.PaintWrap is null && !line.SoftHyphenVisible && line.HyphenChange is not { HasChanges: true } && TextTrimming == TextTrimming.None &&
            TryShapedSpacing(line.StartIndex, line.StartIndex + line.Length, line.Height, out paragraph, visibleEnd)) return true;
        if (!OperatingSystem.IsMacOS() || FontSize <= 0 || !HasAuthoredWhiteSpace() ||
            FlowDirection == FlowDirection.LeftToRight && _cssInlineWhitespace ||
            line.Length == 0 || line.PaintWrap is not null || line.SoftHyphenVisible || line.HyphenChange is { HasChanges: true } || TextTrimming != TextTrimming.None ||
            _usesInlineFontLayout || _hasWordSpacing || _hasLetterSpacing || _hasPaintTextChanges ||
            ContainsPreservedTab(line.StartIndex, line.Length) || _inlines is { Count: > 0 } ||
            CssFlowProperties.NativeLineTextAlignment(this, TextAlignmentProperty, !line.CanJustify) == TextAlignment.Justify ||
            Foreground is not SolidColorBrush brush)
            return false;
        if (_nativeBidiColor != brush.Color || _nativeBidiOpacity != brush.Opacity)
        {
            ClearNativeBidiLines(); _nativeBidiColor = brush.Color; _nativeBidiOpacity = brush.Opacity;
        }
        if (!_nativeBidiLines.TryGetValue(line, out var found))
        {
            var text = PaintSlice(line.StartIndex, line.Length);
            var family = FontFamily.GetRenderingSource(this);
            var width = MeasureInlineRange(line.StartIndex, line.Length, includeTrailingWhitespace: true);
            found = NativeTextParagraph.TryCreate([new(text, family, FontSize, FontWeight.ToOpenTypeWeight(),
                FontStyle.ToOpenTypeStyle(), brush.Color, brush.Opacity)], family, FontSize, Math.Max(1, width),
                line.Height, TextAlignment.Left, FlowDirection, noWrap: true);
            if (found?.Lines.Length != 1) { found?.Dispose(); found = null; }
            // A plain LTR line retains its existing geometry. Only a line with
            // backwards native character edges needs explicit bidi shaping to
            // override CoreText's natural direction of the first strong letter.
            if (found is not null && FlowDirection == FlowDirection.LeftToRight && !HasNativeRtlEdges(found))
            { found.Dispose(); found = null; }
            _nativeBidiLines[line] = found;
        }
        paragraph = found!;
        return found is not null;
    }

    private static bool HasNativeRtlEdges(NativeTextParagraph paragraph)
    {
        var boundaries = GraphemeClusters.GetBoundaries(paragraph.Text);
        for (int i = 0; i + 1 < boundaries.Length; i++)
        {
            try
            {
                if (paragraph.Caret(0, boundaries[i], false).X > paragraph.Caret(0, boundaries[i + 1], true).X + .0001)
                    return true;
            }
            catch (InvalidOperationException) { /* CoreText omits some invisible formatting edges. */ }
        }
        return false;
    }

    private double NativeBidiOverflow(in TextLayoutLine line)
    {
        if (TryCssBidiJustificationOverflow(line, out var justifiedOverflow)) return justifiedOverflow;
        if (TryCssBidiParagraphOverflow(line, out var autospaceOverflow)) return autospaceOverflow;
        var tabOverflow = CssBidiTabOverflow(line);
        if (tabOverflow > 0) return tabOverflow;
        if (FlowDirection != FlowDirection.RightToLeft || HasCssInlineFragments || !TryNativeBidiLine(line, out var paragraph)) return 0;
        // Only trailing RTL advances can hang to the physical left. Spaced
        // lines have already removed collapsed trailing spaces from geometry.
        var physical = paragraph.Lines[0].Metrics.Line.Width;
        return Math.Max(0, physical - line.Width - line.HangingStart - line.HangingEnd);
    }

    private bool DrawNativeBidiLine(DrawingContext context, in TextLayoutLine line, double x, double y)
    {
        if (!TryNativeBidiLine(line, out var paragraph)) return false;
        if (paragraph.ContextPaint)
        {
            DrawContextSpacedLine(context, line, paragraph, line.StartIndex, line.StartIndex + line.Length, x, y);
            return true;
        }
        var native = paragraph.Lines[0];
        var text = new FormattedText(paragraph.Text, FontFamily.GetRenderingSource(this), FontSize)
        {
            PlatformTextLine = native, Foreground = Foreground, MaxTextWidth = Math.Max(1, line.Width),
            MaxTextHeight = Math.Max(1, TextPaintHeight(line)), Baseline = native.Metrics.Baseline,
        };
        text.ApplyTextOptionsFrom(this);
        // CoreText aligns the visible glyphs and may report trailing RTL spaces
        // at negative coordinates. CSS positions the full physical inline box.
        DrawDocumentGlyphs(context, text, new(x - native.Metrics.Line.X, y + line.Baseline - native.Metrics.Baseline), line.StartIndex, line.Length);
        return true;
    }
}
