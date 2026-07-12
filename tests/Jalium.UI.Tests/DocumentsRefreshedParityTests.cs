using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Documents.Serialization;
using Jalium.UI.Media;
using PrintTicket = System.Printing.PrintTicket;
using PrintTicketLevel = Jalium.UI.Xps.Serialization.PrintTicketLevel;

namespace Jalium.UI.Tests;

[Trait("Category", "WpfParity")]
public sealed class DocumentsRefreshedParityTests
{
    [Fact]
    public void RefreshedDocumentTypesLiveInCanonicalNamespaces()
    {
        Type[] types =
        [
            typeof(DynamicDocumentPaginator),
            typeof(FrameworkTextComposition),
            typeof(FrameworkRichTextComposition),
            typeof(GetPageNumberCompletedEventArgs),
            typeof(GetPageRootCompletedEventArgs),
            typeof(LinkTarget),
            typeof(LinkTargetCollection),
            typeof(PagesChangedEventArgs),
            typeof(PaginationProgressEventArgs),
            typeof(TextEffectTarget),
            typeof(TextElementEditingBehaviorAttribute),
            typeof(ZoomPercentageConverter),
            typeof(Glyphs),
        ];

        Assert.All(types, type => Assert.Equal("Jalium.UI.Documents", type.Namespace));
        Assert.True(typeof(TextEffectResolver).IsAbstract && typeof(TextEffectResolver).IsSealed);
        Assert.Equal("Jalium.UI.Media", typeof(AdornerHitTestResult).Namespace);
    }

    [Fact]
    public void CollectionAndTableTypeShapesMatchWpf()
    {
        Assert.Equal(typeof(TextElementCollection<Block>), typeof(BlockCollection).BaseType);
        Assert.Equal(typeof(TextElementCollection<Inline>), typeof(InlineCollection).BaseType);
        Assert.Equal(typeof(TextElementCollection<ListItem>), typeof(ListItemCollection).BaseType);
        Assert.False(typeof(BlockCollection).IsSealed);
        Assert.False(typeof(InlineCollection).IsSealed);
        Assert.False(typeof(ListItemCollection).IsSealed);

        Type genericParameter = typeof(TextElementCollection<>).GetGenericArguments()[0];
        Assert.Contains(typeof(TextElement), genericParameter.GetGenericParameterConstraints());
        MethodInfo? addRange = typeof(TextElementCollection<>).GetMethod(
            nameof(TextElementCollection<TextElement>.AddRange),
            [typeof(IEnumerable)]);
        Assert.NotNull(addRange);
        Assert.Equal("range", Assert.Single(addRange!.GetParameters()).Name);

        Assert.Equal(typeof(FrameworkContentElement), typeof(TableColumn).BaseType);
        Assert.False(typeof(TableColumn).IsSealed);
        Assert.Equal(typeof(object), typeof(TableCellCollection).BaseType);
        Assert.Equal(typeof(object), typeof(TableColumnCollection).BaseType);
        Assert.True(typeof(IList<TableCell>).IsAssignableFrom(typeof(TableCellCollection)));
        Assert.True(typeof(IList).IsAssignableFrom(typeof(TableCellCollection)));
        Assert.True(typeof(IList<TableColumn>).IsAssignableFrom(typeof(TableColumnCollection)));
        Assert.True(typeof(IList).IsAssignableFrom(typeof(TableColumnCollection)));
    }

    [Fact]
    public void TableCollectionsExposeBespokeCollectionSurfaceAndMaintainParents()
    {
        var table = new Table();
        var column = new TableColumn();
        table.Columns.Add(column);
        Assert.False(table.Columns.IsReadOnly);
        Assert.False(table.Columns.IsSynchronized);
        Assert.NotNull(table.Columns.SyncRoot);

        var group = new TableRowGroup();
        var row = new TableRow();
        var cell = new TableCell();
        table.RowGroups.Add(group);
        group.Rows.Add(row);
        row.Cells.Add(cell);
        Assert.Same(table, group.Parent);
        Assert.Same(group, row.Parent);
        Assert.Same(row, cell.Parent);

        Array copied = new TableCell[1];
        row.Cells.CopyTo(copied, 0);
        Assert.Same(cell, copied.GetValue(0));
        row.Cells.TrimToSize();
    }

    [Fact]
    public void DocumentPageIsDisposableAndSupportsProtectedMutationContract()
    {
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(DocumentPage)));
        Assert.False(typeof(DocumentPage).IsSealed);
        Assert.All(
            new[]
            {
                nameof(DocumentPage.Visual), nameof(DocumentPage.Size),
                nameof(DocumentPage.BleedBox), nameof(DocumentPage.ContentBox),
            },
            name => Assert.True(typeof(DocumentPage).GetProperty(name)!.GetMethod!.IsVirtual));

        var page = new MutableDocumentPage();
        var destroyed = 0;
        page.PageDestroyed += (_, _) => destroyed++;
        var visual = new Border();
        page.Update(visual, new Size(10, 20), new Rect(1, 2, 3, 4), new Rect(5, 6, 7, 8));
        Assert.Same(visual, page.Visual);
        Assert.Equal(new Size(10, 20), page.Size);
        page.Dispose();
        page.Dispose();
        Assert.Equal(1, destroyed);
    }

    [Fact]
    public void PaginatorUsesCanonicalCompletionAndPagesChangedContracts()
    {
        MethodInfo? compute = typeof(DocumentPaginator).GetMethod(
            "OnComputePageCountCompleted",
            BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo? pages = typeof(DocumentPaginator).GetMethod(
            "OnPagesChanged",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.Equal(typeof(AsyncCompletedEventArgs), Assert.Single(compute!.GetParameters()).ParameterType);
        Assert.Equal(typeof(PagesChangedEventArgs), Assert.Single(pages!.GetParameters()).ParameterType);
        Assert.Equal(typeof(PagesChangedEventHandler), typeof(DocumentPaginator).GetEvent(nameof(DocumentPaginator.PagesChanged))!.EventHandlerType);

        var paginator = new TestDynamicPaginator();
        GetPageNumberCompletedEventArgs? completed = null;
        paginator.GetPageNumberCompleted += (_, e) => completed = e;
        paginator.GetPageNumberAsync(new TestContentPosition(), "state");
        Assert.Equal(7, completed?.PageNumber);
        Assert.Equal("state", completed?.UserState);
    }

    [Fact]
    public void FixedPageAndPageContentExposeCanonicalChildAndNavigationSurface()
    {
        var page = new FixedPage();
        var child = new Border();
        page.Children.Add(child);
        Assert.Same(page, child.VisualParent);

        var uri = new Uri("https://example.test/page");
        FixedPage.SetNavigateUri(child, uri);
        Assert.Same(uri, FixedPage.GetNavigateUri(child));

        var content = new PageContent { Child = page };
        GetPageRootCompletedEventArgs? completed = null;
        content.GetPageRootCompleted += (_, e) => completed = e;
        content.GetPageRootAsync(forceReload: false);
        Assert.Same(page, completed?.Result);
        Assert.NotNull(content.LinkTargets);
    }

    [Fact]
    public void InlineParagraphAnchoredAndTableCellPropertiesRoundTrip()
    {
        var inline = new Run("text")
        {
            BaselineAlignment = BaselineAlignment.Subscript,
            FlowDirection = FlowDirection.RightToLeft,
        };
        var paragraph = new Paragraph(inline)
        {
            KeepTogether = true,
            KeepWithNext = true,
            MinOrphanLines = 2,
            MinWidowLines = 3,
        };
        Assert.Same(paragraph.Inlines, inline.SiblingInlines);

        var cell = new TableCell
        {
            FlowDirection = FlowDirection.RightToLeft,
            LineHeight = 22,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            TextAlignment = TextAlignment.Center,
        };
        Assert.Equal(22, cell.LineHeight);

        ConstructorInfo? constructor = typeof(AnchoredBlock).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(Block), typeof(TextPointer)],
            modifiers: null);
        Assert.NotNull(constructor);
        Assert.Equal(new[] { "block", "insertionPosition" }, constructor!.GetParameters().Select(parameter => parameter.Name));
    }

    [Fact]
    public void TextPointerContextAndTextRangeFormatsMatchWpf()
    {
        Assert.Equal(2, (int)TextPointerContext.EmbeddedElement);
        Assert.Equal(3, (int)TextPointerContext.ElementStart);
        Assert.Equal(4, (int)TextPointerContext.ElementEnd);

        var document = new FlowDocument(new Paragraph(new Run("abc")));
        var range = new TextRange(document.ContentStart, document.ContentEnd);
        Assert.True(range.CanLoad(DataFormats.XamlPackage));
        Assert.True(range.CanSave(DataFormats.Rtf));
        using var stream = new MemoryStream();
        range.Save(stream, DataFormats.UnicodeText, preserveTextElements: true);
        Assert.True(stream.Length > 0);
        range.ClearAllProperties();
    }

    [Fact]
    public void GlyphsAndZoomConverterProvideFunctionalPortableResults()
    {
        var glyphs = new Glyphs
        {
            UnicodeString = "ab",
            FontRenderingEmSize = 16,
            OriginX = 4,
            OriginY = 12,
            BidiLevel = 1,
            IsSideways = true,
            DeviceFontName = "Portable",
        };
        GlyphRun run = glyphs.ToGlyphRun();
        Assert.Equal(new ushort[] { 'a', 'b' }, run.GlyphIndices);
        Assert.Equal(1, run.BidiLevel);
        Assert.True(run.IsSideways);

        var converter = new ZoomPercentageConverter();
        Assert.Equal("125%", converter.Convert(125d, typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal(125d, converter.ConvertBack("125%", typeof(double), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void SerializationTypesExposeCanonicalShapeAndDescriptorBehavior()
    {
        Type[] types =
        [
            typeof(ISerializerFactory), typeof(SerializerDescriptor), typeof(SerializerProvider),
            typeof(SerializerWriter), typeof(SerializerWriterCollator),
            typeof(WritingCancelledEventArgs), typeof(WritingCompletedEventArgs),
            typeof(WritingPrintTicketRequiredEventArgs), typeof(WritingProgressChangedEventArgs),
        ];
        Assert.All(types, type => Assert.Equal("Jalium.UI.Documents.Serialization", type.Namespace));
        Assert.True(typeof(SerializerWriter).IsAbstract);
        Assert.True(typeof(SerializerWriterCollator).IsAbstract);
        Assert.Equal(typeof(AsyncCompletedEventArgs), typeof(WritingCompletedEventArgs).BaseType);
        Assert.Equal(typeof(ProgressChangedEventArgs), typeof(WritingProgressChangedEventArgs).BaseType);

        var descriptor = SerializerDescriptor.CreateFromFactoryInstance(new TestSerializerFactory());
        Assert.Equal("Test Writer", descriptor.DisplayName);
        Assert.True(descriptor.IsLoadable);
        var provider = new SerializerProvider();
        provider.RegisterSerializer(descriptor, overwrite: true);
        Assert.Contains(descriptor, provider.InstalledSerializers);
        provider.UnregisterSerializer(descriptor);

        var progress = new WritingProgressChangedEventArgs(
            WritingProgressChangeLevel.FixedPageWritingProgress,
            number: 4,
            progressPercentage: 80,
            state: "state");
        Assert.Equal(4, progress.Number);
        Assert.Equal(80, progress.ProgressPercentage);
        var ticket = new WritingPrintTicketRequiredEventArgs(PrintTicketLevel.FixedPagePrintTicket, 3)
        {
            CurrentPrintTicket = new PrintTicket(),
        };
        Assert.Equal(3, ticket.Sequence);
        Assert.Equal(
            typeof(System.Printing.PrintTicket),
            typeof(WritingPrintTicketRequiredEventArgs)
                .GetProperty(nameof(WritingPrintTicketRequiredEventArgs.CurrentPrintTicket))!
                .PropertyType);
    }

    private sealed class MutableDocumentPage : DocumentPage
    {
        public void Update(Visual visual, Size size, Rect bleedBox, Rect contentBox)
        {
            SetVisual(visual);
            SetSize(size);
            SetBleedBox(bleedBox);
            SetContentBox(contentBox);
        }
    }

    private sealed class TestContentPosition : ContentPosition
    {
    }

    private sealed class TestDynamicPaginator : DynamicDocumentPaginator
    {
        public override bool IsPageCountValid => true;
        public override int PageCount => 1;
        public override Size PageSize { get; set; }
        public override IDocumentPaginatorSource Source => null!;
        public override DocumentPage GetPage(int pageNumber) => DocumentPage.Missing;
        public override int GetPageNumber(ContentPosition contentPosition) => 7;
        public override ContentPosition GetPagePosition(DocumentPage page) => ContentPosition.Missing;
        public override ContentPosition GetObjectPosition(object value) => ContentPosition.Missing;
    }

    private sealed class TestSerializerFactory : ISerializerFactory
    {
        public string DisplayName => "Test Writer";
        public string ManufacturerName => "Jalium";
        public Uri ManufacturerWebsite => new("https://example.test/");
        public string DefaultFileExtension => ".test";
        public SerializerWriter CreateSerializerWriter(Stream stream) => null!;
    }
}
