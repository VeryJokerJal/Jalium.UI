using Jalium.UI.Documents;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

internal sealed partial class FlowDocumentPresentation
{
    private readonly record struct DocumentHeight(double Specified, double Minimum, double Maximum, double Insets,
        bool Definite = true, DocumentHeightSizing? Sizing = null)
    {
        internal bool Constrained => Sizing is not null || double.IsFinite(Specified) || Minimum > Insets || double.IsFinite(Maximum);
        internal double Used(double natural) => Sizing?.Used(natural) ?? Math.Max(Minimum, Math.Min(Maximum, double.IsFinite(Specified) ? Specified : natural));
        // An auto height remains indefinite even when min/max changes its used size.
        internal double ContentBasis => Definite && double.IsFinite(Specified) ? Math.Max(0, Used(0) - Insets) : double.NaN;
        internal double ContentMinimum => Math.Max(0, Minimum - Insets);
        internal double ColumnLimit => double.IsFinite(Specified) ? Math.Max(0, Used(0) - Insets) : Math.Max(ContentMinimum, Maximum - Insets);
        internal bool NeedsNatural => Sizing?.NeedsNatural == true;
    }

    private sealed record DocumentHeightSizing(DependencyObject Source, double Basis, double Insets, bool ContentBox, double Stretch,
        CssLayoutLength Preferred, CssLayoutLength Min, CssLayoutLength Max, bool ProbeBounds = false, double? IntrinsicNatural = null)
    {
        internal bool NeedsNatural => !Source.HasLocalOrAnimatedValue(FrameworkElement.HeightProperty) && DependsOnNatural(Preferred)
            || NaturalMinimum || NaturalMaximum;
        // The winning bound controls percentage behavior even when its expression
        // does not use size. A native bound replaces that CSS behavior as well.
        internal bool NaturalMinimum => !Source.HasLocalOrAnimatedValue(FrameworkElement.MinHeightProperty) && IntrinsicBehavior(Min);
        internal bool NaturalMaximum => !Source.HasLocalOrAnimatedValue(FrameworkElement.MaxHeightProperty) && IntrinsicBehavior(Max);
        private bool IntrinsicBehavior(CssLayoutLength value) => value.SizingBasis.IsSizing && !DocumentDefiniteSize(value, Basis);
        private bool DependsOnNatural(CssLayoutLength value) => value.IsSizing &&
            !DocumentDefiniteSize(value, Basis) && (value.Calculation is not { } calc ||
                calc.Expression.UsesSize || calc.From is not null);
        internal double Resolve(DependencyProperty property, CssLayoutLength length, double fallback, double natural)
        {
            if (property != FrameworkElement.HeightProperty && IntrinsicNatural is { } measured) natural = measured;
            var content = Math.Max(0, natural - Insets);
            return DocumentLimit(Source, property, length, Basis, fallback, Insets, ContentBox,
                (content, content), natural, Stretch);
        }
        internal double Used(double natural)
        {
            var preferred = Resolve(FrameworkElement.HeightProperty, Preferred, double.NaN, natural);
            var min = Math.Max(Insets, Resolve(FrameworkElement.MinHeightProperty, Min, 0, natural));
            var max = Math.Max(Insets, Resolve(FrameworkElement.MaxHeightProperty, Max, double.PositiveInfinity, natural));
            return Math.Max(min, Math.Min(max, double.IsFinite(preferred) ? preferred : natural));
        }
    }
    private readonly record struct DocumentHeightSpace(DependencyObject Source, double Start, double End);
    private readonly List<DocumentHeightSpace> _heightSpaces = [];
    private readonly Dictionary<DependencyObject, double> _documentNaturalHeightProbes = [];
    private bool _documentSizingConstraintsChanged;
    private bool _hasDocumentHeights;
    internal bool UsesVisualPagination => HasTables || _hasDocumentHeights;

    private DocumentHeight ResolveDocumentHeight(DependencyObject source, DocumentBox box, double containingHeight)
    {
        var state = CssNode.Get(source).CssLayout;
        var insets = box.Padding.Top + box.Padding.Bottom + box.Border.Top + box.Border.Bottom;
        var contentBox = state is { BoxSizing: CssBoxSizing.ContentBox };
        var preferred = DocumentSizeValue(source, FrameworkElement.HeightProperty, state?.Height ?? default);
        var min = DocumentSizeValue(source, FrameworkElement.MinHeightProperty, state?.MinHeight ?? default);
        var max = DocumentSizeValue(source, FrameworkElement.MaxHeightProperty, state?.MaxHeight ?? default);
        if (preferred.IsSizing || min.IsSizing || max.IsSizing)
        {
            var sizing = new DocumentHeightSizing(source, containingHeight, insets, contentBox,
                Math.Max(0, containingHeight - box.Margin.Top - box.Margin.Bottom), preferred, min, max);
            var native = source.HasLocalOrAnimatedValue(FrameworkElement.HeightProperty);
            var definite = native || !preferred.IsSet
                ? source.GetValue(FrameworkElement.HeightProperty) is double value && double.IsFinite(value)
                : DocumentDefiniteSize(preferred, containingHeight);
            // Sizing 3 §5.2.1: an intrinsic maximum keeps descendant percentages
            // indefinite. An intrinsic minimum may enlarge an otherwise definite
            // height, after which percentages use that final size.
            var maximumIsIntrinsic = sizing.NaturalMaximum;
            var probe = definite && sizing.NaturalMinimum && !maximumIsIntrinsic;
            var measured = probe && _documentNaturalHeightProbes.TryGetValue(source, out var natural) ? (double?)natural : null;
            sizing = sizing with { ProbeBounds = probe, IntrinsicNatural = measured };
            return new(maximumIsIntrinsic || probe && measured is null ? double.NaN : sizing.Resolve(FrameworkElement.HeightProperty, preferred, double.NaN, double.NaN),
                Math.Max(insets, sizing.Resolve(FrameworkElement.MinHeightProperty, min, 0, double.NaN)),
                Math.Max(insets, sizing.Resolve(FrameworkElement.MaxHeightProperty, max, double.PositiveInfinity, double.NaN)),
                insets, definite && !maximumIsIntrinsic && (!probe || measured is not null), sizing);
        }
        var specified = DocumentLimit(source, FrameworkElement.HeightProperty, state?.Height ?? default,
            containingHeight, double.NaN, insets, contentBox);
        var minimum = Math.Max(insets, DocumentLimit(source, FrameworkElement.MinHeightProperty, state?.MinHeight ?? default,
            containingHeight, 0, insets, contentBox));
        var maximum = Math.Max(insets, DocumentLimit(source, FrameworkElement.MaxHeightProperty, state?.MaxHeight ?? default,
            containingHeight, double.PositiveInfinity, insets, contentBox));
        return new(specified, minimum, maximum, insets);
    }

    private double ApplyDocumentHeight(DependencyObject source, DocumentHeight height, double top, double naturalBottom)
    {
        if (!height.Constrained) return naturalBottom;
        _hasDocumentHeights = true;
        CaptureDocumentNaturalHeight(source, height, Math.Max(0, naturalBottom - top));
        var bottom = top + height.Used(Math.Max(0, naturalBottom - top));
        if (bottom > naturalBottom + .00001) _heightSpaces.Add(new(source, naturalBottom, bottom));
        return bottom;
    }

    private void CaptureDocumentNaturalHeight(DependencyObject source, DocumentHeight height, double natural)
    {
        if (height.Sizing is { ProbeBounds: true, IntrinsicNatural: null } && !_documentNaturalHeightProbes.ContainsKey(source))
        {
            // Probe the existing formatting path with an indefinite own height,
            // then keep that intrinsic contribution when percentages are laid out
            // against the final constrained size. Do not feed overflow back into it.
            _documentNaturalHeightProbes[source] = natural;
            _documentSizingConstraintsChanged = true;
        }
    }

    private void PreserveColumnHeightInsets(DependencyObject source, DocumentHeight height, double top, double bottom, DocumentBox box)
    {
        if (!height.Constrained) return;
        _hasDocumentHeights = true;
        var contentTop = top + box.Padding.Top + box.Border.Top;
        var contentBottom = bottom - box.Padding.Bottom - box.Border.Bottom;
        if (contentTop > top + .00001) _heightSpaces.Add(new(source, top, contentTop));
        if (bottom > contentBottom + .00001) _heightSpaces.Add(new(source, contentBottom, bottom));
    }

    // Reserved box space is fragmentable layout, not a glyph or a discardable margin.
    // Subtract real line bands so visible overflow can share this space without
    // turning the whole reserved height into an indivisible text band.
    private IEnumerable<Line> DocumentHeightLines(IReadOnlyList<Line> text)
    {
        if (_heightSpaces.Count == 0) yield break;
        var spans = new List<(double Start, double End)>();
        foreach (var space in _heightSpaces.OrderBy(space => space.Start))
            if (spans.Count > 0 && space.Start <= spans[^1].End + .00001)
                spans[^1] = (spans[^1].Start, Math.Max(spans[^1].End, space.End));
            else spans.Add((space.Start, space.End));
        var ordered = text.OrderBy(line => line.Top).ToArray();
        var ink = new List<(double Start, double End)>();
        foreach (var line in ordered)
            if (ink.Count > 0 && line.Top <= ink[^1].End + .00001)
                ink[^1] = (ink[^1].Start, Math.Max(ink[^1].End, line.Top + line.Height));
            else ink.Add((line.Top, line.Top + line.Height));
        var inkIndex = 0;
        var textEnd = _document?.GetText().Length ?? 0;
        foreach (var span in spans)
        {
            var cursor = span.Start;
            while (inkIndex < ink.Count && ink[inkIndex].End <= cursor) inkIndex++;
            for (var index = inkIndex; index < ink.Count && ink[index].Start < span.End; index++)
            {
                var band = ink[index];
                if (band.Start > cursor + .00001) yield return Space(cursor, band.Start);
                cursor = Math.Max(cursor, band.End);
                if (cursor >= span.End) break;
            }
            if (span.End > cursor + .00001) yield return Space(cursor, span.End);
        }
        Line Space(double start, double end)
        {
            var lo = 0; var hi = ordered.Length;
            while (lo < hi)
            {
                var mid = lo + (hi - lo) / 2;
                if (ordered[mid].Top < end - .00001) lo = mid + 1; else hi = mid;
            }
            var offset = lo < ordered.Length ? ordered[lo].Start : textEnd;
            return new(offset, offset, start, end - start, false, 0, _layoutWidth, IsSpace: true);
        }
    }

    private void ProjectDocumentHeightSpaces(DocumentColumnScope scope, IReadOnlyList<DocumentColumnSlice> slices)
    {
        var original = _heightSpaces.Where(span => InColumnScope(span.Source, scope)).ToArray();
        _heightSpaces.RemoveAll(span => InColumnScope(span.Source, scope));
        foreach (var span in original)
            foreach (var slice in slices)
            {
                if (slice.Spanner is { } spanner && !Within(span.Source, spanner)) continue;
                var start = Math.Max(span.Start, slice.Start); var end = Math.Min(span.End, slice.End);
                if (end - start > .00001) _heightSpaces.Add(span with { Start = start + slice.Dy, End = end + slice.Dy });
            }
    }

    private void MoveDocumentHeightSpaces(DependencyObject source, double dy, Rect? clip, bool removed)
    {
        for (var index = _heightSpaces.Count - 1; index >= 0; index--)
        {
            var span = _heightSpaces[index]; if (!Within(span.Source, source)) continue;
            var start = span.Start + dy; var end = span.End + dy;
            if (clip is { } bounds) { start = Math.Max(start, bounds.Top); end = Math.Min(end, bounds.Bottom); }
            if (removed || end - start <= .00001) _heightSpaces.RemoveAt(index);
            else _heightSpaces[index] = span with { Start = start, End = end };
        }
    }
}
