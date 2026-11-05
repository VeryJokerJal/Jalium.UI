using Jalium.UI.Documents;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Styling;
using DocList = Jalium.UI.Documents.List;

namespace Jalium.UI.Controls;

public partial class RichTextBox
{
    internal WritingToolsParagraph[] GetWritingToolsStructure(int rangeStart = 0, int rangeLength = -1)
    {
        string sourceText = _document.GetText();
        PrepareCssEditingLayout();
        var view = _cssDocumentView;
        sourceText = view?.EditorExposedText ?? sourceText;
        int textLength = view?.EditorExposedText.Length ?? _document.GetText().Length;
        if (rangeStart < 0 || rangeStart > textLength || rangeLength < -1 || rangeLength > textLength - rangeStart)
            throw new ArgumentOutOfRangeException(nameof(rangeStart));
        if (rangeLength < 0) rangeLength = textLength - rangeStart;
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
        var records = new List<WritingToolsParagraph>();
        foreach (var (element, sourceParagraphStart, paragraphLength, emptyCell) in EnumerateWritingToolsNodes(_document.Blocks))
        {
            int paragraphStart = sourceParagraphStart;
            int paragraphEnd = paragraphStart + paragraphLength;
            if (view is not null)
            {
                paragraphStart = view.EditorExposedOffset(paragraphStart);
                paragraphEnd = view.EditorExposedOffset(paragraphEnd);
            }
            int from = Math.Max(rangeStart, paragraphStart), to = Math.Min(rangeStart + rangeLength, paragraphEnd);
            if (to <= from) continue;
            var lists = new List<WritingToolsList>(); var cells = new List<WritingToolsCell>();
            var owners = new List<TextElement>();
            for (DependencyObject? parent = emptyCell ? element : element.Parent; parent is not null; parent = (parent as TextElement)?.Parent)
                if (parent is ListItem or TableCell) owners.Add((TextElement)parent);
            owners.Reverse();
            var ancestors = new List<WritingToolsAncestor>();
            foreach (var owner in owners)
            {
                if (owner is ListItem { Parent: DocList list } item)
                {
                    ancestors.Add(new("list", lists.Count, Id(item), WritingToolsDocumentFragment.OriginFor(item)));
                    lists.Add(new(Id(list), Marker(list.MarkerStyle), list.StartIndex));
                }
                if (owner is TableCell { Parent: TableRow row } cell && row.Parent is TableRowGroup { Parent: Table table })
                {
                    var position = Locate(table, cell);
                    int columns = Math.Max(table.Columns.Count, grids[table].Max(pair => pair.Value.Column + pair.Key.ColumnSpan));
                    ancestors.Add(new("table", cells.Count, Origin: WritingToolsDocumentFragment.OriginFor(cell)));
                    cells.Add(new(Id(table), position.Row, position.Column, cell.RowSpan, cell.ColumnSpan, columns));
                }
            }
            if (lists.Count > 32 || cells.Count > 32) throw new NotSupportedException("The document exceeds the native text service nesting limit.");
            var inlineBreaks = new List<int>();
            if (!emptyCell)
                for (int i = from; i < Math.Min(to, paragraphEnd - 1); ++i)
                    if (sourceText[i] is '\n' or '\r' or '\u2029') inlineBreaks.Add(i - rangeStart);
            records.Add(new(from - rangeStart, to - from, lists.ToArray(), cells.ToArray(), ancestors.ToArray(), emptyCell,
                inlineBreaks.Count == 0 ? null : inlineBreaks.ToArray(), WritingToolsDocumentFragment.OriginFor(element)));
        }
        return records.ToArray();
    }

    private IEnumerable<(TextElement Element, int Start, int Length, bool EmptyCell)> EnumerateWritingToolsNodes(IEnumerable<Block> blocks)
    {
        foreach (var block in blocks)
        {
            if (_cssDocumentView is not null && CssDisplayProperties.SuppressesBoxTree(block))
                continue;

            if (block is Paragraph paragraph)
                yield return (paragraph, paragraph.ContentStart.DocumentOffset,
                    TextElement.GetContentLength(paragraph) + 1, false);
            else if (block is Table table)
            {
                foreach (var group in table.RowGroups)
                {
                    if (_cssDocumentView is not null && CssDisplayProperties.SuppressesBoxTree(group))
                        continue;
                    foreach (var row in group.Rows)
                    {
                        if (_cssDocumentView is not null && CssDisplayProperties.SuppressesBoxTree(row))
                            continue;
                        foreach (var cell in row.Cells)
                        {
                            if (_cssDocumentView is not null && CssDisplayProperties.SuppressesBoxTree(cell))
                                continue;
                            if (cell.Blocks.Count == 0)
                                yield return (cell, cell.ContentStart.DocumentOffset, 1, true);
                            else
                                foreach (var node in EnumerateWritingToolsNodes(cell.Blocks)) yield return node;
                        }
                    }
                }
            }
            else
            {
                IEnumerable<Block> children = block switch
                {
                    Section section => section.Blocks,
                    DocList list => list.ListItems
                        .Where(item => _cssDocumentView is null || !CssDisplayProperties.SuppressesBoxTree(item))
                        .SelectMany(item => item.Blocks),
                    _ => Array.Empty<Block>()
                };
                foreach (var node in EnumerateWritingToolsNodes(children)) yield return node;
            }
        }
    }

    private static string Marker(TextMarkerStyle marker) => marker switch
    {
        TextMarkerStyle.None => "", TextMarkerStyle.Box => "{box}",
        TextMarkerStyle.Decimal => "{decimal}", TextMarkerStyle.LowerLatin => "{lower-alpha}",
        TextMarkerStyle.UpperLatin => "{upper-alpha}", TextMarkerStyle.LowerRoman => "{lower-roman}",
        TextMarkerStyle.UpperRoman => "{upper-roman}", TextMarkerStyle.Circle => "{circle}",
        TextMarkerStyle.Square => "{square}", _ => "{disc}"
    };
}
