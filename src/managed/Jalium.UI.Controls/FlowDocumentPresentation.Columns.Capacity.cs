using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

internal sealed partial class FlowDocumentPresentation
{
    private readonly record struct ColumnFreeSpan(DependencyObject Source, double Start, double End);
    private readonly List<ColumnFreeSpan> _columnFreeSpans = [];

    // Only the unused tail before a following row can shrink an ancestor balance.
    // Final free space outside this context's footprint is never fed back.
    private void AddColumnFreeSpans(DocumentColumnScope scope, DocumentColumnPlan plan)
    {
        foreach (var slot in plan.Slots)
        {
            var start = slot.Top + slot.Used; var end = Math.Min(slot.Top + slot.Capacity, plan.UsedHeight);
            if (end - start > .00001) _columnFreeSpans.Add(new(scope.Source, scope.Top + start, scope.Top + end));
        }
    }

    private void ProjectColumnFreeSpans(DocumentColumnScope scope, IReadOnlyList<DocumentColumnSlice> slices)
    {
        var original = _columnFreeSpans.Where(span => InColumnScope(span.Source, scope)).ToArray();
        _columnFreeSpans.RemoveAll(span => InColumnScope(span.Source, scope));
        foreach (var span in original)
            foreach (var slice in slices)
            {
                if (slice.Spanner is { } spanner && !Within(span.Source, spanner)) continue;
                var start = Math.Max(span.Start, slice.Start); var end = Math.Min(span.End, slice.End);
                if (end - start > .00001) _columnFreeSpans.Add(span with { Start = start + slice.Dy, End = end + slice.Dy });
            }
    }

    private void MoveColumnFreeSpans(DependencyObject source, double dy, Rect? clip, bool removed)
    {
        for (var index = _columnFreeSpans.Count - 1; index >= 0; index--)
        {
            var span = _columnFreeSpans[index]; if (!Within(span.Source, source)) continue;
            var start = span.Start + dy; var end = span.End + dy;
            if (clip is { } bounds) { start = Math.Max(start, bounds.Top); end = Math.Min(end, bounds.Bottom); }
            if (removed || end - start <= .00001) _columnFreeSpans.RemoveAt(index);
            else _columnFreeSpans[index] = span with { Start = start, End = end };
        }
    }

    private List<(double Start, double End)> ReclaimableColumnSpace(DocumentColumnScope scope)
    {
        var protectedSpans = _lines.Skip(scope.FirstLine).Take(scope.EndLine - scope.FirstLine)
            .Where(line => line.Height > 0).Select(line => (Start: line.Top, End: line.Top + line.Height)).ToList();
        // Spanner margins are content, even where no glyph occupies their row.
        protectedSpans.AddRange(_fragmentBoxes.Skip(scope.FirstFragment).Take(scope.EndFragment - scope.FirstFragment)
            .Where(box => box.Source is not null && (bool)box.Source.GetValue(CssMultiColumnProperties.SpanProperty)!)
            .Select(box => (box.MarginTop, box.Bottom)));
        protectedSpans.AddRange(_heightSpaces.Where(span => InColumnScope(span.Source, scope)).Select(span => (span.Start, span.End)));
        var free = new List<(double Start, double End)>();
        foreach (var span in _columnFreeSpans.Where(span => InColumnScope(span.Source, scope)))
        {
            var pieces = new List<(double Start, double End)> { (span.Start, span.End) };
            foreach (var keep in protectedSpans)
            {
                var next = new List<(double Start, double End)>();
                foreach (var piece in pieces)
                {
                    if (keep.End <= piece.Start || keep.Start >= piece.End) { next.Add(piece); continue; }
                    if (keep.Start > piece.Start) next.Add((piece.Start, keep.Start));
                    if (keep.End < piece.End) next.Add((keep.End, piece.End));
                }
                pieces = next; if (pieces.Count == 0) break;
            }
            free.AddRange(pieces.Where(piece => piece.End - piece.Start > .00001));
        }
        var result = new List<(double Start, double End)>();
        foreach (var span in free.OrderBy(span => span.Start))
            if (result.Count > 0 && span.Start <= result[^1].End + .00001)
                result[^1] = (result[^1].Start, Math.Max(result[^1].End, span.End));
            else result.Add(span);
        return result;
    }

    private void UpdateNestedColumnConstraints(DocumentColumnScope scope, DocumentColumnPlan plan)
    {
        if (scope.Synthetic && !double.IsFinite(_paginationColumnHeight)) return;
        var children = _columnScopes.Where(child => ReferenceEquals(child.Parent, scope)).ToArray();
        if (children.Length == 0) return;
        var free = ReclaimableColumnSpace(scope);
        // Rebalance a whole sibling column set together; auto-fill keeps its full extent.
        var groupCapacities = new Dictionary<int, double>();
        foreach (var group in plan.Slices.Where(slice => slice.Balanced).GroupBy(slice => slice.Group))
        {
            var height = 0d;
            foreach (var slice in group)
            {
                var start = scope.Top + slice.Start; var end = scope.Top + slice.End;
                var blank = free.Sum(span => Math.Max(0, Math.Min(end, span.End) - Math.Max(start, span.Start)));
                height = Math.Max(height, Math.Min(slice.Capacity, end - start) - blank);
            }
            groupCapacities[group.Key] = Math.Max(1, height);
        }
        static double Capacity(double value) => value > 1e9 ? value : Math.Ceiling(Math.Max(1, value) * 1024) / 1024;
        foreach (var child in children)
        {
            var at = child.Top - scope.Top; var bottom = at + child.UsedHeight;
            var slices = plan.Slices.Where(slice => slice.End > at + .00001 && slice.Start < bottom - .00001).ToArray();
            if (slices.Length == 0 || slices.Any(slice => !double.IsFinite(slice.Capacity))) continue;
            var capacities = slices.Select((slice, index) => Capacity(groupCapacities.GetValueOrDefault(slice.Group, slice.Capacity) -
                (index == 0 ? Math.Max(0, at - slice.Start) : 0))).ToArray();
            var fallback = Capacity(double.IsFinite(plan.DefaultCapacity) ? plan.DefaultCapacity : slices.Max(slice => slice.Capacity));
            var next = new ColumnConstraint(fallback, capacities[0], capacities);
            if (_nestedColumnConstraints.TryGetValue(child.Source, out var prior) && Math.Abs(prior.Height - next.Height) < .01 &&
                prior.Capacities is { } old && old.Length == capacities.Length && old.Zip(capacities).All(pair => Math.Abs(pair.First - pair.Second) < .01)) continue;
            _nestedColumnConstraints[child.Source] = next; _columnConstraintsChanged = true;
        }
    }
}
