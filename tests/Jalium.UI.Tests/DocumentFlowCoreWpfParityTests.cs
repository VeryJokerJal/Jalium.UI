using System.Reflection;
using System.Windows.Input;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Navigation;
using DocumentList = Jalium.UI.Documents.List;

namespace Jalium.UI.Tests;

[Trait("Category", "WpfParity")]
public sealed class DocumentFlowCoreWpfParityTests
{
    [Fact]
    public void CoreDocumentTypes_ExposeCanonicalFrameworkAndDependencyPropertySurface()
    {
        Assert.True(typeof(FrameworkContentElement).IsAssignableFrom(typeof(TextElement)));
        Assert.True(typeof(FrameworkContentElement).IsAssignableFrom(typeof(FlowDocument)));
        Assert.True(typeof(IDocumentPaginatorSource).IsAssignableFrom(typeof(FlowDocument)));
        Assert.True(typeof(ICommandSource).IsAssignableFrom(typeof(Hyperlink)));

        AssertFields<Block>(
            nameof(Block.BreakColumnBeforeProperty), nameof(Block.BreakPageBeforeProperty),
            nameof(Block.ClearFloatersProperty), nameof(Block.FlowDirectionProperty),
            nameof(Block.IsHyphenationEnabledProperty), nameof(Block.LineStackingStrategyProperty));
        AssertFields<ListItem>(
            nameof(ListItem.MarginProperty), nameof(ListItem.PaddingProperty),
            nameof(ListItem.BorderBrushProperty), nameof(ListItem.BorderThicknessProperty),
            nameof(ListItem.TextAlignmentProperty), nameof(ListItem.FlowDirectionProperty),
            nameof(ListItem.LineHeightProperty), nameof(ListItem.LineStackingStrategyProperty));
        AssertFields<FlowDocument>(
            nameof(FlowDocument.FontStretchProperty), nameof(FlowDocument.FontStyleProperty),
            nameof(FlowDocument.FontWeightProperty), nameof(FlowDocument.FlowDirectionProperty),
            nameof(FlowDocument.TextEffectsProperty), nameof(FlowDocument.LineStackingStrategyProperty),
            nameof(FlowDocument.ColumnRuleBrushProperty), nameof(FlowDocument.ColumnRuleWidthProperty),
            nameof(FlowDocument.MinPageWidthProperty), nameof(FlowDocument.MaxPageWidthProperty),
            nameof(FlowDocument.MinPageHeightProperty), nameof(FlowDocument.MaxPageHeightProperty));
    }

    [Fact]
    public void Collections_KeepSiblingAndLogicalOwnershipInSync()
    {
        var document = new FlowDocument();
        var first = new Paragraph(new Run("one"));
        var second = new Paragraph(new Run("two"));
        document.Blocks.Add(first);
        document.Blocks.Add(second);

        Assert.Same(document.Blocks, first.SiblingBlocks);
        Assert.Same(second, first.NextBlock);
        Assert.Same(first, second.PreviousBlock);

        var list = new DocumentList();
        var firstItem = new ListItem(new Paragraph(new Run("a")));
        var secondItem = new ListItem(new Paragraph(new Run("b")));
        list.ListItems.Add(firstItem);
        list.ListItems.Add(secondItem);
        Assert.Same(list, firstItem.List);
        Assert.Same(secondItem, firstItem.NextListItem);
        Assert.Same(firstItem, secondItem.PreviousListItem);
    }

    [Fact]
    public void TextPointer_MutatesRunsAndSplitsParagraphs()
    {
        var run = new Run("abcdef");
        var paragraph = new Paragraph(run);
        var document = new FlowDocument(paragraph);
        var position = Assert.IsType<TextPointer>(document.GetPositionAtOffset(3, LogicalDirection.Forward));

        position.InsertTextInRun("X");
        Assert.Equal("abcXdef", run.Text);
        Assert.Equal(2, position.DeleteTextInRun(2));
        Assert.Equal("abcef", run.Text);

        var split = position.InsertParagraphBreak();
        Assert.Equal(2, document.Blocks.Count);
        Assert.Same(document, split.Document);
    }

    [Fact]
    public void Hyperlink_DoClick_RaisesRoutedEventsAndExecutesCommand()
    {
        var command = new TestCommand();
        var hyperlink = new Hyperlink(new Run("open"))
        {
            Command = command,
            CommandParameter = 42,
            NavigateUri = new Uri("https://example.test/"),
            TargetName = "content",
        };
        var clicked = false;
        RequestNavigateEventArgs? requested = null;
        hyperlink.Click += (_, _) => clicked = true;
        hyperlink.RequestNavigate += (_, e) => requested = e;

        hyperlink.DoClick();

        Assert.True(clicked);
        Assert.Equal(42, command.LastParameter);
        Assert.Equal(hyperlink.NavigateUri, requested?.Uri);
        Assert.Equal("content", requested?.Target);
    }

    private static void AssertFields<T>(params string[] names)
    {
        foreach (var name in names)
        {
            Assert.NotNull(typeof(T).GetField(name, BindingFlags.Public | BindingFlags.Static));
        }
    }

    private sealed class TestCommand : ICommand
    {
        public object? LastParameter { get; private set; }
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => LastParameter = parameter;
    }
}
