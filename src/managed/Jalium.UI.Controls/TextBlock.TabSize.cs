using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private bool _hasPreservedTabs;

    private bool ContainsPreservedTab(int start, int length)
        => _hasPreservedTabs && length > 0 &&
            _visualText.AsSpan(start, length).Contains('\t');

    private double MeasureTabbedInlineRange(int start, int length,
        bool includeTrailingWhitespace, double tabOrigin)
    {
        var end = start + length;
        if (!includeTrailingWhitespace && !_countTrailingWhitespace)
            while (end > start && IsBreakableWhitespace(_visualText[end - 1])) end--;
        if (end <= start) return 0;

        var position = tabOrigin;
        var cursor = start;
        for (var tab = _visualText.IndexOf('\t', cursor, end - cursor);
             tab >= 0;
             tab = cursor < end ? _visualText.IndexOf('\t', cursor, end - cursor) : -1)
        {
            position += MeasureInlineRangeWithoutTabs(cursor, tab - cursor,
                includeTrailingWhitespace: true, forceFragmented: true);
            position += LetterSpacingAtBoundary(tab, start, end);
            position += GetPreservedTabAdvance(position, tab);
            cursor = tab + 1;
            position += LetterSpacingAtBoundary(cursor, start, end);
        }
        position += MeasureInlineRangeWithoutTabs(cursor, end - cursor,
            includeTrailingWhitespace: true, forceFragmented: true);
        return Math.Max(0, position - tabOrigin);
    }

    private double GetPreservedTabAdvance(double offsetFromContentEdge, int sourceIndex, FrameworkElement? container = null)
    {
        var size = GetTabSizeAt(sourceIndex);
        var step = size.IsLength ? size.Value : size.Value * GetTabSpaceAdvance(container);
        if (!double.IsFinite(step) || step <= 0) return 0;

        var next = (Math.Floor(offsetFromContentEdge / step) + 1) * step;
        var advance = next - offsetFromContentEdge;
        var run = InlineRunAt(GetInlineTextRanges(), sourceIndex);
        var zero = run is null ? MeasureText("0") : MeasureRunText(run, "0");
        var halfCh = Math.Max(0, zero.WidthIncludingTrailingWhitespace / 2);
        // The threshold skips one stop. For very small tab sizes the second
        // stop can still be closer than 0.5ch; do not keep skipping stops.
        if (advance < halfCh)
            advance += step;
        return double.IsFinite(advance) ? Math.Max(0, advance) : 0;
    }

    private CssTabSize GetTabSizeAt(int sourceIndex)
    {
        foreach (var range in GetInlineTextRanges())
            if (range.Start <= sourceIndex && sourceIndex < range.End)
                return (CssTabSize)range.Run.GetValue(CssFlowProperties.TabSizeProperty)!;
        return (CssTabSize)GetValue(CssFlowProperties.TabSizeProperty)!;
    }

    private double GetTabSpaceAdvance(FrameworkElement? container = null)
    {
        // A numeric tab-size uses its block container's space, including that
        // container's spacing. The inline source may have a different font.
        var block = container ?? _cssInlineParagraph?.Owner ?? this;
        var fontSize = (double)block.GetValue(TextElement.FontSizeProperty)!;
        double natural;
        if (block is TextBlock text) natural = text.MeasureText(" ").WidthIncludingTrailingWhitespace;
        else
        {
            var family = (FontFamily)block.GetValue(TextElement.FontFamilyProperty)!;
            var formatted = new FormattedText(" ", family.GetRenderingSource(block), fontSize)
            {
                FontWeight = ((FontWeight)block.GetValue(TextElement.FontWeightProperty)!).ToOpenTypeWeight(),
                FontStyle = ((FontStyle)block.GetValue(TextElement.FontStyleProperty)!).ToOpenTypeStyle(),
                FontStretch = ((FontStretch)block.GetValue(TextElement.FontStretchProperty)!).ToOpenTypeStretch(),
            };
            natural = TextMeasurement.MeasureText(formatted) && formatted.IsMeasured
                ? formatted.WidthIncludingTrailingWhitespace : EstimateTextWidth(" ", fontSize);
        }
        var word = ((CssLayoutLength)block.GetValue(CssFlowProperties.WordSpacingProperty)!)
            .Resolve(fontSize, 0);
        var letter = CssTextSpacing.Letter(block);
        return Math.Max(0, natural + (double.IsFinite(word) ? word : 0) +
            (double.IsFinite(letter) ? letter : 0));
    }

    private void DrawTabbedInlineFragment(DrawingContext context, Run? run,
        int start, int length, ref double x, double lineY, in TextLayoutLine line)
    {
        var end = start + length;
        var cursor = start;
        // Stops belong to the block content box, so text alignment must not
        // shift their ruler along with the line's first glyph.
        var tabStopsOriginX = InlineFragment(line) is { } fragment
            ? fragment.TextX - fragment.TabOrigin : GetTextInsets().Left;
        for (var tab = _visualText.IndexOf('\t', cursor, end - cursor);
             tab >= 0;
             tab = cursor < end ? _visualText.IndexOf('\t', cursor, end - cursor) : -1)
        {
            DrawStyledFragmentRaw(context, run, cursor, tab - cursor, ref x, lineY, line);
            x += GetPreservedTabAdvance(x - tabStopsOriginX, tab);
            cursor = tab + 1;
        }
        DrawStyledFragmentRaw(context, run, cursor, end - cursor, ref x, lineY, line);
    }
}
