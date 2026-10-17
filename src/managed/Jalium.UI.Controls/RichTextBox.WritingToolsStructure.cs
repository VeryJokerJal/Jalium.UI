using Jalium.UI.Documents;
using Jalium.UI.Controls.Platform;
using DocList = Jalium.UI.Documents.List;

namespace Jalium.UI.Controls;

public partial class RichTextBox
{
    internal WritingToolsParagraph[] GetWritingToolsStructure(int rangeStart = 0, int rangeLength = -1)
    {
        var identities = new Dictionary<object, string>(ReferenceEqualityComparer.Instance);
        var grids = new Dictionary<Table, Dictionary<TableCell, (int Row, int Column)>>();
        string Id(object value)
        {
            if (!identities.TryGetValue(value, out string? id))
                identities[value] = id = identities.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return id;
        }
        (int Row, int Column) Locate(Table table, TableCell target)
        {
            if (!grids.TryGetValue(table, out var grid))
            {
                grid = new(); grids[table] = grid;
                var occupied = new HashSet<(int, int)>(); int rowIndex = 0;
                foreach (var group in table.RowGroups)
                foreach (var row in group.Rows)
                {
                    int column = 0;
                    foreach (var cell in row.Cells)
                    {
                        while (occupied.Contains((rowIndex, column))) ++column;
                        if (cell.RowSpan is < 1 or > 4096 || cell.ColumnSpan is < 1 or > 256 ||
                            rowIndex > 4095 || cell.RowSpan > 4096 - rowIndex || column + cell.ColumnSpan > 256)
                            throw new NotSupportedException("The table exceeds the native text service limit.");
                        grid[cell] = (rowIndex, column);
                        for (int y = 0; y < cell.RowSpan; ++y)
                        for (int x = 0; x < cell.ColumnSpan; ++x)
                            if (!occupied.Add((rowIndex + y, column + x)))
                                throw new NotSupportedException("The table contains overlapping cell spans.");
                        column += cell.ColumnSpan;
                    }
                    ++rowIndex;
                }
            }
            return grid[target];
        }
        if (rangeLength < 0) rangeLength = _document.GetText().Length - rangeStart;
        var records = new List<WritingToolsParagraph>();
        foreach (var paragraph in _document.EnumerateParagraphs())
        {
            int paragraphStart = _document.GetDocumentOffset(paragraph, 0);
            int paragraphLength = TextElement.GetContentLength(paragraph) + Environment.NewLine.Length;
            int from = Math.Max(rangeStart, paragraphStart), to = Math.Min(rangeStart + rangeLength, paragraphStart + paragraphLength);
            if (to <= from) continue;
            var lists = new List<WritingToolsList>(); var cells = new List<WritingToolsCell>();
            for (DependencyObject? parent = paragraph.Parent; parent is not null; parent = (parent as TextElement)?.Parent)
            {
                if (parent is ListItem { Parent: DocList list })
                    lists.Add(new(Id(list), Marker(list.MarkerStyle), list.StartIndex));
                if (parent is TableCell { Parent: TableRow row } cell && row.Parent is TableRowGroup { Parent: Table table })
                {
                    var position = Locate(table, cell);
                    int columns = Math.Max(table.Columns.Count, grids[table].Max(pair => pair.Value.Column + pair.Key.ColumnSpan));
                    cells.Add(new(Id(table), position.Row, position.Column, cell.RowSpan, cell.ColumnSpan, columns));
                }
            }
            lists.Reverse(); cells.Reverse();
            records.Add(new(from - rangeStart, to - from, lists.ToArray(), cells.ToArray()));
        }
        return records.ToArray();
    }
    private static string Marker(TextMarkerStyle marker) => marker switch
    {
        TextMarkerStyle.Decimal => "{decimal}", TextMarkerStyle.LowerLatin => "{lower-alpha}",
        TextMarkerStyle.UpperLatin => "{upper-alpha}", TextMarkerStyle.LowerRoman => "{lower-roman}",
        TextMarkerStyle.UpperRoman => "{upper-roman}", TextMarkerStyle.Circle => "{circle}",
        TextMarkerStyle.Square => "{square}", _ => "{disc}"
    };
}

