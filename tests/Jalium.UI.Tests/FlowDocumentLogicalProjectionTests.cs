using Jalium.UI.Controls;
using Jalium.UI.Documents;
using DocList = Jalium.UI.Documents.List;

namespace Jalium.UI.Tests;

public sealed class FlowDocumentLogicalProjectionTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (string kind in new[] { "plain", "section", "list", "table", "mixed" })
            foreach (bool authoredCrLf in new[] { false, true }) yield return [kind, authoredCrLf];
    }

    [Theory, MemberData(nameof(Cases))]
    public void StructuralSeparatorsHaveOneLogicalCharacterOnEveryPlatform(string kind, bool authoredCrLf)
    {
        string body = authoredCrLf ? "α\r\nβ" : "αβ";
        var first = new Paragraph(new Run(body)); var run = new Run("Miii🙂"); var second = new Paragraph(run);
        var document = new FlowDocument(new Paragraph(new Run("prefix")));
        Block Wrap(Block block) { var item = new ListItem(); item.Blocks.Add(block); var list = new DocList(); list.ListItems.Add(item); return list; }
        string expected;
        if (kind is "table" or "mixed")
        {
            var table = new Table(); var group = new TableRowGroup(); var row = new TableRow();
            row.Cells.Add(new TableCell(first)); row.Cells.Add(new TableCell(second)); group.Rows.Add(row); table.RowGroups.Add(group);
            document.Blocks.Add(kind == "mixed" ? Wrap(table) : table);
            expected = "prefix\n" + body + "\n\tMiii🙂\n\t\nlast\n";
        }
        else
        {
            if (kind == "plain") { document.Blocks.Add(first); document.Blocks.Add(second); }
            else
            {
                var section = new Section(); section.Blocks.Add(first); section.Blocks.Add(second);
                document.Blocks.Add(kind == "list" ? Wrap(section) : section);
            }
            expected = "prefix\n" + body + "\nMiii🙂\nlast\n";
        }
        document.Blocks.Add(new Paragraph(new Run("last")));
        Assert.Equal(expected, document.GetText());
        Assert.Equal(expected.Length, document.ContentEnd.DocumentOffset);
        Assert.Equal(run.Text, new TextRange(run.ContentStart, run.ContentEnd).Text);
        Assert.Equal("M", new TextRange(run.ContentStart, run.ContentStart.GetPositionAtOffset(1)!).Text);
        Assert.Equal(expected, new TextRange(document.ContentStart, document.ContentEnd).Text);
        var editor = new RichTextBox(document);
        var styles = editor.GetAutomationTextStyles();
        Assert.Equal(expected.Length, styles.Max(span => span.Start + span.Length));
        Assert.All(editor.GetWritingToolsStructure(), record => Assert.InRange(record.Start + record.Length, 1, expected.Length));
    }

    [Theory]
    [InlineData("\r")] [InlineData("\n")] [InlineData("\r\n")] [InlineData("\u2029")]
    public void AuthoredBreaksRemainRawWhileLineBreakElementsUseOneCharacter(string raw)
    {
        var before = new Run("a" + raw + "b"); var after = new Run("🙂z");
        var paragraph = new Paragraph(); paragraph.Inlines.Add(before); paragraph.Inlines.Add(new LineBreak()); paragraph.Inlines.Add(after);
        var document = new FlowDocument(paragraph); string expected = before.Text + "\n" + after.Text + "\n";
        Assert.Equal(expected, document.GetText());
        Assert.Equal(expected.Length, document.ContentEnd.DocumentOffset);
        Assert.Equal(before.Text, new TextRange(before.ContentStart, before.ContentEnd).Text);
        Assert.Equal(after.Text, new TextRange(after.ContentStart, after.ContentEnd).Text);
        var editor = new RichTextBox(document);
        Assert.Equal(expected.Length, editor.GetAutomationTextStyles().Max(span => span.Start + span.Length));
        var record = Assert.Single(editor.GetWritingToolsStructure());
        Assert.Equal(expected.Length, record.Length);
        Assert.Equal(Enumerable.Range(0, expected.Length - 1).Where(i => expected[i] is '\n' or '\r' or '\u2029'), record.InlineBreaks);
    }
}
