namespace Jalium.UI.Styling;

internal static partial class CssTableTracks
{
    internal readonly record struct MissingSlot(int Row, int Column, int ColumnSpan);

    internal static int MergeColumns(IReadOnlyList<Cell> cells, int count, int explicitColumns, bool fixedMode)
    {
        count = Math.Max(count, explicitColumns);
        if (fixedMode || count < 2) return count;
        // Adjacent implicit tracks have identical occupants exactly when no
        // source cell starts or ends at their boundary. Explicit tracks remain.
        var boundaries = new bool[count + 1];
        foreach (var cell in cells)
        { boundaries[cell.Column] = true; boundaries[cell.Column + cell.ColumnSpan] = true; }
        var map = new int[count]; var last = 0;
        for (var column = 1; column < count; column++)
        {
            if (column < explicitColumns || boundaries[column]) last++;
            map[column] = last;
        }
        foreach (var cell in cells)
        {
            var end = cell.Column + cell.ColumnSpan - 1;
            cell.ColumnSpan = map[end] - map[cell.Column] + 1;
            cell.Column = map[cell.Column];
        }
        return last + 1;
    }

    internal static List<MissingSlot> MissingCells(int rows, int columns, IReadOnlyList<Cell> cells)
    {
        var result = new List<MissingSlot>();
        if (columns == 0) return result;
        var starts = cells.ToLookup(cell => cell.Row);
        var ends = cells.ToLookup(cell => cell.Row + cell.RowSpan);
        var active = new SortedDictionary<int, Cell>();
        var gaps = new List<(int Column, int Span)> { (0, columns) };
        for (var row = 0; row < rows; row++)
        {
            var changed = false;
            foreach (var cell in ends[row]) { active.Remove(cell.Column); changed = true; }
            foreach (var cell in starts[row]) { active.Add(cell.Column, cell); changed = true; }
            // A long rowspan does not require rescanning its occupied columns
            // on every intervening row when the active set has not changed.
            if (changed)
            {
                gaps.Clear(); var column = 0;
                foreach (var cell in active.Values)
                {
                    if (cell.Column > column) gaps.Add((column, cell.Column - column));
                    column = cell.Column + cell.ColumnSpan;
                }
                if (column < columns) gaps.Add((column, columns - column));
            }
            foreach (var gap in gaps) result.Add(new(row, gap.Column, gap.Span));
        }
        return result;
    }
}
