using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>Grid formatting over native children. All placements and tracks are private layout data.</summary>
internal sealed partial class CssGridLayout(Panel owner)
{
    private Snapshot? _snapshot;
    private readonly List<IntrinsicColumns> _intrinsicColumns = [];

    public Size Measure(Size available)
    {
        var insets = CssBoxMetrics.ContentInsets(owner, owner.CssLayout?.ContainingWidthCache ?? available.Width);
        _snapshot = Compute(CssBoxMetrics.InnerSize(available, insets));
        owner.MeasureCssLayoutAbsoluteChildren(available);
        return new(_snapshot.Desired.Width + insets.Left + insets.Right, _snapshot.Desired.Height + insets.Top + insets.Bottom);
    }

    public Size Arrange(Size finalSize)
    {
        var insets = CssBoxMetrics.ContentInsets(owner, owner.CssLayout?.ContainingWidthCache ?? finalSize.Width);
        var innerSize = CssBoxMetrics.InnerSize(finalSize, insets);
        var snapshot = _snapshot;
        if (snapshot is null || snapshot.Available != innerSize || _assignmentDirty) snapshot = _snapshot = Compute(innerSize);
        var justifyItems = (CssBoxAlignment)owner.GetValue(CssGridProperties.JustifyItemsProperty)!;
        var alignItems = (CssBoxAlignment)owner.GetValue(CssGridProperties.AlignItemsProperty)!;
        foreach (var item in snapshot.Items)
        {
            var x = snapshot.Columns.Starts[item.Column];
            var width = snapshot.Columns.SpanSize(item.Column, item.ColumnSpan);
            if (owner.FlowDirection == FlowDirection.RightToLeft) x = innerSize.Width - x - width;
            var slot = new Rect(x + insets.Left, snapshot.Rows.Starts[item.Row] + insets.Top, width, snapshot.Rows.SpanSize(item.Row, item.RowSpan));
            var justify = (CssBoxAlignment)item.Box.Element.GetValue(CssGridProperties.JustifySelfProperty)!;
            if (justify == CssBoxAlignment.Auto) justify = justifyItems;
            var align = FlexPanel.GetAlignSelf(item.Box.Element) switch
            {
                FlexAlign.Auto => alignItems, FlexAlign.Center => CssBoxAlignment.Center,
                FlexAlign.FlexEnd => CssBoxAlignment.End, FlexAlign.Stretch => CssBoxAlignment.Stretch,
                _ => CssBoxAlignment.Start,
            };
            if (owner.FlowDirection == FlowDirection.RightToLeft)
                justify = justify switch { CssBoxAlignment.Start => CssBoxAlignment.End, CssBoxAlignment.End => CssBoxAlignment.Start,
                    CssBoxAlignment.Normal => CssBoxAlignment.End, _ => justify };
            if (item.Box.Element is FrameworkElement element)
            {
                if (CssDisplayLayout.IsSubgridded(element, false)) justify = CssBoxAlignment.Stretch;
                if (CssDisplayLayout.IsSubgridded(element, true)) align = CssBoxAlignment.Stretch;
            }
            item.Box.Arrange(slot, justify, align);
        }
        owner.ArrangeCssLayoutAbsoluteChildren(finalSize);
        return finalSize;
    }

    private Snapshot Compute(Size available)
    {
        var inputVersion = InputVersion(owner);
        var previous = _snapshot is { } prior && prior.InputVersion == inputVersion ? prior : null;
        var plan = CreatePlan(available);
        var (items, columns, rows, firstColumn, firstRow, columnCount, rowCount, columnGap, rowGap) = plan;
        var sameItems = previous is not null && previous.Items.Length == items.Length &&
            items.Zip(previous.Items).All(pair => ReferenceEquals(pair.First.Box.Element, pair.Second.Box.Element) &&
                pair.First.Column == pair.Second.Column && pair.First.Row == pair.Second.Row &&
                pair.First.ColumnSpan == pair.Second.ColumnSpan && pair.First.RowSpan == pair.Second.RowSpan);
        var sameColumns = previous is not null && SameShared(previous.ColumnDefinition.Shared, columns.Shared);
        var sameRows = previous is not null && SameShared(previous.RowDefinition.Shared, rows.Shared);
        var reuseColumns = sameItems && sameColumns && sameRows && previous!.ColumnGap.Equals(columnGap);
        var reuseRows = sameItems && sameRows && previous!.RowGap.Equals(rowGap) && previous.Available.Width.Equals(available.Width);
        var columnsKnown = columns.Shared?.Sizes is not null;
        var rowsKnown = rows.Shared?.Sizes is not null;
        Contribution[] columnContributions = [];
        Contribution[] rowContributions = [];
        SizedAxis? columnSizes = null, rowSizes = null;
        if (columnsKnown) columnSizes = SizeAxis(columns, firstColumn, columnCount, available.Width, columnGap, [], CssBoxAlignment.Normal);
        if (rowsKnown) rowSizes = SizeAxis(rows, firstRow, rowCount, available.Height, rowGap, [], CssBoxAlignment.Normal);
        AssignSubgrids(items, columns, rows, firstColumn, firstRow, columnGap, rowGap, columnSizes, rowSizes);
        if (!columnsKnown)
        {
            columnContributions = reuseColumns ? previous!.ColumnContributions : CachedColumnContributions(plan, inputVersion);
            columnSizes = SizeAxis(columns, firstColumn, columnCount, available.Width, columnGap, columnContributions,
                (CssBoxAlignment)owner.GetValue(CssGridProperties.JustifyContentProperty)!);
            AssignSubgrids(items, columns, rows, firstColumn, firstRow, columnGap, rowGap, columnSizes, rowSizes);
        }
        if (!rowsKnown)
        {
            if (reuseRows && previous!.Columns.Sizes.AsSpan().SequenceEqual(columnSizes!.Sizes) &&
                previous.Columns.Starts.AsSpan().SequenceEqual(columnSizes.Starts)) rowContributions = previous.RowContributions;
            else
            {
                foreach (var item in items)
                {
                    item.Box.Element.Measure(new Size(columnSizes!.SpanSize(item.Column, item.ColumnSpan), double.PositiveInfinity));
                    item.Box.MinContent = item.Box.Element.DesiredSize;
                    item.Box.MaxContent = item.Box.Element.DesiredSize;
                }
                rowContributions = CollectContributions(items, true);
            }
            rowSizes = SizeAxis(rows, firstRow, rowCount, available.Height, rowGap, rowContributions,
                (CssBoxAlignment)owner.GetValue(CssGridProperties.AlignContentProperty)!);
            AssignSubgrids(items, columns, rows, firstColumn, firstRow, columnGap, rowGap, columnSizes, rowSizes);
        }
        foreach (var item in items)
            item.Box.Element.Measure(new Size(columnSizes!.SpanSize(item.Column, item.ColumnSpan), rowSizes!.SpanSize(item.Row, item.RowSpan)));
        _assignmentDirty = false;
        return new(available, new Size(columnSizes!.Extent, rowSizes!.Extent), items, columnSizes, rowSizes,
            columnContributions, rowContributions, columns, rows, columnGap, rowGap, inputVersion);
    }

    private static bool SameShared(BorrowedAxis? a, BorrowedAxis? b) => a?.SameAs(b) ?? b is null;

    private sealed record IntrinsicColumns(Plan Plan, long InputVersion, double ParentWidth, Contribution[] Contributions);

    private Contribution[] CachedColumnContributions(Plan plan, long inputVersion)
    {
        // A grid can be visited under provisional and resolved allocations during a single
        // ancestor pass. Keep both intrinsic results rather than evicting one with the other.
        _intrinsicColumns.RemoveAll(entry => entry.InputVersion != inputVersion);
        foreach (var entry in _intrinsicColumns)
        {
            var old = entry.Plan;
            if (!entry.ParentWidth.Equals(_parentAreaWidth) || old.ColumnGap != plan.ColumnGap || old.RowGap != plan.RowGap ||
                old.FirstColumn != plan.FirstColumn || old.FirstRow != plan.FirstRow || old.ColumnCount != plan.ColumnCount || old.RowCount != plan.RowCount ||
                !SameShared(old.Columns.Shared, plan.Columns.Shared) || !SameShared(old.Rows.Shared, plan.Rows.Shared) || old.Items.Length != plan.Items.Length) continue;
            if (old.Items.Zip(plan.Items).All(pair => ReferenceEquals(pair.First.Box.Element, pair.Second.Box.Element) &&
                pair.First.Column == pair.Second.Column && pair.First.Row == pair.Second.Row &&
                pair.First.ColumnSpan == pair.Second.ColumnSpan && pair.First.RowSpan == pair.Second.RowSpan)) return entry.Contributions;
        }
        var values = MeasureColumnContributions(plan);
        if (_intrinsicColumns.Count == 16) _intrinsicColumns.RemoveAt(0);
        _intrinsicColumns.Add(new(plan, inputVersion, _parentAreaWidth, values));
        return values;
    }

    private static long InputVersion(Visual visual)
    {
        var result = visual is UIElement element ? element.MeasureInputVersion : 0;
        for (var i = 0; i < visual.VisualChildrenCount; i++)
            if (visual.GetVisualChild(i) is { } child) result = Math.Max(result, InputVersion(child));
        return result;
    }

    private Plan CreatePlan(Size available)
    {
        var columnGap = CssGapProperties.Resolve(owner, false, available.Width);
        var rowGap = CssGapProperties.Resolve(owner, true, available.Height);
        var areas = (CssGridAreas)owner.GetValue(CssGridProperties.AreasProperty)!;
        var columns = new AxisDefinition((CssComputedGridTracks)owner.GetValue(CssGridProperties.ColumnsProperty)!,
            (CssComputedGridTracks)owner.GetValue(CssGridProperties.AutoColumnsProperty)!, areas, false, available.Width, columnGap, Borrowed(false));
        var rows = new AxisDefinition((CssComputedGridTracks)owner.GetValue(CssGridProperties.RowsProperty)!,
            (CssComputedGridTracks)owner.GetValue(CssGridProperties.AutoRowsProperty)!, areas, true, available.Height, rowGap, Borrowed(true));
        var items = CssLayoutBox.ChildrenOf(owner).Select(box => new Item(box)).ToArray();
        foreach (var item in items)
        {
            (item.ColumnStart, item.ColumnSpan) = ResolveAxis(item.Box.Element, false, columns);
            (item.RowStart, item.RowSpan) = ResolveAxis(item.Box.Element, true, rows);
        }
        Place(items, columns.ExplicitCount, rows.ExplicitCount,
            (CssGridAutoFlow)owner.GetValue(CssGridProperties.AutoFlowProperty)!, out var firstColumn, out var firstRow,
            out var columnCount, out var rowCount);
        ClampSubgridPlacement(items, columns, rows, ref firstColumn, ref firstRow, ref columnCount, ref rowCount);
        return new(items, columns, rows, firstColumn, firstRow, columnCount, rowCount, columnGap, rowGap);
    }

    private sealed record Plan(Item[] Items, AxisDefinition Columns, AxisDefinition Rows, int FirstColumn, int FirstRow,
        int ColumnCount, int RowCount, double ColumnGap, double RowGap);
    private sealed record Snapshot(Size Available, Size Desired, Item[] Items, SizedAxis Columns, SizedAxis Rows,
        Contribution[] ColumnContributions, Contribution[] RowContributions, AxisDefinition ColumnDefinition, AxisDefinition RowDefinition,
        double ColumnGap, double RowGap, long InputVersion);
    private sealed class Item(CssLayoutBox box)
    {
        public CssLayoutBox Box { get; } = box;
        public int? ColumnStart, RowStart;
        public int ColumnSpan = 1, RowSpan = 1;
        public int Column, Row;
        public Contribution[]? SubgridColumnContributions;
    }

    private sealed class AxisDefinition
    {
        public readonly CssExpandedGridTracks Explicit;
        public readonly CssExpandedGridTracks Automatic;
        public readonly CssLengthContext ExplicitContext, AutoContext;
        public readonly int ExplicitCount;
        public readonly BorrowedAxis? Shared;
        public readonly Dictionary<string, List<(int Start, int End)>> Areas = new(StringComparer.Ordinal);

        public AxisDefinition(CssComputedGridTracks tracks, CssComputedGridTracks automatic, CssGridAreas areas,
            bool row, double available, double gap, BorrowedAxis? borrowed = null)
        {
            Shared = tracks.Tracks.IsSubgrid ? borrowed : null;
            Explicit = tracks.Tracks.Expand(tracks.Context, available, gap);
            Automatic = automatic.Tracks.Expand(automatic.Context, available, gap);
            ExplicitContext = tracks.Context; AutoContext = automatic.Context;
            ExplicitCount = Shared?.Count ?? Math.Max(Explicit.Tracks.Count, row ? areas.Rows : areas.Columns);
            if (Shared is not null)
            {
                foreach (var (name, indices) in Shared.Names)
                    foreach (var line in indices) AddLine(name, line);
                foreach (var (name, indices) in tracks.Tracks.ExpandSubgridNames(ExplicitCount))
                    foreach (var line in indices) AddLine(name, line);
                foreach (var (name, intervals) in Shared.Areas)
                    foreach (var interval in intervals) AddArea(name, interval.Start, interval.End);
            }
            foreach (var (name, area) in areas.Areas)
            {
                AddArea(name, row ? area.Row : area.Column, row ? area.Row + area.RowSpan : area.Column + area.ColumnSpan);
            }
        }

        private void AddArea(string name, int start, int end)
        {
            if (Shared is not null)
            { start = Math.Clamp(start, 0, ExplicitCount); end = Math.Clamp(end, 0, ExplicitCount); }
            if (end <= start) return;
            if (!Areas.TryGetValue(name, out var intervals)) Areas[name] = intervals = [];
            intervals.Add((start, end));
            AddLine(name + "-start", start); AddLine(name + "-end", end);
        }

        private void AddLine(string name, int value)
        {
            if (!Explicit.Lines.TryGetValue(name, out var lines)) Explicit.Lines[name] = lines = [];
            if (!lines.Contains(value)) { lines.Add(value); lines.Sort(); }
        }

        public (CssGridTrack Track, CssLengthContext Context) TrackAt(int index)
        {
            if (Shared is not null) return (CssGridTrack.Auto, ExplicitContext);
            if (index >= 0 && index < Explicit.Tracks.Count) return (Explicit.Tracks[index], ExplicitContext);
            if (Automatic.Tracks.Count == 0) return (CssGridTrack.Auto, AutoContext);
            var offset = index < 0 ? index : index - Explicit.Tracks.Count;
            return (Automatic.Tracks[((offset % Automatic.Tracks.Count) + Automatic.Tracks.Count) % Automatic.Tracks.Count], AutoContext);
        }
    }

    private static (int? Start, int Span) ResolveAxis(UIElement element, bool row, AxisDefinition axis)
    {
        var a = (CssGridLine)element.GetValue(row ? CssGridProperties.RowStartProperty : CssGridProperties.ColumnStartProperty)!;
        var b = (CssGridLine)element.GetValue(row ? CssGridProperties.RowEndProperty : CssGridProperties.ColumnEndProperty)!;
        if (a.Span && b.Span) b = default;
        var start = ResolveLine(a, axis, false);
        var end = ResolveLine(b, axis, true);
        var span = a.Span ? Math.Max(1, a.Number) : b.Span ? Math.Max(1, b.Number) : 1;
        if (start is { } from && end is { } to)
        {
            if (to < from) (from, to) = (to, from);
            start = from; span = Math.Max(1, to - from);
        }
        else if (start is { } definiteStart)
            span = b.Span && b.Name is not null ? Math.Max(1, ResolveNamedSpan(definiteStart, b, axis, 1) - definiteStart) : span;
        else if (end is { } definiteEnd)
        {
            start = a.Span && a.Name is not null ? ResolveNamedSpan(definiteEnd, a, axis, -1) : definiteEnd - span;
            span = Math.Max(1, definiteEnd - start.Value);
        }
        var positionProperty = row ? Grid.RowProperty : Grid.ColumnProperty;
        var spanProperty = row ? Grid.RowSpanProperty : Grid.ColumnSpanProperty;
        if (!a.Span && !b.Span && (start is null || end is null) && element is Panel { CssDisplayMode: CssDisplayMode.Grid } panel &&
            ((CssComputedGridTracks)panel.GetValue(row ? CssGridProperties.RowsProperty : CssGridProperties.ColumnsProperty)!).Tracks is { IsSubgrid: true } subgrid)
        {
            span = subgrid.MinimumSubgridSpan;
            if (a.IsAuto && end is { } anchoredEnd) start = anchoredEnd - span;
        }
        if (element.HasLocalOrAnimatedValue(positionProperty)) start = (int)element.GetValue(positionProperty)!;
        if (element.HasLocalOrAnimatedValue(spanProperty)) span = (int)element.GetValue(spanProperty)!;
        if (axis.Shared is not null)
        {
            if (start is { } position)
            {
                var limit = position + span;
                start = Math.Clamp(position, 0, axis.ExplicitCount - 1);
                span = Math.Clamp(limit - start.Value, 1, axis.ExplicitCount - start.Value);
            }
            else span = Math.Min(span, axis.ExplicitCount);
        }
        return (start, Math.Clamp(span, 1, CssGridTrackList.MaxTracks));
    }

    private static int? ResolveLine(CssGridLine line, AxisDefinition axis, bool end)
    {
        if (line.IsAuto || line.Span) return null;
        if (line.Name is null) return line.Number > 0 ? line.Number - 1 : axis.ExplicitCount + 1 + line.Number;
        if (line.IsNameOnly && axis.Explicit.Lines.TryGetValue(line.Name + (end ? "-end" : "-start"), out var areaLines))
            return areaLines[0];
        axis.Explicit.Lines.TryGetValue(line.Name, out var matches);
        var number = line.Number == 0 ? 1 : line.Number;
        var count = matches?.Count ?? 0;
        if (number > 0) return number <= count ? matches![number - 1] : axis.ExplicitCount + number - count;
        return -number <= count ? matches![count + number] : number + count;
    }

    private static int ResolveNamedSpan(int origin, CssGridLine span, AxisDefinition axis, int direction)
    {
        axis.Explicit.Lines.TryGetValue(span.Name!, out var names);
        var remaining = Math.Max(1, span.Number);
        var line = origin;
        while (remaining > 0 && Math.Abs(line - origin) < CssGridTrackList.MaxTracks * 2)
        {
            line += direction;
            if (line < 0 || line > axis.ExplicitCount || names?.Contains(line) == true) remaining--;
        }
        return line;
    }

    private sealed class Placement(Item item, bool columnFlow)
    {
        public Item Item { get; } = item;
        public int? Major = columnFlow ? item.ColumnStart : item.RowStart;
        public int? Minor = columnFlow ? item.RowStart : item.ColumnStart;
        public int MajorSpan = columnFlow ? item.ColumnSpan : item.RowSpan;
        public int MinorSpan = columnFlow ? item.RowSpan : item.ColumnSpan;
    }

    private static void Place(Item[] items, int explicitColumns, int explicitRows, CssGridAutoFlow flow,
        out int firstColumn, out int firstRow, out int columns, out int rows)
    {
        var columnFlow = (flow & CssGridAutoFlow.Column) != 0;
        var dense = (flow & CssGridAutoFlow.Dense) != 0;
        var placements = items.Select(item => new Placement(item, columnFlow)).ToArray();
        var occupied = new List<Placement>();
        var firstMajor = Math.Min(0, placements.Where(p => p.Major.HasValue).Select(p => p.Major!.Value).DefaultIfEmpty(0).Min());
        var firstMinor = Math.Min(0, placements.Where(p => p.Minor.HasValue).Select(p => p.Minor!.Value).DefaultIfEmpty(0).Min());
        var minorEnd = Math.Max(columnFlow ? explicitRows : explicitColumns,
            placements.Where(p => p.Minor.HasValue).Select(p => p.Minor!.Value + p.MinorSpan).DefaultIfEmpty(0).Max());
        bool Fits(int major, int minor, Placement candidate) => !occupied.Any(p =>
            major < p.Major!.Value + p.MajorSpan && major + candidate.MajorSpan > p.Major.Value &&
            minor < p.Minor!.Value + p.MinorSpan && minor + candidate.MinorSpan > p.Minor.Value);
        void Commit(Placement p, int major, int minor)
        { p.Major = major; p.Minor = minor; occupied.Add(p); }

        foreach (var p in placements.Where(p => p.Major.HasValue && p.Minor.HasValue)) occupied.Add(p);
        var rowCursors = new Dictionary<int, int>();
        foreach (var p in placements.Where(p => p.Major.HasValue && !p.Minor.HasValue))
        {
            var major = p.Major!.Value;
            var minor = dense ? firstMinor : rowCursors.GetValueOrDefault(major, firstMinor);
            while (!Fits(major, minor, p) && minor - firstMinor < CssGridTrackList.MaxTracks) minor++;
            Commit(p, major, minor);
            rowCursors[major] = minor + p.MinorSpan;
            minorEnd = Math.Max(minorEnd, minor + p.MinorSpan);
        }
        minorEnd = Math.Max(minorEnd, firstMinor + (placements.Length == 0 ? 0 : Math.Max(1, placements.Where(p => !p.Minor.HasValue).Select(p => p.MinorSpan).DefaultIfEmpty(1).Max())));
        var cursorMajor = firstMajor;
        var cursorMinor = firstMinor;
        foreach (var p in placements.Where(p => !p.Major.HasValue))
        {
            if (dense) { cursorMajor = firstMajor; cursorMinor = firstMinor; }
            if (p.Minor is { } definiteMinor)
            {
                if (!dense && definiteMinor < cursorMinor) cursorMajor++;
                cursorMinor = definiteMinor;
                while (!Fits(cursorMajor, cursorMinor, p) && cursorMajor - firstMajor < CssGridTrackList.MaxTracks) cursorMajor++;
            }
            else
            {
                while (cursorMajor - firstMajor < CssGridTrackList.MaxTracks)
                {
                    if (cursorMinor + p.MinorSpan > minorEnd) { cursorMinor = firstMinor; cursorMajor++; }
                    else if (Fits(cursorMajor, cursorMinor, p)) break;
                    else cursorMinor++;
                }
            }
            Commit(p, cursorMajor, cursorMinor);
        }
        var majorEnd = Math.Max(columnFlow ? explicitColumns : explicitRows,
            placements.Select(p => p.Major!.Value + p.MajorSpan).DefaultIfEmpty(0).Max());
        var majorCount = Math.Clamp(majorEnd - firstMajor, placements.Length > 0 ? 1 : 0, CssGridTrackList.MaxTracks);
        var minorCount = Math.Clamp(minorEnd - firstMinor, placements.Length > 0 ? 1 : 0, CssGridTrackList.MaxTracks);
        foreach (var p in placements)
        {
            var major = Math.Clamp(p.Major!.Value - firstMajor, 0, majorCount - 1);
            var minor = Math.Clamp(p.Minor!.Value - firstMinor, 0, minorCount - 1);
            var majorSpan = Math.Min(p.MajorSpan, majorCount - major);
            var minorSpan = Math.Min(p.MinorSpan, minorCount - minor);
            p.Item.Row = columnFlow ? minor : major; p.Item.Column = columnFlow ? major : minor;
            p.Item.RowSpan = columnFlow ? minorSpan : majorSpan; p.Item.ColumnSpan = columnFlow ? majorSpan : minorSpan;
        }
        firstColumn = columnFlow ? firstMajor : firstMinor; firstRow = columnFlow ? firstMinor : firstMajor;
        columns = columnFlow ? majorCount : minorCount; rows = columnFlow ? minorCount : majorCount;
    }

    private readonly record struct Contribution(int Start, int Span, double Min, double Max);
    private sealed record SizedAxis(double[] Sizes, double[] Starts, double Extent)
    {
        public double SpanSize(int start, int span) => Math.Max(0, Starts[start + span - 1] + Sizes[start + span - 1] - Starts[start]);
    }

    private static SizedAxis SizeTracks(AxisDefinition definition, int first, int count, double available, double gap,
        Contribution[] contributions, CssBoxAlignment alignment)
    {
        var sizes = new double[count];
        var limits = new double[count];
        var caps = new double[count];
        var flex = new double[count];
        var minKinds = new CssGridBreadthKind[count];
        var maxKinds = new CssGridBreadthKind[count];
        var collapsed = new bool[count];
        for (var i = 0; i < count; i++)
        {
            collapsed[i] = definition.Explicit.AutoFit.Contains(first + i) &&
                !contributions.Any(item => item.Start <= i && item.Start + item.Span > i);
            if (collapsed[i]) { minKinds[i] = maxKinds[i] = CssGridBreadthKind.Length; continue; }
            var (track, context) = definition.TrackAt(first + i);
            var min = track.Min.Resolve(context, available);
            var max = track.Max.Resolve(context, available);
            minKinds[i] = track.Min.Kind == CssGridBreadthKind.Length && !double.IsFinite(min) ? CssGridBreadthKind.Auto : track.Min.Kind;
            maxKinds[i] = track.Max.Kind == CssGridBreadthKind.Length && !double.IsFinite(max) ? CssGridBreadthKind.Auto : track.Max.Kind;
            sizes[i] = double.IsFinite(min) ? min : 0;
            limits[i] = double.IsFinite(max) ? Math.Max(sizes[i], max) : sizes[i];
            caps[i] = track.FitLimit is { } fit ? new CssGridBreadth(CssGridBreadthKind.Length, fit).Resolve(context, available) : double.PositiveInfinity;
            if (!double.IsFinite(caps[i])) caps[i] = double.PositiveInfinity;
            flex[i] = track.Max.Flex;
        }
        var gaps = new double[Math.Max(0, count - 1)];
        for (var i = 0; i < gaps.Length; i++) if (!collapsed[i] && !collapsed[i + 1]) gaps[i] = gap;
        double SpanGaps(Contribution item) => gaps.Skip(item.Start).Take(item.Span - 1).Sum();
        foreach (var item in contributions.OrderBy(item => item.Span))
        {
            var indices = Enumerable.Range(item.Start, item.Span).ToArray();
            var crossingFlex = item.Span > 1 && indices.Any(i => maxKinds[i] == CssGridBreadthKind.Flex);
            var candidates = indices.Where(i => !collapsed[i] && minKinds[i] != CssGridBreadthKind.Length &&
                (!crossingFlex || minKinds[i] != CssGridBreadthKind.Auto)).ToArray();
            var required = indices.Any(i => minKinds[i] == CssGridBreadthKind.MaxContent) ? item.Max : item.Min;
            Increase(sizes, candidates, null, required - SpanGaps(item) - indices.Sum(i => sizes[i]));
        }
        for (var i = 0; i < count; i++) { limits[i] = Math.Max(limits[i], sizes[i]); caps[i] = Math.Max(caps[i], sizes[i]); }
        foreach (var item in contributions.OrderBy(item => item.Span))
        {
            var indices = Enumerable.Range(item.Start, item.Span).ToArray();
            var candidates = indices.Where(i => !collapsed[i] && maxKinds[i] is CssGridBreadthKind.Auto or CssGridBreadthKind.MinContent or CssGridBreadthKind.MaxContent).ToArray();
            var required = candidates.Length > 0 && candidates.All(i => maxKinds[i] == CssGridBreadthKind.MinContent) ? item.Min : item.Max;
            Increase(limits, candidates, caps, required - SpanGaps(item) - indices.Sum(i => limits[i]));
        }
        var fixedTracks = Enumerable.Range(0, count).Where(i => maxKinds[i] != CssGridBreadthKind.Flex).ToArray();
        var totalGaps = gaps.Sum();
        if (double.IsFinite(available)) Increase(sizes, fixedTracks, limits, available - totalGaps - sizes.Sum());
        else foreach (var i in fixedTracks) sizes[i] = limits[i];

        var flexible = Enumerable.Range(0, count).Where(i => maxKinds[i] == CssGridBreadthKind.Flex).ToArray();
        if (flexible.Length > 0)
        {
            double fraction;
            if (double.IsFinite(available)) fraction = Fraction(sizes, flex, Enumerable.Range(0, count).ToArray(), available - totalGaps);
            else
            {
                fraction = flexible.Max(i => sizes[i] / Math.Max(1, flex[i]));
                foreach (var item in contributions)
                {
                    var indices = Enumerable.Range(item.Start, item.Span).ToArray();
                    if (indices.Any(i => flex[i] > 0))
                        fraction = Math.Max(fraction, Fraction(sizes, flex, indices, item.Max - SpanGaps(item)));
                }
            }
            foreach (var i in flexible) sizes[i] = Math.Max(sizes[i], fraction * flex[i]);
        }
        if (double.IsFinite(available) && alignment is CssBoxAlignment.Normal or CssBoxAlignment.Stretch)
            Increase(sizes, Enumerable.Range(0, count).Where(i => maxKinds[i] == CssGridBreadthKind.Auto && !collapsed[i]).ToArray(),
                null, available - totalGaps - sizes.Sum());
        var extent = sizes.Sum() + totalGaps;
        var free = double.IsFinite(available) ? available - extent : 0;
        var visibleCount = collapsed.Count(value => !value);
        var start = alignment switch
        {
            CssBoxAlignment.End => free, CssBoxAlignment.Center => free / 2,
            CssBoxAlignment.SpaceAround when free > 0 && visibleCount > 0 => free / visibleCount / 2,
            CssBoxAlignment.SpaceEvenly when free > 0 && visibleCount > 0 => free / (visibleCount + 1), _ => 0,
        };
        var extraGap = free <= 0 ? 0 : alignment switch
        {
            CssBoxAlignment.SpaceBetween when visibleCount > 1 => free / (visibleCount - 1),
            CssBoxAlignment.SpaceAround when visibleCount > 0 => free / visibleCount,
            CssBoxAlignment.SpaceEvenly when visibleCount > 0 => free / (visibleCount + 1), _ => 0,
        };
        var starts = new double[count];
        for (var i = 0; i < count; i++)
        {
            starts[i] = start;
            start += sizes[i];
            if (i < gaps.Length) start += gaps[i] + (!collapsed[i] && !collapsed[i + 1] ? extraGap : 0);
        }
        return new(sizes, starts, extent);
    }

    private static double Fraction(double[] sizes, double[] flex, int[] indices, double available)
    {
        var active = indices.Where(i => flex[i] > 0).ToHashSet();
        while (active.Count > 0)
        {
            var remaining = available - indices.Where(i => !active.Contains(i)).Sum(i => sizes[i]);
            var fraction = Math.Max(0, remaining / Math.Max(1, active.Sum(i => flex[i])));
            var frozen = active.Where(i => fraction * flex[i] < sizes[i]).ToArray();
            if (frozen.Length == 0) return fraction;
            foreach (var i in frozen) active.Remove(i);
        }
        return 0;
    }

    private static void Increase(double[] values, int[] candidates, double[]? limits, double extra)
    {
        if (!double.IsFinite(extra) || extra <= 0) return;
        var active = candidates.Where(i => limits is null || limits[i] > values[i]).ToList();
        while (active.Count > 0 && extra > 0.000001)
        {
            var share = extra / active.Count;
            for (var j = active.Count - 1; j >= 0; j--)
            {
                var i = active[j];
                var increment = limits is null ? share : Math.Min(share, Math.Max(0, limits[i] - values[i]));
                values[i] += increment; extra -= increment;
                if (limits is not null && limits[i] - values[i] < 0.000001) active.RemoveAt(j);
            }
            if (limits is null) break;
        }
    }
}
