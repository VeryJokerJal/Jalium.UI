using System.Collections.Specialized;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

public sealed class DocumentCollectionPaginatorWpfParityTests
{
    [Fact]
    public void SmallDocumentCollectionAndPaginatorSurfaceMatchesWpf()
    {
        AssertReadOnlyProperty<BlockCollection, Block>(nameof(BlockCollection.FirstBlock));
        AssertReadOnlyProperty<BlockCollection, Block>(nameof(BlockCollection.LastBlock));
        AssertReadOnlyProperty<ListItemCollection, ListItem>(nameof(ListItemCollection.FirstListItem));
        AssertReadOnlyProperty<ListItemCollection, ListItem>(nameof(ListItemCollection.LastListItem));

        var addUiElement = typeof(InlineCollection).GetMethod(
            nameof(InlineCollection.Add),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            binder: null,
            types: [typeof(UIElement)],
            modifiers: null);
        Assert.NotNull(addUiElement);
        Assert.Equal("uiElement", Assert.Single(addUiElement!.GetParameters()).Name);

        AssertVirtualMethod(
            typeof(DocumentPaginator),
            nameof(DocumentPaginator.GetPageAsync),
            [typeof(int)],
            ["pageNumber"]);
        AssertVirtualMethod(
            typeof(DocumentPaginator),
            nameof(DocumentPaginator.ComputePageCountAsync),
            Type.EmptyTypes,
            []);

        Assert.Contains(typeof(IUriContext), typeof(DocumentReference).GetInterfaces());
        Assert.Null(typeof(DocumentReference).GetProperty(
            nameof(IUriContext.BaseUri),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly));

        var collectionChanged = typeof(DocumentReferenceCollection).GetEvent(
            nameof(DocumentReferenceCollection.CollectionChanged),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        Assert.NotNull(collectionChanged);
        Assert.Equal(typeof(NotifyCollectionChangedEventHandler), collectionChanged!.EventHandlerType);
    }

    [Fact]
    public void BlockAndListItemEndpointsTrackCollectionContents()
    {
        var document = new FlowDocument();
        Assert.Null(document.Blocks.FirstBlock);
        Assert.Null(document.Blocks.LastBlock);

        var firstBlock = new Paragraph();
        var lastBlock = new Section();
        document.Blocks.Add(firstBlock);
        document.Blocks.Add(lastBlock);
        Assert.Same(firstBlock, document.Blocks.FirstBlock);
        Assert.Same(lastBlock, document.Blocks.LastBlock);

        document.Blocks.Remove(firstBlock);
        Assert.Same(lastBlock, document.Blocks.FirstBlock);
        Assert.Same(lastBlock, document.Blocks.LastBlock);
        document.Blocks.Clear();
        Assert.Null(document.Blocks.FirstBlock);
        Assert.Null(document.Blocks.LastBlock);

        var list = new Jalium.UI.Documents.List();
        Assert.Null(list.ListItems.FirstListItem);
        Assert.Null(list.ListItems.LastListItem);

        var firstItem = new ListItem();
        var lastItem = new ListItem();
        list.ListItems.Add(firstItem);
        list.ListItems.Add(lastItem);
        Assert.Same(firstItem, list.ListItems.FirstListItem);
        Assert.Same(lastItem, list.ListItems.LastListItem);
    }

    [Fact]
    public void AddingUiElementCreatesInlineUiContainerAndHonorsNullContract()
    {
        var paragraph = new Paragraph();
        var child = new Border();

        paragraph.Inlines.Add(child);

        var container = Assert.IsType<InlineUIContainer>(Assert.Single(paragraph.Inlines));
        Assert.Same(child, container.Child);
        Assert.Same(paragraph, container.Parent);

        var exception = Assert.Throws<ArgumentNullException>(
            () => paragraph.Inlines.Add((UIElement)null!));
        Assert.Equal("uiElement", exception.ParamName);
        Assert.Single(paragraph.Inlines);
    }

    [Fact]
    public void StatelessAsyncOverloadsDelegateToStatefulVirtualOverloadsWithNullState()
    {
        var paginator = new TrackingPaginator();

        paginator.GetPageAsync(3);
        paginator.ComputePageCountAsync();

        Assert.Equal(3, paginator.RequestedPageNumber);
        Assert.Null(paginator.GetPageUserState);
        Assert.True(paginator.ComputePageCountRequested);
        Assert.Null(paginator.ComputePageCountUserState);
    }

    [Fact]
    public void DocumentReferenceBaseUriRoundTripsAbsoluteAndRelativeUris()
    {
        var context = Assert.IsAssignableFrom<IUriContext>(new DocumentReference());
        Assert.Null(context.BaseUri);

        var absolute = new Uri("https://example.test/documents/");
        context.BaseUri = absolute;
        Assert.Same(absolute, context.BaseUri);

        var relative = new Uri("nested/", UriKind.Relative);
        context.BaseUri = relative;
        Assert.Same(relative, context.BaseUri);

        context.BaseUri = null;
        Assert.Null(context.BaseUri);
    }

    [Fact]
    public void DocumentReferenceCollectionRaisesIndexedAddNotifications()
    {
        var references = new FixedDocumentSequence().References;
        var changes = new List<(object? Sender, NotifyCollectionChangedEventArgs Args)>();
        references.CollectionChanged += (sender, args) => changes.Add((sender, args));
        var first = new DocumentReference();
        var second = new DocumentReference();

        references.Add(first);
        references.Add(second);

        Assert.Collection(
            changes,
            change => AssertAdd(change, references, first, 0),
            change => AssertAdd(change, references, second, 1));
    }

    private static void AssertReadOnlyProperty<TDeclaring, TProperty>(string propertyName)
    {
        var property = typeof(TDeclaring).GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        Assert.NotNull(property);
        Assert.Equal(typeof(TProperty), property!.PropertyType);
        Assert.NotNull(property.GetMethod);
        Assert.Null(property.SetMethod);
    }

    private static void AssertVirtualMethod(
        Type declaringType,
        string methodName,
        Type[] parameterTypes,
        string[] parameterNames)
    {
        var method = declaringType.GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            binder: null,
            types: parameterTypes,
            modifiers: null);
        Assert.NotNull(method);
        Assert.True(method!.IsVirtual);
        Assert.False(method.IsFinal);
        Assert.Equal(typeof(void), method.ReturnType);
        Assert.Equal(parameterNames, method.GetParameters().Select(parameter => parameter.Name));
    }

    private static void AssertAdd(
        (object? Sender, NotifyCollectionChangedEventArgs Args) change,
        DocumentReferenceCollection expectedSender,
        DocumentReference expectedItem,
        int expectedIndex)
    {
        Assert.Same(expectedSender, change.Sender);
        Assert.Equal(NotifyCollectionChangedAction.Add, change.Args.Action);
        Assert.Equal(expectedIndex, change.Args.NewStartingIndex);
        Assert.Equal(-1, change.Args.OldStartingIndex);
        Assert.Same(expectedItem, Assert.Single(change.Args.NewItems!.Cast<DocumentReference>()));
        Assert.Null(change.Args.OldItems);
    }

    private sealed class TrackingPaginator : DocumentPaginator
    {
        public int RequestedPageNumber { get; private set; } = -1;

        public object? GetPageUserState { get; private set; }

        public bool ComputePageCountRequested { get; private set; }

        public object? ComputePageCountUserState { get; private set; }

        public override bool IsPageCountValid => true;

        public override int PageCount => 1;

        public override Size PageSize { get; set; }

        public override IDocumentPaginatorSource Source => null!;

        public override DocumentPage GetPage(int pageNumber) => DocumentPage.Missing;

        public override void GetPageAsync(int pageNumber, object? userState)
        {
            RequestedPageNumber = pageNumber;
            GetPageUserState = userState;
        }

        public override void ComputePageCountAsync(object? userState)
        {
            ComputePageCountRequested = true;
            ComputePageCountUserState = userState;
        }
    }
}
