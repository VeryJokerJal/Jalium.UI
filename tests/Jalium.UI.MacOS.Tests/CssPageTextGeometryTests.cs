using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Automation.Text;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssPageTextGeometryTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();
    public static IEnumerable<object[]> Pages()
    {
        foreach (var nested in new[] { false, true }) foreach (var height in new[] { 72, 144 })
        foreach (var padding in new[] { 0, 8 }) foreach (var rtl in new[] { false, true }) yield return [nested, height, padding, rtl];
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public void FooterBoundsAndSelectionBelongOnlyToThePageThatContainsTheFooter(bool nested, int height, int padding, bool rtl)
    {
        var (doc, footer) = Example(nested, height, padding, rtl); var paginator = doc.ViewerPaginator; var count = paginator.PageCount; Assert.True(count > 2);
        for (var index = 0; index < count; index++)
        {
            using var page = Page(doc, index); var provider = Provider(page); var found = provider.DocumentRange.FindText("末段", false, false)!;
            Assert.Equal("末段", found.GetText(-1)); var boxes = Rectangles(page, found);
            if (index == count - 1) { Assert.NotEmpty(boxes); Assert.All(boxes, box => Within(page, box)); }
            else Assert.Empty(boxes);
            found.Select(); Assert.Equal("末段", page.Selection!.Text); Assert.Equal(boxes, Rectangles(page, provider.GetSelection().Single()));
            var visible = string.Concat(provider.GetVisibleRanges().Select(range => range.GetText(-1)));
            Assert.Equal(index == count - 1, visible.Contains("末段")); Assert.Same(footer.Inlines, page.Paragraphs[^1].Inlines);
        }
    }

    [Theory]
    [InlineData(false, 1)] [InlineData(true, 1)] [InlineData(false, 2)] [InlineData(true, 2)]
    public void ClickingTheTailOfAShortPageDoesNotChooseTheFollowingPage(bool rtl, int scale)
    {
        var (doc, _) = Example(true, 144, 0, rtl); using var page = Page(doc, doc.ViewerPaginator.PageCount - 2); VisualTreeHelper.SetRootDpi(page, new(scale, scale)); page.UpdateLayout();
        var provider = Provider(page); var viewport = page.GetChildLayoutClip()!.Bounds; Assert.InRange(viewport.Height, 1, 143);
        var point = new Point(rtl ? viewport.Right - 2 : viewport.Left + 2, viewport.Bottom - .1);
        var range = provider.RangeFromPoint(page.PointToScreen(point)); Assert.NotNull(range);
        var footer = provider.DocumentRange.FindText("末段", false, false)!;
        Assert.True(range!.CompareEndpoints(TextPatternRangeEndpoint.Start, footer, TextPatternRangeEndpoint.Start) < 0);
        var offset = page.OffsetFromPoint(new(point.X, point.Y + 200)); Assert.True(offset < doc.GetDocumentOffset(doc.Blocks[^1], 0));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AllocatedBlankTablePagesExposeNoTextOrPointRange(bool rtl)
    {
        var (doc, _) = Example(true, 72, 0, rtl); using var page = Page(doc, 4); var provider = Provider(page); var clip = page.GetChildLayoutClip()!.Bounds;
        Assert.True(clip.Height > 0); Assert.Empty(provider.GetVisibleRanges()); Assert.Empty(provider.DocumentRange.GetBoundingRectangles());
        Assert.Null(provider.RangeFromPoint(page.PointToScreen(new(clip.Left + 10, clip.Top + clip.Height / 2))));
        Assert.Equal(0, page.OffsetFromPoint(new(10, 20)));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ForcedParityBlankPagesDoNotExposeTheNextParagraph(bool rtl)
    {
        var head = P("页首"); var tail = P("页尾"); Css.SetStyle(tail, "break-before:recto;"); var doc = Document(72, 8, rtl, head, tail); Assert.Equal(3, doc.ViewerPaginator.PageCount);
        using var page = Page(doc, 1); var provider = Provider(page); Assert.Equal(0, page.GetChildLayoutClip()!.Bounds.Height);
        Assert.Empty(provider.DocumentRange.GetBoundingRectangles()); Assert.Empty(provider.GetVisibleRanges()); Assert.Null(provider.RangeFromPoint(page.PointToScreen(new(20, 20))));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void APartialPageClipsLinesAtTheFragmentBoundaryRatherThanThePaperEdge(bool rtl)
    {
        var paragraph = P("甲\n乙\n丙\n丁"); var doc = Document(100, 4, rtl, paragraph); using var page = new FlowDocumentPresentation(); page.SetDocument(doc); page.SetPage(new(400, 100), 24, 48); page.Measure(new(400, 100)); page.Arrange(new(0, 0, 400, 100));
        var provider = Provider(page); Assert.Empty(provider.DocumentRange.FindText("丙", false, false)!.GetBoundingRectangles());
        var beta = Rectangles(page, provider.DocumentRange.FindText("乙", false, false)!); Assert.Single(beta); Within(page, beta[0]); Assert.InRange(beta[0].Height, 1, 24);
        var viewport = page.GetChildLayoutClip()!.Bounds; var offset = page.OffsetFromPoint(new(rtl ? viewport.Right - 2 : viewport.Left + 2, viewport.Bottom + 100));
        Assert.InRange(offset, 0, doc.GetDocumentOffset(paragraph, 4) - 1);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void VisiblePortionsOfAnOversizedLineRemainWithinEveryPageClip(bool rtl)
    {
        var tall = P("高行"); tall.LineHeight = 280; var doc = Document(72, 8, rtl, tall, P("后文")); var total = 0d; var pages = 0;
        for (var index = 0; index < doc.ViewerPaginator.PageCount; index++)
        {
            using var page = Page(doc, index); foreach (var box in Rectangles(page, Provider(page).DocumentRange.FindText("高行", false, false)!)) { Within(page, box); total += box.Height; pages++; }
        }
        // Optical text offsets and the final fragment's allocated capacity
        // affect the total. No visible portion may escape or be counted twice.
        Assert.True(pages > 2); Assert.InRange(total, 1, tall.LineHeight);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void GeometryQueriesRefreshCssPlacementWithoutAnExplicitArrangePass(bool rtl)
    {
        var source = P("可见原文"); var doc = Document(72, 0, rtl, source); using var page = new FlowDocumentPresentation(); page.SetDocument(doc); page.SetPage(new(400, 72), 0, 72); page.Measure(new(400, 72)); page.Arrange(new(0, 0, 400, 72));
        var provider = Provider(page); var range = provider.DocumentRange.FindText("可见", false, false)!; var initial = Rectangles(page, range); Assert.Single(initial);
        Css.SetStyle(source, "margin-top:100px;"); Assert.Empty(range.GetBoundingRectangles()); Assert.Empty(provider.GetVisibleRanges()); Assert.Null(provider.RangeFromPoint(page.PointToScreen(new(20, 20))));
        Css.SetStyle(source, "margin:0;"); Assert.Equal(initial, Rectangles(page, range)); Assert.Same(source.Inlines, page.Paragraphs[0].Inlines);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TextChangesRefreshOriginalOffsetsBeforeTheProviderUsesTheNewRange(bool rtl)
    {
        var source = P("首行"); var footer = P("末段"); var doc = Document(120, 0, rtl, source, footer); using var page = new FlowDocumentPresentation(); page.SetDocument(doc); page.SetPage(new(400, 120), 0, 120); page.Measure(new(400, 120)); page.Arrange(new(0, 0, 400, 120));
        var provider = Provider(page); var before = Rectangles(page, provider.DocumentRange.FindText("末段", false, false)!).Single(); var run = (Run)source.Inlines[0];
        run.Text = "首行\n新增"; var range = provider.DocumentRange.FindText("末段", false, false)!; Assert.Equal("末段", range.GetText(-1)); var after = Rectangles(page, range).Single(); Assert.Equal(before.Top + 24, after.Top, 2);
        range.Select(); Assert.Equal("末段", page.Selection!.Text); Assert.Same(run, source.Inlines[0]); Assert.Same(footer.Inlines, page.Paragraphs[^1].Inlines);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RefreshingAnExistingPageReplacesItsShortClipAndPreservesItsOriginalSource(bool rtl)
    {
        var (doc, footer) = Example(true, 144, 0, rtl); var index = doc.ViewerPaginator.PageCount - 2; using var page = Page(doc, index); var metrics = (IFlowDocumentPagePresentation)doc.ViewerPaginator;
        var provider = Provider(page); Assert.Empty(provider.DocumentRange.FindText("末段", false, false)!.GetBoundingRectangles());
        Assert.True(metrics.TryRefreshPage(doc.ViewerPaginator.PageCount - 1, page, out var size)); page.Measure(size); page.Arrange(new Rect(size)); Assert.NotEmpty(provider.DocumentRange.FindText("末段", false, false)!.GetBoundingRectangles());
        Assert.True(metrics.TryRefreshPage(index, page, out size)); page.Measure(size); page.Arrange(new Rect(size)); Assert.Empty(provider.DocumentRange.FindText("末段", false, false)!.GetBoundingRectangles()); Assert.Same(footer.Inlines, page.Paragraphs[^1].Inlines);
    }

    private static (FlowDocument Document, Paragraph Footer) Example(bool nested, int height, int padding, bool rtl)
    {
        var table = new Table { CellSpacing = 0 };
        for (var groupIndex = 0; groupIndex < 3; groupIndex++)
        {
            var source = P(string.Join('\n', Enumerable.Range(groupIndex * 4, 4).Select(index => $"第{index:00}行"))); Css.SetStyle(source, "width:320px;");
            var row = new TableRow(); row.Cells.Add(Cell(source, 2)); var group = new TableRowGroup(); group.Rows.Add(row); table.RowGroups.Add(group); if (groupIndex != 1) Css.SetStyle(group, "height:25%;");
        }
        var basis = new TableRow(); foreach (var width in new[] { 40, 120 }) { var content = new Section(); Css.SetStyle(content, $"width:{width}px;height:0;"); basis.Cells.Add(Cell(content)); } table.RowGroups[2].Rows.Add(basis); Css.SetStyle(table, "width:auto;height:480px;");
        if (nested) { var outer = new Table { CellSpacing = 0 }; var group = new TableRowGroup(); var row = new TableRow(); outer.RowGroups.Add(group); group.Rows.Add(row); row.Cells.Add(Cell(table)); var side = new Section(); Css.SetStyle(side, "width:20px;height:0;"); row.Cells.Add(Cell(side)); Css.SetStyle(outer, "width:auto;"); table = outer; }
        var footer = P("末段"); return (Document(height, padding, rtl, table, footer), footer);
    }
    private static TableCell Cell(Block content, int span = 1) => new(content) { ColumnSpan = span, Padding = new(0), BorderThickness = new(0) };
    private static Paragraph P(string text) => new(new Run(text));
    private static FlowDocument Document(int height, int padding, bool rtl, params Block[] blocks)
    {
        var doc = new FlowDocument { FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24, Foreground = Brushes.Black, PagePadding = new(padding), PageWidth = 400, PageHeight = height, FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight };
        foreach (var block in blocks) doc.Blocks.Add(block); Css.SetStyleSheet(doc, "FlowDocument{white-space:pre-wrap;text-align:start;}Paragraph,Table{margin:0;}"); return doc;
    }
    private static FlowDocumentPresentation Page(FlowDocument doc, int index) => Assert.IsType<FlowDocumentPresentation>(doc.ViewerPaginator.GetPage(index).Visual);
    private static ITextProvider Provider(FlowDocumentPresentation page) => (ITextProvider)page.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
    private static Rect[] Rectangles(FlowDocumentPresentation page, ITextRangeProvider range) => range.GetBoundingRectangles().Chunk(4).Select(b => { var from = page.PointFromScreen(new(b[0], b[1])); var to = page.PointFromScreen(new(b[0] + b[2], b[1] + b[3])); return new Rect(from, to); }).ToArray();
    private static void Within(FlowDocumentPresentation page, Rect box)
    { var clip = page.GetChildLayoutClip()!.Bounds; Assert.True(box.Left >= clip.Left - .01 && box.Top >= clip.Top - .01 && box.Right <= clip.Right + .01 && box.Bottom <= clip.Bottom + .01, $"Text {box} extends outside page clip {clip}."); }
}
