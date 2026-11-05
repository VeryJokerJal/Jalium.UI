namespace Jalium.UI.Styling;

/// <summary>Track constraints shared by table adapters; sources retain ownership of their boxes.</summary>
internal static partial class CssTableTracks
{
    internal sealed class Cell(DependencyObject source, int row, int column, int rowSpan, int columnSpan)
    {
        internal DependencyObject Source = source;
        internal int Row = row, Column = column, RowSpan = rowSpan, ColumnSpan = columnSpan;
        internal double Min, Max, Preferred = double.NaN, Height, Baseline, OffsetY;
        internal bool HasBaseline;
        internal HeightRequest SpecifiedHeight;
        internal WidthRequest SpecifiedWidth;
    }
    internal readonly record struct Column(double Width = double.NaN, double Weight = 0, WidthRequest Request = default, double Min = 0, double Max = double.PositiveInfinity);
    internal readonly record struct Axis(double[] Sizes, double[] Starts, double Extent, bool[]? Collapsed = null)
    {
        internal double Span(int start, int count)
        {
            var end = start + count - 1;
            if (Collapsed is not null)
            {
                while (start <= end && Collapsed[start]) start++;
                while (end >= start && Collapsed[end]) end--;
            }
            return end < start ? 0 : Starts[end] + Sizes[end] - Starts[start];
        }
    }

    internal static (List<Cell> Cells, int Count) Place(IReadOnlyList<IReadOnlyList<(DependencyObject Source, int Columns, int Rows)>> rows)
    {
        var result = new List<Cell>(); var occupied = new Dictionary<int, int>(); var count = 0;
        for (var row = 0; row < rows.Count; row++)
        {
            var column = 0;
            foreach (var item in rows[row])
            {
                // Bound an individual malformed span without dropping any source cell.
                var span = Math.Clamp(item.Columns, 1, 1000);
                var end = Math.Clamp(item.Rows, 1, rows.Count - row);
                while (true)
                {
                    var conflict = -1;
                    for (var i = column; i < column + span; i++)
                        if (occupied.TryGetValue(i, out var until) && until > row) conflict = i;
                    if (conflict < 0) break;
                    column = conflict + 1;
                }
                result.Add(new(item.Source, row, column, end, span));
                for (var i = column; i < column + span; i++) occupied[i] = row + end;
                column += span; count = Math.Max(count, column);
            }
        }
        return (result, count);
    }

    private static Axis FixedColumns(int count, double available, double gap, IReadOnlyList<Column> definitions, IReadOnlyList<Cell> cells)
    {
        if (count == 0) return new([], [], 0);
        var sizes = new double[count]; var assigned = new bool[count];
        var basis = double.IsFinite(available) ? Math.Max(0, available - gap * (count + 1)) : available;
        for (var i = 0; i < Math.Min(count, definitions.Count); i++)
        {
            if (definitions[i].Request.Percentage && definitions[i].Request.Affine(out var offset, out var fraction) && offset == 0 && fraction == 0) continue;
            var width = definitions[i].Request.Specified ? definitions[i].Request.Resolve(basis) : definitions[i].Width;
            if (double.IsFinite(width)) { sizes[i] = Math.Clamp(Math.Max(0, width), definitions[i].Min, definitions[i].Max); assigned[i] = true; }
        }
        foreach (var cell in cells.Where(cell => cell.Row == 0))
        {
            var width = cell.SpecifiedWidth.Specified ? cell.SpecifiedWidth.Resolve(basis) : cell.Preferred;
            if (!double.IsFinite(width)) continue;
            var indices = Enumerable.Range(cell.Column, cell.ColumnSpan).Where(i => !assigned[i]).ToArray();
            var used = Enumerable.Range(cell.Column, cell.ColumnSpan).Where(i => assigned[i]).Sum(i => sizes[i]);
            var each = indices.Length == 0 ? 0 : Math.Max(0, width - gap * (cell.ColumnSpan - 1) - used) / indices.Length;
            foreach (var i in indices) { sizes[i] = each; assigned[i] = true; }
        }
        var target = double.IsFinite(available) ? Math.Max(sizes.Sum(), basis) : sizes.Sum();
        var extra = Math.Max(0, target - sizes.Sum());
        var flexible = Enumerable.Range(0, count).Where(i => !assigned[i]).ToArray();
        if (flexible.Length == 0) flexible = Enumerable.Range(0, count).ToArray();
        var weight = flexible.Sum(i => i < definitions.Count ? Math.Max(0, definitions[i].Weight) : 0);
        foreach (var i in flexible)
            sizes[i] += extra * (weight > 0 ? (i < definitions.Count ? Math.Max(0, definitions[i].Weight) : 0) / weight : 1d / flexible.Length);
        return Build(sizes, gap);
    }

    private static void Grow(double[] sizes, int start, int count, double required)
    {
        var current = 0d; for (var i = start; i < start + count; i++) current += sizes[i];
        var each = Math.Max(0, required - current) / count;
        for (var i = start; i < start + count; i++) sizes[i] += each;
    }
    private static Axis Build(double[] sizes, double gap, bool[]? collapsed = null)
    {
        var starts = new double[sizes.Length]; var at = sizes.Length == 0 ? 0 : gap;
        for (var i = 0; i < sizes.Length; i++) { starts[i] = at; if (collapsed?[i] != true) at += sizes[i] + gap; }
        return new(sizes, starts, at);
    }
}
