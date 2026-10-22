using System.Runtime.InteropServices;
using System.Text.Json;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using DocList = Jalium.UI.Documents.List;

namespace Jalium.UI.Documents;

/// <summary>Imports AppKit's already-parsed attributed text without treating RTF syntax as text.</summary>
internal sealed partial class WritingToolsDocumentFragment
{
    internal readonly List<Block> Blocks = new();
    internal bool EndsWithParagraphBreak { get; private init; }
    private string _text = "";
    private List<Row> _rows = new();
    private sealed record Container(bool Table, string Id, JsonElement Metadata, string? Item = null, string? Origin = null);
    private sealed record Row(Paragraph? Paragraph, Container[] Path, int Start, int Length,
        string? Origin, bool ParagraphBreak = false);
    internal static WritingToolsDocumentFragment Parse(JsonElement packet)
    {
        if (packet.GetProperty("version").GetInt32() != 1) throw new FormatException("Unsupported text fragment.");
        byte[] bytes = Convert.FromBase64String(packet.GetProperty("text16").GetString()!);
        if (bytes.Length % 2 != 0 || bytes.Length > 32 * 1024 * 1024) throw new FormatException("Invalid UTF-16 fragment.");
        string text = new(MemoryMarshal.Cast<byte, char>(bytes));
        var runs = packet.GetProperty("runs").EnumerateArray().ToArray();
        int through = 0;
        foreach (var run in runs)
        {
            int start = run.GetProperty("start").GetInt32(), length = run.GetProperty("length").GetInt32();
            if (start != through || length <= 0 || length > text.Length - start) throw new FormatException("Invalid style range.");
            through += length;
        }
        if (through != text.Length || runs.Length > 100000) throw new FormatException("Incomplete style ranges.");
        var rows = new List<Row>();
        through = 0;
        bool trailingInlineBreak = false;
        var paragraphRecords = packet.GetProperty("paragraphs");
        if (paragraphRecords.ValueKind != JsonValueKind.Array || paragraphRecords.GetArrayLength() > 100000)
            throw new FormatException("Invalid paragraph records.");
        foreach (var record in paragraphRecords.EnumerateArray())
        {
            int start = record.GetProperty("start").GetInt32(), length = record.GetProperty("length").GetInt32();
            if (start != through || length <= 0 || length > text.Length - start || rows.Count >= 100000)
                throw new FormatException("Invalid paragraph range.");
            through += length;
            int recordStart = start;
            string? origin = null;
            if (record.TryGetProperty("origin", out var sourceIdentity))
            {
                if (sourceIdentity.ValueKind != JsonValueKind.String ||
                    sourceIdentity.GetString() is not { Length: > 0 and <= 1024 } identity)
                    throw new FormatException("Invalid source paragraph identity.");
                origin = identity;
            }
            int end = start + length;
            var inlineBreaks = new HashSet<int>();
            if (record.TryGetProperty("inlineBreaks", out var breaks))
            {
                if (breaks.ValueKind != JsonValueKind.Array || breaks.GetArrayLength() > length)
                    throw new FormatException("Invalid inline line breaks.");
                int previous = start - 1;
                foreach (var value in breaks.EnumerateArray())
                {
                    if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int offset) ||
                        offset <= previous || offset < start || offset >= end || text[offset] is not ('\n' or '\r' or '\u2029'))
                        throw new FormatException("Invalid inline line break position.");
                    inlineBreaks.Add(offset);
                    previous = offset;
                }
                trailingInlineBreak |= inlineBreaks.Contains(text.Length - 1);
            }
            bool emptyCell = false;
            if (record.TryGetProperty("emptyCell", out var empty))
            {
                if (empty.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new FormatException("Invalid empty cell marker.");
                emptyCell = empty.GetBoolean();
            }
            if (record.TryGetProperty("storageSeparator", out var separator))
            {
                if (separator.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new FormatException("Invalid storage separator.");
                if (separator.GetBoolean())
                {
                    if (emptyCell) throw new FormatException("An empty cell cannot also be an omitted storage record.");
                    if (inlineBreaks.Count != 0)
                        throw new FormatException("An authored line break cannot be omitted as storage.");
                    // FlowDocument emits virtual tabs/newlines between cells and
                    // rows. They already belong to their table, not a new paragraph.
                    for (int i = start; i < end; i++)
                        if (text[i] is not ('\t' or '\n' or '\r' or '\u2029'))
                            throw new FormatException("A storage separator contains authored text.");
                    continue;
                }
            }
            var tables = record.GetProperty("tables"); var lists = record.GetProperty("lists");
            if (tables.ValueKind != JsonValueKind.Array || lists.ValueKind != JsonValueKind.Array ||
                tables.GetArrayLength() > 32 || lists.GetArrayLength() > 32) throw new FormatException("Excessive nesting.");
            var path = ReadPath(record, lists, tables);
            if (emptyCell)
            {
                if (length != 1 || text[start] != '\t' || path.Length == 0 || !path[^1].Table ||
                    record.TryGetProperty("skipPrefix", out var prefix) && prefix.GetInt32() != 0)
                    throw new FormatException("Invalid empty cell storage.");
                rows.Add(new(null, path, recordStart, length, origin));
                continue;
            }
            bool paragraphBreak = false;
            if (end > start && text[end - 1] is '\n' or '\r' or '\u2029' && !inlineBreaks.Contains(end - 1))
            {
                paragraphBreak = true;
                --end;
                if (end > start && text[end] == '\n' && text[end - 1] == '\r' && !inlineBreaks.Contains(end - 1)) --end;
            }
            if (tables.GetArrayLength() != 0 && end > start && text[end - 1] == '\t') --end;
            int skip = record.TryGetProperty("skipPrefix", out var marker) ? marker.GetInt32() : 0;
            if (skip < 0 || skip > end - start) throw new FormatException("Invalid list marker.");
            start += skip;
            var paragraph = new Paragraph();
            int alignment = record.GetProperty("alignment").GetInt32();
            paragraph.TextAlignment = alignment switch
            {
                1 => TextAlignment.Right, 2 => TextAlignment.Center, 3 => TextAlignment.Justify,
                _ => TextAlignment.Left
            };
            int direction = record.GetProperty("direction").GetInt32();
            if (direction is 0 or 1) paragraph.FlowDirection = (FlowDirection)direction;
            foreach (var style in runs)
            {
                int from = Math.Max(start, style.GetProperty("start").GetInt32());
                int to = Math.Min(end, style.GetProperty("start").GetInt32() + style.GetProperty("length").GetInt32());
                if (to <= from) continue;
                var run = new Run(text.Substring(from, to - from));
                ApplyStyle(run, style);
                paragraph.Inlines.Add(run);
            }
            if (paragraph.Inlines.Count == 0) paragraph.Inlines.Add(new Run());
            rows.Add(new(paragraph, path, recordStart, length, origin, paragraphBreak));
        }
        if (through != text.Length) throw new FormatException("Incomplete paragraph ranges.");
        var fragment = new WritingToolsDocumentFragment
        { EndsWithParagraphBreak = text.Length > 0 && text[^1] is '\n' or '\r' or '\u2029' && !trailingInlineBreak,
            _text = text, _rows = rows };
        fragment.Blocks.AddRange(Build(rows, 0));
        return fragment;
    }

    private static Container[] ReadPath(JsonElement record, JsonElement lists, JsonElement tables)
    {
        Container Make(bool table, int index, string? item = null, string? origin = null)
        {
            var metadata = (table ? tables : lists)[index];
            if (metadata.ValueKind != JsonValueKind.Object || !metadata.TryGetProperty("id", out var id) ||
                id.ValueKind != JsonValueKind.String || id.GetString() is not { Length: > 0 and <= 1024 } identity)
                throw new FormatException("Invalid container identity.");
            return new(table, identity, metadata, item, origin);
        }
        if (!record.TryGetProperty("ancestors", out var ancestors))
        {
            // Older/native-only proposals have separate arrays. Preserve their
            // established table-then-list interpretation when no order is supplied.
            return Enumerable.Range(0, tables.GetArrayLength()).Select(i => Make(true, i))
                .Concat(Enumerable.Range(0, lists.GetArrayLength()).Select(i => Make(false, i))).ToArray();
        }
        if (ancestors.ValueKind != JsonValueKind.Array ||
            ancestors.GetArrayLength() != lists.GetArrayLength() + tables.GetArrayLength())
            throw new FormatException("Incomplete container ancestry.");
        int list = 0, table = 0;
        var result = new List<Container>();
        foreach (var ancestor in ancestors.EnumerateArray())
        {
            if (ancestor.ValueKind != JsonValueKind.Object ||
                !ancestor.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String ||
                !ancestor.TryGetProperty("index", out var index) || index.ValueKind != JsonValueKind.Number || !index.TryGetInt32(out int value))
                throw new FormatException("Invalid container ancestry.");
            bool isTable = kind.GetString() switch
            { "table" => true, "list" => false, _ => throw new FormatException("Unknown container ancestor.") };
            if (value != (isTable ? table++ : list++) || value >= (isTable ? tables.GetArrayLength() : lists.GetArrayLength()))
                throw new FormatException("Invalid container ancestry index.");
            string? item = null;
            string? origin = null;
            if (ancestor.TryGetProperty("origin", out var original))
            {
                if (original.ValueKind != JsonValueKind.String || original.GetString() is not { Length: > 0 and <= 1024 })
                    throw new FormatException("Invalid container source identity.");
                origin = original.GetString();
            }
            if (ancestor.TryGetProperty("item", out var owner))
            {
                if (isTable || owner.ValueKind != JsonValueKind.String || owner.GetString() is not { Length: > 0 and <= 1024 })
                    throw new FormatException("Invalid list item identity.");
                item = owner.GetString();
            }
            result.Add(Make(isTable, value, item, origin));
        }
        return result.ToArray();
    }

    private static bool SharesContainer(Row row, int depth, Container container) =>
        row.Path.Length > depth && row.Path[depth].Table == container.Table && row.Path[depth].Id == container.Id;

    private static IEnumerable<Block> Build(IReadOnlyList<Row> rows, int depth)
    {
        for (int index = 0; index < rows.Count;)
        {
            Row row = rows[index];
            if (row.Path.Length <= depth) { if (row.Paragraph is { } paragraph) yield return paragraph; ++index; continue; }
            var container = row.Path[depth];
            int end = index + 1;
            while (end < rows.Count && SharesContainer(rows[end], depth, container)) ++end;
            if (container.Table)
            {
                yield return BuildTable(rows.Skip(index).Take(end - index).ToArray(), depth);
                index = end; continue;
            }
            var metadata = container.Metadata;
            string format = metadata.GetProperty("format").GetString()!;
            int start = metadata.GetProperty("start").GetInt32();
            var list = new DocList { MarkerStyle = Marker(format), StartIndex = start };
            while (index < end)
            {
                var itemOwner = rows[index].Path[depth];
                int itemEnd = index + 1;
                if (itemOwner.Item is not null)
                    while (itemEnd < end && rows[itemEnd].Path[depth].Item == itemOwner.Item) ++itemEnd;
                else
                    while (itemEnd < end && rows[itemEnd].Path.Length > depth + 1) ++itemEnd;
                for (int i = index; i < itemEnd; i++)
                    if (rows[i].Path[depth].Metadata.GetProperty("format").GetString() != format ||
                        rows[i].Path[depth].Metadata.GetProperty("start").GetInt32() != start)
                        throw new FormatException("Inconsistent list metadata.");
                var item = new ListItem();
                foreach (var block in Build(rows.Skip(index).Take(itemEnd - index).ToArray(), depth + 1)) item.Blocks.Add(block);
                list.ListItems.Add(item); index = itemEnd;
            }
            yield return list;
        }
    }
    private static Table BuildTable(IReadOnlyList<Row> paragraphs, int depth)
    {
        int columns = paragraphs[0].Path[depth].Metadata.GetProperty("tableColumns").GetInt32();
        if (columns is < 1 or > 256) throw new FormatException("Invalid table dimensions.");
        var table = new Table();
        for (int i = 0; i < columns; ++i) table.Columns.Add(new TableColumn());
        var group = new TableRowGroup(); table.RowGroups.Add(group);
        var occupied = new HashSet<(int Row, int Column)>();
        var rowGroups = paragraphs.GroupBy(row => row.Path[depth].Metadata.GetProperty("row").GetInt32()).OrderBy(row => row.Key).ToArray();
        if (rowGroups.Length > 4096) throw new FormatException("Excessive table rows.");
        int firstRow = rowGroups[0].Key;
        foreach (var rowGroup in rowGroups)
        {
            int rowIndex = rowGroup.Key - firstRow;
            if (rowIndex is < 0 or > 4095) throw new FormatException("Invalid table row.");
            while (group.Rows.Count <= rowIndex) group.Rows.Add(new TableRow());
            var row = group.Rows[rowIndex];
            var cells = rowGroup.GroupBy(value => value.Path[depth].Metadata.GetProperty("column").GetInt32())
                .ToDictionary(value => value.Key, value => value.ToArray());
            if (cells.Keys.Any(column => column < 0 || column >= columns)) throw new FormatException("Invalid table column.");
            for (int column = 0; column < columns;)
            {
                if (occupied.Contains((rowIndex, column)))
                {
                    if (cells.ContainsKey(column)) throw new FormatException("Cell starts inside another span.");
                    ++column; continue;
                }
                if (!cells.TryGetValue(column, out var content))
                { row.Cells.Add(new TableCell(new Paragraph())); ++column; continue; }
                var metadata = content[0].Path[depth].Metadata;
                int rowSpan = metadata.GetProperty("rows").GetInt32(), columnSpan = metadata.GetProperty("columns").GetInt32();
                if (rowSpan is < 1 or > 4096 || columnSpan < 1 || columnSpan > columns - column)
                    throw new FormatException("Invalid table span.");
                if (rowSpan > 4096 - rowIndex || cells.Keys.Any(other => other > column && other < column + columnSpan))
                    throw new FormatException("Overlapping table spans.");
                foreach (var value in content)
                {
                    var details = value.Path[depth].Metadata;
                    if (details.GetProperty("rows").GetInt32() != rowSpan ||
                        details.GetProperty("columns").GetInt32() != columnSpan ||
                        details.GetProperty("tableColumns").GetInt32() != columns)
                        throw new FormatException("Inconsistent table cell.");
                }
                var cell = new TableCell { RowSpan = rowSpan, ColumnSpan = columnSpan };
                for (int y = 0; y < rowSpan; ++y) for (int x = 0; x < columnSpan; ++x)
                    if (!occupied.Add((rowIndex + y, column + x))) throw new FormatException("Overlapping table spans.");
                foreach (var block in Build(content, depth + 1)) cell.Blocks.Add(block);
                row.Cells.Add(cell); column += columnSpan;
            }
        }
        int requiredRows = occupied.Count == 0 ? group.Rows.Count : occupied.Max(cell => cell.Row) + 1;
        while (group.Rows.Count < requiredRows) group.Rows.Add(new TableRow());
        return table;
    }
    private static TextMarkerStyle Marker(string format)
    {
        if (format.Length == 0) return TextMarkerStyle.None;
        format = format.ToLowerInvariant();
        if (format.Contains("{decimal}", StringComparison.Ordinal)) return TextMarkerStyle.Decimal;
        if (format.Contains("{lower-alpha}", StringComparison.Ordinal)) return TextMarkerStyle.LowerLatin;
        if (format.Contains("{upper-alpha}", StringComparison.Ordinal)) return TextMarkerStyle.UpperLatin;
        if (format.Contains("{lower-roman}", StringComparison.Ordinal)) return TextMarkerStyle.LowerRoman;
        if (format.Contains("{upper-roman}", StringComparison.Ordinal)) return TextMarkerStyle.UpperRoman;
        if (format.Contains("{circle}", StringComparison.Ordinal)) return TextMarkerStyle.Circle;
        if (format.Contains("{square}", StringComparison.Ordinal)) return TextMarkerStyle.Square;
        if (format.Contains("{box}", StringComparison.Ordinal)) return TextMarkerStyle.Box;
        return TextMarkerStyle.Disc;
    }
    private static void ApplyStyle(Run run, JsonElement style)
    {
        string family = style.GetProperty("family").GetString()!;
        double size = style.GetProperty("size").GetDouble();
        int weight = style.GetProperty("weight").GetInt32();
        if (string.IsNullOrEmpty(family) || family.Length > 4 * 1024 * 1024 || !double.IsFinite(size) ||
            size <= 0 || size > 65536 || weight is < 1 or > 1000) throw new FormatException("Invalid font.");
        run.FontFamily = new FontFamily(family); run.FontSize = size;
        run.FontWeight = FontWeight.FromOpenTypeWeight(weight);
        run.FontStyle = style.GetProperty("italic").GetBoolean() ? FontStyles.Italic : FontStyles.Normal;
        if (style.TryGetProperty("language", out var language) && language.ValueKind != JsonValueKind.Null)
        {
            if (language.ValueKind != JsonValueKind.String || language.GetString() is not { Length: <= 1024 } tag)
                throw new FormatException("Invalid text language.");
            try { run.Language = XmlLanguage.GetLanguage(tag); }
            catch (ArgumentException error) { throw new FormatException("Invalid text language.", error); }
        }
        if (style.TryGetProperty("foreground", out var foreground)) run.Foreground = Brush(foreground);
        if (style.TryGetProperty("background", out var background)) run.Background = Brush(background);
        var decorations = new TextDecorationCollection();
        void AddDecoration(string enabled, string colorName, TextDecorationLocation location)
        {
            if (style.GetProperty(enabled).GetInt32() == 0) return;
            var decoration = new TextDecoration { Location = location };
            if (style.TryGetProperty(colorName, out var color) && color.ValueKind != JsonValueKind.Null)
            {
                if (color.ValueKind != JsonValueKind.Number || !color.TryGetUInt32(out _))
                    throw new FormatException("Invalid text decoration color.");
                decoration.Brush = Brush(color);
            }
            decorations.Add(decoration);
        }
        AddDecoration("underline", "underlineColor", TextDecorationLocation.Underline);
        AddDecoration("strike", "strikeColor", TextDecorationLocation.Strikethrough);
        if (decorations.Count != 0) run.TextDecorations = decorations;
    }
    private static SolidColorBrush Brush(JsonElement value)
    {
        uint argb = value.GetUInt32();
        return new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
    }

    internal TextPointer Insert(FlowDocument document, int start, int length, Func<bool> current)
    {
        if (HasPreparedSource) return InsertPrepared(document, current);
        string original = document.GetText();
        if (start < 0 || length < 0 || start > original.Length || length > original.Length - start)
            throw new ArgumentOutOfRangeException(nameof(start));
        if (!current()) return document.ContentStart;
        if (!(start == 0 && length == original.Length) &&
            document.GetPositionAtOffset(start, LogicalDirection.Forward) is { } insertion &&
            DocumentTextEditing.HasNonMergeableAncestor(insertion))
            throw new InvalidOperationException("A formatted fragment cannot split a non-mergeable inline.");
        if (start == 0 && length == original.Length)
        {
            document.Blocks.Clear();
            foreach (var block in Blocks) { if (!current()) return document.ContentStart; document.Blocks.Add(block); }
            return document.ContentEnd;
        }
        TextPointer position = DocumentTextEditing.Replace(document, start, length, string.Empty);
        if (!current() || Blocks.Count == 0) return position;
        Paragraph prefix = position.Paragraph ?? throw new InvalidOperationException("No insertion paragraph.");
        BlockCollection collection = prefix.OwnerCollection ?? throw new InvalidOperationException("Detached insertion paragraph.");
        TextPointer split = document.InsertParagraphBreak(position);
        Paragraph suffix = split.Paragraph!;
        int at = collection.IndexOf(suffix);
        var incoming = Blocks.ToList();
        if (incoming[0] is Paragraph first)
        {
            prefix.TextAlignment = first.TextAlignment;
            if (!current()) return document.ContentStart;
            prefix.FlowDirection = first.FlowDirection;
            if (!current()) return document.ContentStart;
            MoveInlines(first, prefix);
            incoming.RemoveAt(0);
        }
        else if (TextElement.GetContentLength(prefix) == 0)
        {
            collection.Remove(prefix);
            at = collection.IndexOf(suffix);
        }
        foreach (var block in incoming)
        { if (!current()) return document.ContentStart; collection.Insert(at++, block); }
        Block last = incoming.LastOrDefault() ?? prefix;
        TextPointer caret = last.ContentEnd;
        if (!EndsWithParagraphBreak && last is Paragraph final)
        {
            MoveInlines(suffix, final);
            collection.Remove(suffix);
        }
        else caret = suffix.ContentStart;
        return caret;
    }
    private static void MoveInlines(Paragraph source, Paragraph destination)
    {
        foreach (var inline in source.Inlines.ToArray())
        {
            var formatting = DocumentTextEditing.CaptureCharacterFormatting(inline);
            source.Inlines.Remove(inline); destination.Inlines.Add(inline);
            DocumentTextEditing.ApplyCharacterFormatting(inline, formatting, preserveLocalValues: true);
        }
    }
}

