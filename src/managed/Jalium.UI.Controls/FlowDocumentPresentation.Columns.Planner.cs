using Jalium.UI.Documents;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

internal sealed partial class FlowDocumentPresentation
{
    private sealed class DocumentColumnPlan
    {
        internal readonly List<DocumentColumnSlice> Slices = [];
        internal readonly List<ColumnPage> Pages = [];
        internal readonly List<Rect> Rules = [];
        internal readonly List<DocumentColumnSlot> Slots = [];
        internal readonly List<(double Start, double End)> ReservedSpace = [];
        internal readonly List<Block> DemotedSpanners = [];
        internal double DefaultCapacity { get; private set; }
        internal readonly List<(double At, CssFragmentBreak Value)> ForcedPages = [];
        internal double ExtentHeight { get; private set; }
        internal double UsedHeight { get; private set; }
        internal double NaturalHeight { get; private set; }
        internal DocumentColumnPlan(CssDocumentColumns columns, IReadOnlyList<Line> lines,
            IReadOnlyList<FragmentBox> boxes, IReadOnlyList<ColumnSpanner> spanners, double height, double end, double firstCapacity = double.NaN,
            IReadOnlyList<(double At, CssFragmentBreak Value)>? forcedPages = null, IReadOnlyList<double>? capacities = null,
            IReadOnlyList<(double Start, double End)>? reservedSpace = null, DocumentHeight? boxHeight = null,
            IReadOnlyList<RepeatableTableInfo>? repeatableTables = null)
        {
            Build(columns, lines, boxes, spanners, height, end, firstCapacity, forcedPages, capacities, reservedSpace, boxHeight, repeatableTables);
        }
        private void Build(CssDocumentColumns columns, IReadOnlyList<Line> _lines,
            IReadOnlyList<FragmentBox> _fragmentBoxes, IReadOnlyList<ColumnSpanner> _columnSpanners,
            double _paginationColumnHeight, double end, double firstCapacity, IReadOnlyList<(double At, CssFragmentBreak Value)>? forcedPages,
            IReadOnlyList<double>? capacities, IReadOnlyList<(double Start, double End)>? reservedSpace, DocumentHeight? boxHeight,
            IReadOnlyList<RepeatableTableInfo>? repeatableTables)
        {

            const double epsilon = .00001;
            var breaks = new CssColumnFragmentation();
            foreach (var line in _lines.Where(line => line.Height > 0 && !line.IsSpace)) breaks.Keeps.Add(new(line.Top, line.Top + line.Height));
            var pageBreaks = new SortedDictionary<double, CssFragmentBreak>();
            var pageAvoids = new List<(double Start, double End)>();
            void Boundary(double at, CssFragmentBreak value)
            {
                if (value == CssFragmentBreak.Column || CssFragmentationProperties.ForcesPage(value)) breaks.Forced.Add(at);
                if (CssFragmentationProperties.ForcesPage(value)) pageBreaks[at] = value;
                if (value is CssFragmentBreak.Avoid or CssFragmentBreak.AvoidColumn or CssFragmentBreak.AvoidPage)
                {
                    var prior = breaks.Keeps.Where(keep => !keep.Soft && keep.End <= at + epsilon).OrderBy(keep => keep.End).LastOrDefault();
                    var next = breaks.Keeps.Where(keep => !keep.Soft && keep.Start >= at - epsilon).OrderBy(keep => keep.Start).FirstOrDefault();
                    if (prior.End > prior.Start && next.End > next.Start)
                    {
                        if (value != CssFragmentBreak.AvoidPage) breaks.Keeps.Add(new(prior.Start, next.End, true));
                        if (value != CssFragmentBreak.AvoidColumn) pageAvoids.Add((prior.Start, next.End));
                    }
                }
            }
            foreach (var box in _fragmentBoxes)
            {
                var own = ReferenceEquals(box.Source, columns.Source);
                if (!own && box.AnonymousOwner is null)
                {
                    Boundary(box.MarginTop, CssFragmentationProperties.Before(box.Source));
                    Boundary(box.Bottom, CssFragmentationProperties.After(box.Source));
                }
                var inside = own || box.AnonymousOwner is not null ? CssFragmentBreak.Auto : CssFragmentationProperties.Inside(box.Source);
                if (inside is CssFragmentBreak.Avoid or CssFragmentBreak.AvoidColumn) breaks.Keeps.Add(new(box.BorderTop, box.Bottom, true));
                if (inside is CssFragmentBreak.Avoid or CssFragmentBreak.AvoidPage) pageAvoids.Add((box.BorderTop, box.Bottom));
                if (box.Source is Paragraph paragraph && (own || !HasOwnDocumentColumns(paragraph)) && box.EndLine > box.FirstLine)
                {
                    var indices = box.LineBands ?? Enumerable.Range(box.FirstLine, box.EndLine - box.FirstLine).ToArray();
                    var lines = indices.Where(index => index < _lines.Count && !_lines[index].IsSpace).Select(index => _lines[index]).ToArray();
                    breaks.Paragraphs.Add(new(lines.Select(line => line.Top).ToArray(), lines.Select(line => line.Top + line.Height).ToArray(),
                        box.AnonymousOwner is { } parent ? (int)parent.GetValue(CssFragmentationProperties.OrphansProperty)! : CssFragmentationProperties.Orphans(paragraph),
                        box.AnonymousOwner is { } owner ? (int)owner.GetValue(CssFragmentationProperties.WidowsProperty)! : CssFragmentationProperties.Widows(paragraph)));
                }
            }
            if (forcedPages is not null) foreach (var item in forcedPages) Boundary(item.At, item.Value);
            var paginated = double.IsFinite(_paginationColumnHeight);
            var nested = columns.Source is not FlowDocument;
            var pageHeight = paginated ? Math.Max(1, _paginationColumnHeight) : double.PositiveInfinity;
            DefaultCapacity = pageHeight;
            var ownHeight = boxHeight is { Constrained: true };
            var ownLimit = ownHeight ? boxHeight!.Value.ColumnLimit : double.PositiveInfinity;
            var specified = ownHeight ? boxHeight!.Value.ContentBasis : double.NaN;
            // Ancestor columns can differ at the first fragment, after a spanner,
            // and on the balanced last page. Beyond known fragments use a fresh page.
            double Limit(int index) => capacities is { Count: > 0 } && index < capacities.Count ? Math.Max(1, capacities[index])
                : index == 0 && double.IsFinite(firstCapacity) ? Math.Max(1, Math.Min(firstCapacity, pageHeight)) : pageHeight;
            var page = 0; var row = 0d; var usedHeight = 0d; var group = 0;
            if (nested && paginated && pageBreaks.TryGetValue(0, out var firstPageBreak)) ForcedPages.Add((0, firstPageBreak));
            bool? Right(CssFragmentBreak value) => value switch
            {
                CssFragmentBreak.Left => false, CssFragmentBreak.Right => true,
                CssFragmentBreak.Recto => columns.Direction != FlowDirection.RightToLeft,
                CssFragmentBreak.Verso => columns.Direction == FlowDirection.RightToLeft, _ => null,
            };
            var firstRight = pageBreaks.TryGetValue(0, out var firstSide) ? Right(firstSide) ?? columns.Direction != FlowDirection.RightToLeft
                : columns.Direction != FlowDirection.RightToLeft;
            void EnsurePage()
            {
                if (!paginated) return;
                while (Pages.Count <= page)
                {
                    var top = Pages.Count == 0 ? 0 : Pages[^1].Bottom; var capacity = Limit(Pages.Count);
                    Pages.Add(new(top, top + capacity)); Slots.Add(new(top, capacity, 0));
                }
            }
            double AtY() { EnsurePage(); return paginated ? Pages[page].Top + row : row; }
            void NextPage(CssFragmentBreak side = CssFragmentBreak.Auto)
            {
                if (!paginated) return;
                EnsurePage(); page++; row = 0;
                if (!nested && Right(side) is { } desired && (firstRight != ((page & 1) != 0)) != desired)
                {
                    EnsurePage(); Pages[page] = Pages[page] with { Blank = true }; page++;
                }
                EnsurePage();
                if (nested && CssFragmentationProperties.ForcesPage(side)) ForcedPages.Add((Pages[page].Top, side));
            }
            void Emit(IReadOnlyList<(double Start, double End)> fragments, double height, Block? spanner = null, bool balanced = false)
            {
                EnsurePage(); var top = AtY(); var currentGroup = group++;
                var maxColumnExtent = 0d;
                for (var index = 0; index < fragments.Count; index++)
                {
                    var fragment = fragments[index];
                    var colLeft = spanner is null ? columns.Left(index) : 0;
                    var colWidth = spanner is null ? columns.Width : columns.Available;
                    var headerRepeatHeight = 0d;
                    var footerRepeatHeight = 0d;
                    RepeatableTableInfo activeRepeat = default;
                    if (spanner is null && repeatableTables is { Count: > 0 })
                    {
                        foreach (var rt in repeatableTables)
                        {
                            var hCap = double.IsFinite(height) && height > 0 ? height : (fragment.End - fragment.Start) * 4;
                            var hHeight = rt.Header is not null && rt.HeaderBounds.Height > 0 && rt.HeaderBounds.Height <= hCap * 0.25 + .01
                                ? rt.HeaderBounds.Height : 0;
                            var fHeight = rt.Footer is not null && rt.FooterBounds.Height > 0 && rt.FooterBounds.Height <= hCap * 0.25 + .01
                                ? rt.FooterBounds.Height : 0;
                            if (hHeight <= 0 && fHeight <= 0) continue;
                            if (fragment.Start >= rt.BodyTop - epsilon && fragment.Start < rt.BodyBottom - epsilon && hHeight > 0 && rt.HeaderBounds.Bottom <= fragment.Start + epsilon)
                            {
                                headerRepeatHeight = hHeight;
                                activeRepeat = rt;
                            }
                            if (fragment.End > rt.BodyTop + epsilon && fragment.Start < rt.BodyBottom - epsilon && fragment.End < rt.FooterBounds.Bottom - epsilon && fHeight > 0)
                            {
                                footerRepeatHeight = fHeight;
                                activeRepeat = rt;
                            }
                            if (headerRepeatHeight > 0 || footerRepeatHeight > 0) break;
                        }
                    }
                    if (headerRepeatHeight > 0 && activeRepeat.Header is not null)
                    {
                        Slices.Add(new(activeRepeat.HeaderBounds.Top, activeRepeat.HeaderBounds.Bottom,
                            new(colLeft, top, colWidth, headerRepeatHeight), null, 0, height, currentGroup, balanced,
                            IsRepeat: true, RepeatGroup: activeRepeat.Header));
                    }
                    var bodyHeight = fragment.End - fragment.Start;
                    Slices.Add(new(fragment.Start, fragment.End, new(colLeft, top + headerRepeatHeight,
                        colWidth, bodyHeight), spanner, 0, height, currentGroup, balanced));
                    if (footerRepeatHeight > 0 && activeRepeat.Footer is not null)
                    {
                        Slices.Add(new(activeRepeat.FooterBounds.Top, activeRepeat.FooterBounds.Bottom,
                            new(colLeft, top + headerRepeatHeight + bodyHeight, colWidth, footerRepeatHeight), null, 0, height, currentGroup, balanced,
                            IsRepeat: true, RepeatGroup: activeRepeat.Footer));
                    }
                    var colExtent = headerRepeatHeight + bodyHeight + footerRepeatHeight;
                    maxColumnExtent = Math.Max(maxColumnExtent, colExtent);
                    if (spanner is null && index > 0)
                    {
                        var x = columns.Direction == FlowDirection.RightToLeft ? columns.Left(index) + columns.Width + columns.Gap / 2 : columns.Left(index) - columns.Gap / 2;
                        Rules.Add(new(x, top, 0, Math.Max(height, colExtent)));
                    }
                }
                if (paginated)
                {
                    var inkEnd = top + maxColumnExtent;
                    for (var index = page; ; index++)
                    {
                        var capacity = Limit(index); var slotTop = index == page ? Pages[index].Top : Slots[index - 1].Top + Slots[index - 1].Capacity;
                        if (slotTop >= inkEnd - epsilon) break;
                        if (index >= Slots.Count) { Slots.Add(new(slotTop, capacity, 0)); Pages.Add(new(slotTop, slotTop + capacity)); }
                        Slots[index] = Slots[index] with { Used = Math.Max(Slots[index].Used, Math.Min(capacity, inkEnd - slotTop)) };
                    }
                }
                var inkHeight = maxColumnExtent;
                var effectiveRowAdvance = Math.Max(height, maxColumnExtent);
                if (ownHeight && effectiveRowAdvance > inkHeight + epsilon) ReservedSpace.Add((top + inkHeight, top + effectiveRowAdvance));
                row += effectiveRowAdvance;
                if (paginated)
                    while (row > Limit(page) + epsilon) { row -= Limit(page); page++; EnsurePage(); }
                usedHeight = Math.Max(usedHeight, AtY());
            }
            double FirstHardAfter(double cursor) => breaks.Keeps.Where(keep => !keep.Soft && keep.End > cursor + epsilon)
                .Select(keep => keep.Start).DefaultIfEmpty(cursor).Min();
            void Group(double start, double until, bool beforeSpanner)
            {
                var cursor = start;
                while (cursor < until - epsilon)
                {
                    if (paginated && row >= Limit(page) - epsilon) NextPage();
                    var available = Math.Min(paginated ? Math.Max(1, Limit(page) - row) : double.PositiveInfinity,
                        double.IsFinite(ownLimit) ? Math.Max(0, ownLimit - row) : double.PositiveInfinity);
                    if (paginated && FirstHardAfter(cursor) >= cursor + available &&
                        reservedSpace?.Any(span => span.Start < cursor + available && span.End > cursor) != true)
                        cursor = Math.Min(until, FirstHardAfter(cursor));
                    if (cursor >= until - epsilon) break;
                    var nextPageBreak = pageBreaks.Keys.FirstOrDefault(value => value > cursor + epsilon && value < until - epsilon, double.NaN);
                    var target = double.IsFinite(nextPageBreak) ? nextPageBreak : until;
                    if (paginated && (row > epsilon || Limit(page) < Limit(page + 1) - epsilon))
                    {
                        var strict = breaks.Breaks(cursor, target, available, 1, preserveSoft: true);
                        var fresh = breaks.Breaks(cursor, target, Limit(page + 1), 1, preserveSoft: true);
                        if (strict.Count > 0 && strict[0].End - strict[0].Start > available + epsilon &&
                            fresh.Count > 0 && fresh[0].End - fresh[0].Start <= Limit(page + 1) + epsilon)
                        { NextPage(); continue; }
                    }
                    var trial = breaks.Breaks(cursor, target, available, paginated ? columns.Count : int.MaxValue);
                    var pageEnd = trial.Count == 0 ? target : trial[^1].End;
                    // Page avoidance is a constraint across the whole column set, not a ban on column breaks.
                    if (paginated)
                        foreach (var avoid in pageAvoids)
                            if (avoid.Start > cursor + epsilon && avoid.Start < pageEnd - epsilon && avoid.End > pageEnd + epsilon &&
                                breaks.Breaks(avoid.Start, avoid.End, Limit(page + 1), columns.Count + 1).Count <= columns.Count)
                            { target = Math.Min(target, avoid.Start); pageEnd = target; }
                    if (trial.Count > 0 && trial[0].End - trial[0].Start > available + epsilon && paginated)
                    {
                        // Continue a monolithic line/table band vertically on subsequent pages, retaining all its ink.
                        if (row > epsilon) { NextPage(); continue; }
                        var fragment = trial[0]; Emit([fragment], fragment.End - fragment.Start);
                        cursor = fragment.End;
                        continue;
                    }
                    var lastGroup = pageEnd >= target - epsilon;
                    var balance = beforeSpanner && lastGroup || columns.Fill == CssColumnFill.BalanceAll ||
                        columns.Fill == CssColumnFill.Balance && lastGroup && target >= until - epsilon;
                    if (!paginated && double.IsFinite(available) && trial.Count > columns.Count) balance = false;
                    var height = balance ? breaks.Balance(cursor, pageEnd, columns.Count, available) : available;
                    if (!double.IsFinite(height)) height = pageEnd - cursor;
                    var fragments = breaks.Breaks(cursor, pageEnd, height);
                    var used = fragments.Count == 0 ? 0 : fragments.Max(fragment => fragment.End - fragment.Start);
                    if (paginated && fragments.Count > columns.Count)
                    { fragments = breaks.Breaks(cursor, pageEnd, available, columns.Count); used = available; balance = false; }
                    var footprint = balance ? used : paginated ? available : used;
                    if (ownHeight && !beforeSpanner)
                    {
                        footprint = double.IsFinite(specified) ? Math.Min(Math.Max(0, specified - row), paginated ? Math.Max(0, Limit(page) - row) : double.PositiveInfinity)
                            : Math.Max(0, Math.Min(used, available));
                        if (!paginated) footprint = Math.Max(footprint, boxHeight!.Value.ContentMinimum - row);
                    }
                    Emit(fragments, footprint, balanced: balance);
                    cursor = fragments.Count == 0 ? pageEnd : fragments[^1].End;
                    if (paginated && (cursor < until - epsilon || pageBreaks.ContainsKey(cursor)))
                        NextPage(pageBreaks.GetValueOrDefault(cursor));
                }
            }
            var start = 0d;
            foreach (var span in _columnSpanners.OrderBy(span => span.Start))
            {
                Group(start, span.Start, true);
                var spanHeight = span.End - span.Start;
                var afterBreak = CssFragmentationProperties.After(span.Source);
                var keepAfterHeight = 0d;
                if (afterBreak is CssFragmentBreak.Avoid or CssFragmentBreak.AvoidPage or CssFragmentBreak.AvoidColumn &&
                    span.End < end - epsilon)
                {
                    var probeLimit = paginated ? Limit(page + 1) : double.IsFinite(ownLimit) ? ownLimit : double.PositiveInfinity;
                    var nextKeep = breaks.Breaks(span.End, end, probeLimit, 1, preserveSoft: true);
                    if (nextKeep.Count > 0)
                        keepAfterHeight = Math.Max(0, nextKeep[0].End - span.End);
                }
                if (!paginated && ownHeight && double.IsFinite(ownLimit))
                {
                    var lastGroupOverflowed = Slices.Count > 0 && Slices.Count(s => s.Group == group - 1 && !s.IsRepeat) > columns.Count;
                    if (lastGroupOverflowed || row + spanHeight + keepAfterHeight > ownLimit + epsilon)
                    {
                        DemotedSpanners.Add(span.Source);
                    }
                }
                if (paginated && row > 0)
                {
                    var pageLim = Limit(page);
                    var nextLim = Limit(page + 1);
                    if (row + spanHeight > pageLim + epsilon ||
                        keepAfterHeight > 0 && row + spanHeight + keepAfterHeight > pageLim + epsilon && spanHeight + keepAfterHeight <= nextLim + epsilon)
                    {
                        NextPage();
                    }
                }
                var spanCursor = span.Start;
                do
                {
                    var capacity = paginated ? Math.Max(1, Limit(page) - row) : span.End - spanCursor;
                    var parts = breaks.Breaks(spanCursor, span.End, capacity, 1);
                    var fragment = parts.Count == 0 ? (spanCursor, span.End) : parts[0];
                    Emit([fragment], fragment.Item2 - fragment.Item1, span.Source); spanCursor = fragment.Item2;
                    if (paginated && spanCursor < span.End - epsilon) NextPage();
                } while (spanCursor < span.End - epsilon);
                start = span.End;
                if (paginated && start < end - epsilon && pageBreaks.TryGetValue(start, out var afterSpan)) NextPage(afterSpan);
            }
            Group(start, end, false); EnsurePage();
            UsedHeight = Math.Max(0, usedHeight);
            NaturalHeight = UsedHeight;
            if (ownHeight)
            {
                var requested = paginated && !boxHeight!.Value.NeedsNatural ? Math.Max(boxHeight.Value.ContentMinimum, double.IsFinite(specified) ? specified : 0)
                    : Math.Max(0, boxHeight!.Value.Used(UsedHeight + boxHeight.Value.Insets) - boxHeight.Value.Insets);
                if (requested > UsedHeight + epsilon) ReservedSpace.Add((UsedHeight, requested));
                UsedHeight = paginated ? Math.Max(UsedHeight, requested) : requested;
                if (paginated)
                    while (Pages[^1].Bottom < UsedHeight - epsilon)
                    {
                        page = Pages.Count; EnsurePage();
                    }
            }
            ExtentHeight = paginated ? Pages[^1].Bottom : UsedHeight;
        }
    }
}
