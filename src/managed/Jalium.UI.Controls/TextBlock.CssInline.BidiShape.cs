using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private bool HasSharedBidiSourceParts => !HasCssColumnText&&HasCssInlineFragments&&
        CssInlineFragmentValues.Any(fragment=>fragment.Shape is CssInlineBidiPart);
    private static IReadOnlyList<(double StartX,double EndX,double Baseline)> JoinSharedBidiSourceSegments(
        IEnumerable<(double StartX,double EndX,double Baseline)> segments)
    {
        var joined=new List<(double StartX,double EndX,double Baseline)>();
        foreach(var segment in segments.OrderBy(range=>range.Baseline).ThenBy(range=>Math.Min(range.StartX,range.EndX)))
        {
            double left=Math.Min(segment.StartX,segment.EndX),right=Math.Max(segment.StartX,segment.EndX);
            if(joined.Count>0&&Math.Abs(joined[^1].Baseline-segment.Baseline)<.000001&&left<=joined[^1].EndX+.000001)
                joined[^1]=(joined[^1].StartX,Math.Max(joined[^1].EndX,right),segment.Baseline);
            else joined.Add((left,right,segment.Baseline));
        }
        return joined;
    }
    private sealed record CssInlineBidiOrder(int Ordinal,bool Rtl);
    private sealed record CssInlineBidiMap(CssInlineFragment Original, int First, int End);
    private sealed record CssInlineBidiCell(int First, int End, double Left, double Right, bool Rtl, CssInlineBidiMap Map, SharedShapeExterior[] Before, SharedShapeExterior[] After);
    private sealed record CssInlineBidiNative(CssInlineBidiPlan Plan, CssBidiProjection Projection, NativeTextParagraph Paragraph,
        CssInlineBidiRequest? Request=null,IReadOnlyDictionary<int,CssInlineBidiExterior>? Exterior=null,bool Expanded=false,IReadOnlyDictionary<int,(double Left,double Right)>? AtomicSpacing=null,
        IReadOnlyDictionary<int,CssInlineBidiOrder>? Ordering=null);
    private sealed record CssInlineBidiPart(CssInlineBidiNative Native, CssInlineBidiMap Map,
        int First, int End, double Left, double Right, SharedShapeExterior[] Before, SharedShapeExterior[] After) : CssInlineShape
    {
        internal uint Row => Native.Plan.Row(Native, First);
        internal override double BeforeOutside(DependencyObject owner)=>Before.Where(gap=>!ContainsAutospaceOwner(owner,gap.Owner)).Sum(gap=>gap.Width);
        internal override double AfterOutside(DependencyObject owner)=>After.Where(gap=>!ContainsAutospaceOwner(owner,gap.Owner)).Sum(gap=>gap.Width);
        internal int SourceStart => Native.Plan.SourceStartPosition(Map.Original.Source, Math.Max(First, Map.First));
        internal int SourceEnd => Native.Plan.SourceEndPosition(Map.Original.Source, Math.Min(End, Map.End));
        internal override double InkOverhang
        {
            get
            {
                var range = Native.Projection.NativeRange(First, End - First);
                var bounds = Native.Paragraph.SourceBounds(Row, range.Start, range.Length);
                return bounds is { Width: > 0 } ink ? Math.Max(0, Math.Max(Left - ink.X, ink.X + ink.Width - Right))
                    : bounds is null ? Native.Paragraph.Lines[Row].Metrics.Line.Width : 0;
            }
        }
        internal override double Boundary(int source)
        {
            string sourceText = Map.Original.Source.Text._displayText;
            if (source >= SourceStart && source < SourceEnd && source < sourceText.Length)
            {
                int next = Math.Min(SourceEnd, Map.Original.Source.Text.CombinedUnitAt(source)?.End ??
                    GraphemeClusters.NextBoundary(sourceText, source));
                if (next > source && Selection(source, next - source) is { Count: > 0 } rects && rects[0].Width > 0)
                {
                    bool rtl = Native.Ordering?.GetValueOrDefault(Native.Plan.Position(Map.Original.Source, source))?.Rtl == true;
                    return rtl ? rects[^1].X + rects[^1].Width : rects[0].X;
                }
            }
            else if (source >= SourceEnd && source > SourceStart && source <= sourceText.Length)
            {
                int prev = Math.Max(SourceStart, Map.Original.Source.Text.CombinedUnitAt(source - 1)?.Start ??
                    GraphemeClusters.PreviousBoundary(sourceText, source));
                if (source > prev && Selection(prev, source - prev) is { Count: > 0 } rects && rects[0].Width > 0)
                {
                    bool rtl = Native.Ordering?.GetValueOrDefault(Native.Plan.Position(Map.Original.Source, prev))?.Rtl == true;
                    return rtl ? rects[0].X : rects[^1].X + rects[^1].Width;
                }
            }
            int raw = Math.Clamp(Native.Plan.Position(Map.Original.Source, source), First, End);
            double edge=Native.Paragraph.Caret(Row, Native.Projection.NativePosition(raw, source >= SourceEnd), source >= SourceEnd).X-Left;
            return Before.Length+After.Length>0?Math.Clamp(edge,0,Math.Max(0,Right-Left)):edge;
        }
        internal override IReadOnlyList<TextRangeMetrics> Selection(int start, int length)
        {
            int first = Math.Max(First, Native.Plan.Position(Map.Original.Source, start));
            int end = Math.Min(End, Native.Plan.Position(Map.Original.Source, start + length));
            if (end <= first) return [];
            var range = Native.Projection.NativeRange(first, end - first);
            return Native.Paragraph.Selection(Row, range.Start, range.Length).Select(r => new TextRangeMetrics
            {
                TextPosition = (uint)start, Length = (uint)length, X = (float)(Math.Max(Left,r.X) - Left),
                Width = (float)Math.Max(0,Math.Min(Right,r.X+r.Width)-Math.Max(Left,r.X)), Height = (float)Map.Original.Line.Height
            }).ToArray();
        }
        internal override int Hit(double x)
        {
            string sourceText = Map.Original.Source.Text._displayText;
            var boundaries = Map.Original.Source.Text._combinedTextBoundaries ?? GraphemeClusters.GetBoundaries(sourceText);
            for (int i = 0; i + 1 < boundaries.Length; i++)
            {
                int c = boundaries[i], next = boundaries[i + 1];
                if (next <= SourceStart || c >= SourceEnd) continue;
                foreach (var r in Selection(c, next - c))
                {
                    if (r.Width > 0 && x >= r.X && x <= r.X + r.Width)
                    {
                        bool rtl = Native.Ordering?.GetValueOrDefault(Native.Plan.Position(Map.Original.Source, c))?.Rtl == true;
                        return Math.Abs(x - r.X) <= Math.Abs(r.X + r.Width - x) ? (rtl ? next : c) : (rtl ? c : next);
                    }
                }
            }
            // Prefer the two boundaries of the glyph under the pointer.
            // Zero-advance tabs and bidi controls can share its trailing caret.
            foreach (var input in Native.Plan.Inputs.Where(input => ReferenceEquals(input.Unit.Source, Map.Original.Source) &&
                input.End > First && input.First < End))
            {
                var range = Native.Projection.NativeRange(input.First, input.End - input.First);
                double a = Native.Paragraph.Caret(Row, range.Start, false).X - Left;
                double b = Native.Paragraph.Caret(Row, range.Start + range.Length, true).X - Left;
                a = Math.Clamp(a, 0, Math.Max(0, Right - Left));
                b = Math.Clamp(b, 0, Math.Max(0, Right - Left));
                if (x > Math.Min(a, b) && x < Math.Max(a, b))
                    return Math.Abs(x - a) < Math.Abs(x - b) ? input.Unit.Start : input.Unit.End;
            }
            var candidates = Native.Plan.Inputs.Where(input => ReferenceEquals(input.Unit.Source, Map.Original.Source) &&
                input.End > First && input.First < End).SelectMany(input => new[] { input.Unit.Start, input.Unit.End }).Distinct();
            int best = SourceStart; double distance = double.PositiveInfinity;
            foreach(int position in candidates)
            {
                double next = Math.Abs(x - Boundary(position));
                if (next < distance || next == distance && position > best) { distance = next; best = position; }
            }
            return best;
        }
    }

    private bool DrawInlineBidi(DrawingContext context, in TextLayoutLine line, CssInlineBidiPart part, double x, double y)
    {
        if (part.Right <= part.Left) return true;
        var native = part.Native.Paragraph.Lines[part.Row];
        double baseline = line.Baseline, height = line.Height, paintHeight = TextPaintHeight(line);
        var filters = new List<uint>(); byte visibility = 0;
        if (DocumentWritingToolsOwner is { } owner && MacOSWritingToolsPresentation.TryGetGlyphRanges(owner, out var preview, out var requested))
        {
            visibility = preview ? (byte)1 : (byte)2;
            foreach(var input in part.Native.Plan.Inputs.Where(input => !input.Unit.Source.Atomic))
                foreach(var request in requested)
                    foreach(var range in input.Unit.Source.Text.DocumentWritingToolsRanges(request.Start, request.Length))
                    {
                        int first = Math.Max(input.Unit.Start, range.Start), end = Math.Min(input.Unit.End, range.Start + range.Length);
                        if (end <= first) continue;
                        int rawFirst = part.Native.Plan.Position(input.Unit.Source, first), rawEnd = part.Native.Plan.Position(input.Unit.Source, end);
                        var projected = part.Native.Projection.NativeRange(rawFirst, rawEnd - rawFirst);
                        filters.Add((uint)projected.Start); filters.Add((uint)projected.Length);
                    }
        }
        int cursor = part.SourceStart, endSource = part.SourceEnd;
        foreach(var range in GetInlineTextRanges())
        {
            int first = Math.Max(cursor, range.Start), last = Math.Min(endSource, range.End);
            if (last <= first) continue;
            if (first > cursor) Paint(cursor, first, Foreground);
            Paint(first, last, range.Run.Foreground ?? Foreground); cursor = last;
        }
        if (cursor < endSource) Paint(cursor, endSource, Foreground);
        return true;
        void Paint(int first, int last, Brush? brush)
        {
            if (brush is null || last <= first) return;
            int rawFirst = Math.Max(part.First, part.Native.Plan.Position(part.Map.Original.Source, first));
            int rawEnd = Math.Min(part.End, part.Native.Plan.Position(part.Map.Original.Source, last));
            if (rawEnd <= rawFirst) return;
            var range = part.Native.Projection.NativeRange(rawFirst, rawEnd - rawFirst);
            int brushFirst=part.Map.Original.Line.StartIndex,brushLast=brushFirst+part.Map.Original.Line.Length;
            foreach(var styled in GetInlineTextRanges())
            {
                if(first>=styled.End){brushFirst=Math.Max(brushFirst,styled.End);continue;}
                if(first>=styled.Start){brushFirst=Math.Max(brushFirst,styled.Start);brushLast=Math.Min(brushLast,styled.End);}
                else brushLast=Math.Min(brushLast,styled.Start);
                break;
            }
            int brushRawFirst=Math.Max(part.Map.First,part.Native.Plan.Position(part.Map.Original.Source,brushFirst)),brushRawLast=Math.Min(part.Map.End,part.Native.Plan.Position(part.Map.Original.Source,brushLast));
            var brushRange=part.Native.Projection.NativeRange(brushRawFirst,brushRawLast-brushRawFirst);
            int paintFirst = _paintOffsets?[first] ?? first; var local = new List<uint>();
            if (visibility != 0)
                foreach(var request in MacOSWritingToolsPresentation.TryGetGlyphRanges(DocumentWritingToolsOwner!, out _, out var requestedRanges) ? requestedRanges : [])
                    foreach(var selected in DocumentWritingToolsRanges(request.Start, request.Length))
                    {
                        int a = Math.Max(first, selected.Start), b = Math.Min(last, selected.Start + selected.Length);
                        if (b <= a) continue;
                        local.Add((uint)((_paintOffsets?[a] ?? a) - paintFirst));
                        local.Add((uint)((_paintOffsets?[b] ?? b) - (_paintOffsets?[a] ?? a)));
                    }
            var paint = native.ForSource(range.Start, range.Length,
                brush is SolidColorBrush solid ? solid.Color : Colors.White, brush.Opacity,
                visibility == 0 ? null : filters.ToArray(), visibility).WithBrushSourceRange(brushRange.Start, brushRange.Length)
                .WithBrushLineBox(native.Metrics.Baseline - baseline, height);
            if (brush is GradientBrush && part.Native.Exterior is { Count: > 0 } exterior)
            {
                // Ancestor-owned autospace lies outside the source's brush box,
                // just as it lies outside its background and selection box.
                var boundaries = GraphemeClusters.GetBoundaries(part.Native.Projection.Source);
                double left = double.PositiveInfinity, right = double.NegativeInfinity;
                for (int i = 0; i + 1 < boundaries.Length; i++)
                {
                    int a = boundaries[i], b = boundaries[i + 1];
                    if (a < brushRawFirst || b > brushRawLast || part.Native.Plan.Row(part.Native, a) != part.Row) continue;
                    var cellRange = part.Native.Projection.NativeRange(a, b - a);
                    double start = part.Native.Paragraph.Caret(part.Row, cellRange.Start, false).X;
                    double end = part.Native.Paragraph.Caret(part.Row, cellRange.Start + cellRange.Length, true).X;
                    var trim = exterior.GetValueOrDefault(a);
                    double l = Math.Min(start, end) + (trim?.Before.Sum(gap => gap.Width) ?? 0);
                    double r = Math.Max(start, end) - (trim?.After.Sum(gap => gap.Width) ?? 0);
                    if (r <= l + .000001) continue;
                    left = Math.Min(left, l); right = Math.Max(right, r);
                }
                if (double.IsFinite(left) && double.IsFinite(right)) paint = paint.WithBrushHorizontalBox(left, right - left);
            }
            var text = new FormattedText(part.Native.Projection.Text.Substring(range.Start, range.Length), FontFamily.GetRenderingSource(this), FontSize)
            {
                Foreground = brush, PlatformTextLine = paint,
                GlyphVisibility = visibility, GlyphRanges = visibility == 0 ? null : local.ToArray(),
                MaxTextWidth = Math.Max(1, native.Metrics.Line.Width), MaxTextHeight = Math.Max(1, paintHeight),
            };
            text.ApplyTextOptionsFrom(this);
            context.DrawText(text, new(x - part.Left, y + baseline - native.Metrics.Baseline));
        }
    }
}
