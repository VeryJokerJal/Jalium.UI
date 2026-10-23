using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using DocList = Jalium.UI.Documents.List;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class WritingToolsMixedStructureTests
{
    public static IEnumerable<object[]> Models() => Enumerable.Range(0, 12).Select(i => new object[] { i });

    [Theory, MemberData(nameof(Models))]
    public void ExplicitAncestryRoundTripPreservesTheOriginalContainerTree(int model)
    {
        var document = Model(model); string expected = Shape(document.Blocks), text = document.GetText();
        var packet = Packet(document, explicitPath: true);
        var imported = new FlowDocument();
        WritingToolsDocumentFragment.Parse(packet).Insert(imported, 0, 0, () => true);
        Assert.Equal(expected, Shape(imported.Blocks)); Assert.Equal(text, imported.GetText());
    }

    [Theory, MemberData(nameof(Models))]
    public void ExportedAncestryRetainsInterleavingAndListItemOwnership(int model)
    {
        var document = Model(model); var editor = new RichTextBox(document);
        var response = new MacOSTextServicesResponse { Structure = editor.GetWritingToolsStructure() };
        var rows = JsonSerializer.SerializeToElement(response, MacOSTextServicesJsonContext.Default.MacOSTextServicesResponse).GetProperty("structure").EnumerateArray().ToArray();
        var paragraphs = document.EnumerateParagraphs().ToArray(); Assert.Equal(paragraphs.Length, rows.Length);
        var items = new Dictionary<ListItem, string>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < rows.Length; i++)
        {
            var expected = Owners(paragraphs[i]); var path = rows[i].GetProperty("ancestors").EnumerateArray().ToArray();
            Assert.Equal(expected.Length, path.Length); int list = 0, table = 0;
            for (int j = 0; j < path.Length; j++)
            {
                Assert.Equal(expected[j] is ListItem ? "list" : "table", path[j].GetProperty("kind").GetString());
                Assert.Equal(expected[j] is ListItem ? list++ : table++, path[j].GetProperty("index").GetInt32());
                if (expected[j] is ListItem item)
                {
                    string identity = path[j].GetProperty("item").GetString()!; Assert.False(string.IsNullOrEmpty(identity));
                    if (items.TryGetValue(item, out string? seen)) Assert.Equal(seen, identity);
                    else { Assert.DoesNotContain(identity, items.Values); items.Add(item, identity); }
                }
            }
        }
    }

    [Theory, InlineData(0), InlineData(1), InlineData(5), InlineData(9)]
    public void RichReplacementUndoAndRedoKeepMixedStructureAndOriginalIdentity(int model)
    {
        var document = Model(model); var original = document.Blocks.ToArray(); var editor = new RichTextBox(document);
        string expected = Shape(document.Blocks), text = document.GetText(); int changes = 0;
        var packet = Packet(document, explicitPath: true, editor); editor.TextChanged += (_, _) => changes++;
        var args = new TextCompositionEventArgs(UIElement.TextInputEvent, text, 0)
        {
            ImeReplacementRange = (0, text.Length), NativeEditLifetime = () => true,
            NativeTextServicesPayload = JsonSerializer.Serialize(new { proposal = packet })
        };
        editor.RaiseEvent(args); Assert.True(args.Handled); Assert.Equal(expected, Shape(document.Blocks));
        Assert.Equal(text, document.GetText()); Assert.Equal(1, changes); Assert.True(editor.CanUndo);
        Assert.True(editor.Undo()); Assert.Equal(expected, Shape(document.Blocks));
        Assert.Equal(original.Length, document.Blocks.Count);
        for (int i = 0; i < original.Length; i++) Assert.Same(original[i], document.Blocks[i]);
        Assert.True(editor.Redo()); Assert.Equal(expected, Shape(document.Blocks)); Assert.Equal(text, document.GetText());
    }

    [Theory, InlineData("kind"), InlineData("index"), InlineData("duplicate"), InlineData("missing"), InlineData("item")]
    public void InvalidExplicitAncestryIsRejectedBeforeDocumentMutation(string fault)
    {
        var document = Model(0); var original = document.Blocks.ToArray(); string text = document.GetText();
        var packet = JsonNode.Parse(Packet(document, explicitPath: true).GetRawText())!;
        var path = packet["paragraphs"]![0]!["ancestors"]!.AsArray();
        switch (fault)
        {
            case "kind": path[0]!["kind"] = "unknown"; break;
            case "index": path[0]!["index"] = -1; break;
            case "duplicate": path.Add(path[0]!.DeepClone()); break;
            case "missing": path.RemoveAt(0); break;
            case "item": path[0]!["item"] = 42; break;
        }
        using var json = JsonDocument.Parse(packet.ToJsonString());
        Assert.Throws<FormatException>(() => WritingToolsDocumentFragment.Parse(json.RootElement));
        Assert.Equal(text, document.GetText()); Assert.Same(original[0], document.Blocks[0]);
    }

    [Fact]
    public void StructuralStorageSeparatorsDoNotBecomeAdditionalParagraphs()
    {
        var document = Model(1); var packet = Packet(document, explicitPath: true);
        Assert.Contains(packet.GetProperty("paragraphs").EnumerateArray(), x => x.TryGetProperty("storageSeparator", out var v) && v.GetBoolean());
        var imported = new FlowDocument(); WritingToolsDocumentFragment.Parse(packet).Insert(imported, 0, 0, () => true);
        Assert.Equal(Shape(document.Blocks), Shape(imported.Blocks)); Assert.Equal(document.GetText(), imported.GetText());
    }

    [Fact]
    public void StorageSeparatorCannotDiscardAuthoredLetters()
    {
        var packet = JsonNode.Parse(Packet(new FlowDocument(P("内容🙂")), explicitPath: true).GetRawText())!;
        packet["paragraphs"]![0]!["storageSeparator"] = true;
        using var json = JsonDocument.Parse(packet.ToJsonString());
        Assert.Throws<FormatException>(() => WritingToolsDocumentFragment.Parse(json.RootElement));
    }

    [Fact]
    public void LegacyListOnlyPacketRemainsAccepted()
    {
        var model = new FlowDocument(MakeList([P("第一项")], [P("第二项🙂")]));
        var packet = JsonNode.Parse(Packet(model, explicitPath: true).GetRawText())!;
        foreach (var row in packet["paragraphs"]!.AsArray()) row!.AsObject().Remove("ancestors");
        using var json = JsonDocument.Parse(packet.ToJsonString()); var restored = new FlowDocument();
        WritingToolsDocumentFragment.Parse(json.RootElement).Insert(restored, 0, 0, () => true);
        Assert.Equal(Shape(model.Blocks), Shape(restored.Blocks));
    }

    [Fact]
    public void TableDocumentPositionsMatchThePublicStorageProjection()
    {
        var before = P("前🙂"); var left = P("左"); var right = P("右"); var after = P("尾");
        var document = new FlowDocument(); document.Blocks.Add(before); document.Blocks.Add(MakeTable([left], [right])); document.Blocks.Add(after);
        Assert.Equal("前🙂\n左\n\t右\n\t\n尾\n", document.GetText());
        Assert.Equal(13, document.ContentEnd.DocumentOffset);
        Assert.Equal(4, document.GetDocumentOffset(left, 0)); Assert.Equal(7, document.GetDocumentOffset(right, 0)); Assert.Equal(11, document.GetDocumentOffset(after, 0));
        Assert.Same(left.Inlines[0], document.GetPositionAtOffset(4, LogicalDirection.Forward)!.Parent);
        Assert.Same(right.Inlines[0], document.GetPositionAtOffset(7, LogicalDirection.Forward)!.Parent);
        Assert.Same(after.Inlines[0], document.GetPositionAtOffset(11, LogicalDirection.Forward)!.Parent);
        foreach (int offset in new[] { 6, 9, 10, 13 }) Assert.Equal(offset, document.GetPositionAtOffset(offset, LogicalDirection.Forward)!.DocumentOffset);
    }

    private static FlowDocument Model(int index)
    {
        Block value = index switch
        {
            0 => MakeList([MakeTable([P("列表内的表格🙂")])]),
            1 => MakeTable([MakeList([P("表格内的列表")], [P("第二项🙂")])]),
            2 => MakeList([MakeTable([MakeList([P("内层编号")])])]),
            3 => MakeTable([MakeList([MakeTable([P("内层表格")])])]),
            4 => MakeList([MakeTable([MakeList([MakeTable([P("交错四层🙂")])])])]),
            5 => MakeList([P("前段"), MakeTable([P("中间表格")]), P("后段🙂")], [P("第二项")]),
            6 => MakeList([MakeTable([P("表一")]), MakeTable([P("表二")]), P("同一项末段")]),
            7 => MakeList([P("外层"), MakeList([P("内层前段"), MakeTable([P("内层表格")]), P("内层后段")])], [P("外层第二项")]),
            8 => MakeList([MakeTable([P("第一项左")], [P("第一项右")])], [MakeTable([P("第二项左")], [P("第二项右")])]),
            9 => SpanningTable(),
            11 => MakeList([P("\t3.\t这也是用户文字🙂")]),
            _ => MakeList([P(""), MakeTable([P("")], [P("中文 e\u0301 🙂")]), P("")], [P("")])
        };
        return new FlowDocument(value);
    }
    private static Paragraph P(string text) => new(new Run(text));
    private static DocList MakeList(params Block[][] contents)
    {
        var list = new DocList { MarkerStyle = TextMarkerStyle.Decimal, StartIndex = 3 };
        foreach (var blocks in contents) { var item = new ListItem(); foreach (var block in blocks) item.Blocks.Add(block); list.ListItems.Add(item); }
        return list;
    }
    private static Table MakeTable(params Block[][] contents)
    {
        var table = new Table(); var group = new TableRowGroup(); var row = new TableRow();
        table.RowGroups.Add(group); group.Rows.Add(row);
        foreach (var blocks in contents)
        { table.Columns.Add(new TableColumn()); var cell = new TableCell(); foreach (var block in blocks) cell.Blocks.Add(block); row.Cells.Add(cell); }
        return table;
    }
    private static Table SpanningTable()
    {
        var table = MakeTable([MakeList([P("跨行内列表"), P("同项续段🙂")])], [P("跨列标题")]);
        table.Columns.Add(new TableColumn()); var group = table.RowGroups[0]; group.Rows[0].Cells[0].RowSpan = 2; group.Rows[0].Cells[1].ColumnSpan = 2;
        var second = new TableRow(); second.Cells.Add(new TableCell(MakeList([MakeTable([P("右下嵌套")])])) { ColumnSpan = 2 }); group.Rows.Add(second); return table;
    }
    private static TextElement[] Owners(Paragraph paragraph)
    {
        var result = new List<TextElement>();
        for (DependencyObject? owner = paragraph.Parent; owner is TextElement element; owner = element.Parent)
            if (element is ListItem or TableCell) result.Add(element);
        result.Reverse(); return result.ToArray();
    }
    private static string Shape(IEnumerable<Block> blocks) => string.Join("|", blocks.Select(block => block switch
    {
        Paragraph paragraph => "P(" + string.Concat(paragraph.Inlines.OfType<Run>().Select(run => run.Text)) + ")",
        DocList list => $"L({list.MarkerStyle},{list.StartIndex})[" + string.Join(";", list.ListItems.Select(item => Shape(item.Blocks))) + "]",
        Table table => "T" + table.Columns.Count + "[" + string.Join(";", table.RowGroups.SelectMany(group => group.Rows).Select(row => string.Join(",", row.Cells.Select(cell => $"({cell.RowSpan},{cell.ColumnSpan})" + Shape(cell.Blocks))))) + "]",
        _ => throw new InvalidOperationException(block.GetType().Name)
    }));
    private static JsonElement Packet(FlowDocument document, bool explicitPath, RichTextBox? host = null)
    {
        string text = document.GetText(); var editor = host ?? new RichTextBox(document);
        var exported = JsonSerializer.SerializeToElement(new MacOSTextServicesResponse { Structure = editor.GetWritingToolsStructure() }, MacOSTextServicesJsonContext.Default.MacOSTextServicesResponse).GetProperty("structure").EnumerateArray().ToArray();
        if (host is null) editor.Document = new FlowDocument();
        var paragraphs = document.EnumerateParagraphs().ToArray(); var rows = new List<object>(); var itemIds = new Dictionary<TextElement, string>(ReferenceEqualityComparer.Instance); int through = 0;
        for (int i = 0; i < exported.Length; i++)
        {
            var row = exported[i]; int start = row.GetProperty("start").GetInt32(), length = row.GetProperty("length").GetInt32();
            if (start > through) rows.Add(new { start = through, length = start - through, alignment = 0, direction = 0, storageSeparator = true, lists = Array.Empty<object>(), tables = Array.Empty<object>() });
            var record = row.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone()); record["alignment"] = 0; record["direction"] = 0;
            if (explicitPath)
            {
                int list = 0, table = 0;
                record["ancestors"] = Owners(paragraphs[i]).Select(owner =>
                {
                    if (!itemIds.TryGetValue(owner, out string? id)) itemIds[owner] = id = itemIds.Count.ToString();
                    return owner is ListItem ? (object)new { kind = "list", index = list++, item = id } : new { kind = "table", index = table++ };
                }).ToArray();
            }
            rows.Add(record); through = start + length;
        }
        if (through < text.Length) rows.Add(new { start = through, length = text.Length - through, alignment = 0, direction = 0, storageSeparator = true, lists = Array.Empty<object>(), tables = Array.Empty<object>() });
        return JsonSerializer.SerializeToElement(new
        {
            version = 1, text16 = Convert.ToBase64String(MemoryMarshal.AsBytes(text.AsSpan())),
            runs = text.Length == 0 ? Array.Empty<object>() : new object[] { new { start = 0, length = text.Length, family = "Helvetica Neue", size = 18, weight = 400, italic = false, underline = 0, strike = 0 } }, paragraphs = rows
        });
    }
}
