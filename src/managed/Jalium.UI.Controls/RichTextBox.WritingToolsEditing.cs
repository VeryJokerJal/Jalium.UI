using System.Text;
using Jalium.UI.Documents;
using Jalium.UI.Styling;
using DocList = Jalium.UI.Documents.List;

namespace Jalium.UI.Controls;

public partial class RichTextBox
{
    private TextPointer? InsertCssWritingToolsFragment(WritingToolsDocumentFragment fragment, FlowDocumentPresentation view,
        int start, int length, Func<bool> current)
    {
        var document = _document;
        string raw = document.GetText();
        if (view.EditorExposedText.Length == 0 && raw.Length > 0) return null;
        var hidden = HiddenRoots(CssNode.Get(document)).OfType<TextElement>()
            .Select(element => (Element: element, Text: SourceText(element))).ToArray();
        var ranges = view.EditorSourceRanges(start, length);
        int sourceStart = view.EditorSourceOffset(start);
        var insertion = view.EditorInsertionPosition(sourceStart);
        if (insertion?.Parent is TextElement element && CssDisplayProperties.SuppressesBoxTree(element))
        {
            sourceStart = view.EditorSourceOffset(start, forward: false);
            insertion = view.EditorInsertionPosition(sourceStart, forward: false);
        }
        var survivingContainers = EnumerateInlineUIContainers(document.Blocks)
            .Where(c => !CssDisplayProperties.SuppressesBoxTree(c) && !IsOffsetDeleted(document.GetDocumentOffset(c, 0), ranges))
            .ToArray();

        TextPointer position;
        if (raw != view.EditorExposedText && FindWholeBlocks(document.Blocks) is { } blocks)
        {
            // Removing an implicit paragraph terminator through the plain-text
            // editor would merge the next hidden paragraph into a visible one.
            // Replace complete visible siblings in their existing collection while
            // preserving the relative slot positions of any intervening hidden blocks.
            int common = Math.Min(blocks.Items.Count, fragment.Blocks.Count);
            int lastInsertIndex = blocks.Owner.IndexOf(blocks.Items[0]);
            for (int i = 0; i < common; i++)
            {
                if (!current()) return null;
                int slot = blocks.Owner.IndexOf(blocks.Items[i]);
                blocks.Owner.RemoveAt(slot);
                if (!current()) return null;
                blocks.Owner.Insert(slot, fragment.Blocks[i]);
                lastInsertIndex = slot + 1;
            }
            for (int i = blocks.Items.Count - 1; i >= common; i--)
            {
                if (!current()) return null;
                blocks.Owner.Remove(blocks.Items[i]);
            }
            for (int i = common; i < fragment.Blocks.Count; i++)
            {
                if (!current()) return null;
                blocks.Owner.Insert(lastInsertIndex++, fragment.Blocks[i]);
            }
            position = fragment.Blocks.LastOrDefault()?.ContentEnd ?? document.GetPositionAtOffset(sourceStart, LogicalDirection.Forward) ?? document.ContentStart;
        }
        else
        {
            // Later source ranges can be removed without shifting the original
            // insertion point. Hidden gaps are never part of a deletion range.
            bool replaceFirst = ranges.Length > 0 && ranges[0].Start == sourceStart;
            for (int index = ranges.Length - 1; index >= (replaceFirst ? 1 : 0); --index)
            {
                if (!current()) return null;
                DocumentTextEditing.Replace(document, ranges[index].Start, ranges[index].Length, string.Empty);
            }
            if (!current()) return null;
            position = fragment.Insert(document, sourceStart, replaceFirst ? ranges[0].Length : 0, current, insertion);
        }
        if (!current()) return null;
        PrepareCssEditingLayout();
        // Non-contiguous structural ranges can cross a container which the
        // mature insertion pipeline cannot merge. Let the outer transaction
        // restore the graph instead of deleting or exposing hidden content.
        bool hiddenPreserved = hidden.All(item => ReferenceEquals(item.Element.GetFlowDocument(), document) &&
            CssDisplayProperties.SuppressesBoxTree(item.Element) && SourceText(item.Element) == item.Text);
        bool containersPreserved = survivingContainers.All(c => ReferenceEquals(c.GetFlowDocument(), document) && c.Child is not null);
        return hiddenPreserved && containersPreserved ? position : null;

        string SourceText(TextElement element)
            => document.GetText().Substring(document.GetDocumentOffset(element, 0), TextElement.GetContentLength(element));

        static bool IsOffsetDeleted(int offset, (int Start, int Length)[] deletedRanges)
        {
            for (int i = 0; i < deletedRanges.Length; i++)
            {
                if (offset >= deletedRanges[i].Start && offset < deletedRanges[i].Start + deletedRanges[i].Length)
                    return true;
            }
            return false;
        }

        WritingToolsBlocks? FindWholeBlocks(BlockCollection owner)
        {
            foreach (var block in owner)
            {
                IEnumerable<BlockCollection> children = block switch
                {
                    Section section => [section.Blocks],
                    DocList list => list.ListItems.Select(item => item.Blocks),
                    Table table => table.RowGroups.SelectMany(group => group.Rows).SelectMany(row => row.Cells).Select(cell => cell.Blocks),
                    _ => [],
                };
                foreach (var collection in children)
                    if (FindWholeBlocks(collection) is { } nested) return nested;
            }
            var selected = new List<Block>(); int cursor = start;
            foreach (var block in owner)
            {
                int source = document.GetDocumentOffset(block, 0), count = FlowDocument.GetBlockLength(block);
                int from = view.EditorExposedOffset(source), to = view.EditorExposedOffset(source + count);
                if (to <= start || from >= start + length || to <= from) continue;
                if (from != cursor || to > start + length || to - from != count) return null;
                selected.Add(block); cursor = to;
            }
            return selected.Count > 0 && cursor == start + length ? new(owner, selected) : null;
        }
    }

    private sealed record WritingToolsBlocks(BlockCollection Owner, List<Block> Items);

    private static IEnumerable<InlineUIContainer> EnumerateInlineUIContainers(IEnumerable<Block> blocks)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    foreach (var container in EnumerateInlineUIContainers(paragraph.Inlines))
                        yield return container;
                    break;
                case Section section:
                    foreach (var container in EnumerateInlineUIContainers(section.Blocks))
                        yield return container;
                    break;
                case DocList list:
                    foreach (var item in list.ListItems)
                        foreach (var container in EnumerateInlineUIContainers(item.Blocks))
                            yield return container;
                    break;
                case Table table:
                    foreach (var group in table.RowGroups)
                        foreach (var row in group.Rows)
                            foreach (var cell in row.Cells)
                                foreach (var container in EnumerateInlineUIContainers(cell.Blocks))
                                    yield return container;
                    break;
            }
        }
    }

    private static IEnumerable<InlineUIContainer> EnumerateInlineUIContainers(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is InlineUIContainer container)
                yield return container;
            else if (inline is Span span)
                foreach (var nested in EnumerateInlineUIContainers(span.Inlines))
                    yield return nested;
        }
    }

    private static IEnumerable<DependencyObject> HiddenRoots(CssNode node)
    {
        if (CssDisplayProperties.SuppressesBoxTree(node.Target)) yield return node.Target;
        else foreach (var child in node.EnumerateChildren())
            foreach (var hidden in HiddenRoots(child)) yield return hidden;
    }

    /// <summary>
    /// Navigates focus/caret from <paramref name="currentCell"/> to the next (Tab) or previous (Shift+Tab)
    /// visible <see cref="TableCell"/> in the same <see cref="Table"/>, skipping cells, rows, row groups,
    /// and columns hidden via <c>display: none</c> or <c>visibility: collapse</c>/<c>hidden</c>.
    /// </summary>
    internal TableCell? NavigateTableCell(TableCell currentCell, bool forward = true)
    {
        ArgumentNullException.ThrowIfNull(currentCell);
        PrepareCssEditingLayout();
        if (FindOwningTable(currentCell) is not { } table)
            return null;
        var navigable = GetNavigableTableCells(table);
        if (navigable.Length == 0)
            return null;

        int currentIndex = Array.IndexOf(navigable, currentCell);
        TableCell? target;
        if (currentIndex >= 0)
        {
            int nextIndex = currentIndex + (forward ? 1 : -1);
            if (nextIndex < 0 || nextIndex >= navigable.Length)
                return null;
            target = navigable[nextIndex];
        }
        else
        {
            var allCells = table.RowGroups.SelectMany(g => g.Rows).SelectMany(r => r.Cells).ToList();
            int rawPos = allCells.IndexOf(currentCell);
            if (rawPos < 0) return null;
            target = forward
                ? allCells.Skip(rawPos + 1).FirstOrDefault(c => navigable.Contains(c))
                : allCells.Take(rawPos).LastOrDefault(c => navigable.Contains(c));
        }

        if (target is not null)
        {
            var targetPos = target.Blocks.OfType<Paragraph>().FirstOrDefault()?.ContentStart ?? target.ContentStart;
            Selection.Select(targetPos, targetPos);
        }
        return target;
    }

    internal TableCell? NavigateTableCell(bool forward = true)
    {
        for (DependencyObject? current = Selection.Start.Parent; current is not null; current = (current as TextElement)?.Parent)
        {
            if (current is TableCell cell)
                return NavigateTableCell(cell, forward);
        }
        return null;
    }

    internal TableCell[] GetNavigableTableCells(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);
        PrepareCssEditingLayout();
        if (!IsElementVisibleInTable(table))
            return [];

        var grid = BuildTableCellCoordinates(table);
        var result = new List<TableCell>();
        foreach (var group in table.RowGroups)
        {
            if (!IsElementVisibleInTable(group)) continue;
            foreach (var row in group.Rows)
            {
                if (!IsElementVisibleInTable(row)) continue;
                foreach (var cell in row.Cells)
                {
                    if (!IsElementVisibleInTable(cell)) continue;
                    if (grid.TryGetValue(cell, out var pos) && IsTableColumnCollapsed(table, pos.Column))
                        continue;
                    result.Add(cell);
                }
            }
        }
        return result.ToArray();
    }

    internal string ExportVisibleTableText(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);
        PrepareCssEditingLayout();
        if (!IsElementVisibleInTable(table))
            return string.Empty;

        var grid = BuildTableCellCoordinates(table);
        var sb = new StringBuilder();
        bool firstRow = true;
        foreach (var group in table.RowGroups)
        {
            if (!IsElementVisibleInTable(group)) continue;
            foreach (var row in group.Rows)
            {
                if (!IsElementVisibleInTable(row)) continue;
                var cellTexts = new List<string>();
                foreach (var cell in row.Cells)
                {
                    if (!IsElementVisibleInTable(cell)) continue;
                    if (grid.TryGetValue(cell, out var pos) && IsTableColumnCollapsed(table, pos.Column))
                        continue;
                    cellTexts.Add(ExtractVisibleCellText(cell));
                }
                if (cellTexts.Count == 0) continue;
                if (!firstRow) sb.Append('\n');
                sb.AppendJoin('\t', cellTexts);
                firstRow = false;
            }
        }
        return sb.ToString();
    }

    private static string ExtractVisibleCellText(TableCell cell)
    {
        var sb = new StringBuilder();
        bool firstBlock = true;
        foreach (var block in cell.Blocks)
        {
            if (!IsElementVisibleInTable(block)) continue;
            if (!firstBlock) sb.Append(' ');
            AppendVisibleBlockText(block, sb);
            firstBlock = false;
        }
        return sb.ToString();
    }

    private static void AppendVisibleBlockText(Block block, StringBuilder sb)
    {
        switch (block)
        {
            case Paragraph paragraph:
                AppendVisibleInlinesText(paragraph.Inlines, sb);
                break;
            case Section section:
                foreach (var child in section.Blocks)
                    if (IsElementVisibleInTable(child))
                        AppendVisibleBlockText(child, sb);
                break;
            case DocList list:
                foreach (var item in list.ListItems)
                    if (IsElementVisibleInTable(item))
                        foreach (var child in item.Blocks)
                            if (IsElementVisibleInTable(child))
                                AppendVisibleBlockText(child, sb);
                break;
        }
    }

    private static void AppendVisibleInlinesText(InlineCollection inlines, StringBuilder sb)
    {
        foreach (var inline in inlines)
        {
            if (!IsElementVisibleInTable(inline)) continue;
            switch (inline)
            {
                case Run run:
                    sb.Append(run.Text);
                    break;
                case LineBreak:
                    sb.Append('\n');
                    break;
                case Span span:
                    AppendVisibleInlinesText(span.Inlines, sb);
                    break;
            }
        }
    }

    private static bool IsElementVisibleInTable(DependencyObject element)
    {
        if (CssDisplayProperties.SuppressesBoxTree(element))
            return false;
        if (!CssTableVisibility.Visible(element))
            return false;
        var visibility = (Visibility)(element.GetValue(UIElement.VisibilityProperty) ?? Visibility.Visible);
        return visibility == Visibility.Visible;
    }

    private static bool IsTableColumnCollapsed(Table table, int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= table.Columns.Count)
            return false;
        var column = table.Columns[columnIndex];
        return !IsElementVisibleInTable(column);
    }

    private static Table? FindOwningTable(TableCell cell)
        => (cell.Parent as TableRow)?.Parent is TableRowGroup group ? group.Parent as Table : null;

    private static Dictionary<TableCell, (int Row, int Column)> BuildTableCellCoordinates(Table table)
    {
        var grid = new Dictionary<TableCell, (int Row, int Column)>();
        var occupied = new HashSet<(int, int)>();
        int rowIndex = 0;
        foreach (var group in table.RowGroups)
        {
            foreach (var row in group.Rows)
            {
                int column = 0;
                foreach (var cell in row.Cells)
                {
                    while (occupied.Contains((rowIndex, column))) ++column;
                    int rSpan = Math.Clamp(cell.RowSpan, 1, 4096);
                    int cSpan = Math.Clamp(cell.ColumnSpan, 1, 256);
                    grid[cell] = (rowIndex, column);
                    for (int y = 0; y < rSpan; ++y)
                        for (int x = 0; x < cSpan; ++x)
                            occupied.Add((rowIndex + y, column + x));
                    column += cSpan;
                }
                ++rowIndex;
            }
        }
        return grid;
    }
}
