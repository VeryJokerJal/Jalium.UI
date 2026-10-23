using Jalium.UI.Controls;
using Jalium.UI.Documents;
using DocList = Jalium.UI.Documents.List;

namespace Jalium.UI.Tests;

public sealed class FlowDocumentEmbeddedObjectProjectionTests
{
    private const string Body = "Miii中文🙂e\u0301";

    public static IEnumerable<object[]> Cases()
    {
        foreach (string topology in new[] { "plain", "section", "list", "table", "nested" })
        foreach (string objects in new[] { "inline", "block", "mixed" })
        foreach (bool populated in new[] { false, true })
            yield return [topology, objects, populated];
    }

    [Theory, MemberData(nameof(Cases))]
    public void TextProjectionRetainsEachObjectSlot(string topology, string objects, bool populated)
    {
        var scene = Create(topology, objects, populated);
        Assert.Equal(scene.Expected, scene.Document.GetText());
        Assert.Equal(scene.Expected.Length, scene.Document.ContentEnd.DocumentOffset);
        Assert.Equal(scene.Expected, new TextRange(scene.Document.ContentStart, scene.Document.ContentEnd).Text);
        foreach (var embedded in scene.Objects)
        {
            Assert.Equal(1, embedded.ContentEnd.DocumentOffset - embedded.ContentStart.DocumentOffset);
            Assert.Equal("\uFFFC", new TextRange(embedded.ContentStart, embedded.ContentEnd).Text);
        }
        Assert.DoesNotContain("private child text", scene.Document.GetText());
    }

    [Theory, MemberData(nameof(Cases))]
    public void RangesAfterObjectsReadTheSelectedTextInBothDirections(string topology, string objects, bool populated)
    {
        var scene = Create(topology, objects, populated);
        Assert.Equal(Body, new TextRange(scene.Target.ContentStart, scene.Target.ContentEnd).Text);
        Assert.Equal(Body, new TextRange(scene.Target.ContentEnd, scene.Target.ContentStart).Text);
        int start = scene.Expected.IndexOf(Body, StringComparison.Ordinal);
        Assert.Equal(start, scene.Target.ContentStart.DocumentOffset);
        foreach (string text in new[] { "M", "iii", "中文", "🙂", "e\u0301" })
        {
            int local = Body.IndexOf(text, StringComparison.Ordinal);
            var from = scene.Target.ContentStart.GetPositionAtOffset(local)!;
            var to = from.GetPositionAtOffset(text.Length)!;
            Assert.Equal(text, new TextRange(from, to).Text);
        }
        var range = new TextRange(scene.Document.ContentStart, scene.Target.ContentEnd);
        Assert.Equal(scene.Expected[..(start + Body.Length)], range.Text);
    }

    [Theory, MemberData(nameof(Cases))]
    public void TextServiceStylesAndParagraphsKeepTheSameOffsets(string topology, string objects, bool populated)
    {
        var scene = Create(topology, objects, populated);
        var editor = new RichTextBox(scene.Document);
        var spans = editor.GetAutomationTextStyles();
        int start = scene.Expected.IndexOf(Body, StringComparison.Ordinal);
        Assert.Equal(scene.Expected.Length, spans.Max(span => span.Start + span.Length));
        Assert.Contains(spans, span => span.Start == start && span.Length == Body.Length);
        var paragraph = Assert.Single(editor.GetWritingToolsStructure(start, Body.Length));
        Assert.Equal(0, paragraph.Start);
        Assert.Equal(Body.Length, paragraph.Length);
        Assert.All(editor.GetWritingToolsStructure(), item => Assert.InRange(item.Start + item.Length, 1, scene.Expected.Length));
    }

    [Theory, MemberData(nameof(Cases))]
    public void EditingAfterAnObjectPreservesUnselectedNodes(string topology, string objects, bool populated)
    {
        var scene = Create(topology, objects, populated);
        var editor = new RichTextBox(scene.Document);
        var range = new TextRange(scene.Target.ContentStart, scene.Target.ContentEnd);
        range.Text = "换🙂";
        Assert.Equal(scene.Expected.Replace(Body, "换🙂", StringComparison.Ordinal), scene.Document.GetText());
        Assert.Equal("换🙂", range.Text);
        Assert.Equal("换🙂", scene.Target.Text);
        Assert.All(scene.Objects, item => Assert.Same(scene.Document, item.GetFlowDocument()));
        Assert.Same(scene.Document, editor.Document);
    }

    [Theory]
    [InlineData("plain")] [InlineData("section")] [InlineData("list")]
    [InlineData("table")] [InlineData("nested")]
    public void DocumentsWithoutObjectsKeepTheirExistingProjection(string topology)
    {
        var scene = Create(topology, "none", false);
        Assert.Equal(scene.Expected, scene.Document.GetText());
        Assert.Equal(Body, new TextRange(scene.Target.ContentStart, scene.Target.ContentEnd).Text);
        Assert.Equal(scene.Expected.Length, new RichTextBox(scene.Document).GetAutomationTextStyles().Max(span => span.Start + span.Length));
    }

    private sealed record Scene(FlowDocument Document, Run Target, TextElement[] Objects, string Expected);

    private static Scene Create(string topology, string kind, bool populated)
    {
        var objects = new List<TextElement>();
        UIElement? Child() => populated ? new TextBox { Text = "private child text" } : null;
        BlockUIContainer BlockObject() => populated ? new BlockUIContainer(Child()!) : new BlockUIContainer();
        var target = new Run(Body) { FontSize = 29 };
        var paragraph = new Paragraph(new Run("A"));
        string middle = "A";
        if (kind is "inline" or "mixed")
        {
            var first = new InlineUIContainer(Child());
            var nested = new Span(); nested.Inlines.Add(first); paragraph.Inlines.Add(nested);
            objects.Add(first); middle += "\uFFFC";
        }
        paragraph.Inlines.Add(target); middle += Body;
        if (kind is "inline" or "mixed")
        {
            var second = new InlineUIContainer(Child()); paragraph.Inlines.Add(second);
            objects.Add(second); middle += "\uFFFC";
        }
        paragraph.Inlines.Add(new Run("Z")); middle += "Z\n";
        var blocks = new List<Block>();
        if (kind is "block" or "mixed")
        {
            var first = BlockObject(); blocks.Add(first); objects.Add(first);
            middle = "\uFFFC" + middle;
        }
        blocks.Add(paragraph);
        if (kind is "block" or "mixed")
        {
            var last = BlockObject(); blocks.Add(last); objects.Add(last);
            middle += "\uFFFC";
        }
        Block Section(IEnumerable<Block> children)
        {
            var section = new Section(); foreach (var child in children) section.Blocks.Add(child); return section;
        }
        Block List(Block child)
        {
            var list = new DocList(); var item = new ListItem(); item.Blocks.Add(child); list.ListItems.Add(item); return list;
        }
        Block Table(Block child)
        {
            var table = new Table(); var group = new TableRowGroup(); var row = new TableRow();
            row.Cells.Add(new TableCell(child)); group.Rows.Add(row); table.RowGroups.Add(group); return table;
        }
        var document = new FlowDocument(new Paragraph(new Run("prefix")));
        if (topology == "plain") foreach (var block in blocks) document.Blocks.Add(block);
        else
        {
            Block section = Section(blocks);
            document.Blocks.Add(topology switch
            {
                "section" => section,
                "list" => List(section),
                "table" => Table(section),
                _ => List(Table(List(section))),
            });
        }
        if (topology is "table" or "nested") middle += "\t\n";
        document.Blocks.Add(new Paragraph(new Run("last")));
        return new(document, target, objects.ToArray(), "prefix\n" + middle + "last\n");
    }
}
