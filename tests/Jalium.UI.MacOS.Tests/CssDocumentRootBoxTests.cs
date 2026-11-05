using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssDocumentRootBoxTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData("width:50%;", 200)]
    [InlineData("width:80%;max-width:150px;", 150)]
    [InlineData("width:20px;min-width:25%;", 100)]
    [InlineData("width:auto;max-width:50%;", 200)]
    [InlineData("inline-size:50%;min-inline-size:60%;max-inline-size:40%;", 240)]
    [InlineData("width:calc(50% - 20px);", 180)]
    public void RootWidthsResolveAgainstThePageAreaAndRetainOriginalSources(string style, double expected)
    {
        var source = P("原文"); var run = source.Inlines[0]; var doc = Doc(source); var parent = source.Parent; Css.SetStyle(doc, style);
        using var body = Body(doc); Assert.Equal(expected, body.Paragraphs[0].ActualWidth, 2);
        Assert.Same(source.Inlines, body.Paragraphs[0].Inlines); Assert.Same(run, body.Paragraphs[0].Inlines[0]); Assert.Same(parent, source.Parent);
    }

    [Theory]
    [InlineData("width:50%;margin-inline:auto;", 100)]
    [InlineData("width:50%;margin-left:auto;margin-right:10%;", 160)]
    [InlineData("width:50%;margin-left:10%;margin-right:auto;", 40)]
    [InlineData("width:auto;max-width:50%;margin-inline:auto;", 100)]
    public void RootAutoMarginsUseTheFinalConstrainedWidth(string style, double left)
    {
        var doc = Doc(P("原文")); Css.SetStyle(doc, style); using var body = Body(doc);
        Assert.Equal(left, body.Paragraphs[0].TransformToAncestor(body).X, 2);
    }

    [Theory]
    [InlineData("margin:5%;padding:2.5%;")]
    [InlineData("margin-block:5%;margin-inline:5%;padding-block:2.5%;padding-inline:2.5%;")]
    [InlineData("margin:calc(2.5% + 9px);padding:calc(1.25% + 4.5px);")]
    public void RootCssInsetsComposeWithNativePagePaddingAndUseItsContentWidth(string style)
    {
        var doc = Doc(P("原文")); doc.PagePadding = new(20, 10, 20, 10); Css.SetStyle(doc, style);
        using var body = Body(doc); var text = body.Paragraphs[0]; var at = text.TransformToAncestor(body);
        Assert.Equal(47, at.X, 2); Assert.Equal(37, at.Y, 2); Assert.Equal(306, text.ActualWidth, 2);
        Assert.Equal(body.Paragraphs[0].ActualHeight + 74, body.DesiredSize.Height, 2);
    }

    [Theory]
    [InlineData("border-box", 176, 120)] [InlineData("content-box", 200, 144)]
    public void RootSizingUsesTheSelectedContentAndBorderBoxes(string sizing, double width, double height)
    {
        var doc = Doc(P("原文")); Css.SetStyle(doc, $"width:50%;height:120px;box-sizing:{sizing};padding:10px;border:2px solid blue;");
        using var body = Body(doc); Assert.Equal(width, body.Paragraphs[0].ActualWidth, 2);
        Assert.Equal(new Point(12, 12), body.Paragraphs[0].TransformToAncestor(body)); Assert.Equal(height, body.DesiredSize.Height, 2);
        Assert.Equal(Colors.Blue, Pixel(body, 2, (int)height - 1)); Assert.Equal(Colors.White, Pixel(body, (int)width + 24, 5));
    }

    [Theory]
    [InlineData("height:96px;", 96)]
    [InlineData("height:30px;min-height:96px;max-height:40px;", 96)]
    [InlineData("height:auto;min-height:96px;max-height:40px;", 96)]
    [InlineData("height:200px;max-height:120px;", 120)]
    [InlineData("block-size:calc(48px * 2);", 96)]
    public void RootHeightConstraintsReserveSpaceWithoutChangingThePaperSize(string style, double height)
    {
        var doc = Doc(P("原文")); Css.SetStyle(doc, style); using var body = Body(doc);
        Assert.Equal(height, body.DesiredSize.Height, 2); Assert.Equal(400, doc.PageWidth); Assert.Equal(144, doc.PageHeight);
    }

    [Theory]
    [InlineData("border-box", 100, 50)] [InlineData("content-box", 124, 62)]
    public void RootPercentageHeightUsesTheViewportAndSuppliesTheChildContentBasis(string sizing, double height, double after)
    {
        var first = P("原文"); var doc = Doc(first, P("后文")); Css.SetStyle(doc, $"height:50%;box-sizing:{sizing};padding:10px;border:2px solid blue;"); Css.SetStyle(first, "height:50%;");
        using var body = Body(doc, 400, 200); Assert.Equal(height, body.DesiredSize.Height, 2);
        Assert.Equal(after, body.Paragraphs[1].TransformToAncestor(body).Y, 2);
    }

    [Theory]
    [InlineData("")] [InlineData("min-height:200px;")] [InlineData("max-height:200px;")]
    public void RootAutoHeightDoesNotSupplyAChildPercentageBasis(string style)
    {
        var first = P("原文"); var doc = Doc(first, P("后文")); Css.SetStyle(doc, style); Css.SetStyle(first, "height:90%;min-height:80%;max-height:10%;");
        using var body = Body(doc, 400, 300); Assert.Equal(body.Paragraphs[0].ActualHeight, body.Paragraphs[1].TransformToAncestor(body).Y, 2);
    }

    [Fact]
    public void UnboundedScrollViewDoesNotUseItsOwnContentHeightAsAPercentageBasis()
    {
        var first = P("原文"); var doc = Doc(first, P("后文")); Css.SetStyle(doc, "height:50%;"); Css.SetStyle(first, "height:50%;");
        var viewer = new FlowDocumentScrollViewer { Document = doc, IsToolBarVisible = false, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
        var root = new StackPanel(); root.Children.Add(viewer); Layout(root, 400, 300); var body = Descendants(viewer).OfType<FlowDocumentPresentation>().Single();
        Assert.Equal(body.Paragraphs[0].ActualHeight, body.Paragraphs[1].TransformToAncestor(body).Y, 2); Assert.Equal(body.Paragraphs.Sum(text => text.ActualHeight), body.ExtentHeight, 2); viewer.Document = null;
    }

    [Fact]
    public void NativeAttachedRootValuesOverrideCssAndClearLiveEvenAtDefaultValues()
    {
        var doc = Doc(P("原文")); Css.SetStyle(doc, "width:50%;height:120px;margin:5%;padding:10px;border:2px solid blue;");
        using var body = Body(doc); Assert.Equal(32, body.Paragraphs[0].TransformToAncestor(body).X, 2);
        doc.SetValue(FrameworkElement.WidthProperty, 300d); doc.SetValue(FrameworkElement.HeightProperty, 180d);
        doc.SetValue(FrameworkElement.MarginProperty, new Thickness(0)); doc.SetValue(Control.PaddingProperty, new Thickness(0));
        doc.SetValue(Control.BorderThicknessProperty, new Thickness(0)); Layout(body);
        Assert.Equal(300, body.Paragraphs[0].ActualWidth, 2); Assert.Equal(0, body.Paragraphs[0].TransformToAncestor(body).X, 2); Assert.Equal(180, body.DesiredSize.Height, 2);
        doc.SetValue(FrameworkElement.WidthProperty, double.NaN); doc.SetValue(FrameworkElement.HeightProperty, double.NaN); Layout(body);
        Assert.Equal(400, body.Paragraphs[0].ActualWidth, 2); Assert.Equal(body.Paragraphs[0].ActualHeight, body.DesiredSize.Height, 2);
        foreach (var property in new[] { FrameworkElement.WidthProperty, FrameworkElement.HeightProperty, FrameworkElement.MarginProperty, Control.PaddingProperty, Control.BorderThicknessProperty }) doc.ClearValue(property);
        Layout(body); Assert.Equal(176, body.Paragraphs[0].ActualWidth, 2); Assert.Equal(32, body.Paragraphs[0].TransformToAncestor(body).X, 2); Assert.Equal(160, body.DesiredSize.Height, 2);
    }

    [Fact]
    public void RootCssChangesRefreshBothPresentationsAndTheCachedPaginator()
    {
        var doc = Doc(P("原文")); using var one = Body(doc); using var two = Body(doc); var paginator = doc.ViewerPaginator; Assert.Equal(1, paginator.PageCount);
        Css.SetStyle(doc, "width:50%;height:300px;padding:10px;"); Layout(one); Layout(two);
        Assert.Equal(180, one.Paragraphs[0].ActualWidth, 2); Assert.Equal(one.Paragraphs[0].ActualWidth, two.Paragraphs[0].ActualWidth, 2); Assert.Equal(3, paginator.PageCount);
        Css.SetStyle(doc, ""); Layout(one); Layout(two); Assert.Equal(400, one.Paragraphs[0].ActualWidth, 2); Assert.Equal(1, paginator.PageCount);
    }

    [Theory]
    [InlineData(72)] [InlineData(144)]
    public void RootEmptyHeightAndInsetsSurviveFragmentation(int pageHeight)
    {
        var doc = Doc(); doc.PageHeight = pageHeight; Css.SetStyle(doc, "height:300px;padding:10px;border:2px solid blue;");
        Assert.Equal((int)Math.Ceiling(300d / pageHeight), doc.ViewerPaginator.PageCount);
        using var last = (FlowDocumentPresentation)doc.ViewerPaginator.GetPage(doc.ViewerPaginator.PageCount - 1).Visual!;
        Assert.Equal(Colors.Blue, Pixel(last, 2, 299 % pageHeight)); Assert.Empty(last.Paragraphs);
    }

    [Fact]
    public void PagedPercentageHeightUsesThePageAreaAfterNativePadding()
    {
        var first = P("原文"); var doc = Doc(first, P("后文")); doc.PageHeight = 160; doc.PagePadding = new(20, 10, 20, 10);
        Css.SetStyle(doc, "height:50%;padding:10px;border:2px solid blue;"); Css.SetStyle(first, "height:50%;");
        using var page = (FlowDocumentPresentation)doc.ViewerPaginator.GetPage(0).Visual!;
        Assert.Equal(45, page.Paragraphs[1].TransformToAncestor(page).Y, 2); Assert.Equal(Colors.Blue, Pixel(page, 20, 79));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void LeadingRootInsetsPreserveBlankPagesAndEveryOriginalLine(bool columns)
    {
        var source = P(12); var doc = Doc(source); var parent = source.Parent; doc.PageHeight = 72; Css.SetStyle(doc, "padding:160px 10px 12px;border:2px solid blue;" + (columns ? "columns:2;column-gap:20px;" : ""));
        var seen = new HashSet<int>(); Assert.True(doc.ViewerPaginator.PageCount >= (columns ? 5 : 7));
        for (var index = 0; index < doc.ViewerPaginator.PageCount; index++)
        {
            using var page = (FlowDocumentPresentation)doc.ViewerPaginator.GetPage(index).Visual!;
            for (var line = 0; line < 12; line++)
            {
                var boxes = Rects(Provider(page).DocumentRange.FindText($"第{line:00}行", false, false)!);
                Assert.All(boxes, box => Assert.True(box.Y >= 0 && box.Bottom <= 72.02));
                if (boxes.Length > 0) { Assert.True(index >= 2); seen.Add(line); }
            }
        }
        Assert.Equal(12, seen.Count); Assert.Same(parent, source.Parent);
    }

    [Fact]
    public void RootColumnsUseTheirOwnContentBoxAndDefiniteHeight()
    {
        var doc = Doc(P(12)); Css.SetStyle(doc, "width:80%;height:120px;padding:10px;border:2px solid blue;margin:20px auto;columns:2;column-gap:20px;");
        using var body = Body(doc); var boxes = Bounds(body, 0);
        Assert.Equal(3, boxes.Select(box => Math.Round(box.X, 2)).Distinct().Count()); Assert.Equal(52, boxes[0].X, 2); Assert.InRange(boxes[0].Y, 32, 35);
        Assert.Equal(160, body.DesiredSize.Height, 2); Assert.True(body.DesiredSize.Width > 400); Assert.Equal(Colors.Blue, Pixel(body, 40, 139));
    }

    [Fact]
    public void NarrowRootHeightKeepsVisibleOverflowAndOriginalSearchGeometry()
    {
        var source = P(8); var doc = Doc(source); Css.SetStyle(doc, "height:24px;width:50%;margin:10px;padding:6px;border:2px solid blue;"); using var body = Body(doc);
        Assert.True(body.ExtentHeight >= 210); var range = Provider(body).DocumentRange.FindText("第07行", false, false)!; range.Select(); Assert.Equal("第07行", body.Selection!.Text);
        var box = Rects(range).Single(); var index = doc.GetText().IndexOf("第07行", StringComparison.Ordinal);
        Assert.InRange(body.OffsetFromPoint(new(box.X + box.Width / 2, box.Y + 12)), index, index + 4); Assert.Same(source.Inlines, body.Paragraphs[0].Inlines);
    }

    [Theory]
    [InlineData(100d)] [InlineData(150d)]
    public void HorizontalOverflowKeepsAStableViewportBasisAcrossRepeatedLayoutAndSearch(double zoom)
    {
        var doc = Doc(P("原文目标")); Css.SetStyle(doc, "width:150%;height:160px;padding:12px;border:2px solid blue;");
        var viewer = new FlowDocumentScrollViewer { Document = doc, Zoom = zoom, Height = 180, IsToolBarVisible = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        Layout(viewer, 400, 180); var body = Descendants(viewer).OfType<FlowDocumentPresentation>().Single();
        var host = Descendants(viewer).OfType<ScrollViewer>().Single(); var width = body.Paragraphs[0].ActualWidth;
        Assert.True(host.ScrollableWidth > 0); Assert.Equal(host.ViewportWidth / (zoom / 100) * 1.5 - 28, width, 2);
        for (var index = 0; index < 4; index++) { Layout(viewer, 400, 180); Assert.Equal(width, body.Paragraphs[0].ActualWidth, 2); }
        Assert.True(viewer.Find("目标")); Layout(viewer, 400, 180); Assert.Equal("目标", viewer.Selection!.Text); Assert.Equal(width, body.Paragraphs[0].ActualWidth, 2); viewer.Document = null;
    }

    [Theory]
    [InlineData(100d)] [InlineData(150d)] [InlineData(200d)]
    public void ContinuousRootPercentagesFollowTheUnscaledViewportAndNativePageArea(double zoom)
    {
        var first = P("原文"); var doc = Doc(first, P("后文")); doc.PagePadding = new(10);
        Css.SetStyle(doc, "height:50%;padding:10px;border:2px solid blue;"); Css.SetStyle(first, "height:50%;");
        var viewer = new FlowDocumentScrollViewer { Document = doc, Zoom = zoom, Height = 200, IsToolBarVisible = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Layout(viewer, 400, 200); var body = Descendants(viewer).OfType<FlowDocumentPresentation>().Single(); var host = Descendants(viewer).OfType<ScrollViewer>().Single();
        var rootHeight = (host.ViewportHeight / (zoom / 100) - 20) / 2;
        Assert.Equal(22 + (rootHeight - 24) / 2, body.Paragraphs[1].TransformToAncestor(body).Y, 2);
        viewer.Height = 300; Layout(viewer, 400, 300); rootHeight = (host.ViewportHeight / (zoom / 100) - 20) / 2;
        Assert.Equal(22 + (rootHeight - 24) / 2, body.Paragraphs[1].TransformToAncestor(body).Y, 2); viewer.Document = null;
    }

    [Fact]
    public void RootNativeMinMaxChangesInvalidatePresentationsAndTheCachedPaginator()
    {
        var doc = Doc(P("原文")); Css.SetStyle(doc, "width:50%;height:300px;"); using var body = Body(doc); var paginator = doc.ViewerPaginator; Assert.Equal(3, paginator.PageCount);
        doc.SetValue(FrameworkElement.MinWidthProperty, 250d); doc.SetValue(FrameworkElement.MaxHeightProperty, 100d); Layout(body);
        Assert.Equal(250, body.Paragraphs[0].ActualWidth, 2); Assert.Equal(100, body.DesiredSize.Height, 2); Assert.Equal(1, paginator.PageCount);
        doc.SetValue(FrameworkElement.MinHeightProperty, 200d); Layout(body); Assert.Equal(200, body.DesiredSize.Height, 2); Assert.Equal(2, paginator.PageCount);
        doc.ClearValue(FrameworkElement.MinWidthProperty); doc.ClearValue(FrameworkElement.MaxHeightProperty); doc.ClearValue(FrameworkElement.MinHeightProperty); Layout(body);
        Assert.Equal(200, body.Paragraphs[0].ActualWidth, 2); Assert.Equal(300, body.DesiredSize.Height, 2); Assert.Equal(3, paginator.PageCount);
    }

    [Fact]
    public void RootWidthAndInsetsReachNestedColumnsInsideTheOriginalTableCell()
    {
        var source = P(12); var section = new Section(source); Css.SetStyle(section, "columns:2;column-gap:20px;height:96px;");
        var cell = new TableCell(section) { Padding = new(0), BorderThickness = new(0) }; var row = new TableRow(); row.Cells.Add(cell); var group = new TableRowGroup(); group.Rows.Add(row);
        var table = new Table { CellSpacing = 0 }; table.RowGroups.Add(group); var doc = Doc(table); Css.SetStyle(doc, "width:50%;padding:10px;border:2px solid blue;margin:20px auto;");
        using var body = Body(doc); var boxes = Bounds(body, 0); Assert.Equal(3, boxes.Select(box => Math.Round(box.X, 2)).Distinct().Count()); Assert.Equal(112, boxes[0].X, 2);
        Assert.Equal(176, body.TableCells.Single().Bounds.Width, 2); Assert.Same(cell, section.Parent); Assert.Same(section, source.Parent);
        Css.SetStyle(doc, ""); Layout(body); Assert.Equal(400, body.TableCells.Single().Bounds.Width, 2); Assert.Same(source.Inlines, body.Paragraphs[0].Inlines);
    }

    [Fact]
    public void RootBorderBrushNativeOverrideAndClearUpdateThePaintedFrame()
    {
        var doc = Doc(P("原文")); Css.SetStyle(doc, "height:96px;border:2px solid blue;"); using var body = Body(doc); Assert.Equal(Colors.Blue, Pixel(body, 2, 95));
        doc.SetValue(Control.BorderBrushProperty, Brushes.Red); Layout(body); Assert.Equal(Colors.Red, Pixel(body, 2, 95));
        doc.ClearValue(Control.BorderBrushProperty); Layout(body); Assert.Equal(Colors.Blue, Pixel(body, 2, 95));
    }

    private static Paragraph P(int lines) => P(string.Join('\n', Enumerable.Range(0, lines).Select(index => $"第{index:00}行")));
    private static Paragraph P(string text) => new(new Run(text));
    private static FlowDocument Doc(params Block[] blocks)
    {
        var doc = new FlowDocument { FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24, PagePadding = new(0), PageWidth = 400, PageHeight = 144, Foreground = Brushes.Black, Background = Brushes.White };
        foreach (var block in blocks) doc.Blocks.Add(block);
        Css.SetStyleSheet(doc, "FlowDocument{white-space:pre-wrap;text-align:start;}Paragraph,Section,List,ListItem{margin:0;}"); return doc;
    }
    private static FlowDocumentPresentation Body(FlowDocument doc, double width = 400, double height = double.PositiveInfinity) { var body = new FlowDocumentPresentation(); body.SetDocument(doc); Layout(body, width, height); return body; }
    private static void Layout(FrameworkElement view, double width = 400, double height = double.PositiveInfinity)
    {
        for (var index = 0; index < 3; index++) { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); view.Measure(new(width, height)); view.Arrange(new(0, 0, width, double.IsFinite(height) ? height : view.DesiredSize.Height)); view.UpdateLayout(); }
    }
    private static IEnumerable<Visual> Descendants(Visual source) { for (var index = 0; index < VisualTreeHelper.GetChildrenCount(source); index++) if (VisualTreeHelper.GetChild(source, index) is Visual child) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
    private static Rect[] Bounds(FlowDocumentPresentation body, int index) { var text = body.Paragraphs[index]; var at = text.TransformToAncestor(body); return text.DocumentTextBounds(0, text.Text.Length).Select(box => new Rect(box.X + at.X, box.Y + at.Y, box.Width, box.Height)).ToArray(); }
    private static ITextProvider Provider(FlowDocumentPresentation body) => (ITextProvider)body.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
    private static Rect[] Rects(ITextRangeProvider range) { var values = range.GetBoundingRectangles(); return Enumerable.Range(0, values.Length / 4).Select(index => new Rect(values[index * 4], values[index * 4 + 1], values[index * 4 + 2], values[index * 4 + 3])).ToArray(); }
    private static Color Pixel(FrameworkElement view, int x, int y) { var bitmap = new RenderTargetBitmap(700, Math.Max(200, (int)Math.Ceiling(view.ActualHeight)), 96, 96, PixelFormats.Bgra32); bitmap.Render(view); var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); var at = (y * bitmap.PixelWidth + x) * 4; return Color.FromArgb(pixels[at + 3], pixels[at + 2], pixels[at + 1], pixels[at]); }
}
