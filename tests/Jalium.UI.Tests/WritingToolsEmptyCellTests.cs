using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jalium.UI.Controls;
using Jalium.UI.Documents;

namespace Jalium.UI.Tests;

public class WritingToolsEmptyCellTests
{
    [Fact]
    public void Structure_RecordsTheVirtualTabOfEachZeroBlockCell()
    {
        var document = RichTextTableLayoutTests.Model(5); var editor = new RichTextBox(document);
        var records = JsonSerializer.SerializeToElement(editor.GetWritingToolsStructure(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains(records.EnumerateArray(), row => row.TryGetProperty("emptyCell", out var flag) && flag.GetBoolean() &&
            row.GetProperty("length").GetInt32() == 1 && row.GetProperty("tables").GetArrayLength() > 0);
    }

    [Theory, InlineData(false), InlineData(true)]
    public void Import_PreservesZeroBlocksAndTheExactCellProjection(bool allEmpty)
    {
        var packet = Packet(allEmpty); var fragment = WritingToolsDocumentFragment.Parse(JsonSerializer.SerializeToElement(packet));
        var document = new FlowDocument(); fragment.Insert(document, 0, 0, () => true);
        var table = Assert.IsType<Table>(Assert.Single(document.Blocks)); var row = Assert.Single(Assert.Single(table.RowGroups).Rows);
        Assert.Empty(row.Cells[0].Blocks);
        if (allEmpty) Assert.Empty(row.Cells[1].Blocks);
        else Assert.IsType<Paragraph>(Assert.Single(row.Cells[1].Blocks));
        Assert.Equal(allEmpty ? "\t\t\n" : "\tX\n\t\n", document.GetText());
    }

    [Theory, InlineData("authored"), InlineData("number"), InlineData("string"), InlineData("separator"), InlineData("length"), InlineData("table")]
    public void InvalidEmptyCellMetadata_IsRejectedBeforeImport(string problem)
    {
        var packet = Packet(false); var row = packet["paragraphs"]![0]!;
        if (problem == "authored") packet["text16"] = Convert.ToBase64String(Encoding.Unicode.GetBytes("AX\n\t\n"));
        if (problem == "number") row["emptyCell"] = 1;
        if (problem == "string") row["emptyCell"] = "true";
        if (problem == "separator") row["storageSeparator"] = true;
        if (problem == "length") row["length"] = 2;
        if (problem == "table") { row["tables"] = new JsonArray(); row["ancestors"] = new JsonArray(); }
        Assert.Throws<FormatException>(() => WritingToolsDocumentFragment.Parse(JsonSerializer.SerializeToElement(packet)));
    }

    [Theory, InlineData(true), InlineData(false)]
    public void PartialContext_IncludesOnlyTheIntersectedEmptySlot(bool include)
    {
        var document = RichTextTableLayoutTests.Model(5); var editor = new RichTextBox(document);
        var records = JsonSerializer.SerializeToElement(editor.GetWritingToolsStructure(include ? 0 : 1, 1),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(include, records.EnumerateArray().Any(row => row.TryGetProperty("emptyCell", out var flag) && flag.GetBoolean()));
    }

    [Fact]
    public void ARealEmptyParagraph_RemainsAParagraphInItsCell()
    {
        var packet = Packet(false); packet["text16"] = Convert.ToBase64String(Encoding.Unicode.GetBytes("\n\tX\n\t\n"));
        packet["runs"]![0]!["length"] = 6;
        var first = packet["paragraphs"]![0]!.DeepClone(); first["emptyCell"] = false;
        var second = packet["paragraphs"]![1]!.DeepClone(); second["start"] = 2;
        var last = packet["paragraphs"]![2]!.DeepClone(); last["start"] = 4;
        packet["paragraphs"] = new JsonArray(first,
            new JsonObject { ["start"] = 1, ["length"] = 1, ["storageSeparator"] = true }, second, last);
        var fragment = WritingToolsDocumentFragment.Parse(JsonSerializer.SerializeToElement(packet));
        var document = new FlowDocument(); fragment.Insert(document, 0, 0, () => true);
        var table = Assert.IsType<Table>(Assert.Single(document.Blocks));
        Assert.IsType<Paragraph>(Assert.Single(table.RowGroups[0].Rows[0].Cells[0].Blocks));
        Assert.Equal("\n\tX\n\t\n", document.GetText());
    }

    private static JsonObject Packet(bool allEmpty)
    {
        var text = allEmpty ? "\t\t\n" : "\tX\n\t\n";
        object Cell(int start, int length, int column, bool empty) => new {
            start, length, emptyCell = empty, alignment = 0, direction = 0, skipPrefix = 0,
            lists = Array.Empty<object>(), tables = new[] { new { id = "t", row = 0, column, rows = 1, columns = 1, tableColumns = 2 } },
            ancestors = new[] { new { kind = "table", index = 0 } }
        };
        var value = new { version = 1, text16 = Convert.ToBase64String(Encoding.Unicode.GetBytes(text)),
            runs = new[] { new { start = 0, length = text.Length, family = "Arial", size = 15, weight = 400, italic = false, underline = 0, strike = 0 } },
            paragraphs = new object[] { Cell(0, 1, 0, true), Cell(1, allEmpty ? 1 : 2, 1, allEmpty),
                new { start = allEmpty ? 2 : 3, length = allEmpty ? 1 : 2, storageSeparator = true } }
        };
        return JsonSerializer.SerializeToNode(value)!.AsObject();
    }
}
