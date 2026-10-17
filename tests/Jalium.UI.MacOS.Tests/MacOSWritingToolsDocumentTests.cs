using System.Runtime.InteropServices;
using System.Text.Json;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Media;
using DocList = Jalium.UI.Documents.List;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSWritingToolsDocumentTests
{
    private static JsonElement Packet(string text, object[]? paragraphs = null)
    {
        var rows = new List<object>();
        if (paragraphs is null)
        {
            int from = 0;
            while (from < text.Length)
            {
                int next = text.IndexOf('\n', from);
                int end = next < 0 ? text.Length : next + 1;
                rows.Add(Paragraph(from, end - from)); from = end;
            }
        }
        var runs = text.Length == 0 ? Array.Empty<object>() : new object[] { new
        {
            start = 0, length = text.Length, family = "Helvetica Neue", size = 23, weight = 700,
            italic = true, underline = 1, strike = 0, foreground = 0xff2255ffu
        }};
        return JsonSerializer.SerializeToElement(new
        {
            version = 1, text16 = Convert.ToBase64String(MemoryMarshal.AsBytes(text.AsSpan())),
            runs, paragraphs = paragraphs ?? rows.ToArray()
        });
    }
    private static object Paragraph(int start, int length, object[]? lists = null, object[]? tables = null) =>
        new { start, length, alignment = 0, direction = 0, lists = lists ?? Array.Empty<object>(), tables = tables ?? Array.Empty<object>() };
    private static object List(string id = "list", string format = "{decimal}", int start = 3) => new { id, format, start };
    private static object Cell(int row, int column, int rows = 1, int columns = 1, int tableColumns = 2) =>
        new { id = "table", row, column, rows, columns, tableColumns };

    [Theory]
    [InlineData("Miii 中文🙂")] [InlineData("e\u0301 אבג")] [InlineData("")]
    public void WholeDocumentReplacement_ImportsRealRunsAndRetainsTheDocument(string value)
    {
        var original = new Run("original");
        var document = new FlowDocument(new Paragraph(original));
        var fragment = WritingToolsDocumentFragment.Parse(Packet(value));
        fragment.Insert(document, 0, document.GetText().Length, () => true);
        Assert.Equal(value.Length == 0 ? "" : value + "\n", document.GetText());
        if (value.Length == 0) { Assert.Empty(document.Blocks); return; }
        var paragraph = Assert.IsType<Paragraph>(Assert.Single(document.Blocks));
        var run = Assert.IsType<Run>(Assert.Single(paragraph.Inlines));
        Assert.Equal(value, run.Text); Assert.Equal(23, run.FontSize); Assert.Equal(FontWeights.Bold, run.FontWeight);
        Assert.Equal(FontStyles.Italic, run.FontStyle); Assert.Equal(Colors.Blue.B, Assert.IsType<SolidColorBrush>(run.Foreground).Color.B);
        Assert.NotSame(original, run);
    }

    [Theory]
    [InlineData("UV", "abUVYZ\n")] [InlineData("one\ntwo", "abone\ntwoYZ\n")]
    [InlineData("one\n", "abone\nYZ\n")] [InlineData("", "abYZ\n")]
    public void CrossParagraphReplacement_PreservesSurvivingNodesAndTheirStyles(string replacement, string expected)
    {
        var prefix = new Run("abc") { FontWeight = FontWeights.Black };
        var suffix = new Run("XYZ") { FontSize = 31 };
        var italic = new Italic(suffix);
        var first = new Paragraph(prefix); var last = new Paragraph(italic);
        var document = new FlowDocument(); document.Blocks.Add(first); document.Blocks.Add(new Paragraph(new Run("middle"))); document.Blocks.Add(last);
        var fragment = WritingToolsDocumentFragment.Parse(Packet(replacement));
        fragment.Insert(document, 2, 10, () => true);
        Assert.Equal(expected, document.GetText());
        Assert.Same(first, document.Blocks[0]); Assert.Same(prefix, first.Inlines[0]);
        Assert.Equal(FontWeights.Black, prefix.GetEffectiveFontWeight()); Assert.Equal(31, suffix.GetEffectiveFontSize());
        Assert.Same(italic, suffix.Parent); Assert.Equal(FontStyles.Italic, suffix.GetEffectiveFontStyle());
    }

    [Fact]
    public void ListResult_ProducesListItemsAndNumbering()
    {
        const string text = "one\ntwo\n";
        var packet = Packet(text, [Paragraph(0, 4, [List()]), Paragraph(4, 4, [List()])]);
        var fragment = WritingToolsDocumentFragment.Parse(packet);
        var list = Assert.IsType<DocList>(Assert.Single(fragment.Blocks));
        Assert.Equal(TextMarkerStyle.Decimal, list.MarkerStyle); Assert.Equal(3, list.StartIndex);
        Assert.Equal(2, list.ListItems.Count);
        var document = new FlowDocument(); foreach (var block in fragment.Blocks) document.Blocks.Add(block);
        Assert.Equal(text, document.GetText());
    }

    [Fact]
    public void NestedListResult_StaysInsideItsOwningItem()
    {
        var packet = Packet("one\nsub\ntwo\n",
            [Paragraph(0,4,[List("outer")]),Paragraph(4,4,[List("outer"),List("inner","{circle}",1)]),Paragraph(8,4,[List("outer")])]);
        var outer = Assert.IsType<DocList>(Assert.Single(WritingToolsDocumentFragment.Parse(packet).Blocks));
        Assert.Equal(2, outer.ListItems.Count);
        var nested = Assert.IsType<DocList>(outer.ListItems[0].Blocks[1]);
        Assert.Equal(TextMarkerStyle.Circle, nested.MarkerStyle); Assert.Single(nested.ListItems);
    }

    [Fact]
    public void TableResult_ProducesRowsCellsAndColumnSpans()
    {
        var packet = Packet("head\na\tb\n", [Paragraph(0,5,tables:[Cell(0,0,columns:2)]),
            Paragraph(5,2,tables:[Cell(1,0)]),Paragraph(7,2,tables:[Cell(1,1)])]);
        var fragment = WritingToolsDocumentFragment.Parse(packet);
        var table = Assert.IsType<Table>(Assert.Single(fragment.Blocks));
        Assert.Equal(2, table.Columns.Count);
        var group = Assert.Single(table.RowGroups); Assert.Equal(2, group.Rows.Count);
        Assert.Equal(2, Assert.Single(group.Rows[0].Cells).ColumnSpan); Assert.Equal(2, group.Rows[1].Cells.Count);
        var document = new FlowDocument(); document.Blocks.Add(table);
        // Preserve the existing FlowDocument projection: paragraph newline, cell tab, row newline.
        Assert.Equal("head\n\t\na\n\tb\n\t\n", document.GetText());
    }

    [Fact]
    public void CancelledLifetime_DoesNotModifyAnyOriginalNode()
    {
        var run = new Run("abc");var paragraph = new Paragraph(run);var document = new FlowDocument(paragraph);
        WritingToolsDocumentFragment.Parse(Packet("replacement")).Insert(document,0,4,()=>false);
        Assert.Same(paragraph,Assert.Single(document.Blocks));Assert.Same(run,Assert.Single(paragraph.Inlines));Assert.Equal("abc",run.Text);
    }

    [Theory]
    [InlineData(0,4)] [InlineData(1,2)]
    public void RichInput_OneUndoRestoresOriginalIdentityAndRedoRestoresFormatting(int start,int length)
    {
        var original = new Run("abc") { FontSize = 31 };var paragraph = new Paragraph(original);
        var document = new FlowDocument(paragraph);var editor = new RichTextBox(document);
        var packet = Packet("UV");
        string payload = JsonSerializer.Serialize(new { proposal = packet });
        int changes = 0;editor.TextChanged += (_,_)=>++changes;
        var args = new TextCompositionEventArgs(UIElement.TextInputEvent,"UV",0)
        { ImeReplacementRange=(start,length),NativeTextServicesPayload=payload,NativeEditLifetime=()=>true };
        editor.RaiseEvent(args);
        Assert.True(args.Handled);Assert.Single(document.Blocks);
        Assert.Equal(start==0?"UV\n":"aUV\n",document.GetText());Assert.Equal(1,changes);Assert.True(editor.CanUndo);
        Assert.True(editor.Undo());Assert.Same(paragraph,Assert.Single(document.Blocks));Assert.Same(original,Assert.Single(paragraph.Inlines));
        Assert.Equal("abc",original.Text);Assert.Equal(31,original.FontSize);
        Assert.True(editor.Redo());Assert.Equal(start==0?"UV\n":"aUV\n",document.GetText());
        var inserted=document.EnumerateParagraphs().SelectMany(p=>p.Inlines.OfType<Run>()).First(run=>run.Text=="UV");
        Assert.Equal(23,inserted.FontSize);Assert.Equal(FontWeights.Bold,inserted.FontWeight);Assert.Equal(FontStyles.Italic,inserted.FontStyle);
    }

    [Theory]
    [InlineData(true,false)] [InlineData(false,true)]
    public void ReadOnlyOrRetiredInput_RejectsBeforeMutation(bool readOnly,bool retired)
    {
        var run=new Run("abc");var document=new FlowDocument(new Paragraph(run));var editor=new RichTextBox(document){IsReadOnly=readOnly};
        var args=new TextCompositionEventArgs(UIElement.TextInputEvent,"UV",0)
        { ImeReplacementRange=(0,4),NativeTextServicesPayload=JsonSerializer.Serialize(new{proposal=Packet("UV")}),NativeEditLifetime=()=>!retired };
        editor.RaiseEvent(args);
        Assert.False(args.Handled);Assert.Equal("abc\n",document.GetText());Assert.Same(run,Assert.Single(((Paragraph)document.Blocks[0]).Inlines));
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void InvalidPacket_RejectsBeforeReplacingTheDocument()
    {
        var packet=Packet("text").GetRawText().Replace("\"length\":4", "\"length\":40",StringComparison.Ordinal);
        using var json=JsonDocument.Parse(packet);
        Assert.Throws<FormatException>(()=>WritingToolsDocumentFragment.Parse(json.RootElement));
    }

    [Theory]
    [InlineData(1, TextAlignment.Right)] [InlineData(2, TextAlignment.Center)]
    [InlineData(3, TextAlignment.Justify)]
    public void AppKitAlignment_MapsToTheFrameworkEnum(int alignment, TextAlignment expected)
    {
        using var json = JsonDocument.Parse(Packet("x").GetRawText().Replace("\"alignment\":0", "\"alignment\":" + alignment));
        Assert.Equal(expected, Assert.IsType<Paragraph>(Assert.Single(WritingToolsDocumentFragment.Parse(json.RootElement).Blocks)).TextAlignment);
    }

    [Fact]
    public void HyperlinkBoundary_RejectsBeforeDeletingText()
    {
        var run = new Run("abc"); var link = new Hyperlink(run);
        var paragraph = new Paragraph(link); var document = new FlowDocument(paragraph);
        Assert.Throws<InvalidOperationException>(() => WritingToolsDocumentFragment.Parse(Packet("new")).Insert(document, 1, 1, () => true));
        Assert.Equal("abc\n", document.GetText()); Assert.Same(run, Assert.Single(link.Inlines));
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void OverlappingNativeCells_RejectRatherThanDroppingContent(bool rowSpan)
    {
        object[] paragraphs = rowSpan
            ? [Paragraph(0,2,tables:[Cell(0,0,rows:2)]),Paragraph(2,2,tables:[Cell(1,0)])]
            : [Paragraph(0,2,tables:[Cell(0,0,columns:2)]),Paragraph(2,2,tables:[Cell(0,1)])];
        Assert.Throws<FormatException>(() => WritingToolsDocumentFragment.Parse(Packet("a\nb\n", paragraphs)));
    }

    [Fact]
    public void NativeListMarkers_AreRemovedWithoutLosingItemText()
    {
        var paragraph = JsonSerializer.SerializeToElement(new
        { start=0,length=8,alignment=0,direction=0,skipPrefix=4,lists=new[]{List(format:"{decimal}.")},tables=Array.Empty<object>() });
        var fragment = WritingToolsDocumentFragment.Parse(Packet("\t3.\tone\n", [paragraph]));
        var list = Assert.IsType<DocList>(Assert.Single(fragment.Blocks));
        Assert.Equal("one", Assert.IsType<Run>(Assert.IsType<Paragraph>(Assert.Single(Assert.Single(list.ListItems).Blocks)).Inlines[0]).Text);
        Assert.Equal(3, list.StartIndex);
    }

    [Fact]
    public void SourceGeneratedContract_PreservesNestedStructureWithoutReflection()
    {
        var response = new Jalium.UI.Controls.Platform.MacOSTextServicesResponse
        {
            Editor="e",Session="s",CanRead=true,
            Structure=[new(0,2,[new("list","{decimal}",3)],[new("table",0,1,1,1,2)])]
        };
        byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(response,
            Jalium.UI.Controls.Platform.MacOSTextServicesJsonContext.Default.MacOSTextServicesResponse);
        using var json=JsonDocument.Parse(bytes); var record=json.RootElement.GetProperty("structure")[0];
        Assert.Equal(3,record.GetProperty("lists")[0].GetProperty("start").GetInt32());
        Assert.Equal(2,record.GetProperty("tables")[0].GetProperty("tableColumns").GetInt32());
        Assert.False(json.RootElement.TryGetProperty("png",out _));
    }

    [Theory]
    [InlineData(null)] [InlineData("")]
    public void NativeCapabilityQueryWithoutPayload_ParsesAsAnEmptyObject(string? payload)
    {
        var request=new Jalium.UI.Controls.Platform.PlatformTextServicesRequest(0,0,0,0,payload);
        using var json=JsonDocument.Parse(request.Payload);
        Assert.Equal(JsonValueKind.Object,json.RootElement.ValueKind);
    }
}

