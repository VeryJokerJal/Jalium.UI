using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssDocumentFragmentCapacityTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();
    private const string Columns = "columns:2;column-gap:20px;";

    [Theory]
    [InlineData("balance")] [InlineData("balance-all")]
    public void TheShorterLastPageReflowsInnerColumnsWithoutAnUnusedGapAfterItsSpanner(string fill)
    {
        var (doc, _, heading, tail) = Article(fill); Assert.Equal(2, doc.ViewerPaginator.PageCount);
        using var first = Page(doc, 0); using var last = Page(doc, 1);
        Assert.Equal(Colors.Blue, Pixel(first, 230, 144)); Assert.Equal(Colors.White, Pixel(last, 230, 110));
        var title = last.Paragraphs[1].TransformToAncestor(last); var after = last.Paragraphs[2].TransformToAncestor(last);
        Assert.Equal(240, title.X, 2); Assert.Equal(8, title.Y, 2);
        Assert.Equal(38, after.Y - title.Y, 2); Assert.Equal(240, after.X, 2);
        Assert.Same(heading.Inlines, last.Paragraphs[1].Inlines); Assert.Same(tail.Inlines, last.Paragraphs[2].Inlines);
    }

    [Fact]
    public void SequentialOuterFillKeepsItsFullHeightAndOriginalReadingOrder()
    {
        var (doc, _, _, tail) = Article("auto"); Assert.Equal(2, doc.ViewerPaginator.PageCount); using var page = Page(doc, 1);
        Assert.Equal(Colors.Blue, Pixel(page, 230, 144));
        Assert.Equal(2, ((IFlowDocumentPageMetrics)doc.ViewerPaginator).GetPageNumberForOffset(doc.GetDocumentOffset(tail, 0)));
        var rectangles = Bounds(page, 2); Assert.Equal(4, rectangles.Count); Assert.Equal(240, rectangles[0].X, 2);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AShortFirstFragmentAndAShortLastFragmentBothUseTheirActualCapacity(bool rtl)
    {
        var (doc, section, heading, _) = Article("balance", prefix: true);
        if (rtl) Css.SetStyle(section, Columns + "direction:rtl;text-align:start;");
        Assert.Equal(2, doc.ViewerPaginator.PageCount); using var last = Page(doc, 1);
        var text = last.Paragraphs[1]; var offset = text.Text.IndexOf("第26行", StringComparison.Ordinal);
        var rectangle = Bounds(last, 1, offset, 4).Single(); Assert.InRange(rectangle.Y, 0, 24);
        Assert.Equal(rtl ? 220 : 0, rtl ? rectangle.Right : rectangle.X, 2);
        var title = last.Paragraphs[2].TransformToAncestor(last); var after = last.Paragraphs[3].TransformToAncestor(last);
        Assert.Equal(38, after.Y - title.Y, 2); Assert.Same(heading.Inlines, last.Paragraphs[2].Inlines);
    }

    [Fact]
    public void AnOuterSpannerLeavesShortColumnsBeforeTheNextFullPage()
    {
        var source = P(Lines(34)); var section = new Section { Margin = new(0) }; section.Blocks.Add(source); var heading = P("外层标题"); heading.ClearValue(Block.MarginProperty);
        var doc = Doc(heading, section); Css.SetStyle(doc, Columns + "column-rule:4px solid blue;"); Css.SetStyle(section, Columns);
        Css.SetStyle(heading, "column-span:all;line-height:30px;margin:8px 0;");
        Assert.Equal(2, doc.ViewerPaginator.PageCount); using var page = Page(doc, 1);
        foreach (var (line, x) in new[] { (20, 0d), (24, 120d), (28, 240d), (31, 360d) })
        {
            var offset = source.Inlines.OfType<Run>().Single().Text.IndexOf($"第{line:00}行", StringComparison.Ordinal);
            var bounds = Bounds(page, 1, offset, 4).Single(); Assert.Equal(x, bounds.X, 2); Assert.InRange(bounds.Y, 0, 24);
        }
    }

    [Theory]
    [InlineData(8)] [InlineData(12)]
    public void CapacityFeedbackReachesPastEightIndependentContexts(int depth)
    {
        var source = P(Lines(12)); Block block = source; Css.SetStyle(source, Columns);
        for (var i = 0; i < depth; i++) { var section = new Section { Margin = new(0) }; section.Blocks.Add(block); Css.SetStyle(section, "columns:1;"); block = section; }
        var doc = Doc(block); Css.SetStyle(doc, Columns); using var body = Body(doc);
        var text = body.Paragraphs[0]; var offset = text.Text.IndexOf("第03行", StringComparison.Ordinal);
        Assert.Equal(120, Bounds(body, 0, offset, 4).Single().X, 2); Assert.Equal(72, body.ActualHeight, 2);
        Assert.Same(source.Inlines, text.Inlines);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SourceSelectionProviderBoundsAndPointLookupUseTheRebalancedLastPage(bool rtl)
    {
        var (doc, section, _, tail) = Article("balance"); if (rtl) Css.SetStyle(section, Columns + "direction:rtl;text-align:start;");
        using var page = Page(doc, 1); var provider = Assert.IsAssignableFrom<ITextProvider>(page.GetAutomationPeer()!.GetPattern(PatternInterface.Text));
        var range = provider.DocumentRange.FindText("尾丙", false, false)!; range.Select(); Assert.Equal("尾丙", page.Selection!.Text);
        var boxes = range.GetBoundingRectangles(); Assert.Equal(4, boxes.Length); Assert.InRange(boxes[1], 46, 48);
        Assert.Equal(rtl ? 340 : 360, rtl ? boxes[0] + boxes[2] : boxes[0], 2);
        var point = new Point(boxes[0] + .1, boxes[1] + boxes[3] / 2);
        Assert.Equal(doc.GetDocumentOffset(tail, 6), page.OffsetFromPoint(point));
        Assert.Equal(doc.GetDocumentOffset(tail, 8), page.OffsetFromPoint(new(boxes[0] + boxes[2] - .1, point.Y)));
    }

    [Fact]
    public void ChangingThePageHeightAndRestoringItRetainsTheOriginalRangesAndSpannerGap()
    {
        var (doc, _, _, tail) = Article("balance"); var run = tail.Inlines[0];
        using (var first = Page(doc, 1)) Assert.Equal(46, first.Paragraphs[2].TransformToAncestor(first).Y, 2);
        doc.PageHeight = 216; Assert.InRange(doc.ViewerPaginator.PageCount, 1, 2);
        doc.PageHeight = 168; using var restored = Page(doc, 1);
        Assert.Equal(46, restored.Paragraphs[2].TransformToAncestor(restored).Y, 2); Assert.Same(run, restored.Paragraphs[2].Inlines[0]);
    }

    [Fact]
    public void AnOversizedInnerLineKeepsItsBackgroundOnEveryPage()
    {
        var source = P("超高原文"); source.LineHeight = 200; Css.SetStyle(source, "background:blue;");
        var section = new Section { Margin = new(0) }; section.Blocks.Add(source); var doc = Doc(section); doc.PageHeight = 72;
        Css.SetStyle(doc, Columns); Css.SetStyle(section, Columns); Assert.Equal(3, doc.ViewerPaginator.PageCount);
        for (var index = 0; index < 3; index++) { using var page = Page(doc, index); Assert.Equal(Colors.Blue, Pixel(page, 1, 1)); }
    }

    [Fact]
    public void AnEmptyInnerContextDoesNotConsumeASlotOrMoveItsFollowingParagraph()
    {
        var empty = new Section { Margin = new(0) }; Css.SetStyle(empty, Columns); var source = P("原文"); var doc = Doc(empty, source); Css.SetStyle(doc, Columns);
        using var body = Body(doc); Assert.Equal(24, body.ActualHeight, 2); Assert.InRange(Bounds(body, 0).Single().Y, 0, 2);
    }

    [Theory]
    [InlineData("margin:8px 12px;")]
    [InlineData("margin-block:8px;margin-inline:12px;")]
    public void DocumentMarginsUseTheBlockPropertyAndKeepNativePrecedenceAndRemoval(string style)
    {
        var source = P("原文"); var doc = Doc(source); Css.SetStyle(doc, Columns); Css.SetStyle(source, style);
        using (var native = Body(doc)) Assert.Equal(0, native.Paragraphs[0].TransformToAncestor(native).X, 2);
        source.ClearValue(Block.MarginProperty);
        using (var styled = Body(doc))
        {
            Assert.Equal(new Thickness(12, 8, 12, 8), source.Margin);
            var at = styled.Paragraphs[0].TransformToAncestor(styled); Assert.Equal(12, at.X, 2); Assert.Equal(8, at.Y, 2);
        }
        Css.SetStyle(source, ""); using var restored = Body(doc);
        Assert.Equal(new Thickness(0, 0, 0, 10), source.Margin); Assert.Equal(0, restored.Paragraphs[0].TransformToAncestor(restored).Y, 2);
    }

    [Fact]
    public void AnExplicitPageAfterTheInnerSpannerSurvivesLastPageBalancing()
    {
        var (doc, _, heading, tail) = Article("balance"); Css.SetStyle(tail, "break-before:page;");
        var pages = (IFlowDocumentPageMetrics)doc.ViewerPaginator;
        Assert.Equal(3, doc.ViewerPaginator.PageCount);
        Assert.Equal(2, pages.GetPageNumberForOffset(doc.GetDocumentOffset(heading, 0)));
        Assert.Equal(3, pages.GetPageNumberForOffset(doc.GetDocumentOffset(tail, 0)));
        using var last = Page(doc, 2); var boxes = Bounds(last, 2); Assert.Equal(4, boxes.Count);
        Assert.Equal(0, boxes[0].X, 2); Assert.InRange(boxes[0].Y, 0, 2);
        Assert.All(boxes, box => { Assert.True(box.Y >= 0 && box.Bottom <= 168); Assert.True(box.X >= 0 && box.Right <= 460); });
        Assert.Same(tail.Inlines, last.Paragraphs[2].Inlines);
    }

    [Fact]
    public void ReclaimingAnInnerRowTailDoesNotShrinkAFixedTableAllocation()
    {
        var heading = P("标题"); heading.ClearValue(Block.MarginProperty);
        var section = new Section { Margin = new(0) }; section.Blocks.Add(P(Lines(10))); section.Blocks.Add(heading); section.Blocks.Add(P("甲\n乙\n丙\n丁"));
        Css.SetStyle(section, Columns); Css.SetStyle(heading, "column-span:all;line-height:30px;margin:8px 0;");
        var cell = new TableCell(section) { Padding = new(0) }; var row = new TableRow(); row.Cells.Add(cell);
        var group = new TableRowGroup(); group.Rows.Add(row); var table = new Table { Margin = new(0), CellSpacing = 0 }; table.RowGroups.Add(group);
        Css.SetStyle(table, "width:100%;height:300px;table-layout:fixed;"); var doc = Doc(table); Css.SetStyle(doc, Columns);
        using var body = Body(doc, 168); Assert.Equal(300, body.TableCells.Where(item => ReferenceEquals(item.Source, cell)).Sum(item => item.Bounds.Height), 2);
        Assert.Same(cell, section.Parent); Assert.Same(section, heading.Parent); Assert.Equal(4, Bounds(body, 2).Count);
    }

    private static (FlowDocument Doc, Section Section, Paragraph Heading, Paragraph Tail) Article(string fill, bool prefix = false)
    {
        var heading = P("局部标题"); heading.ClearValue(Block.MarginProperty); var tail = P("尾甲\n尾乙\n尾丙\n尾丁");
        var section = new Section { Margin = new(0) }; section.Blocks.Add(P(Lines(36))); section.Blocks.Add(heading); section.Blocks.Add(tail);
        var doc = prefix ? Doc(P("序"), section) : Doc(section);
        Css.SetStyle(doc, Columns + "column-fill:" + fill + ";column-rule:4px solid blue;"); Css.SetStyle(section, Columns);
        Css.SetStyle(heading, "column-span:all;line-height:30px;margin:8px 0;"); return (doc, section, heading, tail);
    }
    private static string Lines(int count) => string.Join('\n', Enumerable.Range(0, count).Select(i => $"第{i:00}行"));
    private static Paragraph P(string text) => new(new Run(text)) { Margin = new(0) };
    private static FlowDocument Doc(params Block[] blocks)
    { var doc = new FlowDocument { FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24, Foreground = Brushes.Black, Background = Brushes.White, PagePadding = new(0), PageWidth = 460, PageHeight = 168 }; foreach (var block in blocks) doc.Blocks.Add(block); Css.SetStyleSheet(doc, "FlowDocument{white-space:pre-wrap;text-align:start;}"); return doc; }
    private static FlowDocumentPresentation Page(FlowDocument doc, int number) => Assert.IsType<FlowDocumentPresentation>(doc.ViewerPaginator.GetPage(number).Visual);
    private static FlowDocumentPresentation Body(FlowDocument doc, double capacity = double.PositiveInfinity)
    { var body = new FlowDocumentPresentation(); body.SetDocument(doc); if (double.IsFinite(capacity)) body.SetPaginationColumnHeight(capacity); for (var i = 0; i < 3; i++) { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); body.Measure(new(doc.PageWidth, double.PositiveInfinity)); body.Arrange(new(0, 0, doc.PageWidth, body.DesiredSize.Height)); } return body; }
    private static IReadOnlyList<Rect> Bounds(FlowDocumentPresentation body, int index, int start = 0, int length = -1)
    { var text = body.Paragraphs[index]; var at = text.TransformToAncestor(body); return text.DocumentTextBounds(start, length < 0 ? text.Text.Length : length).Select(rect => new Rect(at.X + rect.X, at.Y + rect.Y, rect.Width, rect.Height)).ToArray(); }
    private static Color Pixel(FrameworkElement root, int x, int y)
    {
        var width = Math.Max(1, (int)Math.Ceiling(root.ActualWidth)); var height = Math.Max(1, (int)Math.Ceiling(root.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Bgra32); var drawing = new SoftwareDrawingContext(bitmap);
        drawing.DrawRectangle(Brushes.White, null, new(0, 0, width, height)); root.Render(drawing); drawing.Close(); var pixels = new byte[width * height * 4]; bitmap.CopyPixels(pixels, width * 4, 0);
        var at = (y * width + x) * 4; return Color.FromArgb(pixels[at + 3], pixels[at + 2], pixels[at + 1], pixels[at]);
    }
}
