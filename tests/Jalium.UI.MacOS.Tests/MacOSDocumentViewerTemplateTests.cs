using System.ComponentModel;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Data;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Tests;
[Collection("macOS Window globals")]
public sealed class MacOSDocumentViewerTemplateTests
{
    public MacOSDocumentViewerTemplateTests() => FlowDocumentTemplateTestHelper.Register();
    [Fact]
    public void DefaultPanelPresentsRetainedLogicalPagesAndPublishesRealMetrics()
    {
        var viewer = new DocumentViewer
        {
            Document = Document(),
            PageDisplay = DocumentViewerPageDisplay.Continuous,
            MaxPagesAcross = 2
        };
        Assert.NotEmpty(viewer.PageViews);
        Assert.All(viewer.PageViews, page => Assert.NotSame(viewer, page.VisualParent));
        Assert.All(viewer.PageViews, page => Assert.Same(viewer, page.LogicalParentInternal));
        Layout(viewer);
        var panel = Assert.IsType<DocumentPagePanel>(viewer.FindName("PART_PagePanel"));
        Assert.Same(viewer.VisiblePageViews, panel.PagesSource);
        Assert.Equal(viewer.PageViews, panel.Children.Cast<DocumentPageView>());
        Assert.Equal(490, viewer.ExtentWidth);
        Assert.Equal(Math.Ceiling(viewer.PageViews.Count / 2.0) * 330 - 10, viewer.ExtentHeight);
        Assert.Equal(300, viewer.ViewportWidth);
        Assert.Equal(200, viewer.ViewportHeight);
        viewer.MoveRight();
        viewer.MoveDown();
        Layout(viewer);
        Assert.Equal(16, viewer.HorizontalOffset);
        Assert.Equal(16, viewer.VerticalOffset);
        Assert.Equal(-16, viewer.PageViews[0].TransformToVisual(panel).Transform(new Point()).X);
    }

    [Fact]
    public void RetiredPanelCannotStealPagesAfterMissingTemplateAndRestoration()
    {
        var viewer = new DocumentViewer
        {
            Document = Document(),
            PageDisplay = DocumentViewerPageDisplay.Continuous
        };
        Layout(viewer);
        viewer.MoveDown();
        var pages = viewer.PageViews.ToArray();
        var old = Assert.IsType<DocumentPagePanel>(viewer.FindName("PART_PagePanel"));
        double offset = viewer.VerticalOffset;
        var blank = new ControlTemplate(typeof(DocumentViewer));
        blank.SetVisualTree(() => new Border());
        viewer.Template = blank;
        Layout(viewer);
        Assert.Null(old.PagesSource);
        Assert.Empty(old.Children);
        Assert.Equal(offset, viewer.VerticalOffset);
        viewer.ClearValue(Control.TemplateProperty);
        Layout(viewer);
        var current = Assert.IsType<DocumentPagePanel>(viewer.FindName("PART_PagePanel"));
        Assert.Equal(pages, current.Children.Cast<DocumentPageView>());
        Assert.All(pages, page => Assert.Same(viewer, page.LogicalParentInternal));
        old.Measure(new Size(100, 100));
        old.Arrange(new Rect(0, 0, 100, 100));
        Assert.Empty(old.Children);
        Assert.All(pages, page => Assert.Same(current, page.VisualParent));
        viewer.NextPage();
        Layout(viewer);
        Assert.Empty(old.Children);
    }

    [Fact]
    public void FacingPagesUseTheRightFirstPageAndTheNextSpread()
    {
        var viewer = new DocumentViewer
        {
            Document = Document(),
            PageDisplay = DocumentViewerPageDisplay.TwoUpFacing
        };
        Layout(viewer);
        var panel = Assert.IsType<DocumentPagePanel>(viewer.FindName("PART_PagePanel"));
        var first = Assert.Single(viewer.PageViews);
        Assert.Equal(250, first.TransformToVisual(panel).Transform(new Point()).X);
        Assert.Equal("TwoUpFacing", VisualStateManager.GetCurrentStateName(viewer, "PageDisplayStates"));
        viewer.NextPage();
        Layout(viewer);
        Assert.Equal(new[] { 1, 2 }, viewer.PageViews.Select(page => page.PageNumber));
        Assert.Equal(0, viewer.PageViews[0].TransformToVisual(panel).Transform(new Point()).X);
        Assert.Equal(250, viewer.PageViews[1].TransformToVisual(panel).Transform(new Point()).X);
    }

    [Fact]
    public void NavigationAndFitKeepCallerZoomAndOffsetBindings()
    {
        var model = new Model();
        var viewer = new DocumentViewer
        {
            Document = Document(),
            PageDisplay = DocumentViewerPageDisplay.Continuous
        };
        BindingOperations.SetBinding(viewer, DocumentViewer.PageDisplayProperty, new Binding(nameof(Model.Mode)) { Source = model, Mode = BindingMode.TwoWay });
        BindingOperations.SetBinding(viewer, DocumentViewer.MaxPagesAcrossProperty, new Binding(nameof(Model.Columns)) { Source = model, Mode = BindingMode.TwoWay });
        BindingOperations.SetBinding(viewer, DocumentViewer.ZoomProperty, new Binding(nameof(Model.Zoom)) { Source = model, Mode = BindingMode.TwoWay });
        BindingOperations.SetBinding(viewer, DocumentViewer.VerticalOffsetProperty, new Binding(nameof(Model.Offset)) { Source = model, Mode = BindingMode.TwoWay });
        Layout(viewer);
        viewer.IncreaseZoom();
        Layout(viewer);
        viewer.MoveDown();
        Assert.Equal(viewer.Zoom, model.Zoom);
        Assert.Equal(viewer.VerticalOffset, model.Offset);
        viewer.FitToWidth();
        Assert.NotNull(BindingOperations.GetBindingExpression(viewer, DocumentViewer.ZoomProperty));
        Assert.NotNull(BindingOperations.GetBindingExpression(viewer, DocumentViewer.VerticalOffsetProperty));
        viewer.FitToMaxPagesAcross(2);
        Assert.NotNull(BindingOperations.GetBindingExpression(viewer, DocumentViewer.PageDisplayProperty));
        Assert.NotNull(BindingOperations.GetBindingExpression(viewer, DocumentViewer.MaxPagesAcrossProperty));
        Assert.Equal(viewer.PageDisplay, model.Mode);
        Assert.Equal(2, model.Columns);
        model.Mode = DocumentViewerPageDisplay.TwoUpFacing;
        Assert.Equal(model.Mode, viewer.PageDisplay);
        model.Zoom = 80;
        model.Offset = 20;
        Assert.Equal(80, viewer.Zoom);
        Assert.Equal(20, viewer.VerticalOffset);
    }

    [Fact]
    public void FitWidthFollowsTheTemplateViewportAfterResize()
    {
        var viewer = new DocumentViewer
        {
            Document = Document(),
            FitMode = DocumentViewerFitMode.FitWidth
        };
        Layout(viewer);
        Assert.Equal(viewer.ViewportWidth / 240 * 100, viewer.Zoom, 5);
        Assert.Equal(240 * viewer.Zoom / 100, viewer.DisplayPageSize.Width, 5);
        Assert.Equal(viewer.DisplayPageSize.Width, viewer.ExtentWidth, 5);
        for (int i = 0; i < 4; i++)
        {
            viewer.Measure(new Size(420, 300));
            viewer.Arrange(new Rect(0, 0, 420, 300));
        }

        Assert.Equal(viewer.ViewportWidth / 240 * 100, viewer.Zoom, 5);
        Assert.Equal(240 * viewer.Zoom / 100, viewer.DisplayPageSize.Width, 5);
        Assert.Equal(viewer.DisplayPageSize.Width, viewer.ExtentWidth, 5);
        Assert.Equal(DocumentViewerFitMode.FitWidth, viewer.FitMode);
    }

    [Fact]
    public void ResizeFitPreservesFacingModeAndScrollInfoRevealsTheLastPage()
    {
        var viewer = new DocumentViewer
        {
            Document = Document(),
            FitMode = DocumentViewerFitMode.FitWidth
        };
        Layout(viewer);
        viewer.PageDisplay = DocumentViewerPageDisplay.ContinuousFacing;
        for (int i = 0; i < 4; i++)
        {
            viewer.Measure(new Size(420, 300));
            viewer.Arrange(new Rect(0, 0, 420, 300));
        }

        Assert.Equal(DocumentViewerPageDisplay.ContinuousFacing, viewer.PageDisplay);
        var panel = Assert.IsType<DocumentPagePanel>(viewer.FindName("PART_PagePanel"));
        Assert.Same(Assert.IsType<ScrollViewer>(viewer.FindName("PART_ScrollHost")), panel.ScrollOwner);
        var visible = panel.MakeVisible(viewer.PageViews[^1], new Rect(0, 0, 10, 10));
        Assert.False(visible.IsEmpty);
        Assert.InRange(visible.Bottom, 0, panel.ViewportHeight);
        Assert.True(viewer.VerticalOffset > 0);
        Assert.Throws<NotSupportedException>(() => ((IList<DocumentPageView>)viewer.VisiblePageViews).Clear());
    }

    [Theory]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(175)]
    public void PageTextGeometryScalesWithItsPaperRatherThanBeingClipped(double zoom)
    {
        var document = FlowDocument.FromText(string.Join("\n", Enumerable.Repeat("Needle original text", 10)));
        document.PageWidth = 240;
        document.PageHeight = 320;
        document.PagePadding = new Thickness(12);
        document.FontSize = 16;
        document.Background = Brushes.Blue;
        var viewer = new DocumentViewer
        {
            Document = document,
            Zoom = zoom
        };
        Layout(viewer);
        var page = Assert.Single(viewer.PageViews);
        var body = Assert.IsType<FlowDocumentPresentation>(page.GetVisualChild(0));
        Assert.Equal(page.DocumentPage.Size, body.RenderSize);
        var source = body.TextBounds(document.GetText().LastIndexOf("text", StringComparison.Ordinal), 4, clip: false).Last();
        var actual = body.TransformToVisual(page).Transform(new Point(source.Right, source.Bottom));
        Assert.Equal(source.Right * page.RenderSize.Width / page.DocumentPage.Size.Width, actual.X, 5);
        Assert.Equal(source.Bottom * page.RenderSize.Height / page.DocumentPage.Size.Height, actual.Y, 5);
        Assert.InRange(actual.X, 0, page.RenderSize.Width);
        Assert.InRange(actual.Y, 0, page.RenderSize.Height);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(page.RenderSize.Width), (int)Math.Ceiling(page.RenderSize.Height), 96, 96, PixelFormats.Bgra32);
        var drawing = new SoftwareDrawingContext(bitmap);
        page.Render(drawing);
        drawing.Close();
        var pixels = bitmap.GetPixelBuffer();
        int sample = ((int)(page.RenderSize.Height * 0.9) * bitmap.PixelWidth + (int)(page.RenderSize.Width * 0.9)) * 4;
        Assert.Equal(255, pixels[sample]);
        Assert.Equal(0, pixels[sample + 1]);
        Assert.Equal(0, pixels[sample + 2]);
    }

    [Fact]
    public void DocumentAndDisabledStatesFollowTheDefaultTemplate()
    {
        var viewer = new DocumentViewer
        {
            Document = Document()
        };
        Layout(viewer);
        var root = Assert.IsType<Border>(viewer.FindName("PART_Root"));
        viewer.IsEnabled = false;
        Assert.Equal(0.5, root.Opacity);
        Assert.Equal("Disabled", VisualStateManager.GetCurrentStateName(viewer, "CommonStates"));
        viewer.IsEnabled = true;
        Assert.Equal(1, root.Opacity);
        viewer.Document = null;
        Layout(viewer);
        Assert.Empty(viewer.VisiblePageViews);
        Assert.Empty(Assert.IsType<DocumentPagePanel>(viewer.FindName("PART_PagePanel")).Children);
        Assert.Equal("Empty", VisualStateManager.GetCurrentStateName(viewer, "DocumentStates"));
    }

    [Fact]
    public void FlowDocumentSearchUsesOriginalPageGeometryAndWordBoundaries()
    {
        var document = Document();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph(new Run("Needle needles needle_ needle 文needle针 needle\u0301")));
        var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
        var provider = Assert.IsAssignableFrom<IDocumentTextSearchProvider>(paginator);
        Assert.Equal(6, provider.Find("needle", false, false).Count());
        var words = provider.Find("needle", false, true).ToArray();
        Assert.Equal(2, words.Length);
        Assert.Single(provider.Find("needle", true, true));
        Assert.All(words, result =>
        {
            Assert.Equal(1, result.PageNumber);
            Assert.False(result.BoundingRect.IsEmpty);
            Assert.True(result.BoundingRect.Width > 0);
        });
        Assert.Equal("Needle needles needle_ needle 文needle针 needle\u0301\n", document.GetText());
        var viewer = new DocumentViewer
        {
            Document = document
        };
        Layout(viewer);
        viewer.Find("needle", matchWholeWord: true);
        viewer.FindNext();
        Assert.Equal(2, viewer.SearchResultCount);
        Assert.Equal(1, viewer.CurrentPage);
    }

    [Fact]
    public void SearchBringsALaterScaledPageIntoTheContinuousViewport()
    {
        var document = Document();
        var viewer = new DocumentViewer
        {
            Document = document,
            PageDisplay = DocumentViewerPageDisplay.Continuous,
            Zoom = 150
        };
        Layout(viewer);
        var provider = Assert.IsAssignableFrom<IDocumentTextSearchProvider>(((IDocumentPaginatorSource)document).DocumentPaginator);
        var result = Assert.Single(provider.Find("section 20", false, false));
        Assert.True(result.PageNumber > 1);
        viewer.Find("section 20");
        viewer.FindNext();
        Layout(viewer);
        var panel = Assert.IsType<DocumentPagePanel>(viewer.FindName("PART_PagePanel"));
        var page = viewer.PageViews.Single(item => item.PageNumber == result.PageNumber - 1);
        var point = page.TransformToVisual(panel).Transform(new Point(result.BoundingRect.X * 1.5, result.BoundingRect.Y * 1.5));
        Assert.InRange(point.X, -0.1, viewer.ViewportWidth);
        Assert.InRange(point.Y, -0.1, viewer.ViewportHeight);
        Assert.True(point.Y + result.BoundingRect.Height * 1.5 <= viewer.ViewportHeight + 0.1);
        Assert.Equal(result.PageNumber, viewer.CurrentPage);
    }

    private static FlowDocument Document()
    {
        var document = FlowDocument.FromText(string.Join("\n\n", Enumerable.Range(1, 20).Select(i => $"Page section {i}\nNeedle text stays in the original document. " + new string ('x', 120))));
        document.PageWidth = 240;
        document.PageHeight = 320;
        document.PagePadding = new Thickness(12);
        document.FontSize = 16;
        return document;
    }

    private static void Layout(DocumentViewer viewer)
    {
        viewer.Style ??= (Style)MenuFlyoutTemplateTestHelper.Themes.Value[typeof(DocumentViewer)];
        for (int i = 0; i < 4; i++)
        {
            viewer.Measure(new Size(300, 200));
            viewer.Arrange(new Rect(0, 0, 300, 200));
        }
    }

    private sealed class Model : INotifyPropertyChanged
    {
        private double _zoom = 100, _offset;
        private DocumentViewerPageDisplay _mode = DocumentViewerPageDisplay.Continuous;
        private int _columns = 1;
        public DocumentViewerPageDisplay Mode
        {
            get => _mode;
            set
            {
                _mode = value;
                PropertyChanged?.Invoke(this, new(nameof(Mode)));
            }
        }

        public int Columns
        {
            get => _columns;
            set
            {
                _columns = value;
                PropertyChanged?.Invoke(this, new(nameof(Columns)));
            }
        }

        public double Zoom
        {
            get => _zoom;
            set
            {
                _zoom = value;
                PropertyChanged?.Invoke(this, new(nameof(Zoom)));
            }
        }

        public double Offset
        {
            get => _offset;
            set
            {
                _offset = value;
                PropertyChanged?.Invoke(this, new(nameof(Offset)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
