using System.ComponentModel;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public sealed class DocumentConstructorParityTests
{
    [Fact]
    public void ConstructorSurfaceAndParameterNamesMatchWpf()
    {
        AssertConstructor<BlockUIContainer>([typeof(UIElement)], ["uiElement"]);
        AssertConstructor<InlineUIContainer>([typeof(UIElement)], ["childUIElement"]);
        AssertConstructor<InlineUIContainer>([typeof(UIElement), typeof(TextPointer)], ["childUIElement", "insertionPosition"]);
        AssertConstructor<Bold>([typeof(Inline), typeof(TextPointer)], ["childInline", "insertionPosition"]);
        AssertConstructor<Bold>([typeof(TextPointer), typeof(TextPointer)], ["start", "end"]);
        AssertConstructor<Italic>([typeof(Inline), typeof(TextPointer)], ["childInline", "insertionPosition"]);
        AssertConstructor<Italic>([typeof(TextPointer), typeof(TextPointer)], ["start", "end"]);
        AssertConstructor<Underline>([typeof(Inline), typeof(TextPointer)], ["childInline", "insertionPosition"]);
        AssertConstructor<Underline>([typeof(TextPointer), typeof(TextPointer)], ["start", "end"]);
        AssertConstructor<LineBreak>([typeof(TextPointer)], ["insertionPosition"]);
        AssertConstructor<Floater>([typeof(Block), typeof(TextPointer)], ["childBlock", "insertionPosition"]);
    }

    [Fact]
    public void ChildConstructorsHonorWpfNullContracts()
    {
        var child = new Border();
        Assert.Same(child, new BlockUIContainer(child).Child);
        Assert.Throws<ArgumentNullException>(() => new BlockUIContainer(null!));

        Assert.Null(new InlineUIContainer((UIElement?)null).Child);
        Assert.Null(new InlineUIContainer(null, null).Child);
        Assert.Empty(new Bold((Inline?)null, null).Inlines);
        Assert.Empty(new Italic((Inline?)null, null).Inlines);
        Assert.Empty(new Underline((Inline?)null, null).Inlines);
        Assert.Null(new LineBreak(null).Parent);
        Assert.Empty(new Floater(null, null).Blocks);
    }

    [Fact]
    public void InlineChildAndInsertionConstructorsMaintainTheTextTree()
    {
        var (boldDocument, boldParagraph, boldPosition) = CreateDocumentPosition("abcd", 2);
        var boldChild = new Run("B");
        var bold = new Bold(boldChild, boldPosition);
        AssertInsertedBetweenSplitRuns(boldDocument, boldParagraph, bold, "ab", "cd");
        Assert.Same(bold, boldChild.Parent);

        var (_, italicParagraph, italicPosition) = CreateDocumentPosition("abcd", 2);
        var italic = new Italic(new Run("I"), italicPosition);
        AssertInsertedBetweenSplitRuns(null, italicParagraph, italic, "ab", "cd");

        var (_, underlineParagraph, underlinePosition) = CreateDocumentPosition("abcd", 2);
        var underline = new Underline(new Run("U"), underlinePosition);
        AssertInsertedBetweenSplitRuns(null, underlineParagraph, underline, "ab", "cd");

        var (_, breakParagraph, breakPosition) = CreateDocumentPosition("abcd", 2);
        var lineBreak = new LineBreak(breakPosition);
        AssertInsertedBetweenSplitRuns(null, breakParagraph, lineBreak, "ab", "cd");

        var (_, uiParagraph, uiPosition) = CreateDocumentPosition("abcd", 2);
        var element = new Border();
        var inlineContainer = new InlineUIContainer(element, uiPosition);
        AssertInsertedBetweenSplitRuns(null, uiParagraph, inlineContainer, "ab", "cd");
        Assert.Same(element, inlineContainer.Child);

        var (_, floaterParagraph, floaterPosition) = CreateDocumentPosition("abcd", 2);
        var block = new Paragraph(new Run("inside"));
        var floater = new Floater(block, floaterPosition);
        AssertInsertedBetweenSplitRuns(null, floaterParagraph, floater, "ab", "cd");
        Assert.Same(block, Assert.Single(floater.Blocks));
        Assert.Same(floater, block.Parent);
    }

    [Fact]
    public void RangeConstructorsSplitRunsAndWrapExistingContent()
    {
        AssertRangeConstructor((start, end) => new Bold(start, end), typeof(Bold));
        AssertRangeConstructor((start, end) => new Italic(start, end), typeof(Italic));
        AssertRangeConstructor((start, end) => new Underline(start, end), typeof(Underline));
    }

    [Fact]
    public void RangeConstructorPreservesNestedInlineIdentityAndParentage()
    {
        var first = new Run("ab");
        var nestedRun = new Run("cd");
        var italic = new Italic(nestedRun);
        var last = new Run("ef");
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(first);
        paragraph.Inlines.Add(italic);
        paragraph.Inlines.Add(last);
        var document = new FlowDocument(paragraph);

        var bold = new Bold(
            document.GetPositionAtOffset(1, LogicalDirection.Forward)!,
            document.GetPositionAtOffset(5, LogicalDirection.Backward)!);

        Assert.Equal(3, paragraph.Inlines.Count);
        Assert.Equal("a", Assert.IsType<Run>(paragraph.Inlines[0]).Text);
        Assert.Same(bold, paragraph.Inlines[1]);
        Assert.Equal("f", Assert.IsType<Run>(paragraph.Inlines[2]).Text);
        Assert.Equal(3, bold.Inlines.Count);
        Assert.Equal("b", Assert.IsType<Run>(bold.Inlines[0]).Text);
        Assert.Same(italic, bold.Inlines[1]);
        Assert.Equal("e", Assert.IsType<Run>(bold.Inlines[2]).Text);
        Assert.Same(bold, italic.Parent);
        Assert.Same(italic, nestedRun.Parent);
    }

    [Fact]
    public void RangeConstructorSplitsFormattingAncestorsAtSelectionEdges()
    {
        var italic = new Italic(new Run("abcd"));
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(italic);
        paragraph.Inlines.Add(new Run("ef"));
        var document = new FlowDocument(paragraph);

        var bold = new Bold(
            document.GetPositionAtOffset(1, LogicalDirection.Forward)!,
            document.GetPositionAtOffset(5, LogicalDirection.Backward)!);

        Assert.Equal(3, paragraph.Inlines.Count);
        Assert.Same(italic, paragraph.Inlines[0]);
        Assert.Equal("a", Assert.IsType<Run>(Assert.Single(italic.Inlines)).Text);
        Assert.Same(bold, paragraph.Inlines[1]);
        Assert.Equal("f", Assert.IsType<Run>(paragraph.Inlines[2]).Text);
        Assert.Equal(2, bold.Inlines.Count);
        var selectedItalic = Assert.IsType<Italic>(bold.Inlines[0]);
        Assert.Equal("bcd", Assert.IsType<Run>(Assert.Single(selectedItalic.Inlines)).Text);
        Assert.Equal("e", Assert.IsType<Run>(bold.Inlines[1]).Text);
        Assert.Same(bold, selectedItalic.Parent);
    }

    [Fact]
    public void CollapsedRangeConstructorInsertsAnEmptySpanAtThePosition()
    {
        var (_, paragraph, position) = CreateDocumentPosition("abcd", 2);

        var underline = new Underline(position, position);

        AssertInsertedBetweenSplitRuns(null, paragraph, underline, "ab", "cd");
        Assert.Empty(underline.Inlines);
    }

    [Fact]
    public void RangeConstructorsRejectInvalidWpfPositionPairs()
    {
        var (document, paragraph, _) = CreateDocumentPosition("abcdef", 0);
        var start = document.GetPositionAtOffset(1, LogicalDirection.Forward)!;
        var end = document.GetPositionAtOffset(5, LogicalDirection.Backward)!;

        Assert.Throws<ArgumentNullException>(() => new Bold((TextPointer)null!, end));
        Assert.Throws<ArgumentNullException>(() => new Bold(start, (TextPointer)null!));
        Assert.Throws<ArgumentException>(() => new Bold(end, start));

        var otherDocument = new FlowDocument(new Paragraph(new Run("other")));
        Assert.Throws<ArgumentException>(() => new Bold(start, otherDocument.ContentStart));

        var secondParagraph = new Paragraph(new Run("second"));
        document.Blocks.Add(secondParagraph);
        var secondStart = new TextPointer(document, secondParagraph.Inlines[0], 0, LogicalDirection.Forward);
        Assert.Throws<ArgumentException>(() => new Bold(start, secondStart));

        var hyperlinkRun = new Run("link");
        var hyperlinkDocument = new FlowDocument(new Paragraph(new Hyperlink(hyperlinkRun)));
        Assert.Throws<InvalidOperationException>(() => new Bold(
            hyperlinkDocument.GetPositionAtOffset(1, LogicalDirection.Forward)!,
            hyperlinkDocument.GetPositionAtOffset(3, LogicalDirection.Backward)!));

        Assert.Same(paragraph, start.Paragraph);
    }

    [Fact]
    public void PositionConstructorsRejectPointersOutsideTheirTextContainer()
    {
        var document = new FlowDocument(new Paragraph(new Run("attached")));
        var orphan = new Run("orphan");
        var invalidPosition = new TextPointer(document, orphan, 0, LogicalDirection.Forward);

        Assert.Throws<InvalidOperationException>(() => new LineBreak(invalidPosition));
        Assert.Throws<InvalidOperationException>(() => new InlineUIContainer(new Border(), invalidPosition));
        Assert.Throws<InvalidOperationException>(() => new Floater(new Paragraph(), invalidPosition));
    }

    [Fact]
    public void TableCollectionSerializationHooksReflectContent()
    {
        var row = new TableRow();
        var rowGroup = new TableRowGroup();

        Assert.False(row.ShouldSerializeCells());
        Assert.False(rowGroup.ShouldSerializeRows());

        row.Cells.Add(new TableCell());
        rowGroup.Rows.Add(row);
        Assert.True(row.ShouldSerializeCells());
        Assert.True(rowGroup.ShouldSerializeRows());

        row.Cells.Clear();
        rowGroup.Rows.Clear();
        Assert.False(row.ShouldSerializeCells());
        Assert.False(rowGroup.ShouldSerializeRows());

        AssertNeverBrowsable(typeof(TableRow), nameof(TableRow.ShouldSerializeCells));
        AssertNeverBrowsable(typeof(TableRowGroup), nameof(TableRowGroup.ShouldSerializeRows));
    }

    private static void AssertConstructor<T>(Type[] parameterTypes, string[] parameterNames)
    {
        var constructor = typeof(T).GetConstructor(parameterTypes);
        Assert.NotNull(constructor);
        Assert.Equal(parameterNames, constructor!.GetParameters().Select(parameter => parameter.Name));
    }

    private static (FlowDocument document, Paragraph paragraph, TextPointer position) CreateDocumentPosition(
        string text,
        int offset)
    {
        var paragraph = new Paragraph(new Run(text));
        var document = new FlowDocument(paragraph);
        var position = document.GetPositionAtOffset(offset, LogicalDirection.Forward);
        return (document, paragraph, Assert.IsType<TextPointer>(position));
    }

    private static void AssertInsertedBetweenSplitRuns(
        FlowDocument? document,
        Paragraph paragraph,
        Inline inserted,
        string leadingText,
        string trailingText)
    {
        Assert.Equal(3, paragraph.Inlines.Count);
        var leading = Assert.IsType<Run>(paragraph.Inlines[0]);
        Assert.Same(inserted, paragraph.Inlines[1]);
        var trailing = Assert.IsType<Run>(paragraph.Inlines[2]);
        Assert.Equal(leadingText, leading.Text);
        Assert.Equal(trailingText, trailing.Text);
        Assert.Same(paragraph, inserted.Parent);
        Assert.Same(inserted, leading.NextInline);
        Assert.Same(leading, inserted.PreviousInline);
        Assert.Same(trailing, inserted.NextInline);
        Assert.Same(inserted, trailing.PreviousInline);
        if (document != null)
        {
            Assert.Same(paragraph, document.Blocks[0]);
        }
    }

    private static void AssertRangeConstructor(
        Func<TextPointer, TextPointer, Span> constructor,
        Type expectedType)
    {
        var brush = new SolidColorBrush(Color.FromRgb(10, 20, 30));
        var run = new Run("abcdef") { Foreground = brush };
        var paragraph = new Paragraph(run);
        var document = new FlowDocument(paragraph);

        var span = constructor(
            document.GetPositionAtOffset(1, LogicalDirection.Forward)!,
            document.GetPositionAtOffset(5, LogicalDirection.Backward)!);

        Assert.Equal(expectedType, span.GetType());
        Assert.Equal(3, paragraph.Inlines.Count);
        var leading = Assert.IsType<Run>(paragraph.Inlines[0]);
        Assert.Same(span, paragraph.Inlines[1]);
        var trailing = Assert.IsType<Run>(paragraph.Inlines[2]);
        var selected = Assert.IsType<Run>(Assert.Single(span.Inlines));
        Assert.Equal("a", leading.Text);
        Assert.Equal("bcde", selected.Text);
        Assert.Equal("f", trailing.Text);
        Assert.Same(paragraph, span.Parent);
        Assert.Same(span, selected.Parent);
        Assert.Same(brush, leading.Foreground);
        Assert.Same(brush, selected.Foreground);
        Assert.Same(brush, trailing.Foreground);
    }

    private static void AssertNeverBrowsable(Type type, string methodName)
    {
        var method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);
        var attribute = method!.GetCustomAttribute<EditorBrowsableAttribute>();
        Assert.Equal(EditorBrowsableState.Never, attribute?.State);
    }
}
