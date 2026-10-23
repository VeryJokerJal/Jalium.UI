using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Media;
using DocList = Jalium.UI.Documents.List;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class WritingToolsPartialFragmentTests
{
    private sealed record Fixture(FlowDocument Document, RichTextBox Editor, Paragraph Paragraph, Run Run,
        List<DependencyObject> Containers);

    private static Fixture Create(string kind)
    {
        var run = new Run("前缀旧字词后缀") { FontSize = 31 };
        var paragraph = new Paragraph(run); Block root = paragraph;
        var containers = new List<DependencyObject>();
        void List()
        {
            var item = new ListItem(); item.Blocks.Add(root);
            var list = new DocList { MarkerStyle = TextMarkerStyle.Decimal, StartIndex = 7 };
            list.ListItems.Add(item); containers.Add(item); containers.Add(list); root = list;
        }
        void Table()
        {
            var cell = new TableCell(root) { ColumnSpan = 2, Padding = new Thickness(9), Background = Brushes.Coral };
            var row = new TableRow(); row.Cells.Add(cell); var group = new TableRowGroup(); group.Rows.Add(row);
            var table = new Table(); table.Columns.Add(new TableColumn { Width = new GridLength(137) });
            table.Columns.Add(new TableColumn { Width = new GridLength(199) }); table.RowGroups.Add(group);
            containers.Add(cell); containers.Add(row); containers.Add(group); containers.Add(table); root = table;
        }
        if (kind == "section-list") { var section = new Section(root) { Padding = new Thickness(13) }; containers.Add(section); root = section; List(); }
        else if (kind == "list") List();
        else if (kind == "table") Table();
        else if (kind == "list-table") { Table(); List(); }
        else if (kind == "table-list") { List(); Table(); }
        else throw new ArgumentException(kind);
        var document = new FlowDocument(new Paragraph(new Run("outside-before")));
        document.Blocks.Add(root); document.Blocks.Add(new Paragraph(new Run("outside-after")));
        return new(document, new RichTextBox(document), paragraph, run, containers);
    }

    private static JsonObject Packet(string text, IEnumerable<JsonObject> paragraphs)
    {
        var runs = new JsonArray();
        if (text.Length != 0) runs.Add(JsonSerializer.SerializeToNode(new
        {
            start = 0, length = text.Length, family = "Helvetica Neue", size = 23, weight = 700,
            italic = true, underline = 1, strike = 0, foreground = 0xff2255ffu
        }));
        return new JsonObject { ["version"] = 1,
            ["text16"] = Convert.ToBase64String(MemoryMarshal.AsBytes(text.AsSpan())),
            ["runs"] = runs, ["paragraphs"] = new JsonArray(paragraphs.Select(p => (JsonNode)p).ToArray()) };
    }

    private static JsonObject Source(RichTextBox editor, int start, int length)
    {
        string all = editor.Document.GetText(); var rows = new List<JsonObject>(); int through = start;
        foreach (var record in editor.GetWritingToolsStructure())
        {
            int a = Math.Max(start, record.Start), b = Math.Min(start + length, record.Start + record.Length);
            if (b <= a) continue;
            if (a > through) rows.Add(Storage(through - start, a - through));
            var row = JsonSerializer.SerializeToNode(record, new JsonSerializerOptions(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull })!.AsObject();
            row["alignment"] = 0; row["direction"] = 0;
            row["start"] = a - start; row["length"] = b - a; row["origin"] ??= "original:" + record.Start;
            if (record.InlineBreaks is { } breaks)
                row["inlineBreaks"] = new JsonArray(breaks.Where(i => i >= a && i < b).Select(i => JsonValue.Create(i - start)).Cast<JsonNode>().ToArray());
            rows.Add(row); through = b;
        }
        if (through < start + length) rows.Add(Storage(through - start, start + length - through));
        return Packet(all.Substring(start, length), rows);
    }

    private static JsonObject Storage(int start, int length) => new()
    { ["start"] = start, ["length"] = length, ["alignment"] = 0, ["direction"] = 0,
        ["lists"] = new JsonArray(), ["tables"] = new JsonArray(), ["storageSeparator"] = true };

    private static JsonObject Proposed(JsonObject source, params string[] replacements)
    {
        string old = System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(source["text16"]!.GetValue<string>()));
        var rows = new List<JsonObject>(); var text = new System.Text.StringBuilder(); int next = 0;
        foreach (var node in source["paragraphs"]!.AsArray())
        {
            var row = node!.DeepClone().AsObject(); int a = row["start"]!.GetValue<int>(), n = row["length"]!.GetValue<int>();
            string value = row["storageSeparator"]?.GetValue<bool>() == true ? old.Substring(a, n) : replacements[next++];
            row["start"] = text.Length; row["length"] = value.Length; row.Remove("inlineBreaks");
            if (value.Length != 0) { rows.Add(row); text.Append(value); }
        }
        Assert.Equal(replacements.Length, next); return Packet(text.ToString(), rows);
    }

    private static TextCompositionEventArgs Apply(RichTextBox editor, int start, int length, JsonObject source,
        JsonObject proposal, int? sourceStart = null)
    {
        string replacement = System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(proposal["text16"]!.GetValue<string>()));
        string payload = new JsonObject { ["source"] = source, ["sourceStart"] = sourceStart ?? start, ["proposal"] = proposal }.ToJsonString();
        var args = new TextCompositionEventArgs(UIElement.TextInputEvent, replacement, 0)
        { ImeReplacementRange = (start, length), NativeTextServicesPayload = payload, NativeEditLifetime = () => true };
        editor.RaiseEvent(args); return args;
    }

    [Theory]
    [InlineData("list")] [InlineData("table")] [InlineData("list-table")] [InlineData("table-list")] [InlineData("section-list")]
    public void WordReplacementUsesTheExistingEnclosure(string kind)
    {
        var f = Create(kind); int start = f.Run.ContentStart.DocumentOffset + 2;
        string before = f.Document.GetText(); var first = f.Document.Blocks[0]; var last = f.Document.Blocks[^1];
        var source = Source(f.Editor, start, 3); var proposal = Proposed(source, "Miii 中文🙂");
        var args = Apply(f.Editor, start, 3, source, proposal);
        Assert.True(args.Handled); Assert.Equal(before.Replace("旧字词", "Miii 中文🙂", StringComparison.Ordinal), f.Document.GetText());
        Assert.Same(first, f.Document.Blocks[0]); Assert.Same(last, f.Document.Blocks[^1]);
        Assert.All(f.Containers, value => Assert.Contains(value, Descendants(f.Document)));
        Assert.DoesNotContain(f.Paragraph.OwnerCollection!, block => block is Table or DocList);
        var inserted = Assert.Single(f.Paragraph.Inlines.OfType<Run>(), run => run.Text == "Miii 中文🙂");
        Assert.Equal(23, inserted.FontSize); Assert.Equal(FontWeights.Bold, inserted.FontWeight);
        Assert.Equal(31, f.Run.FontSize); Assert.True(f.Editor.Undo()); Assert.Equal(before, f.Document.GetText());
        Assert.True(f.Editor.Redo()); Assert.Equal(before.Replace("旧字词", "Miii 中文🙂", StringComparison.Ordinal), f.Document.GetText());
    }

    [Theory]
    [InlineData("list", 0)] [InlineData("list", 2)] [InlineData("list", 7)]
    [InlineData("table", 0)] [InlineData("table", 2)] [InlineData("table", 7)]
    [InlineData("list-table", 2)] [InlineData("table-list", 2)]
    public void EmptyRangeKeepsTheCaretInsideItsExistingOwner(string kind, int offset)
    {
        var f = Create(kind); int start = f.Run.ContentStart.DocumentOffset + offset;
        string before = f.Document.GetText(); var source = Source(f.Editor, start, 1); var proposal = Proposed(source, "新🙂");
        var args = Apply(f.Editor, start, 0, source, proposal);
        Assert.True(args.Handled); Assert.Equal(before.Insert(start, "新🙂"), f.Document.GetText());
        Assert.All(f.Containers, value => Assert.Contains(value, Descendants(f.Document)));
        Assert.DoesNotContain(f.Paragraph.OwnerCollection!, block => block is Table or DocList);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CrossItemOrCellReplacementRetainsEachDestinationAndTheOutsideRuns(bool table)
    {
        var paragraphs = Enumerable.Range(0, 3).Select(i => new Paragraph(new Run("abc" + i + "XYZ") { FontSize = 30 + i })).ToArray();
        var document = new FlowDocument(new Paragraph(new Run("outside"))); var owners = new List<DependencyObject>();
        if (table)
        {
            var value = new Table(); var row = new TableRow(); var group = new TableRowGroup(); group.Rows.Add(row); value.RowGroups.Add(group);
            foreach (var p in paragraphs) { var cell = new TableCell(p) { Padding = new Thickness(7) }; row.Cells.Add(cell); owners.Add(cell); }
            document.Blocks.Add(value); owners.Add(value);
        }
        else
        {
            var value = new DocList { StartIndex = 7, MarkerStyle = TextMarkerStyle.LowerRoman };
            foreach (var p in paragraphs) { var item = new ListItem(p); value.ListItems.Add(item); owners.Add(item); }
            document.Blocks.Add(value); owners.Add(value);
        }
        document.Blocks.Add(new Paragraph(new Run("after"))); var editor = new RichTextBox(document);
        var prefix = Assert.IsType<Run>(paragraphs[0].Inlines[0]); var suffix = Assert.IsType<Run>(paragraphs[^1].Inlines[0]);
        int start = paragraphs[0].ContentStart.DocumentOffset + 2, end = paragraphs[^1].ContentStart.DocumentOffset + 5;
        var source = Source(editor, start, end - start); var proposal = Proposed(source, "one\n", "two\n", "three");
        var args = Apply(editor, start, end - start, source, proposal);
        Assert.True(args.Handled); Assert.All(owners, owner => Assert.Contains(owner, Descendants(document)));
        Assert.Equal(new[] { "abone", "two", "threeYZ" }, document.EnumerateParagraphs().Skip(1).Take(3).Select(p => Text(p)));
        Assert.Same(paragraphs[0], prefix.Parent); Assert.Same(paragraphs[^1], suffix.Parent);
        Assert.Equal(30, prefix.FontSize); Assert.Equal(32, suffix.FontSize);
        Assert.Equal(3, table ? Assert.IsType<Table>(document.Blocks[1]).RowGroups[0].Rows[0].Cells.Count : Assert.IsType<DocList>(document.Blocks[1]).ListItems.Count);
        Assert.True(editor.Undo()); Assert.Equal("abc0XYZ", Text(paragraphs[0])); Assert.True(editor.Redo());
        Assert.Equal("threeYZ", Text(paragraphs[^1]));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void StaleSourceRejectsBeforeDeletingOriginalText(bool badOffset)
    {
        var f = Create("list"); int start = f.Run.ContentStart.DocumentOffset + 2; string before = f.Document.GetText();
        var source = Source(f.Editor, start, 3); var proposal = Proposed(source, "new");
        if (!badOffset) source["text16"] = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes("错误文"));
        var args = Apply(f.Editor, start, 3, source, proposal, badOffset ? start + 1 : start);
        Assert.False(args.Handled); Assert.Equal(before, f.Document.GetText()); Assert.False(f.Editor.CanUndo);
    }

    [Theory]
    [InlineData("list")] [InlineData("table")]
    public void MultipleParagraphsInOneOwnerKeepTheirOutsideRunFormatting(string kind)
    {
        var f = Create(kind); var suffix = new Run("XYZ") { FontSize = 41 };
        var last = new Paragraph(new Italic(suffix)); f.Paragraph.OwnerCollection!.Add(last);
        int start = f.Run.ContentStart.DocumentOffset + 2, end = suffix.ContentStart.DocumentOffset + 1;
        string before = f.Document.GetText(); var source = Source(f.Editor, start, end - start);
        Assert.True(Apply(f.Editor, start, end - start, source, Proposed(source, "one\n", "two")).Handled);
        Assert.Equal(before[..start] + "one\ntwo" + before[end..], f.Document.GetText());
        Assert.Equal(41, suffix.GetEffectiveFontSize()); Assert.Equal(FontStyles.Italic, suffix.GetEffectiveFontStyle());
        Assert.Same(f.Document, suffix.GetFlowDocument());
        Assert.True(f.Editor.Undo()); Assert.Same(last, suffix.Parent!.Parent); Assert.Equal(before, f.Document.GetText());
    }

    [Theory]
    [InlineData("list")] [InlineData("table")] [InlineData("list-table")] [InlineData("table-list")]
    public void NativeContainerAddressesCanChangeWithoutChangingTheOriginalEnclosure(string kind)
    {
        var f = Create(kind); int start = f.Run.ContentStart.DocumentOffset + 2; string before = f.Document.GetText();
        var source = Source(f.Editor, start, 3); var proposed = Proposed(source, "new");
        foreach (var row in proposed["paragraphs"]!.AsArray())
            foreach (string property in new[] { "lists", "tables" })
                foreach (var owner in row![property]!.AsArray()) owner!["id"] = "renewed-native:" + owner["id"]!.GetValue<string>();
        Assert.True(Apply(f.Editor, start, 3, source, proposed).Handled);
        Assert.Equal(before.Replace("旧字词", "new", StringComparison.Ordinal), f.Document.GetText());
        Assert.All(f.Containers, owner => Assert.Contains(owner, Descendants(f.Document)));
        Assert.DoesNotContain(f.Paragraph.OwnerCollection!, block => block is Table or DocList);
    }

    [Theory]
    [InlineData("list")] [InlineData("table")]
    public void AReplacedParagraphWithIdenticalTextInvalidatesTheOldSource(string kind)
    {
        var f = Create(kind); int start = f.Run.ContentStart.DocumentOffset + 2;
        var source = Source(f.Editor, start, 3); var proposal = Proposed(source, "new");
        var owner = f.Paragraph.OwnerCollection!; int at = owner.IndexOf(f.Paragraph);
        owner.RemoveAt(at); var fresh = new Paragraph(new Run(f.Run.Text)); owner.Insert(at, fresh);
        string before = f.Document.GetText(); bool undo = f.Editor.CanUndo;
        Assert.False(Apply(f.Editor, start, 3, source, proposal).Handled);
        Assert.Equal(before, f.Document.GetText()); Assert.Same(fresh, owner[at]); Assert.Equal(undo, f.Editor.CanUndo);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ExplicitListMetadataUpdatesTheExistingListAndIsUndoable(bool format)
    {
        var f = Create("list"); var list = Assert.Single(f.Containers.OfType<DocList>()); int start = f.Run.ContentStart.DocumentOffset + 2;
        var source = Source(f.Editor, start, 3); var proposal = Proposed(source, "new");
        proposal["paragraphs"]![0]!["lists"]![0]![format ? "format" : "start"] = format ? JsonValue.Create("{upper-roman}") : JsonValue.Create(99);
        Assert.True(Apply(f.Editor, start, 3, source, proposal).Handled);
        Assert.Same(list, f.Document.Blocks[1]); Assert.Equal(format ? TextMarkerStyle.UpperRoman : TextMarkerStyle.Decimal, list.MarkerStyle);
        Assert.Equal(format ? 7 : 99, list.StartIndex); Assert.Single(list.ListItems);
        Assert.True(f.Editor.Undo()); Assert.Equal(7, list.StartIndex); Assert.Equal(TextMarkerStyle.Decimal, list.MarkerStyle);
        Assert.True(f.Editor.Redo()); Assert.Equal(format ? 7 : 99, list.StartIndex);
    }

    [Theory]
    [InlineData(2)] [InlineData(4)]
    public void ExplicitTableColumnCountPreservesTheSurvivingColumnObjects(int columns)
    {
        var f = Create("table"); var table = Assert.Single(f.Containers.OfType<Table>());
        table.Columns.Add(new TableColumn { Width = new GridLength(222) }); var original = table.Columns.ToArray();
        int start = f.Run.ContentStart.DocumentOffset + 2; var source = Source(f.Editor, start, 3); var proposal = Proposed(source, "new");
        proposal["paragraphs"]![0]!["tables"]![0]!["tableColumns"] = columns;
        Assert.True(Apply(f.Editor, start, 3, source, proposal).Handled);
        Assert.Equal(columns, table.Columns.Count); Assert.Same(original[0], table.Columns[0]); Assert.Same(original[1], table.Columns[1]);
        Assert.Equal(new GridLength(137), table.Columns[0].Width); Assert.Equal(new GridLength(199), table.Columns[1].Width);
        Assert.True(f.Editor.Undo()); Assert.Equal(original, table.Columns.ToArray()); Assert.True(f.Editor.Redo()); Assert.Equal(columns, table.Columns.Count);
    }

    [Fact]
    public void ANewInnerListDoesNotDuplicateItsExistingOuterTable()
    {
        var f = Create("table"); int start = f.Run.ContentStart.DocumentOffset + 2;
        var source = Source(f.Editor, start, 3); var proposal = Proposed(source, "new"); var row = proposal["paragraphs"]![0]!;
        row["lists"]!.AsArray().Add(JsonSerializer.SerializeToNode(new { id = "new-list", format = "{circle}", start = 1 }));
        row["ancestors"]!.AsArray().Add(JsonSerializer.SerializeToNode(new { kind = "list", index = 0 }));
        Assert.True(Apply(f.Editor, start, 3, source, proposal).Handled);
        Assert.Single(Descendants(f.Document).OfType<Table>()); Assert.Single(Descendants(f.Document).OfType<DocList>());
        var cell = Assert.Single(f.Containers.OfType<TableCell>());
        Assert.IsType<DocList>(cell.Blocks[1]); Assert.Equal("前缀", Text(Assert.IsType<Paragraph>(cell.Blocks[0])));
        Assert.Equal("后缀", Text(Assert.IsType<Paragraph>(cell.Blocks[2])));
    }

    [Theory]
    [InlineData("list", false)] [InlineData("table", false)]
    [InlineData("list", true)] [InlineData("table", true)]
    public void WholeContainerConversionReplacesThatBlockInItsParent(string kind, bool structured)
    {
        var f = Create(kind); Block old = f.Document.Blocks[1]; var outside = f.Document.Blocks[^1];
        int start = f.Document.GetDocumentOffset(old, 0), length = FlowDocument.GetBlockLength(old);
        var source = Source(f.Editor, start, length);
        var row = new JsonObject { ["start"] = 0, ["length"] = 4, ["alignment"] = 0, ["direction"] = 0,
            ["lists"] = new JsonArray(), ["tables"] = new JsonArray() };
        if (structured && kind == "list") row["tables"]!.AsArray().Add(JsonSerializer.SerializeToNode(new
        { id = "new-table", row = 0, column = 0, rows = 1, columns = 1, tableColumns = 1 }));
        if (structured && kind == "table") row["lists"]!.AsArray().Add(JsonSerializer.SerializeToNode(new { id = "new-list", format = "{decimal}", start = 1 }));
        Assert.True(Apply(f.Editor, start, length, source, Packet("new\n", [row])).Handled);
        Assert.Equal(3, f.Document.Blocks.Count); Assert.Same(outside, f.Document.Blocks[^1]);
        Assert.IsType(structured ? kind == "list" ? typeof(Table) : typeof(DocList) : typeof(Paragraph), f.Document.Blocks[1]);
        Assert.True(f.Editor.Undo()); Assert.Same(old, f.Document.Blocks[1]); Assert.Same(outside, f.Document.Blocks[^1]);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void RetirementDuringAContainerPropertyCallbackRollsBackWithoutStealingTheNewDocumentCaret(int mode)
    {
        var f = Create("list"); var list = Assert.Single(f.Containers.OfType<DocList>()); int start = f.Run.ContentStart.DocumentOffset + 2;
        var source = Source(f.Editor, start, 3); var proposal = Proposed(source, "new");
        proposal["paragraphs"]![0]!["lists"]![0]!["start"] = 99;
        var fresh = new FlowDocument(new Paragraph(new Run("new document"))); string before = f.Document.GetText();
        bool live = true;
        list.PropertyChangedInternal += (property, _, value) =>
        {
            if (property != DocList.StartIndexProperty || !Equals(value, 99)) return;
            if (mode == 0) f.Editor.Document = fresh;
            else if (mode == 1) f.Editor.IsReadOnly = true;
            else live = false;
        };
        string payload = new JsonObject { ["source"] = source, ["sourceStart"] = start, ["proposal"] = proposal }.ToJsonString();
        var args = new TextCompositionEventArgs(UIElement.TextInputEvent, "new", 0)
        { ImeReplacementRange = (start, 3), NativeTextServicesPayload = payload, NativeEditLifetime = () => live };
        f.Editor.RaiseEvent(args);
        Assert.False(args.Handled); Assert.Equal(before, f.Document.GetText()); Assert.Equal(7, list.StartIndex);
        Assert.Same(mode == 0 ? fresh : f.Document, f.Editor.Document);
        Assert.Same(f.Editor.Document, f.Editor.CaretPosition.Document); Assert.False(f.Editor.CanUndo);
    }

    [Theory]
    [InlineData("list", "\r")] [InlineData("list", "\n")] [InlineData("list", "\r\n")] [InlineData("list", "\u2029")]
    [InlineData("table", "\r")] [InlineData("table", "\n")] [InlineData("table", "\r\n")] [InlineData("table", "\u2029")]
    public void AuthoredSeparatorsRemainInsideTheirParagraphAfterRebasing(string kind, string ending)
    {
        var f = Create(kind); int start = f.Run.ContentStart.DocumentOffset + 2;
        string before = f.Document.GetText(); var source = Source(f.Editor, start, 3); var proposal = Proposed(source, "x" + ending + "y");
        proposal["paragraphs"]![0]!["inlineBreaks"] = new JsonArray(Enumerable.Range(1, ending.Length).Select(i => (JsonNode)JsonValue.Create(i)!).ToArray());
        Assert.True(Apply(f.Editor, start, 3, source, proposal).Handled);
        Assert.Equal(before.Replace("旧字词", "x" + ending + "y", StringComparison.Ordinal), f.Document.GetText());
        Assert.Single(f.Paragraph.OwnerCollection!); Assert.Same(f.Paragraph, f.Run.Parent);
    }

    [Fact]
    public void AnEmptyCellUsesItsOwnInsertionSlot()
    {
        var table = new Table(); var row = new TableRow(); var group = new TableRowGroup(); group.Rows.Add(row); table.RowGroups.Add(group);
        var empty = new TableCell(); row.Cells.Add(empty); row.Cells.Add(new TableCell(new Paragraph(new Run("neighbor"))));
        var document = new FlowDocument(table); var editor = new RichTextBox(document); int start = empty.ContentStart.DocumentOffset;
        var source = Source(editor, start, 1); var proposal = Proposed(source, "新🙂"); proposal["paragraphs"]![0]!.AsObject().Remove("emptyCell");
        Assert.True(Apply(editor, start, 0, source, proposal).Handled);
        Assert.Equal("新🙂", Text(Assert.IsType<Paragraph>(Assert.Single(empty.Blocks))));
        Assert.Equal("neighbor", Text(Assert.IsType<Paragraph>(row.Cells[1].Blocks[0])));
        Assert.True(editor.Undo()); Assert.Empty(empty.Blocks); Assert.True(editor.Redo()); Assert.Single(empty.Blocks);
    }

    [Theory]
    [InlineData("UV")] [InlineData("one\ntwo")]
    public void OlderPlainFormattedPacketsRemainSupported(string value)
    {
        var f = Create("list"); int start = f.Run.ContentStart.DocumentOffset + 2; string before = f.Document.GetText();
        var rows = new List<JsonObject>(); int offset = 0;
        foreach (string part in value.Split('\n'))
        {
            int length = part.Length + (offset + part.Length < value.Length ? 1 : 0);
            rows.Add(new JsonObject { ["start"] = offset, ["length"] = length, ["alignment"] = 0, ["direction"] = 0,
                ["lists"] = new JsonArray(), ["tables"] = new JsonArray() }); offset += length;
        }
        var packet = Packet(value, rows); var args = new TextCompositionEventArgs(UIElement.TextInputEvent, value, 0)
        { ImeReplacementRange = (start, 3), NativeTextServicesPayload = new JsonObject { ["proposal"] = packet }.ToJsonString(), NativeEditLifetime = () => true };
        f.Editor.RaiseEvent(args); Assert.True(args.Handled); Assert.Equal(before.Replace("旧字词", value, StringComparison.Ordinal), f.Document.GetText());
    }

    [Fact]
    public void ParagraphProvenanceSurvivesOffsetShiftsAndIsDifferentForDifferentNodes()
    {
        var f = Create("list"); var first = Source(f.Editor, f.Run.ContentStart.DocumentOffset, 1);
        string? origin = first["paragraphs"]![0]!["origin"]?.GetValue<string>(); Assert.NotNull(origin);
        f.Document.Blocks.Insert(0, new Paragraph(new Run("new prefix")));
        var second = Source(f.Editor, f.Run.ContentStart.DocumentOffset, 1);
        Assert.Equal(origin, second["paragraphs"]![0]!["origin"]?.GetValue<string>());
        Assert.NotEqual(origin, Source(f.Editor, 0, 1)["paragraphs"]![0]!["origin"]?.GetValue<string>());
    }

    [Theory]
    [InlineData(false, 0)] [InlineData(false, 1)] [InlineData(false, 2)] [InlineData(false, 3)]
    [InlineData(true, 0)] [InlineData(true, 1)] [InlineData(true, 2)] [InlineData(true, 3)]
    public void MalformedSourceIdentitiesRejectBeforeMutation(bool owner, int invalid)
    {
        var f = Create("list"); int start = f.Run.ContentStart.DocumentOffset + 2; string before = f.Document.GetText();
        var source = Source(f.Editor, start, 3); var proposal = Proposed(source, "new");
        JsonNode value = invalid switch { 0 => JsonValue.Create(1)!, 1 => JsonValue.Create(true)!, 2 => JsonValue.Create("")!, _ => JsonValue.Create(new string('x', 1025))! };
        if (owner) source["paragraphs"]![0]!["ancestors"]![0]!["origin"] = value;
        else source["paragraphs"]![0]!["origin"] = value;
        Assert.Throws<FormatException>(() => Apply(f.Editor, start, 3, source, proposal));
        Assert.Equal(before, f.Document.GetText()); Assert.False(f.Editor.CanUndo);
    }

    private static string Text(Paragraph p) => new TextRange(p.ContentStart, p.ContentEnd).Text;

    private static IEnumerable<DependencyObject> Descendants(DependencyObject value)
    {
        yield return value;
        IEnumerable<DependencyObject> children = value switch
        {
            FlowDocument d => d.Blocks, Paragraph p => p.Inlines, Section s => s.Blocks,
            DocList l => l.ListItems, ListItem i => i.Blocks, Table t => t.RowGroups,
            TableRowGroup g => g.Rows, TableRow r => r.Cells, TableCell c => c.Blocks,
            Span s => s.Inlines, _ => []
        };
        foreach (var child in children) foreach (var descendant in Descendants(child)) yield return descendant;
    }
}
