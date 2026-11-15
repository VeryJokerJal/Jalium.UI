using Jalium.UI.Interop;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private Brush? _nativeSelectionTextBrush;
    internal Brush? NativeSelectionTextBrush
    {
        get => this is InlineSourceText source ? source.SourceOwner.NativeSelectionTextBrush : _nativeSelectionTextBrush;
        set => _nativeSelectionTextBrush = value;
    }

    internal bool HasCssSelectionStyle => CssSelection.For(AutospaceStyleSource(this)) is { HasPaint: true } ||
        GetInlineTextRanges().Any(range => CssSelection.For(range.Run) is { HasPaint: true });

    internal bool HasCssSelectionShadows => CssTextShadowProperties.Value(this)?.HasEffect == true ||
        CssSelection.For(AutospaceStyleSource(this)) is { } own && CssTextShadowProperties.Value(own.Values)?.HasEffect == true ||
        GetInlineTextRanges().Any(range => CssSelection.For(range.Run) is { } style && CssTextShadowProperties.Value(style.Values)?.HasEffect == true);

    private bool DrawCssSelection(DrawingContext context)
    {
        if (_selectionLength <= 0 || (!HasCssSelectionStyle && NativeSelectionTextBrush is null)) return false;
        var regions = SelectionPaintRegions(context);
        if (regions.Count == 0) return false;
        CssSelectionPainter.Paint(context, GetRenderClip(), regions, ResolveSelectionBrush(), NativeSelectionTextBrush, DrawTextLines,
            (paint, region) => CssSelectionPainter.Decorations(paint, region, this), backgrounds: !CssInlineSelectionPainter.Suppressed(this),
            overlay: CssInlineSelectionPainter.Overlay(this), shadows: !CssInlineSelectionPainter.ShadowsSuppressed(this), lineClips: true,
            wholeInk: WholeSelectionInk());
        return true;
    }

    private Geometry? WholeSelectionInk() => _selectionStart == 0 && _selectionLength >= _displayText.Length ? GetRenderClip() : null;

    private List<CssSelectionPainter.Region> SelectionPaintRegions(DrawingContext context)
    {
        var regions = new List<CssSelectionPainter.Region>();
        var end = Math.Min(_displayText.Length, _selectionStart + _selectionLength);
        var cursor = _selectionStart;
        var baselines = new List<(double Top, double Height, double Baseline, Rect PhysicalBounds)>();
        var lineY = GetVerticalContentOffset(GetLineHeight());
        foreach (var line in _layoutLines)
        {
            var top = TextLineTop(line, lineY);
            baselines.Add((top, line.Height, top + line.Baseline,
                TextWritingRect(new Rect(0, top, TextContentSize.Width, line.Height), top, line.Height)));
            lineY += line.Height;
        }
        void Add(int first, int last, DependencyObject source, Brush? foreground)
        {
            if (last <= first) return;
            var style = CssSelection.For(source);
            var capture = new CssSelectionPainter.GeometryCapture(context);
            DrawSelection(capture, first, last - first, Brushes.Black);
            foreach (var rect in capture.Rectangles)
            {
                var row = baselines.Count == 0 ? default : baselines.MinBy(line => IsVerticalTextLayout
                    ? Math.Abs(line.PhysicalBounds.Left + line.PhysicalBounds.Width / 2 - rect.Left - rect.Width / 2)
                    : Math.Abs(line.Top - rect.Top));
                double? baseline = baselines.Count == 0 ? null : row.Baseline;
                var matrix = TextWritingMatrix(row.Top, row.Height);
                var inverse = matrix; inverse.Invert();
                var logical = IsVerticalTextLayout ? TextMatrixBounds(inverse, rect) : rect;
                var ink = rect;
                if (baseline is { } inkBaseline)
                {
                    var fontSource = source as TextElement;
                    var size = fontSource?.FontSize ?? FontSize;
                    var family = fontSource?.FontFamily ?? FontFamily;
                    var metrics = TextMeasurement.GetFontMetrics(family.GetRenderingSource(source), size,
                        (fontSource?.FontWeight ?? FontWeight).ToOpenTypeWeight(), (fontSource?.FontStyle ?? FontStyle).ToOpenTypeStyle());
                    var top = Math.Min(logical.Top, inkBaseline - metrics.Ascent);
                    var bottom = Math.Max(logical.Bottom, inkBaseline + metrics.Descent);
                    ink = TextMatrixBounds(matrix, new Rect(logical.X, top, logical.Width, bottom - top));
                }
                if (_selectionStart == 0 && end >= _displayText.Length && InitialLetterInkInText() is { } initialInk)
                {
                    var pixel = 1 / Math.Max(.01, DpiScale.DpiScaleX);
                    ink = Rect.Union(ink, new Rect(initialInk.X - pixel, initialInk.Y - pixel, initialInk.Width + 2 * pixel, initialInk.Height + 2 * pixel));
                }
                regions.Add(new(rect, style, foreground, baseline, ink,
                    TextTop: IsVerticalTextLayout ? logical.Top : null,
                    WritingTransform: IsVerticalTextLayout ? matrix : null));
            }
        }
        var own = AutospaceStyleSource(this);
        foreach (var range in GetInlineTextRanges())
        {
            var first = Math.Max(_selectionStart, range.Start); var last = Math.Min(end, range.End);
            if (last <= first) continue;
            Add(cursor, first, own, Foreground);
            Add(first, last, range.Run, range.Run.Foreground ?? Foreground); cursor = last;
        }
        Add(cursor, end, own, Foreground);
        return regions;
    }

}
