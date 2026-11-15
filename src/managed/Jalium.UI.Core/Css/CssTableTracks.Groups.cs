namespace Jalium.UI.Styling;

internal static partial class CssTableTracks
{
    internal readonly record struct RowGroup(int First, int Count, HeightRequest Height = default, bool Body = true);

    private sealed class GroupRowsState(RowGroup group, double[] minimums, HeightRequest[] definitions, Cell[] cells, bool[] collapsed, double height)
    {
        internal readonly RowGroup Group = group;
        internal readonly double[] Minimums = minimums;
        internal readonly HeightRequest[] Definitions = definitions;
        internal readonly Cell[] Cells = cells;
        internal readonly bool[] Collapsed = collapsed;
        internal double Height = height;
    }

    private static Axis GroupRows(int count, double gap, IReadOnlyList<double> minimums, IReadOnlyList<Cell> cells,
        bool[]? collapsed, double minimumHeight, IReadOnlyList<HeightRequest>? definitions, IReadOnlyList<RowGroup> groups)
    {
        var sections = new List<GroupRowsState>();
        var byRow = cells.ToLookup(cell => cell.Row);
        foreach (var group in groups)
        {
            if (group.Count == 0) continue;
            var hidden = Enumerable.Range(group.First, group.Count).Select(row => collapsed?[row] == true).ToArray();
            if (hidden.All(value => value)) continue;
            var minima = Enumerable.Range(group.First, group.Count).Select(row => row < minimums.Count ? minimums[row] : double.NaN).ToArray();
            var requests = Enumerable.Range(group.First, group.Count).Select(row => row < (definitions?.Count ?? 0) ? definitions![row] : default).ToArray();
            var localCells = Enumerable.Range(group.First, group.Count).SelectMany(row => byRow[row]).Select(cell =>
                new Cell(cell.Source, cell.Row - group.First, cell.Column, Math.Min(cell.RowSpan, group.First + group.Count - cell.Row), cell.ColumnSpan)
                { Height = cell.Height, Baseline = cell.Baseline, OffsetY = cell.OffsetY, SpecifiedHeight = cell.SpecifiedHeight }).ToArray();
            var natural = Rows(group.Count, gap, minima, localCells, hidden, definitions: requests);
            // A section owns its internal gaps; the table owns the outer and
            // inter-section gaps. Empty sections cannot invent a source row.
            var height = Math.Max(Math.Max(0, natural.Extent - 2 * gap), group.Height.Resolve(0));
            sections.Add(new(group, minima, requests, localCells, hidden, height));
        }
        var sizes = new double[count];
        if (sections.Count == 0) return Build(sizes, gap, collapsed);
        var spacing = gap * (sections.Count + 1);
        var target = Math.Max(sections.Sum(section => section.Height) + spacing, double.IsFinite(minimumHeight) ? minimumHeight : 0);
        var remaining = Math.Max(0, target - spacing - sections.Sum(section => section.Height));
        var deficits = sections.Select(section => section.Group.Height.Percentage
            ? Math.Max(0, section.Group.Height.Resolve(Math.Max(0, target - spacing)) - section.Height) : 0).ToArray();
        var total = deficits.Sum(); var used = Math.Min(remaining, total);
        if (total > 0) for (var index = 0; index < sections.Count; index++) sections[index].Height += used * deficits[index] / total;
        remaining -= used;
        if (remaining > 0)
        {
            // Body groups receive surplus before headers/footers; within that
            // class auto, then fixed, then percentage groups have priority.
            var candidates = sections.Any(section => section.Group.Body) ? sections.Where(section => section.Group.Body).ToArray() : sections.ToArray();
            var priority = candidates.Min(section => section.Group.Height.Percentage ? 2 : section.Group.Height.Specified ? 1 : 0);
            var growing = candidates.Where(section => (section.Group.Height.Percentage ? 2 : section.Group.Height.Specified ? 1 : 0) == priority).ToArray();
            var weight = growing.Sum(section => section.Height); var left = remaining;
            for (var index = 0; index < growing.Length; index++)
            {
                var section = growing[index]; var delta = index == growing.Length - 1 ? left : remaining * (weight > 0 ? section.Height / weight : 1d / growing.Length);
                section.Height += delta; left -= delta;
            }
        }
        foreach (var section in sections)
        {
            // Preserve the shared row percentage reference: the full grid
            // height. A group's allocation limits how much its rows can grow.
            var rows = Rows(section.Group.Count, gap, section.Minimums, section.Cells, section.Collapsed,
                section.Height + 2 * gap, section.Definitions, percentageResolutionHeight: target);
            Array.Copy(rows.Sizes, 0, sizes, section.Group.First, rows.Sizes.Length);
        }
        return Build(sizes, gap, collapsed);
    }
}
