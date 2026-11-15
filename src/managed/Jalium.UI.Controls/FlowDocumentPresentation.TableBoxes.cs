using Jalium.UI.Documents;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

internal sealed partial class FlowDocumentPresentation
{
    private sealed class DocumentTableBoxes
    {
        internal readonly List<TableRowGroup> Groups = [];
        internal readonly List<TableColumn> Columns = [];
    }
    private sealed record DocumentAnonymousTableBox(TextElement Box, DependencyObject Parent, DependencyObject[] Items);
    private readonly Dictionary<Table, DocumentTableBoxes> _documentTableBoxes = [];
    private readonly Dictionary<TableRowGroup, TableRow[]> _documentBoxRows = [];
    private readonly Dictionary<TableRow, TableCell[]> _documentBoxCells = [];
    private readonly Dictionary<TableCell, DependencyObject[]> _documentCellNodes = [];
    private readonly Dictionary<TableRow, TableRowGroup> _documentRowGroups = [];
    private readonly Dictionary<DependencyObject, DependencyObject> _documentContentBoxParents = [];
    private readonly Dictionary<DependencyObject, DependencyObject> _documentBoxParents = [];
    private readonly Dictionary<DependencyObject, Table> _documentTableOwners = [];
    private readonly Dictionary<DependencyObject, (int Start, int End)> _documentBoxSourceRanges = [];
    private readonly HashSet<DependencyObject> _documentAnonymousTableBoxes = [];
    private readonly HashSet<DependencyObject> _documentTableWhitespace = [];
    private readonly List<DocumentAnonymousTableBox> _documentAnonymousBoxes = [];

    private void ResetDocumentTableBoxes()
    {
        _documentTableBoxes.Clear(); _documentBoxRows.Clear(); _documentBoxCells.Clear(); _documentCellNodes.Clear(); _documentRowGroups.Clear(); _documentContentBoxParents.Clear();
        _documentBoxParents.Clear(); _documentTableOwners.Clear(); _documentBoxSourceRanges.Clear();
        _documentAnonymousTableBoxes.Clear(); _documentAnonymousBoxes.Clear(); _documentTableWhitespace.Clear();
    }

    private DependencyObject? DocumentBoxParent(DependencyObject source)
        => _documentBoxParents.GetValueOrDefault(source) ?? (source as FrameworkContentElement)?.Parent;

    private DependencyObject ActualDocumentBoxParent(DependencyObject source)
    {
        var parent = DocumentBoxParent(source) ?? _document!;
        while (DocumentContents(parent)) parent = DocumentBoxParent(parent) ?? _document!;
        return parent;
    }

    private DependencyObject DocumentFlowParent(DependencyObject source)
    {
        if (_documentContentBoxParents.TryGetValue(source, out var direct)) return direct;
        var parent = (source as FrameworkContentElement)?.Parent;
        while (parent is not null && DocumentContents(parent))
        {
            if (_documentContentBoxParents.TryGetValue(parent, out var mapped)) return mapped;
            parent = (parent as FrameworkContentElement)?.Parent;
        }
        return parent ?? _document!;
    }

    private (int Start, int End) DocumentBoxSourceRange(DependencyObject source)
    {
        if (_documentBoxSourceRanges.TryGetValue(source, out var range)) return range;
        if (source is TableColumn)
        {
            var parent = (source as FrameworkContentElement)?.Parent;
            var at = parent is not null ? DocumentBoxSourceRange(parent).Start : 0;
            return (at, at);
        }
        var start = source is TextElement text ? _document!.GetDocumentOffset(text, 0) : 0;
        var length = source is TextElement element ? TextElement.GetContentLength(element) : 0;
        return (start, start + length + (source is Paragraph or TableCell or TableRow ? 1 : 0));
    }

    private T AnonymousDocumentTableBox<T>(DependencyObject parent, IReadOnlyList<DependencyObject> items) where T : TextElement, new()
    {
        var existing = _documentAnonymousBoxes.FirstOrDefault(item => item.Box is T && ReferenceEquals(item.Parent, parent) && item.Items.SequenceEqual(items));
        if (existing is not null) return (T)existing.Box;
        var box = new T();
        // Anonymous styles inherit through their box parent, while every original
        // node retains its document parent and already computed source styles.
        foreach (var property in CssPropertyRegistry.GetInheritedStorageProperties().Concat(new[]
        {
            TextElement.FontFamilyProperty, TextElement.FontSizeProperty, TextElement.FontWeightProperty,
            TextElement.FontStyleProperty, TextElement.FontStretchProperty, TextElement.ForegroundProperty,
            FrameworkElement.FlowDirectionProperty, Block.LineHeightProperty, CssComputedLineHeightValue.Property,
        }).Distinct()) box.SetLayerValue(property, parent.GetValue(property), LayerValueSource.StyleSetter);
        if (box is Table table) table.CellSpacing = 0;
        if (box is TableCell cell) { cell.Padding = new(0); cell.BorderThickness = new(0); cell.BorderBrush = null; }
        _documentAnonymousBoxes.Add(new(box, parent, items.ToArray()));
        _documentAnonymousTableBoxes.Add(box); _documentBoxParents[box] = parent;
        var ranges = items.Select(DocumentBoxSourceRange).ToArray();
        _documentBoxSourceRanges[box] = ranges.Length == 0 ? (0, 0) : (ranges.Min(range => range.Start), ranges.Max(range => range.End));
        return box;
    }

    private static bool DocumentInternalTableBox(DependencyObject source) => source is TableRowGroup or TableRow or TableCell or TableColumn;
    private static bool DocumentProjectedWhitespace(DependencyObject source)
    {
        if (source is not Paragraph paragraph || !DocumentContents(paragraph)) return false;
        static string Text(DependencyObject node) => CssDisplayProperties.SuppressesBoxTree(node) ? "" : node switch
        {
            Run run => run.Text,
            Span span => string.Concat(span.Inlines.Select(Text)),
            _ => "\uFFFC",
        };
        var value = string.Concat(paragraph.Inlines.Select(Text));
        var mode = (CssWhiteSpaceCollapse)paragraph.GetValue(CssFlowProperties.WhiteSpaceProperty)!;
        return mode is CssWhiteSpaceCollapse.Collapse or CssWhiteSpaceCollapse.Discard && value.All(c => c is ' ' or '\t' or '\r' or '\n' or '\f') ||
            mode == CssWhiteSpaceCollapse.PreserveBreaks && value.All(c => c is ' ' or '\t');
    }

    private static IEnumerable<DependencyObject> ElidedDocumentChildren(IEnumerable<DependencyObject> nodes)
    {
        foreach (var node in nodes)
        {
            if (CssDisplayProperties.SuppressesBoxTree(node) || !DocumentContents(node) || node is Paragraph)
            { yield return node; continue; }
            IEnumerable<DependencyObject> children = node switch
            {
                Section section => section.Blocks,
                Documents.List list => list.ListItems,
                ListItem item => item.Blocks,
                Table table => table.Columns.Cast<DependencyObject>().Concat(table.RowGroups),
                TableRowGroup group => group.Rows,
                TableRow row => row.Cells,
                TableCell cell => cell.Blocks,
                _ => [],
            };
            foreach (var child in ElidedDocumentChildren(children)) yield return child;
        }
    }

    private IEnumerable<DependencyObject> FixDocumentFlowTables(IEnumerable<DependencyObject> nodes)
    {
        var pending = new List<DependencyObject>();
        Table Flush()
        {
            var table = AnonymousDocumentTableBox<Table>(DocumentFlowParent(pending.First(node => !CssDisplayProperties.SuppressesBoxTree(node) && !_documentTableWhitespace.Contains(node))), pending);
            pending.Clear(); return table;
        }
        var projected = ElidedDocumentChildren(nodes).ToArray();
        var whitespace = projected.Select(DocumentProjectedWhitespace).ToArray();
        for (var i = 0; i < projected.Length; i++)
        {
            if (!whitespace[i]) continue;
            var before = i - 1; var after = i + 1;
            while (before >= 0 && CssDisplayProperties.SuppressesBoxTree(projected[before])) before--;
            while (after < projected.Length && (CssDisplayProperties.SuppressesBoxTree(projected[after]) || whitespace[after])) after++;
            if ((before < 0 || DocumentInternalTableBox(projected[before])) &&
                (after >= projected.Length || DocumentInternalTableBox(projected[after])) &&
                (before >= 0 || after < projected.Length))
                for (var at = i; at < after; at++) if (whitespace[at]) _documentTableWhitespace.Add(projected[at]);
            i = after - 1;
        }
        foreach (var node in projected)
        {
            if (CssDisplayProperties.SuppressesBoxTree(node) || _documentTableWhitespace.Contains(node))
            { if (pending.Count > 0) pending.Add(node); else yield return node; continue; }
            if (DocumentInternalTableBox(node)) { pending.Add(node); continue; }
            if (pending.Count > 0 && DocumentProjectedWhitespace(node))
            { _documentTableWhitespace.Add(node); pending.Add(node); continue; }
            if (pending.Count > 0) yield return Flush();
            yield return node;
        }
        if (pending.Count > 0) yield return Flush();
    }

    private DocumentTableBoxes DocumentTableModel(Table table)
    {
        if (_documentTableBoxes.TryGetValue(table, out var model)) return model;
        _documentTableBoxes[table] = model = new();
        _documentTableOwners[table] = table;
        var rootNodes = _documentAnonymousBoxes.FirstOrDefault(box => ReferenceEquals(box.Box, table))?.Items ??
            table.Columns.Cast<DependencyObject>().Concat(table.RowGroups).ToArray();
        var rootPending = new List<DependencyObject>();
        void Row(TableRow row, TableRowGroup group, DependencyObject parent, IEnumerable<DependencyObject> children)
        {
            _documentBoxParents[row] = parent; _documentTableOwners[row] = table; _documentRowGroups[row] = group;
            var cells = new List<TableCell>(); var pending = new List<DependencyObject>();
            void FlushCell()
            {
                if (pending.Any(node => !CssDisplayProperties.SuppressesBoxTree(node) && !DocumentProjectedWhitespace(node)))
                {
                    var cell = AnonymousDocumentTableBox<TableCell>(row, pending);
                    _documentCellNodes[cell] = pending.ToArray(); cells.Add(cell); _documentTableOwners[cell] = table;
                    foreach (var node in pending) if (!CssDisplayProperties.SuppressesBoxTree(node)) { _documentBoxParents[node] = cell; _documentContentBoxParents[node] = cell; }
                }
                else foreach (var node in pending.Where(DocumentProjectedWhitespace)) _documentTableWhitespace.Add(node);
                pending.Clear();
            }
            foreach (var node in ElidedDocumentChildren(children))
            {
                if (node is TableCell cell && !CssDisplayProperties.SuppressesBoxTree(node))
                {
                    FlushCell(); cells.Add(cell); _documentBoxParents[cell] = row; _documentTableOwners[cell] = table;
                    foreach (var block in cell.Blocks) { _documentBoxParents[block] = cell; _documentContentBoxParents[block] = cell; }
                }
                else pending.Add(node);
            }
            FlushCell(); _documentBoxCells[row] = cells.ToArray();
        }
        void Group(TableRowGroup group, DependencyObject parent, IEnumerable<DependencyObject> children)
        {
            model.Groups.Add(group); _documentBoxParents[group] = parent; _documentTableOwners[group] = table;
            var rows = new List<TableRow>(); var pending = new List<DependencyObject>();
            var rowParent = _documentAnonymousTableBoxes.Contains(group) ? parent : group;
            void FlushRow()
            {
                if (pending.Any(node => !CssDisplayProperties.SuppressesBoxTree(node) && !DocumentProjectedWhitespace(node)))
                {
                    var row = AnonymousDocumentTableBox<TableRow>(rowParent, pending);
                    rows.Add(row); Row(row, group, rowParent, pending);
                }
                else foreach (var node in pending.Where(DocumentProjectedWhitespace)) _documentTableWhitespace.Add(node);
                pending.Clear();
            }
            foreach (var node in ElidedDocumentChildren(children))
            {
                if (_documentAnonymousTableBoxes.Contains(group) && node is TableColumn) { FlushRow(); continue; }
                if (node is TableRow row && !CssDisplayProperties.SuppressesBoxTree(node))
                { FlushRow(); rows.Add(row); Row(row, group, rowParent, row.Cells); }
                else pending.Add(node);
            }
            FlushRow(); _documentBoxRows[group] = rows.ToArray();
        }
        void FlushRoot()
        {
            if (rootPending.Any(node => node is not TableColumn && !CssDisplayProperties.SuppressesBoxTree(node) && !DocumentProjectedWhitespace(node)))
            {
                var group = AnonymousDocumentTableBox<TableRowGroup>(table, rootPending);
                Group(group, table, rootPending);
            }
            else foreach (var node in rootPending.Where(DocumentProjectedWhitespace)) _documentTableWhitespace.Add(node);
            rootPending.Clear();
        }
        foreach (var node in ElidedDocumentChildren(rootNodes))
        {
            if (CssDisplayProperties.SuppressesBoxTree(node)) { rootPending.Add(node); continue; }
            if (node is TableColumn column) { rootPending.Add(column); model.Columns.Add(column); _documentTableOwners[column] = table; }
            else if (node is TableRowGroup group) { FlushRoot(); Group(group, table, group.Rows); }
            else rootPending.Add(node);
        }
        FlushRoot(); return model;
    }

    private TableRowGroup[] SourceTableGroups(Table table) => DocumentTableModel(table).Groups.ToArray();
    private TableRow[] BoxRows(TableRowGroup group) => _documentBoxRows.GetValueOrDefault(group) ?? group.Rows.Where(row => !CssDisplayProperties.SuppressesBoxTree(row)).ToArray();
    private TableCell[] BoxCells(TableRow row) => _documentBoxCells.GetValueOrDefault(row) ?? row.Cells.Where(cell => !CssDisplayProperties.SuppressesBoxTree(cell)).ToArray();
    private IEnumerable<DependencyObject> CellNodes(TableCell cell) => _documentCellNodes.GetValueOrDefault(cell) ?? cell.Blocks.Cast<DependencyObject>();
    private TableRowGroup? BoxRowGroup(TableRow row) => _documentRowGroups.GetValueOrDefault(row);
    private bool DocumentCollapsedPart(DependencyObject source) => _documentTableOwners.TryGetValue(source, out var table)
        ? CssTableProperties.Collapsed(table) : CssCollapsedTableBorders.DocumentPart(source);

    private void ConsumeSkippedTableParagraphs(ref int index, int until, double y)
    {
        while (index < _paragraphs.Count && _paragraphs[index].Offset < until)
        {
            var view = _paragraphs[index++]; view.Text.DocumentDisplaySuppressed = true; view.Text.Visibility = Visibility.Collapsed;
            view.Bounds = new(0, y, 0, 0);
        }
    }
}
