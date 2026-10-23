using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Jalium.UI.Documents;

internal sealed partial class WritingToolsDocumentFragment
{
    // Paragraph identities survive offset changes and native context refreshes.
    // Weak keys do not retain documents after an editor is released.
    private sealed class Origin { internal readonly string Id = Guid.NewGuid().ToString("N"); }
    private static readonly ConditionalWeakTable<TextElement, Origin> Origins = new();
    internal static string OriginFor(TextElement element) => Origins.GetValue(element, _ => new()).Id;

    private sealed record OwnerKey(bool Table, string Id, bool Stable, string? Item = null, int Row = 0, int Column = 0,
        int Rows = 0, int Columns = 0, int TableColumns = 0, string? Format = null, int Start = 0);
    private sealed class Insertion(BlockCollection owner)
    {
        internal readonly BlockCollection Owner = owner;
        internal readonly List<(int Start, int Length)> Ranges = new();
        internal readonly List<Row> Rows = new();
        internal TextElement? LastElement;
        internal TextPointer? Anchor;
        internal bool ConsumesTerminator;
        internal WritingToolsDocumentFragment? Fragment;
    }
    private List<Insertion>? _insertions;
    private sealed record DirectInsertion(BlockCollection Owner, Block[] Removed, Block[] Added, int Index);
    private DirectInsertion? _directInsertion;
    private FlowDocument? _insertionDocument;
    private readonly Dictionary<DependencyObject, Dictionary<DependencyProperty, object?>> _propertyUpdates = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Table, int> _columnUpdates = new(ReferenceEqualityComparer.Instance);
    internal bool HasPreparedSource => _wholeInlineSource is not null || _insertions is not null || _directInsertion is not null;

    private static OwnerKey Key(Container value) => value.Table
        ? new(true, value.Origin ?? value.Id, value.Origin is not null, null,
            value.Metadata.GetProperty("row").GetInt32(), value.Metadata.GetProperty("column").GetInt32(),
            value.Metadata.GetProperty("rows").GetInt32(), value.Metadata.GetProperty("columns").GetInt32(),
            value.Origin is null ? value.Metadata.GetProperty("tableColumns").GetInt32() : 0)
        : new(false, value.Origin ?? value.Id, value.Origin is not null, value.Origin is null ? value.Item : null,
            Format: value.Origin is null ? value.Metadata.GetProperty("format").GetString() : null,
            Start: value.Origin is null ? value.Metadata.GetProperty("start").GetInt32() : 0);

    internal bool TryPrepareSource(FlowDocument document, string before, int start, int length, JsonElement payload,
        Func<int, int, (int Start, int Length)[]>? sourceRanges = null,
        Func<int, TextPointer?>? sourcePosition = null)
    {
        if (!payload.TryGetProperty("source", out var originalPacket)) return true;
        if (!payload.TryGetProperty("sourceStart", out var at) || at.ValueKind != JsonValueKind.Number || !at.TryGetInt32(out int sourceStart)) return false;
        var original = Parse(originalPacket);
        if (sourceStart < 0 || sourceStart > before.Length || original._text.Length > before.Length - sourceStart ||
            !before.AsSpan(sourceStart, original._text.Length).SequenceEqual(original._text) ||
            sourceStart > start || start + length > sourceStart + original._text.Length) return false;
        string raw = document.GetText();
        bool wholeDocument = start == 0 && length == before.Length && before == raw;
        sourceRanges ??= (from, count) => [(from, count)];
        sourcePosition ??= from => document.GetPositionAtOffset(from, LogicalDirection.Forward);

        var insertions = new List<Insertion>();
        var identities = new Dictionary<string, Insertion>(StringComparer.Ordinal);
        var owners = new Dictionary<OwnerKey, TextElement>();
        var sourceContainers = new Dictionary<OwnerKey, Container>();
        var keys = new Dictionary<Insertion, HashSet<OwnerKey>>(ReferenceEqualityComparer.Instance);
        foreach (var row in original._rows)
        {
            int absolute = sourceStart + row.Start;
            var point = sourcePosition(absolute);
            TextElement? element = row.Paragraph is null ? point?.Parent as TableCell : point?.Paragraph;
            BlockCollection? owner = element switch { Paragraph p => p.OwnerCollection, TableCell c => c.Blocks, _ => null };
            if (owner is null || element?.GetFlowDocument() != document ||
                row.Origin is { } identity && OriginFor(element) != identity) return false;
            var chain = new List<TextElement>();
            for (var node = element is TableCell ? element : element.Parent as TextElement; node is not null; node = node.Parent as TextElement)
                if (node is ListItem or TableCell) chain.Add(node);
            chain.Reverse();
            if (chain.Count != row.Path.Length) return false;
            for (int i = 0; i < chain.Count; ++i)
            {
                if (row.Path[i].Table != (chain[i] is TableCell)) return false;
                if (row.Path[i].Origin is { } originalOwner && OriginFor(chain[i]) != originalOwner) return false;
                var key = Key(row.Path[i]);
                if (owners.TryGetValue(key, out var existing) && !ReferenceEquals(existing, chain[i])) return false;
                owners[key] = chain[i];
                sourceContainers[key] = row.Path[i];
            }
            var insertion = insertions.LastOrDefault();
            int previousEnd = insertion?.LastElement is Paragraph previousParagraph ? previousParagraph.ContentEnd.DocumentOffset + 1 : -1;
            if (insertion is null || !ReferenceEquals(insertion.Owner, owner) || point!.DocumentOffset > previousEnd)
            { insertion = new(owner); insertions.Add(insertion); keys[insertion] = new(); }
            foreach (var value in row.Path) keys[insertion].Add(Key(value));
            if (row.Origin is { } origin)
            {
                if (identities.TryGetValue(origin, out var previous) && !ReferenceEquals(previous, insertion)) return false;
                identities[origin] = insertion;
            }
            insertion.LastElement = element;
            int from = Math.Max(start, absolute), to = Math.Min(start + length, absolute + row.Length);
            if (to > from && element is not TableCell)
                foreach (var range in sourceRanges(from, to - from))
                {
                    if (range.Start < 0 || range.Length < 0 || range.Start > raw.Length || range.Length > raw.Length - range.Start) return false;
                    if (insertion.Ranges.LastOrDefault() is var prior && insertion.Ranges.Count != 0 &&
                        prior.Start + prior.Length == range.Start)
                        insertion.Ranges[^1] = (prior.Start, prior.Length + range.Length);
                    else insertion.Ranges.Add(range);
                }
            insertion.Anchor ??= sourcePosition(Math.Max(start, absolute));
        }
        if (wholeDocument && !PrepareWholeInlineSource(document, original, sourceStart)) return false;
        if (wholeDocument || insertions.Count == 0 || original._rows.All(row => row.Path.Length == 0)) return true;
        foreach (var insertion in insertions)
        {
            if (insertion.LastElement is Paragraph last && insertion.Ranges.Count > 0)
            {
                int end = last.ContentEnd.DocumentOffset;
                var final = insertion.Ranges[^1];
                if (final.Start + final.Length > end)
                { insertion.ConsumesTerminator = true; insertion.Ranges[^1] = (final.Start, Math.Max(0, end - final.Start)); }
            }
            insertion.Anchor ??= sourcePosition(start);
            if (insertion.Anchor is { } anchor && DocumentTextEditing.HasNonMergeableAncestor(anchor))
                throw new InvalidOperationException("A formatted fragment cannot split a non-mergeable inline.");
        }

        var mapped = sourceRanges(start, length);
        if (mapped.Length == 1 && mapped[0].Length > 0)
        foreach (var complete in FindWholeBlocks(document, document.Blocks, mapped[0].Start, mapped[0].Start + mapped[0].Length))
        {
            bool retained = _rows.Any(row => row.Path.Any(value => owners.TryGetValue(Key(value), out var owner) &&
                complete.Items.Any(block => HasAncestor(owner, block))));
            if (!retained)
            {
                var rows = new List<Row>();
                foreach (var row in _rows)
                {
                    int depth = Depth(row, complete.Owner);
                    for (int i = 0; i < depth; ++i)
                        if (!PrepareContainerChange(owners[Key(row.Path[i])], sourceContainers[Key(row.Path[i])], row.Path[i])) return false;
                    rows.Add(RebaseRow(row, depth));
                }
                _directInsertion = new(complete.Owner, complete.Items, Build(rows, 0).ToArray(), complete.Owner.IndexOf(complete.Items[0]));
                _insertionDocument = document; return true;
            }
        }

        int Depth(Row row, BlockCollection destination)
        {
            int depth = 0;
            while (depth < row.Path.Length && owners.TryGetValue(Key(row.Path[depth]), out var owner) && HasAncestor(destination.Parent, owner)) ++depth;
            return depth;
        }

        Insertion? destination = null;
        foreach (var row in _rows)
        {
            Insertion? located = row.Origin is { } origin && identities.TryGetValue(origin, out var location) ? location : null;
            if (row.Origin is not null && located is null) return false;
            if (located is null)
            {
                var matches = insertions.Where(candidate => row.Path.Any(value => keys[candidate].Contains(Key(value)))).ToArray();
                if (matches.Length == 1) located = matches[0];
                else if (destination is not null && matches.Contains(destination)) located = destination;
            }
            destination = located ?? destination ?? insertions[0];
            int depth = 0;
            while (depth < row.Path.Length && owners.TryGetValue(Key(row.Path[depth]), out var ancestor) &&
                HasAncestor(destination.Owner.Parent, ancestor))
            {
                if (!PrepareContainerChange(ancestor, sourceContainers[Key(row.Path[depth])], row.Path[depth])) return false;
                ++depth;
            }
            destination.Rows.Add(RebaseRow(row, depth));
        }
        foreach (var insertion in insertions)
        {
            var fragment = new WritingToolsDocumentFragment
            { EndsWithParagraphBreak = insertion.Rows.LastOrDefault()?.ParagraphBreak == true && !insertion.ConsumesTerminator };
            fragment.Blocks.AddRange(Build(insertion.Rows, 0)); insertion.Fragment = fragment;
        }
        _insertions = insertions; _insertionDocument = document; return true;
    }

    private static bool HasAncestor(DependencyObject? value, TextElement ancestor)
    {
        for (; value is not null; value = (value as FrameworkContentElement)?.Parent)
            if (ReferenceEquals(value, ancestor)) return true;
        return false;
    }

    private TextPointer InsertPrepared(FlowDocument document, Func<bool> current)
    {
        if (!ReferenceEquals(document, _insertionDocument)) throw new InvalidOperationException("The fragment belongs to another document.");
        foreach (var (element, changes) in _propertyUpdates)
            foreach (var (property, value) in changes)
            { if (!current()) return document.ContentStart; element.SetValue(property, value); }
        foreach (var (table, count) in _columnUpdates)
        {
            while (table.Columns.Count > count) { if (!current()) return document.ContentStart; table.Columns.RemoveAt(table.Columns.Count - 1); }
            while (table.Columns.Count < count) { if (!current()) return document.ContentStart; table.Columns.Add(new TableColumn()); }
        }
        if (_directInsertion is { } direct)
        {
            foreach (var block in direct.Removed) { if (!current()) return document.ContentStart; direct.Owner.Remove(block); }
            int at = direct.Index;
            foreach (var block in direct.Added) { if (!current()) return document.ContentStart; direct.Owner.Insert(at++, block); }
            return direct.Added.LastOrDefault()?.ContentEnd ?? (at < direct.Owner.Count ? direct.Owner[at].ContentStart : document.ContentEnd);
        }
        var last = _insertions!.LastOrDefault(value => value.Rows.Count > 0) ?? _insertions[0];
        TextPointer caret = document.ContentStart;
        for (int i = _insertions.Count - 1; i >= 0; --i)
        {
            if (!current()) return document.ContentStart;
            var insertion = _insertions[i]; var fragment = insertion.Fragment!;
            var ranges = insertion.Ranges;
            if (insertion.Owner.Parent is TableCell cell && cell.Blocks.Count == 0)
            {
                foreach (var block in fragment.Blocks) { if (!current()) return document.ContentStart; cell.Blocks.Add(block); }
                if (ReferenceEquals(insertion, last)) caret = cell.Blocks.LastOrDefault()?.ContentEnd ?? cell.ContentStart;
                continue;
            }
            for (int n = ranges.Count - 1; n > 0; --n)
            {
                if (!current()) return document.ContentStart;
                DocumentTextEditing.Replace(document, ranges[n].Start, ranges[n].Length, string.Empty);
            }
            if (!current()) return document.ContentStart;
            int start = ranges.Count > 0 ? ranges[0].Start : insertion.Anchor!.DocumentOffset;
            int length = ranges.Count > 0 ? ranges[0].Length : 0;
            var result = fragment.Insert(document, start, length, current);
            if (ReferenceEquals(insertion, last)) caret = result;
        }
        return caret;
    }

    private static Row RebaseRow(Row row, int depth)
    {
        Paragraph? paragraph = null;
        if (row.Paragraph is { } value)
        {
            paragraph = new Paragraph { TextAlignment = value.TextAlignment, FlowDirection = value.FlowDirection };
            MoveInlines(value, paragraph);
        }
        return row with { Paragraph = paragraph, Path = row.Path.Skip(depth).ToArray() };
    }

    private static IEnumerable<(BlockCollection Owner, Block[] Items)> FindWholeBlocks(FlowDocument document, BlockCollection owner, int start, int end)
    {
        var selected = new List<Block>(); int cursor = start; bool valid = true;
        foreach (var block in owner)
        {
            int at = document.GetDocumentOffset(block, 0), length = FlowDocument.GetBlockLength(block);
            if (at + length <= start || at >= end) continue;
            if (at != cursor || at + length > end) { valid = false; break; }
            selected.Add(block); cursor = at + length;
        }
        if (valid && selected.Count > 0 && cursor == end) yield return (owner, selected.ToArray());
        foreach (var block in owner)
        {
            IEnumerable<BlockCollection> nested = block switch
            {
                Section section => [section.Blocks], List list => list.ListItems.Select(item => item.Blocks),
                Table table => table.RowGroups.SelectMany(group => group.Rows).SelectMany(row => row.Cells).Select(cell => cell.Blocks), _ => []
            };
            foreach (var child in nested)
                foreach (var match in FindWholeBlocks(document, child, start, end)) yield return match;
        }
    }

    private bool PrepareContainerChange(TextElement owner, Container before, Container after)
    {
        bool Property(DependencyObject element, DependencyProperty property, object? value)
        {
            if (!_propertyUpdates.TryGetValue(element, out var changes)) _propertyUpdates[element] = changes = new();
            if (changes.TryGetValue(property, out var previous) && !Equals(previous, value)) return false;
            changes[property] = value; return true;
        }
        if (owner is ListItem { Parent: List list })
        {
            string oldFormat = before.Metadata.GetProperty("format").GetString()!, newFormat = after.Metadata.GetProperty("format").GetString()!;
            int oldStart = before.Metadata.GetProperty("start").GetInt32(), newStart = after.Metadata.GetProperty("start").GetInt32();
            return (oldFormat == newFormat || Property(list, List.MarkerStyleProperty, Marker(newFormat))) &&
                (oldStart == newStart || Property(list, List.StartIndexProperty, newStart));
        }
        if (owner is TableCell { Parent: TableRow { Parent: TableRowGroup { Parent: Table table } } })
        {
            int oldCount = before.Metadata.GetProperty("tableColumns").GetInt32(), count = after.Metadata.GetProperty("tableColumns").GetInt32();
            if (oldCount == count) return true;
            if (count < MinimumColumns(table) || _columnUpdates.TryGetValue(table, out int previous) && previous != count) return false;
            _columnUpdates[table] = count;
        }
        return true;
    }

    private static int MinimumColumns(Table table)
    {
        var occupied = new HashSet<(int Row, int Column)>(); int row = 0, maximum = 0;
        foreach (var group in table.RowGroups)
        foreach (var value in group.Rows)
        {
            int column = 0;
            foreach (var cell in value.Cells)
            {
                while (occupied.Contains((row, column))) ++column;
                for (int y = 0; y < cell.RowSpan; ++y)
                for (int x = 0; x < cell.ColumnSpan; ++x) occupied.Add((row + y, column + x));
                column += cell.ColumnSpan; maximum = Math.Max(maximum, column);
            }
            ++row;
        }
        return maximum;
    }
}
