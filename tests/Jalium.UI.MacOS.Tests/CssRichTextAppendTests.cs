using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssRichTextAppendTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData("paragraph")]
    [InlineData("section")]
    [InlineData("list")]
    [InlineData("table")]
    public void AppendingThroughTheBaseApiRetainsTheStyledDocumentAndOriginalNodes(string kind)
    {
        var run = new Run("正文"); var span = new Span(run); var paragraph = new Paragraph(span); Block root = paragraph;
        if (kind == "section") root = new Section(paragraph);
        if (kind == "list") root = new Jalium.UI.Documents.List(new ListItem(paragraph));
        if (kind == "table")
        {
            var table = new Table(); var group = new TableRowGroup(); var row = new TableRow(); var cell = new TableCell(paragraph);
            row.Cells.Add(cell); group.Rows.Add(row); table.RowGroups.Add(group); root = table;
        }
        var document = new FlowDocument(root); Css.SetClass(span, "accent");
        Css.SetStyleSheet(document, ".accent{color:#146342;display:inline;font-weight:700;}");
        Css.SetStyle(document, "font-size:20px;line-height:32px;");
        var parent = paragraph.Parent; var original = document.GetText(); var editor = new RichTextBox(document) { Template = null };
        TextBoxBase control = editor; control.AppendText("追加");
        Assert.Same(document, editor.Document); Assert.Same(root, Assert.Single(document.Blocks));
        Assert.Same(parent, paragraph.Parent); Assert.Same(span, Assert.Single(paragraph.Inlines)); Assert.Same(run, Assert.Single(span.Inlines));
        Assert.Equal("正文追加", run.Text); Assert.Equal("font-size:20px;line-height:32px;", Css.GetStyle(document));
        Assert.True(editor.Undo()); Assert.Same(document, editor.Document); Assert.Equal(original, document.GetText()); Assert.Equal("正文", run.Text);
        Assert.True(editor.Redo()); Assert.Same(document, editor.Document); Assert.Equal("正文追加", run.Text); Assert.Same(span, run.Parent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProgrammaticAppendUsesTheLogicalEndAndDoesNotReplaceTheSelection(bool readOnly)
    {
        var run = new Run("AB"); var paragraph = new Paragraph(run); var document = new FlowDocument(paragraph);
        var editor = new RichTextBox(document) { IsReadOnly = readOnly, Template = null }; editor.SelectAll();
        editor.AppendText("C");
        Assert.Same(document, editor.Document); Assert.Equal("ABC\n", document.GetText()); Assert.Equal("", editor.Selection.Text);
        Assert.Equal(document.GetDocumentOffset(paragraph, 3), Assert.IsType<TextPointer>(editor.CaretPosition).DocumentOffset);
    }

    [Fact]
    public void EmptyChunksPreserveTheDocumentSelectionAndRedoQueue()
    {
        var run = new Run("AB"); var document = new FlowDocument(new Paragraph(run)); var editor = new RichTextBox(document);
        editor.AppendText("C"); Assert.True(editor.Undo()); Assert.True(editor.CanRedo);
        var caret = editor.CaretPosition; editor.AppendText(null!); editor.AppendText("");
        Assert.Same(document, editor.Document); Assert.Same(caret, editor.CaretPosition); Assert.True(editor.CanRedo);
        Assert.True(editor.Redo()); Assert.Equal("ABC", run.Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProgrammaticAppendPreservesHiddenTailNodesAndTheirCss(bool wholeDocumentHidden)
    {
        var visible = new Paragraph(new Run("可见")); var hiddenRun = new Run("隐藏"); var hidden = new Paragraph(hiddenRun);
        var document = new FlowDocument(visible); document.Blocks.Add(hidden);
        Css.SetStyle(wholeDocumentHidden ? (DependencyObject)document : hidden, "display:none;");
        var editor = new RichTextBox(document) { Template = null }; editor.AppendText("追加");
        Assert.Same(document, editor.Document); Assert.Equal("可见\n隐藏追加\n", document.GetText()); Assert.Equal("隐藏追加", hiddenRun.Text);
        Assert.Same(hidden, hiddenRun.Parent); Assert.Equal("display:none;", Css.GetStyle(wholeDocumentHidden ? (DependencyObject)document : hidden));
        Assert.True(editor.Undo()); Assert.Equal("隐藏", hiddenRun.Text);
    }

    [Fact]
    public void UnicodeAndLineBreakChunksUseTheExistingRichParagraphEditor()
    {
        var run = new Run("正文"); var paragraph = new Paragraph(run); var section = new Section(paragraph);
        var document = new FlowDocument(section); var editor = new RichTextBox(document) { Template = null };
        Css.SetStyle(section, "color:#146342;"); editor.AppendText("👨‍👩‍👧‍👦\r\n后文\r末段");
        Assert.Same(document, editor.Document); Assert.Same(section, Assert.Single(document.Blocks));
        Assert.Equal("正文👨‍👩‍👧‍👦\n后文\n末段\n", document.GetText()); Assert.Equal(3, section.Blocks.Count);
        Assert.True(editor.Undo()); Assert.Same(paragraph, Assert.Single(section.Blocks)); Assert.Equal("正文", run.Text);
    }

    [Fact]
    public void PlainTextBoxAppendKeepsItsExistingTextAndCaretContract()
    {
        var text = new TextBox { Text = "AB" }; TextBoxBase control = text; control.AppendText("C"); control.AppendText(null!);
        Assert.Equal("ABC", text.Text); Assert.Equal(3, text.CaretIndex); Assert.Equal(0, text.SelectionLength);
    }
}
