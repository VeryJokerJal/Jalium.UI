using Jalium.UI.Controls;

namespace Jalium.UI.Styling;

internal sealed partial class CssGridLayout
{
    private Panel? _parentGrid;
    private BorrowedAxis? _borrowedColumns, _borrowedRows;
    private double _parentAreaWidth = double.NaN;
    private bool _assignmentDirty;

    internal bool IsSubgridded(bool row) => Borrowed(row) is not null;

    private BorrowedAxis? Borrowed(bool row)
    {
        if (_parentGrid is null || _parentGrid.CssDisplayMode != CssDisplayMode.Grid || !ReferenceEquals(owner.VisualParent, _parentGrid))
        {
            _parentGrid = null; _borrowedColumns = _borrowedRows = null;
            return null;
        }
        var template = (CssComputedGridTracks)owner.GetValue(row ? CssGridProperties.RowsProperty : CssGridProperties.ColumnsProperty)!;
        return template.Tracks.IsSubgrid ? row ? _borrowedRows : _borrowedColumns : null;
    }

    private sealed class BorrowedAxis(int count, Dictionary<string, List<int>> names,
        Dictionary<string, List<(int Start, int End)>> areas, double defaultGap, bool reversed,
        double[]? sizes, double[]? gaps)
    {
        public int Count { get; } = count;
        public Dictionary<string, List<int>> Names { get; } = names;
        public Dictionary<string, List<(int Start, int End)>> Areas { get; } = areas;
        public double DefaultGap { get; } = defaultGap;
        public bool Reversed { get; } = reversed;
        public double[]? Sizes { get; } = sizes;
        public double[]? Gaps { get; } = gaps;
        public double GapAt(int index) => Gaps is not null && index >= 0 && index < Gaps.Length ? Gaps[index] : DefaultGap;

        public bool SameAs(BorrowedAxis? other) => other is not null && Count == other.Count && DefaultGap.Equals(other.DefaultGap) &&
            Reversed == other.Reversed && EqualArray(Sizes, other.Sizes) && EqualArray(Gaps, other.Gaps) &&
            Names.Count == other.Names.Count && Names.All(pair => other.Names.TryGetValue(pair.Key, out var values) && pair.Value.SequenceEqual(values)) &&
            Areas.Count == other.Areas.Count && Areas.All(pair => other.Areas.TryGetValue(pair.Key, out var values) && pair.Value.SequenceEqual(values));
        private static bool EqualArray(double[]? a, double[]? b) => a is null ? b is null : b is not null && a.AsSpan().SequenceEqual(b);
    }

    private void SetAssignment(Panel parent, BorrowedAxis? columns, BorrowedAxis? rows, double width)
    {
        var same = ReferenceEquals(parent, _parentGrid) && width.Equals(_parentAreaWidth) &&
            (columns?.SameAs(_borrowedColumns) ?? _borrowedColumns is null) && (rows?.SameAs(_borrowedRows) ?? _borrowedRows is null);
        _parentGrid = parent; _borrowedColumns = columns; _borrowedRows = rows; _parentAreaWidth = width;
        if (!same) { _assignmentDirty = true; owner.InvalidateCssAllocation(); }
    }

    private void AssignSubgrids(Item[] items, AxisDefinition columns, AxisDefinition rows, int firstColumn, int firstRow,
        double columnGap, double rowGap, SizedAxis? columnSizes, SizedAxis? rowSizes)
    {
        foreach (var item in items)
        {
            if (item.Box.Element is not Panel { CssDisplayMode: CssDisplayMode.Grid } child) continue;
            var columnTemplate = ((CssComputedGridTracks)child.GetValue(CssGridProperties.ColumnsProperty)!).Tracks;
            var rowTemplate = ((CssComputedGridTracks)child.GetValue(CssGridProperties.RowsProperty)!).Tracks;
            var borrowedColumns = columnTemplate.IsSubgrid
                ? BorrowAxis(columns, item.Column + firstColumn, item.ColumnSpan, firstColumn, columnGap, columnSizes,
                    owner.FlowDirection != child.FlowDirection) : null;
            var borrowedRows = rowTemplate.IsSubgrid
                ? BorrowAxis(rows, item.Row + firstRow, item.RowSpan, firstRow, rowGap, rowSizes, false) : null;
            CssDisplayLayout.GridFor(child).SetAssignment(owner, borrowedColumns, borrowedRows,
                columnSizes?.SpanSize(item.Column, item.ColumnSpan) ?? double.NaN);
        }
    }

    private static BorrowedAxis BorrowAxis(AxisDefinition parent, int start, int count, int first, double gap,
        SizedAxis? sized, bool reversed)
    {
        var names = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var (name, indices) in parent.Explicit.Lines)
        {
            var selected = indices.Where(index => index >= start && index <= start + count)
                .Select(index => reversed ? count - (index - start) : index - start).Order().ToList();
            if (selected.Count > 0) names[name] = selected;
        }
        var areas = new Dictionary<string, List<(int Start, int End)>>(StringComparer.Ordinal);
        foreach (var (name, intervals) in parent.Areas)
        {
            foreach (var interval in intervals)
            {
                var low = Math.Max(start, interval.Start); var high = Math.Min(start + count, interval.End);
                if (low >= high) continue;
                if (!areas.TryGetValue(name, out var selected)) areas[name] = selected = [];
                selected.Add(reversed ? (count - (high - start), count - (low - start)) : (low - start, high - start));
            }
        }
        double[]? sizes = null, gaps = null;
        if (sized is not null)
        {
            sizes = sized.Sizes.Skip(start - first).Take(count).ToArray();
            gaps = Enumerable.Range(start - first, Math.Max(0, count - 1))
                .Select(index => sized.Starts[index + 1] - sized.Starts[index] - sized.Sizes[index]).ToArray();
            if (reversed) { Array.Reverse(sizes); Array.Reverse(gaps); }
        }
        return new(count, names, areas, gap, reversed, sizes, gaps);
    }

    private static void ClampSubgridPlacement(Item[] items, AxisDefinition columns, AxisDefinition rows,
        ref int firstColumn, ref int firstRow, ref int columnCount, ref int rowCount)
    {
        foreach (var item in items)
        {
            if (columns.Shared is not null)
            {
                var start = item.Column + firstColumn; var end = start + item.ColumnSpan;
                item.Column = Math.Clamp(start, 0, columns.ExplicitCount - 1);
                item.ColumnSpan = Math.Clamp(end - item.Column, 1, columns.ExplicitCount - item.Column);
            }
            if (rows.Shared is not null)
            {
                var start = item.Row + firstRow; var end = start + item.RowSpan;
                item.Row = Math.Clamp(start, 0, rows.ExplicitCount - 1);
                item.RowSpan = Math.Clamp(end - item.Row, 1, rows.ExplicitCount - item.Row);
            }
        }
        if (columns.Shared is not null) { firstColumn = 0; columnCount = columns.ExplicitCount; }
        if (rows.Shared is not null) { firstRow = 0; rowCount = rows.ExplicitCount; }
    }

    private Contribution[] CollectContributions(Item[] items, bool row)
    {
        var contributions = new List<Contribution>();
        foreach (var item in items)
        {
            var start = row ? item.Row : item.Column;
            var span = row ? item.RowSpan : item.ColumnSpan;
            if (item.Box.Element is Panel { CssDisplayMode: CssDisplayMode.Grid } child &&
                CssDisplayLayout.GridFor(child) is { } subgrid && subgrid.Borrowed(row) is { } borrowed &&
                (row ? subgrid._snapshot?.RowContributions : item.SubgridColumnContributions) is { } source)
            {
                // Track occupancy belongs to the subgrid box even when it has no item in a particular track.
                contributions.Add(new(start, span, 0, 0));
                var (before, after) = subgrid.EdgeInsets(row, double.NaN);
                var basis = row ? subgrid._snapshot!.Available.Height : double.NaN;
                var normalGap = CssGapProperties.IsNormal(child, row);
                var ownGap = CssGapProperties.Resolve(child, row, basis);
                foreach (var contribution in source)
                {
                    var edgeBefore = contribution.Start == 0 ? before : normalGap ? 0 : (ownGap - borrowed.GapAt(contribution.Start - 1)) / 2;
                    var edgeAfter = contribution.Start + contribution.Span == borrowed.Count ? after : normalGap ? 0
                        : (ownGap - borrowed.GapAt(contribution.Start + contribution.Span - 1)) / 2;
                    var translated = borrowed.Reversed ? span - contribution.Start - contribution.Span : contribution.Start;
                    contributions.Add(new(start + translated, contribution.Span,
                        Math.Max(0, contribution.Min + edgeBefore + edgeAfter), Math.Max(0, contribution.Max + edgeBefore + edgeAfter)));
                }
                if (span == 1) contributions.Add(new(start, 1, Math.Max(0, before + after), Math.Max(0, before + after)));
                else
                {
                    if (borrowed.Reversed) (before, after) = (after, before);
                    contributions.Add(new(start, 1, Math.Max(0, before), Math.Max(0, before)));
                    contributions.Add(new(start + span - 1, 1, Math.Max(0, after), Math.Max(0, after)));
                }
            }
            else contributions.Add(new(start, span, row ? item.Box.MinContent.Height : item.Box.MinContent.Width,
                row ? item.Box.MaxContent.Height : item.Box.MaxContent.Width));
        }
        return contributions.ToArray();
    }

    private Contribution[] MeasureColumnContributions(Plan plan)
    {
        foreach (var item in plan.Items)
        {
            if (item.Box.Element is Panel { CssDisplayMode: CssDisplayMode.Grid } child &&
                CssDisplayLayout.GridFor(child) is { } grid && grid.IsSubgridded(false))
            {
                var nested = grid.CreatePlan(new Size(double.PositiveInfinity, double.PositiveInfinity));
                grid.AssignSubgrids(nested.Items, nested.Columns, nested.Rows, nested.FirstColumn, nested.FirstRow,
                    nested.ColumnGap, nested.RowGap, null, null);
                item.SubgridColumnContributions = grid.MeasureColumnContributions(nested);
            }
            else item.Box.MeasureIntrinsicWidth();
        }
        return CollectContributions(plan.Items, false);
    }

    private (double Before, double After) EdgeInsets(bool row, double widthBasis)
    {
        widthBasis = _parentAreaWidth;
        var margin = CssBoxMetrics.Margin(owner, widthBasis);
        var insets = CssBoxMetrics.ContentInsets(owner, widthBasis);
        margin = new(margin.Left + insets.Left, margin.Top + insets.Top, margin.Right + insets.Right, margin.Bottom + insets.Bottom);
        var before = row ? margin.Top : owner.FlowDirection == FlowDirection.RightToLeft ? margin.Right : margin.Left;
        var after = row ? margin.Bottom : owner.FlowDirection == FlowDirection.RightToLeft ? margin.Left : margin.Right;
        return (before, after);
    }

    private SizedAxis SizeAxis(AxisDefinition definition, int first, int count, double available, double gap,
        Contribution[] contributions, CssBoxAlignment alignment)
    {
        if (definition.Shared is not { Sizes: { } parentSizes } shared)
            return SizeTracks(definition, first, count, available, gap, contributions, alignment);
        var row = ReferenceEquals(shared, _borrowedRows);
        var sizes = parentSizes.ToArray();
        var starts = new double[count];
        var normalGap = CssGapProperties.IsNormal(owner, row);
        var gaps = new double[Math.Max(0, count - 1)];
        for (var i = 0; i < gaps.Length; i++) gaps[i] = normalGap ? shared.GapAt(i) : gap;
        var (before, after) = EdgeInsets(row, row ? owner.PreviousAvailableSize.Width : available);
        for (var i = 0; i < count; i++)
        {
            sizes[i] += i == 0 ? -before : (shared.GapAt(i - 1) - gaps[i - 1]) / 2;
            sizes[i] += i == count - 1 ? -after : (shared.GapAt(i) - gaps[i]) / 2;
            sizes[i] = Math.Max(0, sizes[i]);
            if (i > 0) starts[i] = starts[i - 1] + sizes[i - 1] + gaps[i - 1];
        }
        return new(sizes, starts, sizes.Sum() + gaps.Sum());
    }
}
