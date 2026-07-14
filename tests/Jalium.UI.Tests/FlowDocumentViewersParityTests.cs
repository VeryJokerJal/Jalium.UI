using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public sealed class FlowDocumentViewersParityTests
{
    [Fact]
    public void TierOneSurface_UsesExactViewerInheritanceSelectionTypesAndCommandHooks()
    {
        Assert.Equal(typeof(DocumentViewerBase), typeof(FlowDocumentPageViewer).BaseType);

        Assert.Equal(typeof(TextSelection), AssertProperty(typeof(FlowDocumentPageViewer), nameof(FlowDocumentPageViewer.Selection)).PropertyType);
        Assert.Equal(typeof(TextSelection), AssertProperty(typeof(FlowDocumentReader), nameof(FlowDocumentReader.Selection)).PropertyType);
        Assert.Equal(typeof(TextSelection), AssertProperty(typeof(FlowDocumentScrollViewer), nameof(FlowDocumentScrollViewer.Selection)).PropertyType);

        AssertProtectedStaticKey(typeof(FlowDocumentPageViewer), "CanIncreaseZoomPropertyKey");
        AssertProtectedStaticKey(typeof(FlowDocumentPageViewer), "CanDecreaseZoomPropertyKey");
        AssertReadOnlyDependencyProperty(typeof(FlowDocumentPageViewer), nameof(FlowDocumentPageViewer.CanIncreaseZoomProperty));
        AssertReadOnlyDependencyProperty(typeof(FlowDocumentPageViewer), nameof(FlowDocumentPageViewer.CanDecreaseZoomProperty));

        foreach (var propertyName in new[]
        {
            nameof(FlowDocumentPageViewer.SelectionBrushProperty),
            nameof(FlowDocumentPageViewer.SelectionOpacityProperty),
            nameof(FlowDocumentPageViewer.IsSelectionActiveProperty),
            nameof(FlowDocumentPageViewer.IsInactiveSelectionHighlightEnabledProperty),
        })
        {
            AssertDependencyProperty(typeof(FlowDocumentPageViewer), propertyName);
        }

        AssertProtectedOverride(typeof(FlowDocumentPageViewer), "OnDocumentChanged");
        AssertProtectedOverride(typeof(FlowDocumentPageViewer), "OnPageViewsChanged");
        AssertProtectedOverride(typeof(FlowDocumentPageViewer), "OnPrintCommand");
        AssertProtectedOverride(typeof(FlowDocumentPageViewer), "OnCancelPrintCommand");
        AssertProtectedOverride(typeof(FlowDocumentPageViewer), "OnFirstPageCommand");
        AssertProtectedOverride(typeof(FlowDocumentPageViewer), "OnLastPageCommand");
        AssertProtectedOverride(typeof(FlowDocumentPageViewer), "OnNextPageCommand");
        AssertProtectedOverride(typeof(FlowDocumentPageViewer), "OnPreviousPageCommand");
        AssertProtectedOverride(typeof(FlowDocumentPageViewer), "OnGoToPageCommand", typeof(int));
        AssertProtectedVirtual(typeof(FlowDocumentPageViewer), "OnFindCommand");
        AssertProtectedVirtual(typeof(FlowDocumentPageViewer), "OnIncreaseZoomCommand");
        AssertProtectedVirtual(typeof(FlowDocumentPageViewer), "OnDecreaseZoomCommand");
        AssertProtectedVirtual(typeof(FlowDocumentPageViewer), "OnPrintCompleted");

        foreach (var propertyName in new[]
        {
            nameof(FlowDocumentReader.CanIncreaseZoomProperty),
            nameof(FlowDocumentReader.CanDecreaseZoomProperty),
            nameof(FlowDocumentReader.CanGoToNextPageProperty),
            nameof(FlowDocumentReader.CanGoToPreviousPageProperty),
            nameof(FlowDocumentReader.SelectionBrushProperty),
            nameof(FlowDocumentReader.SelectionOpacityProperty),
            nameof(FlowDocumentReader.IsSelectionActiveProperty),
            nameof(FlowDocumentReader.IsInactiveSelectionHighlightEnabledProperty),
        })
        {
            AssertDependencyProperty(typeof(FlowDocumentReader), propertyName);
        }

        Assert.True(typeof(FlowDocumentReader).GetField(
            nameof(FlowDocumentReader.SwitchViewingModeCommand),
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)!.IsInitOnly);
        AssertProtectedVirtual(typeof(FlowDocumentReader), "OnCancelPrintCommand");
        AssertProtectedVirtual(typeof(FlowDocumentReader), "OnDecreaseZoomCommand");
        AssertProtectedVirtual(typeof(FlowDocumentReader), "OnFindCommand");
        AssertProtectedVirtual(typeof(FlowDocumentReader), "OnIncreaseZoomCommand");
        AssertProtectedVirtual(typeof(FlowDocumentReader), "OnPrintCommand");
        AssertProtectedVirtual(typeof(FlowDocumentReader), "OnPrintCompleted");
        AssertProtectedVirtual(typeof(FlowDocumentReader), "OnSwitchViewingModeCommand", typeof(FlowDocumentReaderViewingMode));
        AssertProtectedVirtual(typeof(FlowDocumentReader), "SwitchViewingModeCore", typeof(FlowDocumentReaderViewingMode));

        foreach (var propertyName in new[]
        {
            nameof(FlowDocumentScrollViewer.CanIncreaseZoomProperty),
            nameof(FlowDocumentScrollViewer.CanDecreaseZoomProperty),
            nameof(FlowDocumentScrollViewer.SelectionBrushProperty),
            nameof(FlowDocumentScrollViewer.SelectionOpacityProperty),
            nameof(FlowDocumentScrollViewer.IsSelectionActiveProperty),
            nameof(FlowDocumentScrollViewer.IsInactiveSelectionHighlightEnabledProperty),
        })
        {
            AssertDependencyProperty(typeof(FlowDocumentScrollViewer), propertyName);
        }

        AssertProtectedVirtual(typeof(FlowDocumentScrollViewer), "OnCancelPrintCommand");
        AssertProtectedVirtual(typeof(FlowDocumentScrollViewer), "OnDecreaseZoomCommand");
        AssertProtectedVirtual(typeof(FlowDocumentScrollViewer), "OnFindCommand");
        AssertProtectedVirtual(typeof(FlowDocumentScrollViewer), "OnIncreaseZoomCommand");
        AssertProtectedVirtual(typeof(FlowDocumentScrollViewer), "OnPrintCommand");
        AssertProtectedVirtual(typeof(FlowDocumentScrollViewer), "OnPrintCompleted");

        Assert.NotNull(ApplicationCommands.CancelPrint);
        Assert.Same(ApplicationCommands.CancelPrint, ApplicationCommands.CancelPrint);
    }

    [Fact]
    public void PageViewer_PaginatesNavigatesZoomsAndPublishesRealPageViews()
    {
        var document = CreateMultiPageDocument();
        var viewer = new FlowDocumentPageViewer { Document = document };

        Assert.True(viewer.PageCount > 2);
        Assert.Equal(1, viewer.PageNumber);
        Assert.Single(viewer.PageViews);
        Assert.Same(document.ViewerPaginator, viewer.PageViews[0].DocumentPaginator);
        Assert.False(viewer.CanGoToPreviousPage);
        Assert.True(viewer.CanGoToNextPage);

        viewer.NextPage();
        Assert.Equal(2, viewer.PageNumber);
        Assert.Equal(1, viewer.PageViews[0].PageNumber);
        viewer.LastPage();
        Assert.Equal(viewer.PageCount, viewer.PageNumber);
        Assert.False(viewer.CanGoToNextPage);
        viewer.FirstPage();
        Assert.Equal(1, viewer.PageNumber);

        viewer.MaxZoom = 110;
        viewer.Zoom = 110;
        Assert.False(viewer.CanIncreaseZoom);
        viewer.DecreaseZoom();
        Assert.Equal(100, viewer.Zoom);
        Assert.True(viewer.CanIncreaseZoom);

        viewer.Measure(new Size(300, 180));
        viewer.Arrange(new Rect(0, 0, 300, 180));
        Assert.True(viewer.PageViews[0].RenderSize.Width > 0);
        Assert.NotNull(viewer.PageViews[0].DocumentPage?.Visual);
    }

    [Fact]
    public void Find_UpdatesDocumentTextSelectionAndNavigatesToItsPage()
    {
        var document = CreateMultiPageDocument(prefix: new string('x', 160) + " needle ");
        var pageViewer = new FlowDocumentPageViewer { Document = document };
        var reader = new FlowDocumentReader { Document = document };
        var scrollViewer = new FlowDocumentScrollViewer { Document = document };

        Assert.True(pageViewer.Find("needle"));
        Assert.Equal("needle", pageViewer.Selection!.Text);
        Assert.True(pageViewer.PageNumber > 1);

        Assert.True(reader.Find("needle"));
        Assert.Equal("needle", reader.Selection!.Text);
        Assert.True(reader.PageNumber > 1);

        Assert.True(scrollViewer.Find("needle"));
        Assert.Equal("needle", scrollViewer.Selection!.Text);
        Assert.False(scrollViewer.Find("not-present"));
    }

    [Fact]
    public void Reader_PreservesPageAcrossModesAndHonorsModeAvailability()
    {
        var reader = new FlowDocumentReader { Document = CreateMultiPageDocument() };
        Assert.True(reader.PageCount > 4);

        reader.NextPage();
        Assert.Equal(2, reader.PageNumber);
        reader.ViewingMode = FlowDocumentReaderViewingMode.TwoPage;
        Assert.Equal(2, reader.PageNumber);
        reader.NextPage();
        Assert.Equal(4, reader.PageNumber);

        reader.IsTwoPageViewEnabled = false;
        Assert.NotEqual(FlowDocumentReaderViewingMode.TwoPage, reader.ViewingMode);
        Assert.Throws<ArgumentException>(() => reader.ViewingMode = FlowDocumentReaderViewingMode.TwoPage);

        reader.ViewingMode = FlowDocumentReaderViewingMode.Scroll;
        Assert.Equal(4, reader.PageNumber);
        reader.PreviousPage();
        Assert.Equal(3, reader.PageNumber);
        Assert.True(reader.CanGoToPreviousPage);

        reader.MinZoom = 90;
        reader.MaxZoom = 120;
        reader.Zoom = 120;
        Assert.False(reader.CanIncreaseZoom);
        reader.DecreaseZoom();
        Assert.Equal(110, reader.Zoom);
    }

    [Fact]
    public void ScrollViewer_RendersContinuousTextAndSynchronizesScrollBarSettings()
    {
        var document = CreateMultiPageDocument();
        var viewer = new FlowDocumentScrollViewer
        {
            Document = document,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        viewer.Measure(new Size(220, 120));
        viewer.Arrange(new Rect(0, 0, 220, 120));

        Assert.Equal(1, viewer.PageNumberInternal);
        viewer.GoToPageInternal(Math.Min(3, viewer.PageCountInternal));
        Assert.Equal(Math.Min(3, viewer.PageCountInternal), viewer.PageNumberInternal);
        Assert.True(viewer.VisualChildrenCount > 0);

        viewer.IsSelectionEnabled = false;
        Assert.NotNull(viewer.Selection);
        viewer.IsSelectionEnabled = true;
        Assert.True(viewer.Find("segment"));
        Assert.False(viewer.Selection!.IsEmpty);
    }

    [Fact]
    public void DocumentMutation_RecomputesPaginationWithoutReplacingTheDocument()
    {
        var run = new Run("short");
        var document = new FlowDocument(new Paragraph(run))
        {
            PageWidth = 100,
            PageHeight = 60,
            PagePadding = new Thickness(4),
            FontSize = 10,
        };
        var viewer = new FlowDocumentPageViewer { Document = document };
        var initialCount = viewer.PageCount;

        run.Text = new string('z', 500);

        Assert.True(viewer.PageCount > initialCount);
        Assert.Same(document, viewer.Document);
        Assert.InRange(viewer.PageNumber, 1, viewer.PageCount);
    }

    private static FlowDocument CreateMultiPageDocument(string? prefix = null)
    {
        var text = (prefix ?? string.Empty) + string.Join(' ', Enumerable.Repeat("segment", 120));
        var document = FlowDocument.FromText(text);
        document.PageWidth = 100;
        document.PageHeight = 60;
        document.PagePadding = new Thickness(4);
        document.FontSize = 10;
        return document;
    }

    private static PropertyInfo AssertProperty(Type ownerType, string name)
    {
        var property = ownerType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.NotNull(property);
        return property!;
    }

    private static void AssertDependencyProperty(Type ownerType, string name)
    {
        var field = ownerType.GetField(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.NotNull(field);
        Assert.True(field!.IsInitOnly);
        Assert.Equal(typeof(DependencyProperty), field.FieldType);
    }

    private static void AssertReadOnlyDependencyProperty(Type ownerType, string name)
    {
        AssertDependencyProperty(ownerType, name);
        var field = ownerType.GetField(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)!;
        Assert.True(Assert.IsType<DependencyProperty>(field.GetValue(null)).ReadOnly);
    }

    private static void AssertProtectedStaticKey(Type ownerType, string name)
    {
        var field = ownerType.GetField(name, BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.NotNull(field);
        Assert.True(field!.IsFamily);
        Assert.True(field.IsInitOnly);
        Assert.Equal(typeof(DependencyPropertyKey), field.FieldType);
    }

    private static void AssertProtectedVirtual(Type ownerType, string name, params Type[] parameterTypes)
    {
        var method = ownerType.GetMethod(
            name,
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            binder: null,
            types: parameterTypes,
            modifiers: null);
        Assert.NotNull(method);
        Assert.True(method!.IsFamily);
        Assert.True(method.IsVirtual);
    }

    private static void AssertProtectedOverride(Type ownerType, string name, params Type[] parameterTypes)
    {
        AssertProtectedVirtual(ownerType, name, parameterTypes);
        var method = ownerType.GetMethod(
            name,
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            binder: null,
            types: parameterTypes,
            modifiers: null)!;
        Assert.NotEqual(method, method.GetBaseDefinition());
    }
}
