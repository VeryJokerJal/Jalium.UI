using System.Collections.ObjectModel;
using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public sealed class DocumentViewerParityTests
{
    private const string ReadOnlyPropertyNames = """
        CanDecreaseZoom CanIncreaseZoom CanMoveDown CanMoveLeft CanMoveRight CanMoveUp
        ExtentHeight ExtentWidth ViewportHeight ViewportWidth
        """;

    [Fact]
    public void TierOneSurface_HasCommandsDependencyPropertiesAndLifecycleHooks()
    {
        Assert.Equal(typeof(DocumentViewerBase), typeof(DocumentViewer).BaseType);

        AssertProperty<RoutedUICommand>(nameof(DocumentViewer.ViewThumbnailsCommand));
        AssertProperty<RoutedUICommand>(nameof(DocumentViewer.FitToWidthCommand));
        AssertProperty<RoutedUICommand>(nameof(DocumentViewer.FitToHeightCommand));
        AssertProperty<RoutedUICommand>(nameof(DocumentViewer.FitToMaxPagesAcrossCommand));

        AssertProperty<double>(nameof(DocumentViewer.HorizontalOffset));
        AssertProperty<double>(nameof(DocumentViewer.VerticalOffset));
        AssertProperty<double>(nameof(DocumentViewer.ExtentWidth));
        AssertProperty<double>(nameof(DocumentViewer.ExtentHeight));
        AssertProperty<double>(nameof(DocumentViewer.ViewportWidth));
        AssertProperty<double>(nameof(DocumentViewer.ViewportHeight));
        AssertProperty<int>(nameof(DocumentViewer.MaxPagesAcross));
        AssertProperty<bool>(nameof(DocumentViewer.CanMoveUp));
        AssertProperty<bool>(nameof(DocumentViewer.CanMoveDown));
        AssertProperty<bool>(nameof(DocumentViewer.CanMoveLeft));
        AssertProperty<bool>(nameof(DocumentViewer.CanMoveRight));
        AssertProperty<bool>(nameof(DocumentViewer.CanIncreaseZoom));
        AssertProperty<bool>(nameof(DocumentViewer.CanDecreaseZoom));

        foreach (var name in Names(ReadOnlyPropertyNames))
        {
            var field = AssertField(name + "Property");
            Assert.True(Assert.IsType<DependencyProperty>(field.GetValue(null)).ReadOnly);
        }

        Assert.False(Assert.IsType<DependencyProperty>(AssertField(nameof(DocumentViewer.HorizontalOffsetProperty)).GetValue(null)).ReadOnly);
        Assert.False(Assert.IsType<DependencyProperty>(AssertField(nameof(DocumentViewer.VerticalOffsetProperty)).GetValue(null)).ReadOnly);
        Assert.False(Assert.IsType<DependencyProperty>(AssertField(nameof(DocumentViewer.MaxPagesAcrossProperty)).GetValue(null)).ReadOnly);

        AssertPublicMethod(nameof(DocumentViewer.Find));
        AssertPublicMethod(nameof(DocumentViewer.DecreaseZoom));
        AssertPublicMethod(nameof(DocumentViewer.IncreaseZoom));
        AssertPublicMethod(nameof(DocumentViewer.FitToHeight));
        AssertPublicMethod(nameof(DocumentViewer.FitToMaxPagesAcross));
        AssertPublicMethod(nameof(DocumentViewer.FitToMaxPagesAcross), typeof(int));
        AssertPublicMethod(nameof(DocumentViewer.MoveDown));
        AssertPublicMethod(nameof(DocumentViewer.MoveLeft));
        AssertPublicMethod(nameof(DocumentViewer.MoveRight));
        AssertPublicMethod(nameof(DocumentViewer.MoveUp));
        AssertPublicMethod(nameof(DocumentViewer.ScrollPageDown));
        AssertPublicMethod(nameof(DocumentViewer.ScrollPageLeft));
        AssertPublicMethod(nameof(DocumentViewer.ScrollPageRight));
        AssertPublicMethod(nameof(DocumentViewer.ScrollPageUp));
        AssertPublicMethod(nameof(DocumentViewer.ViewThumbnails));

        AssertDeclaredPublicNonVirtual(nameof(DocumentViewer.FitToWidth));
        AssertDeclaredPublicNonVirtual(nameof(DocumentViewer.FitToHeight));
        AssertDeclaredPublicNonVirtual(nameof(DocumentViewer.FitToMaxPagesAcross));
        AssertDeclaredPublicNonVirtual(nameof(DocumentViewer.FitToMaxPagesAcross), typeof(int));

        Assert.DoesNotContain(
            typeof(DocumentViewerBase).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            static method => method.Name is nameof(DocumentViewer.FitToWidth)
                or nameof(DocumentViewer.FitToHeight)
                or nameof(DocumentViewer.FitToMaxPagesAcross));

        AssertProtectedVirtual("GetPageViewsCollection", typeof(bool).MakeByRefType());
        AssertProtectedVirtual("OnBringIntoView", typeof(DependencyObject), typeof(Rect), typeof(int));
        AssertProtectedVirtual("OnDocumentChanged");
        AssertProtectedVirtual("OnPreviousPageCommand");
        AssertProtectedVirtual("OnNextPageCommand");
        AssertProtectedVirtual("OnFirstPageCommand");
        AssertProtectedVirtual("OnLastPageCommand");
        AssertProtectedVirtual("OnGoToPageCommand", typeof(int));
        AssertProtectedVirtual("OnViewThumbnailsCommand");
        AssertProtectedVirtual("OnFitToWidthCommand");
        AssertProtectedVirtual("OnFitToHeightCommand");
        AssertProtectedVirtual("OnFitToMaxPagesAcrossCommand");
        AssertProtectedVirtual("OnFitToMaxPagesAcrossCommand", typeof(int));
        AssertProtectedVirtual("OnFindCommand");
        AssertProtectedVirtual("OnScrollPageUpCommand");
        AssertProtectedVirtual("OnScrollPageDownCommand");
        AssertProtectedVirtual("OnScrollPageLeftCommand");
        AssertProtectedVirtual("OnScrollPageRightCommand");
        AssertProtectedVirtual("OnMoveUpCommand");
        AssertProtectedVirtual("OnMoveDownCommand");
        AssertProtectedVirtual("OnMoveLeftCommand");
        AssertProtectedVirtual("OnMoveRightCommand");
        AssertProtectedVirtual("OnIncreaseZoomCommand");
        AssertProtectedVirtual("OnDecreaseZoomCommand");
    }

    [Fact]
    public void DocumentAssignmentAndNavigation_UseBasePaginatorStateAndLifecycleHooks()
    {
        var viewer = new ProbeViewer();
        var source = new TestDocumentSource(new TestPaginator(5, new Size(200, 300)));
        var pageChanges = new List<(int OldPage, int NewPage)>();
        var loaded = 0;
        viewer.PageChanged += (_, args) => pageChanges.Add((args.OldPageNumber, args.NewPageNumber));
        viewer.DocumentLoaded += (_, _) => loaded++;

        viewer.Document = source;

        Assert.Equal(1, loaded);
        Assert.Equal(5, viewer.PageCount);
        Assert.Equal(1, viewer.CurrentPage);
        Assert.False(viewer.CanGoToPreviousPage);
        Assert.True(viewer.CanGoToNextPage);
        Assert.Contains((0, 1), pageChanges);

        viewer.NextPage();
        Assert.Equal(2, viewer.CurrentPage);
        viewer.GoToPage(4);
        Assert.Equal(4, viewer.CurrentPage);
        viewer.PreviousPage();
        Assert.Equal(3, viewer.CurrentPage);
        viewer.LastPage();
        Assert.Equal(5, viewer.CurrentPage);
        Assert.False(viewer.CanGoToNextPage);
        viewer.FirstPage();
        Assert.Equal(1, viewer.CurrentPage);

        viewer.CallBringIntoView(viewer, new Rect(0, 0, 10, 10), 3);
        Assert.Equal(3, viewer.CurrentPage);
    }

    [Fact]
    public void PageViewsLayoutOffsetsAndMovement_ReflectRealPaginatorGeometry()
    {
        var viewer = new ProbeViewer
        {
            Document = new TestDocumentSource(new TestPaginator(5, new Size(200, 300))),
            PageDisplay = DocumentViewerPageDisplay.Continuous,
            MaxPagesAcross = 2,
        };

        var views = viewer.CallGetPageViews(out var changed);
        Assert.True(changed);
        Assert.Equal(5, views.Count);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, views.Select(static view => view.PageNumber));
        Assert.Same(views, viewer.CallGetPageViews(out changed));
        Assert.False(changed);

        viewer.Measure(new Size(300, 200));
        viewer.Arrange(new Rect(0, 0, 300, 200));

        Assert.Equal(410.0, viewer.ExtentWidth);
        Assert.Equal(920.0, viewer.ExtentHeight);
        Assert.Equal(300.0, viewer.ViewportWidth);
        Assert.Equal(200.0, viewer.ViewportHeight);
        Assert.False(viewer.CanMoveLeft);
        Assert.True(viewer.CanMoveRight);
        Assert.False(viewer.CanMoveUp);
        Assert.True(viewer.CanMoveDown);

        viewer.MoveRight();
        viewer.MoveDown();
        Assert.Equal(16.0, viewer.HorizontalOffset);
        Assert.Equal(16.0, viewer.VerticalOffset);
        Assert.True(viewer.CanMoveLeft);
        Assert.True(viewer.CanMoveUp);

        viewer.ScrollPageDown();
        Assert.Equal(216.0, viewer.VerticalOffset);
        viewer.VerticalOffset = 10_000;
        viewer.Arrange(new Rect(0, 0, 300, 200));
        Assert.Equal(720.0, viewer.VerticalOffset);
        Assert.False(viewer.CanMoveDown);

        Assert.Throws<InvalidOperationException>(() => viewer.SetValue(DocumentViewer.ExtentWidthProperty, 10.0));
        Assert.Throws<ArgumentException>(() => viewer.HorizontalOffset = -1.0);
        Assert.Throws<ArgumentException>(() => viewer.MaxPagesAcross = 33);
    }

    [Fact]
    public void PageDisplayModes_ChooseTheExpectedVisiblePages()
    {
        var viewer = new ProbeViewer
        {
            Document = new TestDocumentSource(new TestPaginator(6, new Size(200, 300))),
        };

        Assert.Equal(new[] { 0 }, viewer.CallGetPageViews(out _).Select(static view => view.PageNumber));

        viewer.PageDisplay = DocumentViewerPageDisplay.TwoPages;
        Assert.Equal(new[] { 0, 1 }, viewer.CallGetPageViews(out _).Select(static view => view.PageNumber));
        viewer.NextPage();
        Assert.Equal(3, viewer.CurrentPage);
        Assert.Equal(new[] { 2, 3 }, viewer.CallGetPageViews(out _).Select(static view => view.PageNumber));

        viewer.PageDisplay = DocumentViewerPageDisplay.TwoUpFacing;
        viewer.FirstPage();
        Assert.Equal(new[] { 0 }, viewer.CallGetPageViews(out _).Select(static view => view.PageNumber));
        viewer.NextPage();
        Assert.Equal(2, viewer.CurrentPage);
        Assert.Equal(new[] { 1, 2 }, viewer.CallGetPageViews(out _).Select(static view => view.PageNumber));

        viewer.PageDisplay = DocumentViewerPageDisplay.Continuous;
        Assert.Equal(6, viewer.CallGetPageViews(out _).Count);
    }

    [Fact]
    public void ZoomFitAndCommands_UpdateZoomAndCanExecuteState()
    {
        var viewer = new DocumentViewer
        {
            Document = new TestDocumentSource(new TestPaginator(4, new Size(200, 300))),
        };
        viewer.Measure(new Size(400, 300));
        viewer.Arrange(new Rect(0, 0, 400, 300));

        Assert.True(viewer.CanIncreaseZoom);
        Assert.True(viewer.CanDecreaseZoom);
        viewer.IncreaseZoom();
        Assert.Equal(125.0, viewer.Zoom);
        viewer.DecreaseZoom();
        Assert.Equal(100.0, viewer.Zoom);

        Assert.Same(DocumentViewer.FitToWidthCommand, DocumentViewer.FitToWidthCommand);
        Assert.True(DocumentViewer.FitToWidthCommand.CanExecute(null, viewer));
        DocumentViewer.FitToWidthCommand.Execute(null, viewer);
        Assert.Equal(200.0, viewer.Zoom, precision: 6);
        Assert.Equal(DocumentViewerFitMode.FitWidth, viewer.FitMode);

        DocumentViewer.FitToMaxPagesAcrossCommand.Execute("2", viewer);
        Assert.Equal(2, viewer.MaxPagesAcross);
        Assert.Equal(DocumentViewerPageDisplay.Continuous, viewer.PageDisplay);
        Assert.Equal(97.5, viewer.Zoom, precision: 6);

        viewer.MaxZoom = viewer.Zoom;
        Assert.False(viewer.CanIncreaseZoom);
        Assert.Throws<ArgumentOutOfRangeException>(() => viewer.FitToMaxPagesAcross(0));
    }

    [Fact]
    public void SearchProvider_ProducesResultsAndNavigatesWithoutPlaceholderSearch()
    {
        var paginator = new SearchablePaginator(4, new Size(200, 300));
        var viewer = new DocumentViewer
        {
            Document = new TestDocumentSource(paginator),
        };
        var completedCount = -1;
        viewer.SearchCompleted += (_, args) => completedCount = args.ResultCount;

        viewer.Find("needle", matchCase: true, matchWholeWord: false);

        Assert.Equal(2, completedCount);
        Assert.Equal(2, viewer.SearchResultCount);
        Assert.Equal(("needle", true, false), paginator.LastSearch);

        viewer.FindNext();
        Assert.Equal(2, viewer.CurrentPage);
        viewer.FindNext();
        Assert.Equal(4, viewer.CurrentPage);
        viewer.FindPrevious();
        Assert.Equal(2, viewer.CurrentPage);

        viewer.Find();
        Assert.Equal(2, viewer.SearchResultCount);

        var unsupported = new DocumentViewer
        {
            Document = new TestDocumentSource(new TestPaginator(1, new Size(200, 300))),
        };
        Assert.Throws<NotSupportedException>(() => unsupported.Find("needle"));
        Assert.Throws<NotSupportedException>(() => unsupported.Find());
    }

    private static void AssertProperty<T>(string name)
    {
        var property = typeof(DocumentViewer).GetProperty(
            name,
            BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.NotNull(property);
        Assert.Equal(typeof(T), property!.PropertyType);
    }

    private static FieldInfo AssertField(string name)
    {
        var field = typeof(DocumentViewer).GetField(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.NotNull(field);
        Assert.True(field!.IsInitOnly);
        Assert.Equal(typeof(DependencyProperty), field.FieldType);
        return field;
    }

    private static void AssertPublicMethod(string name, params Type[] parameterTypes)
    {
        var method = typeof(DocumentViewer).GetMethod(
            name,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            binder: null,
            types: parameterTypes,
            modifiers: null);
        Assert.NotNull(method);
        Assert.Equal(typeof(void), method!.ReturnType);
    }

    private static void AssertProtectedVirtual(string name, params Type[] parameterTypes)
    {
        var method = typeof(DocumentViewer).GetMethod(
            name,
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            binder: null,
            types: parameterTypes,
            modifiers: null);
        Assert.NotNull(method);
        Assert.True(method!.IsFamily);
        Assert.True(method.IsVirtual);
    }

    private static void AssertDeclaredPublicNonVirtual(string name, params Type[] parameterTypes)
    {
        var method = typeof(DocumentViewer).GetMethod(
            name,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            binder: null,
            types: parameterTypes,
            modifiers: null);
        Assert.NotNull(method);
        Assert.False(method!.IsVirtual);
        Assert.False(method.IsAbstract);
    }

    private static string[] Names(string names) =>
        names.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private sealed class ProbeViewer : DocumentViewer
    {
        public ReadOnlyCollection<DocumentPageView> CallGetPageViews(out bool changed) =>
            base.GetPageViewsCollection(out changed);

        public void CallBringIntoView(DependencyObject element, Rect rect, int pageNumber) =>
            base.OnBringIntoView(element, rect, pageNumber);
    }

    private sealed class TestDocumentSource : IDocumentPaginatorSource
    {
        public TestDocumentSource(DocumentPaginator paginator)
        {
            DocumentPaginator = paginator;
        }

        public DocumentPaginator DocumentPaginator { get; }
    }

    private class TestPaginator : DocumentPaginator, IDocumentPaginatorSource
    {
        public TestPaginator(int pageCount, Size pageSize)
        {
            PageCount = pageCount;
            PageSize = pageSize;
        }

        public override int PageCount { get; }
        public override bool IsPageCountValid => true;
        public override Size PageSize { get; set; }
        public override IDocumentPaginatorSource Source => this;

        DocumentPaginator IDocumentPaginatorSource.DocumentPaginator => this;

        public override DocumentPage GetPage(int pageNumber) =>
            pageNumber >= 0 && pageNumber < PageCount
                ? new DocumentPage { Size = PageSize }
                : DocumentPage.Missing;
    }

    private sealed class SearchablePaginator : TestPaginator, IDocumentTextSearchProvider
    {
        public SearchablePaginator(int pageCount, Size pageSize)
            : base(pageCount, pageSize)
        {
        }

        public (string Text, bool MatchCase, bool MatchWholeWord) LastSearch { get; private set; }

        public IEnumerable<TextSearchResult> Find(string text, bool matchCase, bool matchWholeWord)
        {
            LastSearch = (text, matchCase, matchWholeWord);
            return
            [
                new TextSearchResult(2, new Rect(10, 20, 30, 10), text),
                new TextSearchResult(4, new Rect(5, 15, 30, 10), text),
            ];
        }
    }
}
