using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using DocList = Jalium.UI.Documents.List;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssDocumentHeightTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData("height:96px;box-sizing:border-box;", 96)]
    [InlineData("height:96px;box-sizing:content-box;", 120)]
    [InlineData("height:0;box-sizing:border-box;", 24)]
    [InlineData("height:0;box-sizing:content-box;", 24)]
    [InlineData("block-size:calc(48px * 2);box-sizing:border-box;", 96)]
    public void UsedHeightPositionsTheNextBlockAndPaintsTheSelectedBox(string style, double expected)
    {
        var source = P("原文"); var next = P("后文"); var doc = Doc(source, next);
        Css.SetStyle(source, style + "padding:10px;border:2px solid blue;background:#dceee4;");
        using var body = Body(doc);
        Assert.Equal(expected, body.Paragraphs[1].TransformToAncestor(body).Y, 2);
        Assert.Equal(12, body.Paragraphs[0].TransformToAncestor(body).Y, 2);
        Assert.Equal(Colors.Blue, Pixel(body, 2, (int)expected - 1));
        Assert.Same(source.Inlines, body.Paragraphs[0].Inlines);
    }

    [Theory]
    [InlineData("height:30px;min-height:96px;max-height:40px;", 96)]
    [InlineData("height:auto;min-height:96px;max-height:40px;", 96)]
    [InlineData("height:96px;max-height:48px;", 48)]
    [InlineData("height:auto;max-height:24px;", 24)]
    [InlineData("block-size:10px;min-block-size:96px;max-block-size:40px;", 96)]
    public void MinimumHeightWinsAndMaximumCanConstrainAutoContent(string style, double expected)
    {
        var source = P("甲\n乙\n丙"); Css.SetStyle(source, style); using var body = Body(Doc(source, P("后文")));
        Assert.Equal(expected, body.Paragraphs[1].TransformToAncestor(body).Y, 2);
        Assert.True(body.Paragraphs[0].ActualHeight >= 72); // Overflow keeps its shaped original lines.
    }

    [Theory]
    [InlineData("content-box", 124, 224)] [InlineData("border-box", 112, 200)]
    public void PercentagesUseTheDefiniteParentContentHeight(string sizing, double inside, double outside)
    {
        var source = P("原文"); var after = P("章内后文"); var section = new Section(); section.Blocks.Add(source); section.Blocks.Add(after);
        Css.SetStyle(section, $"height:200px;box-sizing:{sizing};padding:10px;border:2px solid blue;");
        Css.SetStyle(source, "height:50%;box-sizing:content-box;padding:5px;border:1px solid green;");
        using var body = Body(Doc(section, P("章外后文")));
        Assert.Equal(inside, body.Paragraphs[1].TransformToAncestor(body).Y, 2);
        Assert.Equal(outside, body.Paragraphs[2].TransformToAncestor(body).Y, 2);
    }

    [Theory]
    [InlineData("")] [InlineData("min-height:200px;")] [InlineData("max-height:200px;")]
    public void AutoOrMinMaxOnlyParentsDoNotInventAPercentageBasis(string style)
    {
        var source = P("原文"); var after = P("后文"); var section = new Section(); section.Blocks.Add(source); section.Blocks.Add(after);
        Css.SetStyle(section, style); Css.SetStyle(source, "height:90%;min-height:80%;max-height:10%;");
        using var body = Body(Doc(section)); Assert.Equal(body.Paragraphs[0].ActualHeight, body.Paragraphs[1].TransformToAncestor(body).Y, 2);
    }

    [Fact]
    public void DefinitePercentageChainsUseTheImmediateContainingBlock()
    {
        var first = P("原文"); var next = P("后文"); var inner = new Section(first); inner.Blocks.Add(next); var outer = new Section(inner);
        Css.SetStyle(outer, "height:200px;"); Css.SetStyle(inner, "height:50%;"); Css.SetStyle(first, "height:50%;");
        using var body = Body(Doc(outer, P("章外")));
        Assert.Equal(50, body.Paragraphs[1].TransformToAncestor(body).Y, 2); Assert.Equal(200, body.Paragraphs[2].TransformToAncestor(body).Y, 2);
    }

    [Fact]
    public void ListAndItemHeightsResolveTheirOwnDescendantPercentages()
    {
        var first = P("原文"); var inside = P("项内"); var item = new ListItem(first); item.Blocks.Add(inside);
        var list = new DocList(); list.ListItems.Add(item); var second = new ListItem(P("第二项")); list.ListItems.Add(second);
        Css.SetStyle(list, "height:200px;"); Css.SetStyle(item, "height:50%;background:#dceee4;"); Css.SetStyle(first, "height:25%;");
        using var body = Body(Doc(list, P("列表后文")));
        Assert.Equal(25, body.Paragraphs[1].TransformToAncestor(body).Y, 2);
        Assert.Equal(100, body.Paragraphs[2].TransformToAncestor(body).Y, 2);
        Assert.Equal(200, body.Paragraphs[3].TransformToAncestor(body).Y, 2);
        Assert.Equal(20, body.Paragraphs[0].TransformToAncestor(body).X, 2);
    }

    [Fact]
    public void NativeHeightAndDefaultValuedAutoOverrideCssUntilCleared()
    {
        var source = P("原文"); Css.SetStyle(source, "height:96px;"); var doc = Doc(source, P("后文"));
        source.SetValue(FrameworkElement.HeightProperty, 80d); using var body = Body(doc);
        Assert.Equal(80, body.Paragraphs[1].TransformToAncestor(body).Y, 2);
        source.SetValue(FrameworkElement.HeightProperty, double.NaN); Layout(body);
        Assert.Equal(body.Paragraphs[0].ActualHeight, body.Paragraphs[1].TransformToAncestor(body).Y, 2);
        source.ClearValue(FrameworkElement.HeightProperty); Layout(body); Assert.Equal(96, body.Paragraphs[1].TransformToAncestor(body).Y, 2);
    }

    [Fact]
    public void VisibleOverflowRetainsOriginalSelectionBoundsAndScrollableExtent()
    {
        var source = P("甲\n乙\n丙\n丁\n戊"); var run = source.Inlines[0]; Css.SetStyle(source, "height:24px;");
        var doc = Doc(source, P("后文")); using var body = Body(doc);
        Assert.Equal(24, body.Paragraphs[1].TransformToAncestor(body).Y, 2); Assert.True(body.ExtentHeight >= 120);
        var provider = Provider(body); var last = provider.DocumentRange.FindText("戊", false, false)!; var rect = Rects(last).Single();
        Assert.True(rect.Y >= 96); Assert.Equal(24, rect.Height, 2); last.Select(); Assert.Equal("戊", body.Selection!.Text);
        Assert.InRange(body.OffsetFromPoint(new(rect.X + 2, rect.Y + 12)), 8, 9); Assert.Same(run, body.Paragraphs[0].Inlines[0]);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ReservedHeightSurvivesEmptyAndTextPagination(bool withText)
    {
        var empty = new Section(); Css.SetStyle(empty, "height:300px;border:2px solid blue;box-sizing:border-box;");
        var doc = withText ? Doc(empty, P("末段原文")) : Doc(empty); doc.PageHeight = 72;
        var paginator = doc.ViewerPaginator; Assert.Equal(5, paginator.PageCount);
        using var page = (FlowDocumentPresentation)paginator.GetPage(4).Visual!;
        if (withText)
        {
            Assert.Equal(doc.GetText(), Provider(page).DocumentRange.GetText(-1));
            Assert.Equal(12, page.Paragraphs[0].TransformToAncestor(page).Y, 2);
            Assert.Equal(24, Rects(Provider(page).DocumentRange.FindText("末段", false, false)!).Single().Height, 2);
        }
        Assert.Equal(Colors.Blue, Pixel(page, 2, 11));
    }

    [Fact]
    public void HeightChangesInvalidateBothConsumersAndCachedPagesAndRestoreTheirSources()
    {
        var source = P("原文"); var run = source.Inlines[0]; var next = P("末段"); var doc = Doc(source, next); doc.PageHeight = 72;
        Css.SetStyle(source, "height:48px;"); using var one = Body(doc); using var two = Body(doc); var before = doc.ViewerPaginator.PageCount;
        Css.SetStyle(source, "height:240px;"); Layout(one); Layout(two);
        Assert.Equal(240, one.Paragraphs[1].TransformToAncestor(one).Y, 2); Assert.Equal(240, two.Paragraphs[1].TransformToAncestor(two).Y, 2);
        Assert.True(doc.ViewerPaginator.PageCount > before);
        Css.SetStyle(source, ""); Layout(one); Layout(two);
        Assert.Equal(one.Paragraphs[0].ActualHeight, one.Paragraphs[1].TransformToAncestor(one).Y, 2); Assert.Same(run, one.Paragraphs[0].Inlines[0]);
    }

    [Fact]
    public void ForcedPageBreakFollowsTheWholeReservedHeight()
    {
        var first = P("原文"); Css.SetStyle(first, "height:144px;break-after:page;"); var doc = Doc(first, P("末段")); doc.PageHeight = 72;
        Assert.Equal(3, doc.ViewerPaginator.PageCount);
        using var page = (FlowDocumentPresentation)doc.ViewerPaginator.GetPage(2).Visual!;
        Assert.Single(Rects(Provider(page).DocumentRange.FindText("末段", false, false)!));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AncestorColumnsRetainReservedChildHeightAndOriginalText(bool nested)
    {
        var empty = new Section(); Css.SetStyle(empty, "height:240px;"); var original = P("末段原文"); var section = new Section(empty); section.Blocks.Add(original);
        var doc = Doc(section); doc.PageHeight = 72; Css.SetStyle(nested ? section : doc, "columns:2;column-gap:20px;");
        Assert.True(doc.ViewerPaginator.PageCount >= 2);
        var found = false;
        for (var index = 0; index < doc.ViewerPaginator.PageCount; index++)
        {
            using var page = (FlowDocumentPresentation)doc.ViewerPaginator.GetPage(index).Visual!;
            found |= Rects(Provider(page).DocumentRange.FindText("末段", false, false)!).Length > 0;
        }
        Assert.True(found); Assert.Same(section, original.Parent); Assert.Same(empty, section.Blocks[0]);
    }

    [Fact]
    public void CollapsingATableRowRemovesItsHeightSpaceAndRestoringKeepsItsSources()
    {
        var original = P("第一行"); Css.SetStyle(original, "height:180px;"); var first = new TableRow(); first.Cells.Add(new TableCell(original) { Padding = new(0), BorderThickness = new(0) });
        var second = new TableRow(); second.Cells.Add(new TableCell(P("第二行")) { Padding = new(0), BorderThickness = new(0) });
        var group = new TableRowGroup(); group.Rows.Add(first); group.Rows.Add(second); var table = new Table { CellSpacing = 0 }; table.RowGroups.Add(group);
        var doc = Doc(table, P("末段")); doc.PageHeight = 72; using var body = Body(doc); var tall = body.ActualHeight;
        Css.SetStyle(first, "visibility:collapse;"); Layout(body);
        Assert.True(body.ActualHeight < 72); Assert.Equal(1, doc.ViewerPaginator.PageCount);
        Css.SetStyle(first, ""); Layout(body); Assert.Equal(tall, body.ActualHeight, 2); Assert.Same(first.Cells[0], original.Parent);
    }

    [Fact]
    public void ClampedDefiniteParentHeightProvidesThePercentageBasis()
    {
        var first = P("原文"); var after = P("后文"); var section = new Section(first); section.Blocks.Add(after);
        Css.SetStyle(section, "height:200px;min-height:240px;max-height:100px;"); Css.SetStyle(first, "height:50%;");
        using var body = Body(Doc(section, P("章外")));
        Assert.Equal(120, body.Paragraphs[1].TransformToAncestor(body).Y, 2); Assert.Equal(240, body.Paragraphs[2].TransformToAncestor(body).Y, 2);
    }

    [Theory]
    [InlineData("height:0;")] [InlineData("height:calc(10% - 100px);")]
    public void ZeroHeightLeavesVisibleOriginalOverflowWithoutAdvancingFlow(string style)
    {
        var first = P("原文\n仍可选择"); var after = P("后文"); var section = new Section(first); section.Blocks.Add(after);
        Css.SetStyle(section, "height:200px;"); Css.SetStyle(first, style);
        using var body = Body(Doc(section)); Assert.Equal(0, body.Paragraphs[1].TransformToAncestor(body).Y, 2);
        Assert.Equal(24, Rects(Provider(body).DocumentRange.FindText("仍可选择", false, false)!).Single().Height, 2);
    }

    [Fact]
    public void FirstAndRestoredTypographicLinesAndOriginalInkOriginsStayStable()
    {
        var first = P("原文\n仍可选择"); Css.SetStyle(first, "height:96px;"); var doc = Doc(first, P("后文")); using var body = Body(doc);
        var initial = Rects(Provider(body).DocumentRange.FindText("原文", false, false)!).Single();
        var lineTop = body.TopForOffset(0); Assert.InRange(initial.Y, lineTop, lineTop + 3);
        Css.SetStyle(first, "height:160px;"); Layout(body); Css.SetStyle(first, "height:96px;"); Layout(body);
        Assert.Equal(lineTop, body.TopForOffset(0), 2);
        Assert.Equal(initial.Y, Rects(Provider(body).DocumentRange.FindText("原文", false, false)!).Single().Y, 2);
    }

    [Fact]
    public void AnOversizedLineWithinAShortBoxContinuesByItsTypographicHeight()
    {
        var source = P("超高原文"); source.LineHeight = 280; Css.SetStyle(source, "height:96px;"); var doc = Doc(source); doc.PageHeight = 100;
        Assert.Equal(3, doc.ViewerPaginator.PageCount);
        using var body = Body(doc); Assert.Equal(280, body.Lines.Single().Height, 2); Assert.Equal(0, body.Lines.Single().Top, 2);
        Assert.InRange(body.ExtentHeight, 280, 284);
    }

    private static Paragraph P(string text) => new(new Run(text));
    private static FlowDocument Doc(params Block[] blocks)
    {
        var doc = new FlowDocument { FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24, PagePadding = new(0), PageWidth = 400, PageHeight = 240, Background = Brushes.White };
        foreach (var block in blocks) doc.Blocks.Add(block);
        Css.SetStyleSheet(doc, "FlowDocument{white-space:pre-wrap;text-align:start;}Paragraph,Section,List,ListItem{margin:0;}"); return doc;
    }
    private static FlowDocumentPresentation Body(FlowDocument doc) { var body = new FlowDocumentPresentation(); body.SetDocument(doc); Layout(body); return body; }
    private static void Layout(FlowDocumentPresentation body) { body.Measure(new(400, double.PositiveInfinity)); body.Arrange(new(0, 0, 400, body.DesiredSize.Height)); }
    private static ITextProvider Provider(FlowDocumentPresentation body) => (ITextProvider)UIElementAutomationPeer.CreatePeerForElement(body)!.GetPattern(PatternInterface.Text)!;
    private static Rect[] Rects(ITextRangeProvider range) { var v = range.GetBoundingRectangles(); return Enumerable.Range(0, v.Length / 4).Select(i => new Rect(v[i * 4], v[i * 4 + 1], v[i * 4 + 2], v[i * 4 + 3])).ToArray(); }
    private static Color Pixel(FrameworkElement view, int x, int y)
    {
        view.Measure(new(400, double.PositiveInfinity)); if (view.ActualHeight == 0) view.Arrange(new(0, 0, 400, view.DesiredSize.Height));
        var bitmap = new RenderTargetBitmap(400, Math.Max(y + 1, (int)Math.Ceiling(view.ActualHeight)), 96, 96, PixelFormats.Bgra32); bitmap.Render(view); var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); var at = (y * bitmap.PixelWidth + x) * 4;
        return Color.FromArgb(pixels[at + 3], pixels[at + 2], pixels[at + 1], pixels[at]);
    }
}
