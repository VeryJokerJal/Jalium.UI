namespace Jalium.UI.Styling;

/// <summary>Project fully measured tracks without changing their original content constraints.</summary>
internal sealed class CssTableVisibility(int rows, int columns)
{
    internal bool[] Rows { get; } = new bool[rows];
    internal bool[] Columns { get; } = new bool[columns];
    internal bool Any => Rows.Contains(true) || Columns.Contains(true);
    internal static bool Collapsed(DependencyObject? source) => source is not null &&
        CssDisplayProperties.ComputedVisibility(CssNode.Get(source)) == CssVisibilityMode.Collapse;
    internal static bool Visible(DependencyObject source) =>
        CssDisplayProperties.ComputedVisibility(CssNode.Get(source)) == CssVisibilityMode.Visible;
    internal static bool All(bool[] mask, int first, int count) =>
        Enumerable.Range(first, count).All(i => mask[i]);
    internal bool Removed(CssTableTracks.Cell cell) => All(Rows, cell.Row, cell.RowSpan) || All(Columns, cell.Column, cell.ColumnSpan);
    internal bool Intersects(CssTableTracks.Cell cell) =>
        Enumerable.Range(cell.Row, cell.RowSpan).Any(i => Rows[i]) || Enumerable.Range(cell.Column, cell.ColumnSpan).Any(i => Columns[i]);
    internal static CssTableTracks.Axis Project(CssTableTracks.Axis axis, bool[] mask, double gap)
    {
        if (!mask.Contains(true)) return axis;
        var sizes = new double[mask.Length]; var starts = new double[mask.Length];
        var at = mask.Any(hidden => !hidden) ? gap : 0;
        for (var i = 0; i < mask.Length; i++)
        {
            starts[i] = at;
            if (!mask[i]) { sizes[i] = axis.Sizes[i]; at += sizes[i] + gap; }
        }
        return new(sizes, starts, at, mask);
    }

    internal static CssTableTracks.Axis Compact(CssTableTracks.Axis axis, bool[] mask) =>
        mask.Contains(true) ? new(axis.Sizes.Where((_, i) => !mask[i]).ToArray(), axis.Starts.Where((_, i) => !mask[i]).ToArray(), axis.Extent) : axis;
}
