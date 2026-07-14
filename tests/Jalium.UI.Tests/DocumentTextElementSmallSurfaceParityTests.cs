using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Xml;
using Jalium.UI.Documents;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using DocumentList = Jalium.UI.Documents.List;

namespace Jalium.UI.Tests;

public sealed class DocumentTextElementSmallSurfaceParityTests
{
    [Fact]
    public void ConstructorAndSerializationSurfaceMatchesWpf()
    {
        AssertConstructor<Figure>([typeof(Block), typeof(TextPointer)], ["childBlock", "insertionPosition"]);
        AssertConstructor<DocumentList>([typeof(ListItem)], ["listItem"]);
        AssertConstructor<Run>([typeof(string), typeof(TextPointer)], ["text", "insertionPosition"]);
        AssertConstructor<Section>([typeof(Block)], ["block"]);
        AssertConstructor<Span>([typeof(Inline), typeof(TextPointer)], ["childInline", "insertionPosition"]);
        AssertConstructor<Span>([typeof(TextPointer), typeof(TextPointer)], ["start", "end"]);

        Assert.Equal(nameof(Figure.CanDelayPlacement), Figure.CanDelayPlacementProperty.Name);
        Assert.Equal(nameof(DocumentList.MarkerOffset), DocumentList.MarkerOffsetProperty.Name);
        Assert.Equal(nameof(Run.Text), Run.TextProperty.Name);

        AssertNeverBrowsable(typeof(Run), nameof(Run.ShouldSerializeText));
        AssertNeverBrowsable(typeof(Section), nameof(Section.ShouldSerializeBlocks));
        AssertNeverBrowsable(typeof(Span), nameof(Span.ShouldSerializeInlines));
    }

    [Fact]
    public void NewPropertiesUseWpfDefaultsAndMetadataContracts()
    {
        var figure = new Figure();
        Assert.True(figure.CanDelayPlacement);
        Assert.False(figure.HasLocalValue(Figure.CanDelayPlacementProperty));
        figure.CanDelayPlacement = false;
        Assert.False(figure.CanDelayPlacement);
        Assert.True(figure.HasLocalValue(Figure.CanDelayPlacementProperty));

        var list = new DocumentList();
        Assert.True(double.IsNaN(list.MarkerOffset));
        Assert.False(list.HasLocalValue(DocumentList.MarkerOffsetProperty));
        list.MarkerOffset = -24.5;
        Assert.Equal(-24.5, list.MarkerOffset);
        Assert.Throws<ArgumentException>(() => list.MarkerOffset = double.PositiveInfinity);
        Assert.Throws<ArgumentException>(() => list.MarkerOffset = 1_000_001d);

        var markerOffsetDescriptor = TypeDescriptor.GetProperties(typeof(DocumentList))[nameof(DocumentList.MarkerOffset)];
        Assert.NotNull(markerOffsetDescriptor);
        Assert.IsType<LengthConverter>(markerOffsetDescriptor!.Converter);

        var run = new Run();
        Assert.Equal(string.Empty, run.Text);
        Assert.False(run.HasLocalValue(Run.TextProperty));
        run.Text = null!;
        Assert.Equal(string.Empty, run.Text);
        Assert.True(run.HasLocalValue(Run.TextProperty));

        var section = new Section();
        Assert.True(section.HasTrailingParagraphBreakOnPaste);
        section.HasTrailingParagraphBreakOnPaste = false;
        Assert.False(section.HasTrailingParagraphBreakOnPaste);

        var pasteProperty = typeof(Section).GetProperty(nameof(Section.HasTrailingParagraphBreakOnPaste));
        Assert.NotNull(pasteProperty);
        Assert.Equal(
            DesignerSerializationVisibility.Hidden,
            pasteProperty!.GetCustomAttribute<DesignerSerializationVisibilityAttribute>()?.Visibility);
        Assert.True(Assert.IsType<bool>(pasteProperty.GetCustomAttribute<DefaultValueAttribute>()?.Value));
    }

    [Fact]
    public void ChildConstructorsEstablishOwnershipAndRejectNullWhereWpfDoes()
    {
        var block = new Paragraph(new Run("section"));
        var section = new Section(block);
        Assert.Same(block, Assert.Single(section.Blocks));
        Assert.Same(section, block.Parent);

        var item = new ListItem(new Paragraph(new Run("item")));
        var list = new DocumentList(item);
        Assert.Same(item, Assert.Single(list.ListItems));
        Assert.Same(list, item.Parent);

        Assert.Throws<ArgumentNullException>(() => new Section(null!));
        Assert.Throws<ArgumentNullException>(() => new DocumentList(null!));

        Assert.Empty(new Figure(null, null).Blocks);
        Assert.Empty(new Span((Inline?)null, null).Inlines);
        Assert.Equal(string.Empty, new Run(null!, null).Text);
    }

    [Fact]
    public void PositionConstructorsSplitRunsAndPreserveTheTextTree()
    {
        var runParagraph = new Paragraph(new Run("abcd")
        {
            Foreground = new SolidColorBrush(Color.FromRgb(1, 2, 3))
        });
        var runDocument = new FlowDocument(runParagraph);
        var insertedRun = new Run("R", runDocument.GetPositionAtOffset(2, LogicalDirection.Forward));

        AssertInsertedInline(runParagraph, insertedRun, "ab", "cd");
        Assert.Same(runParagraph.Inlines[0].Foreground, runParagraph.Inlines[2].Foreground);

        var spanParagraph = new Paragraph(new Run("abcd"));
        var spanDocument = new FlowDocument(spanParagraph);
        var spanChild = new Run("inside");
        var insertedSpan = new Span(spanChild, spanDocument.GetPositionAtOffset(2, LogicalDirection.Forward));

        AssertInsertedInline(spanParagraph, insertedSpan, "ab", "cd");
        Assert.Same(insertedSpan, spanChild.Parent);

        var figureParagraph = new Paragraph(new Run("abcd"));
        var figureDocument = new FlowDocument(figureParagraph);
        var figureBlock = new Paragraph(new Run("floating"));
        var figure = new Figure(figureBlock, figureDocument.GetPositionAtOffset(2, LogicalDirection.Forward));

        AssertInsertedInline(figureParagraph, figure, "ab", "cd");
        Assert.Same(figure, figureBlock.Parent);
    }

    [Fact]
    public void SpanRangeConstructorWrapsExistingContentAndValidatesPositions()
    {
        var paragraph = new Paragraph(new Run("abcdef"));
        var document = new FlowDocument(paragraph);
        var span = new Span(
            document.GetPositionAtOffset(1, LogicalDirection.Forward)!,
            document.GetPositionAtOffset(5, LogicalDirection.Backward)!);

        Assert.Equal(3, paragraph.Inlines.Count);
        Assert.Equal("a", Assert.IsType<Run>(paragraph.Inlines[0]).Text);
        Assert.Same(span, paragraph.Inlines[1]);
        Assert.Equal("bcde", Assert.IsType<Run>(Assert.Single(span.Inlines)).Text);
        Assert.Equal("f", Assert.IsType<Run>(paragraph.Inlines[2]).Text);

        var otherDocument = new FlowDocument(new Paragraph(new Run("other")));
        Assert.Throws<ArgumentException>(() => new Span(document.ContentStart, otherDocument.ContentEnd));
        Assert.Throws<ArgumentException>(() => new Span(document.ContentEnd, document.ContentStart));

        var orphan = new Run("orphan");
        var invalidPosition = new TextPointer(document, orphan, 0, LogicalDirection.Forward);
        Assert.Throws<InvalidOperationException>(() => new Run("x", invalidPosition));
        Assert.Throws<InvalidOperationException>(() => new Span(new Run("x"), invalidPosition));
        Assert.Throws<InvalidOperationException>(() => new Figure(new Paragraph(), invalidPosition));
    }

    [Fact]
    public void DesignerSerializationHooksHonorManagerWriterState()
    {
        var valueOnlyManager = new XamlDesignerSerializationManager(null!);
        Assert.True(new Run().ShouldSerializeText(valueOnlyManager));
        Assert.True(new Section().ShouldSerializeBlocks(valueOnlyManager));
        Assert.True(new Span().ShouldSerializeInlines(valueOnlyManager));

        Assert.False(new Run().ShouldSerializeText(null!));
        Assert.False(new Section().ShouldSerializeBlocks(null!));
        Assert.False(new Span().ShouldSerializeInlines(null!));

        var output = new StringBuilder();
        using var writer = XmlWriter.Create(output);
        var writerManager = new XamlDesignerSerializationManager(writer);
        Assert.False(new Run("content").ShouldSerializeText(writerManager));
        Assert.False(new Section(new Paragraph()).ShouldSerializeBlocks(writerManager));
        Assert.False(new Span(new Run("content")).ShouldSerializeInlines(writerManager));
    }

    private static void AssertConstructor<T>(Type[] parameterTypes, string[] parameterNames)
    {
        var constructor = typeof(T).GetConstructor(parameterTypes);
        Assert.NotNull(constructor);
        Assert.Equal(parameterNames, constructor!.GetParameters().Select(parameter => parameter.Name));
    }

    private static void AssertNeverBrowsable(Type type, string methodName)
    {
        var method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);
        Assert.Equal(
            EditorBrowsableState.Never,
            method!.GetCustomAttribute<EditorBrowsableAttribute>()?.State);
    }

    private static void AssertInsertedInline(
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
    }
}
