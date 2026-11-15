using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private List<(Inline Element, int Start, int End)>? _cssInlineMetricRanges;
    private double _cssLineInkPadding;

    private bool UsesCssLineMetrics => IsVerticalTextLayout || UsesCssTextBody || _cssInlineWhitespace ||
        this is CssGeneratedText || CssComputedLineHeightValue.HasCssSource(this) || HasCssTextEmphasis || CssRubyBoxMetrics.IsFontArea(this);

    // CSS line boxes include signed half-leading. Ink is allowed to extend
    // outside them; padding/borders and native optical centering cannot turn
    // that ink into additional CSS line advance.
    private InlineLineMetrics GetCssInlineLineMetrics(int start, int length)
    {
        var metrics = TextMeasurement.GetFontMetrics(FontFamily.GetRenderingSource(this), FontSize,
            FontWeight.ToOpenTypeWeight(), FontStyle.ToOpenTypeStyle());
        if (CssInitialLetterProperties.Metrics(this) is { } initial)
        {
            var initialOverhang = Math.Max(0, Math.Max(metrics.Ascent - initial.Baseline, initial.Baseline + metrics.Descent - initial.Height));
            _cssLineInkPadding = Math.Max(_cssLineInkPadding, initialOverhang + 1);
            return new(initial.Height, initial.Baseline, metrics.Ascent + metrics.Descent);
        }
        var box = CssComputedLineHeightValue.LayoutBounds(this, FontSize, metrics);
        var above = box.Baseline; var below = box.Height - box.Baseline;
        var inkAbove = 0d; var inkBelow = 0d;
        void GlyphBounds(TextMeasurementCacheEntry measurement, in TextMetrics firstFont, DependencyObject source)
        {
            var text = measurement.FormattedText;
            var a = text?.Ascent > 0 ? text.Ascent : firstFont.Ascent;
            var d = text?.Descent > 0 ? text.Descent : firstFont.Descent;
            inkAbove = Math.Max(inkAbove, a); inkBelow = Math.Max(inkBelow, d);
            if (text is not null && CssFontFaces.HasMetricOverrides(text.FontFamily) && TextMeasurement.TryGetFontInkBounds(text, out var ink))
            {
                inkAbove = Math.Max(inkAbove, -ink.Top); inkBelow = Math.Max(inkBelow, ink.Bottom);
            }
            if (CssRubyBoxMetrics.IsFontArea(this))
            {
                above = Math.Max(above, a); below = Math.Max(below, d); return;
            }
            if (CssComputedLineHeightValue.Inherited(CssNode.Get(source)).Kind != CssLineHeightKind.Normal) return;
            var natural = Math.Max(text?.Height ?? 0, a + d);
            var b = a + (natural - a - d) / 2;
            above = Math.Max(above, b); below = Math.Max(below, natural - b);
        }
        if (length > 0 && _inlines is not { Count: > 0 })
            GlyphBounds(MeasureText(PaintSlice(start, length)), metrics, this);
        foreach (var range in CssInlineMetricRanges())
        {
            if (range.End > range.Start ? range.End <= start || range.Start >= start + length
                : range.Start < start || range.Start > start + length) continue;
            var inline = range.Element;
            var font = inline.FontFamily.GetRenderingSource(inline);
            var m = TextMeasurement.GetFontMetrics(font, inline.FontSize,
                inline.FontWeight.ToOpenTypeWeight(), inline.FontStyle.ToOpenTypeStyle());
            var b = CssComputedLineHeightValue.LayoutBounds(inline, inline.FontSize, m);
            above = Math.Max(above, b.Baseline); below = Math.Max(below, b.Height - b.Baseline);
            if (inline is Run run && range.End > range.Start && length > 0)
            {
                var first = Math.Max(start, range.Start); var last = Math.Min(start + length, range.End);
                if (last > first) GlyphBounds(MeasureInlineFragment(run, first, last - first), m, inline);
            }
        }
        EmphasisLineBounds(start,length,ref above,ref below);
        var height = Math.Max(0, above + below);
        var overhang = Math.Max(0, Math.Max(inkAbove - above, above + inkBelow - height));
        // Mirrored during layout: dirty registration can read this on a worker.
        if (overhang > .000001) _cssLineInkPadding = Math.Max(_cssLineInkPadding, overhang + 1);
        return new(height, above, inkAbove + inkBelow);
    }

    private List<(Inline Element, int Start, int End)> CssInlineMetricRanges()
    {
        if (_cssInlineMetricRanges is not null) return _cssInlineMetricRanges;
        var result = new List<(Inline, int, int)>(); var offset = 0;
        void Append(Inline inline)
        {
            var start = offset;
            if (inline is Run run) offset += run.Text.Length;
            else if (inline is Span span) foreach (var child in span.Inlines) Append(child);
            else if (inline is LineBreak) offset++;
            if(!CssDisplayProperties.SuppressesBoxTree(inline))result.Add((inline, start, offset));
        }
        if (_inlines is not null) foreach (var inline in _inlines) Append(inline);
        return _cssInlineMetricRanges = result;
    }

    private Point TextPaintOrigin(FormattedText text, in TextLayoutLine line, double x, double y)
    {
        if (!UsesCssLineMetrics) return new(x, y);
        if (!text.IsMeasured) TextMeasurement.MeasureText(text);
        return new(x, y + line.Baseline - text.Baseline);
    }

    private double TextPaintHeight(in TextLayoutLine line) => UsesCssLineMetrics
        ? Math.Max(line.Height, Math.Max(line.GlyphHeight, FontSize * 2)) : line.Height;

    internal override double CssTextInkPadding => Math.Max(base.CssTextInkPadding, System.Threading.Volatile.Read(ref _cssLineInkPadding));
}
