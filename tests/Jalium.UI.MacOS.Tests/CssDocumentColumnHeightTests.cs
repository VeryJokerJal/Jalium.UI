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
public sealed class CssDocumentColumnHeightTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();
    private const string Columns = "columns:2;column-gap:20px;";

    [Theory]
    [InlineData("paragraph", "auto")] [InlineData("paragraph", "balance")]
    [InlineData("section", "auto")] [InlineData("section", "balance")]
    [InlineData("list", "auto")] [InlineData("list", "balance")]
    [InlineData("item", "auto")] [InlineData("item", "balance")]
    public void ConstrainedOwnersProduceInlineOverflowColumnsAndAdvanceByTheirUsedHeight(string kind, string fill)
    {
        var source = P(12); Block block = source; DependencyObject owner = source;
        if (kind == "section") owner = block = new Section(source);
        if (kind is "list" or "item")
        {
            var list = new DocList(); var item = new ListItem(source); list.ListItems.Add(item); block = list; owner = kind == "list" ? list : item;
        }
        var doc = Doc(block, P("后文")); Css.SetStyle(owner, Columns + $"height:96px;column-fill:{fill};"); using var body = Body(doc);
        var boxes = Bounds(body, 0); Assert.Equal(12, boxes.Length); Assert.Equal(3, boxes.Select(box => Math.Round(box.X, 2)).Distinct().Count());
        Assert.Equal(boxes[0].Y, boxes[4].Y, 2); Assert.Equal(boxes[0].Y, boxes[8].Y, 2);
        Assert.Equal(96, body.Paragraphs[1].TransformToAncestor(body).Y, 2); Assert.Same(source.Inlines, body.Paragraphs[0].Inlines);
    }

    [Theory]
    [InlineData("auto", 1)] [InlineData("balance", 2)]
    public void ShortContentUsesTheRequestedFillWithoutShrinkingTheSpecifiedFrame(string fill, int count)
    {
        var source = P(4); var section = new Section(source); var doc = Doc(section, P("后文"));
        Css.SetStyle(section, Columns + $"height:144px;column-fill:{fill};column-rule:4px solid blue;"); using var body = Body(doc);
        Assert.Equal(count, Bounds(body, 0).Select(box => Math.Round(box.X, 2)).Distinct().Count());
        Assert.Equal(144, body.Paragraphs[1].TransformToAncestor(body).Y, 2);
        if (count == 2) Assert.Equal(Colors.Blue, Pixel(body, 200, 120));
    }

    [Theory]
    [InlineData("max-height:96px;", 3, 96)]
    [InlineData("max-height:240px;", 2, 144)]
    [InlineData("min-height:192px;", 2, 192)]
    public void AutoHeightUsesItsMaximumAsACapAndItsMinimumAsAFrameFloor(string style, int count, double after)
    {
        var source = P(12); var section = new Section(source); Css.SetStyle(section, Columns + style);
        using var body = Body(Doc(section, P("后文"))); Assert.Equal(count, Bounds(body, 0).Select(box => Math.Round(box.X, 2)).Distinct().Count());
        Assert.Equal(after, body.Paragraphs[1].TransformToAncestor(body).Y, 2);
    }

    [Theory]
    [InlineData("height:120px;box-sizing:border-box;")] [InlineData("height:96px;box-sizing:content-box;")]
    public void ColumnCapacityUsesTheContentHeightInsidePaddingAndBorders(string style)
    {
        var source = P(12); var section = new Section(source); Css.SetStyle(section, Columns + style + "padding:10px;border:2px solid blue;");
        using var body = Body(Doc(section, P("后文"))); var boxes = Bounds(body, 0);
        Assert.Equal(3, boxes.Select(box => Math.Round(box.X, 2)).Distinct().Count()); Assert.Equal(12, boxes[0].X, 2); Assert.InRange(boxes[0].Y, 12, 15);
        Assert.Equal(120, body.Paragraphs[1].TransformToAncestor(body).Y, 2); Assert.Equal(Colors.Blue, Pixel(body, 2, 119));
    }

    [Fact]
    public void PercentageColumnHeightUsesTheDefiniteParentContentBox()
    {
        var source = P(8); var inner = new Section(source); var outside = P("章内后文"); var outer = new Section(inner); outer.Blocks.Add(outside);
        Css.SetStyle(outer, "height:200px;"); Css.SetStyle(inner, Columns + "height:calc(50% - 4px);");
        using var body = Body(Doc(outer, P("章外后文")));
        Assert.Equal(2, Bounds(body, 0).Select(box => Math.Round(box.X, 2)).Distinct().Count());
        Assert.Equal(96, body.Paragraphs[1].TransformToAncestor(body).Y, 2); Assert.Equal(200, body.Paragraphs[2].TransformToAncestor(body).Y, 2);
    }

    [Fact]
    public void NativeHeightAndAutoCanBeClearedWithoutLosingOriginalSources()
    {
        var source = P(12); var run = source.Inlines[0]; var section = new Section(source); Css.SetStyle(section, Columns + "height:96px;");
        using var body = Body(Doc(section, P("后文"))); Assert.Equal(3, Bounds(body, 0).Select(box => Math.Round(box.X, 2)).Distinct().Count());
        section.SetValue(FrameworkElement.HeightProperty, 144d); Layout(body); Assert.Equal(2, Bounds(body, 0).Select(box => Math.Round(box.X, 2)).Distinct().Count());
        section.SetValue(FrameworkElement.HeightProperty, double.NaN); Layout(body); Assert.Equal(144, body.Paragraphs[1].TransformToAncestor(body).Y, 2);
        section.ClearValue(FrameworkElement.HeightProperty); Layout(body); Assert.Equal(3, Bounds(body, 0).Select(box => Math.Round(box.X, 2)).Distinct().Count());
        Assert.Same(run, body.Paragraphs[0].Inlines[0]); Assert.Same(section, source.Parent);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OverflowDirectionAndOriginalTextHitGeometryFollowTheColumnOwner(bool rtl)
    {
        var source = P(12); var section = new Section(source); var doc = Doc(section); Css.SetStyle(section, Columns + $"height:96px;direction:{(rtl ? "rtl" : "ltr")};");
        using var body = Body(doc); var boxes = Bounds(body, 0);
        Assert.True(rtl ? boxes[8].X < boxes[4].X && boxes[4].X < boxes[0].X : boxes[8].X > boxes[4].X && boxes[4].X > boxes[0].X);
        var range = Provider(body).DocumentRange.FindText("第04行", false, false)!; range.Select(); Assert.Equal("第04行", body.Selection!.Text);
        var rectangle = Rects(range).Single(); var index = doc.GetText().IndexOf("第04行", StringComparison.Ordinal);
        Assert.InRange(body.OffsetFromPoint(new(rectangle.X + rectangle.Width / 2, rectangle.Y + 12)), index, index + 4);
    }

    [Fact]
    public void SpannersConsumeHeightBeforeTheFollowingColumnGroup()
    {
        var before = P(4); var heading = P("跨列标题"); var after = P(12); var section = new Section(before); section.Blocks.Add(heading); section.Blocks.Add(after);
        Css.SetStyle(section, Columns + "height:144px;"); Css.SetStyle(heading, "column-span:all;");
        using var body = Body(Doc(section, P("末段"))); var boxes = Bounds(body, 2);
        Assert.Equal(48, body.Paragraphs[1].TransformToAncestor(body).Y, 2); Assert.Equal(400, body.Paragraphs[1].ActualWidth, 2);
        Assert.Equal(4, boxes.Select(box => Math.Round(box.X, 2)).Distinct().Count()); Assert.Equal(boxes[0].Y, boxes[3].Y, 2);
        Assert.Equal(144, body.Paragraphs[3].TransformToAncestor(body).Y, 2);
    }

    [Theory]
    [InlineData(72, 3)] [InlineData(144, 2)]
    public void FragmentedColumnsUseTheirPageCapacityAndRetainEveryOriginalLine(int height, int expectedPages)
    {
        var source = P(12); var section = new Section(source); var footer = P("末段"); var doc = Doc(section, footer); doc.PageHeight = height;
        Css.SetStyle(section, Columns + "height:96px;"); Assert.Equal(expectedPages, doc.ViewerPaginator.PageCount);
        var seen = new HashSet<string>();
        for (var pageIndex = 0; pageIndex < doc.ViewerPaginator.PageCount; pageIndex++)
        {
            using var page = (FlowDocumentPresentation)doc.ViewerPaginator.GetPage(pageIndex).Visual!;
            for (var index = 0; index < 12; index++)
            {
                var text = $"第{index:00}行"; var boxes = Rects(Provider(page).DocumentRange.FindText(text, false, false)!);
                if (boxes.Length == 0) continue;
                Assert.All(boxes, box => { Assert.True(box.Height > 0 && box.Height <= 24.02); Assert.True(box.Y >= 0 && box.Bottom <= height + .02); }); seen.Add(text);
            }
        }
        Assert.Equal(12, seen.Count);
        Assert.Equal(expectedPages, ((IFlowDocumentPageMetrics)doc.ViewerPaginator).GetPageNumberForOffset(doc.GetDocumentOffset(footer, 0)));
    }

    [Fact]
    public void EmptySpecifiedHeightAndItsInsetsSurvivePagination()
    {
        var section = new Section(); Css.SetStyle(section, Columns + "height:120px;box-sizing:border-box;padding:10px;border:2px solid blue;");
        var doc = Doc(section, P("末段")); doc.PageHeight = 36; Assert.True(doc.ViewerPaginator.PageCount >= 4);
        using var page = (FlowDocumentPresentation)doc.ViewerPaginator.GetPage(3).Visual!; Assert.Equal(Colors.Blue, Pixel(page, 2, 11));
    }

    [Fact]
    public void ZeroHeightPreservesVisibleLineOverflowAndLeavesFollowingContentAtTheSameTop()
    {
        var source = P(4); var section = new Section(source); Css.SetStyle(section, Columns + "height:0;"); using var body = Body(Doc(section, P("后文")));
        var boxes = Bounds(body, 0); Assert.Equal(4, boxes.Length); Assert.Equal(4, boxes.Select(box => Math.Round(box.X, 2)).Distinct().Count());
        Assert.Equal(0, body.Paragraphs[1].TransformToAncestor(body).Y, 2); Assert.True(body.ExtentHeight >= 24);
    }

    [Fact]
    public void ResizingAndRemovingColumnsRestoresTheOriginalNaturalFlow()
    {
        var source = P(12); var run = source.Inlines[0]; var section = new Section(source); var doc = Doc(section, P("后文")); Css.SetStyle(section, Columns + "height:96px;"); using var body = Body(doc);
        body.Measure(new(300, double.PositiveInfinity)); body.Arrange(new(0, 0, 300, body.DesiredSize.Height)); Assert.Equal(3, Bounds(body, 0).Select(box => Math.Round(box.X, 2)).Distinct().Count());
        Css.SetStyle(section, ""); Layout(body); Assert.Single(Bounds(body, 0).Select(box => Math.Round(box.X, 2)).Distinct());
        Assert.True(body.Paragraphs[1].TransformToAncestor(body).Y >= 288); Assert.Same(run, body.Paragraphs[0].Inlines[0]);
    }

    [Fact]
    public void FixedInnerColumnsRespectRealAncestorColumnsAcrossPages()
    {
        var source = P(24); var section = new Section(source); var footer = P("末段"); var doc = Doc(section, footer); doc.PageWidth = 820; doc.PageHeight = 72;
        Css.SetStyle(doc, Columns); Css.SetStyle(section, Columns + "height:96px;"); Assert.Equal(3, doc.ViewerPaginator.PageCount);
        var seen = new HashSet<int>();
        for (var pageIndex = 0; pageIndex < doc.ViewerPaginator.PageCount; pageIndex++)
        {
            using var page = (FlowDocumentPresentation)doc.ViewerPaginator.GetPage(pageIndex).Visual!;
            for (var index = 0; index < 24; index++)
                if (Rects(Provider(page).DocumentRange.FindText($"第{index:00}行", false, false)!).Length > 0) seen.Add(index);
        }
        Assert.Equal(24, seen.Count); Assert.Same(section, source.Parent);
    }

    [Fact]
    public void AFixedColumnOwnerInsideATableCellKeepsItsUsedRowHeightAndRestoresAfterCollapse()
    {
        var source = P(12); var section = new Section(source); Css.SetStyle(section, Columns + "height:96px;");
        var cell = new TableCell(section) { Padding = new(0), BorderThickness = new(0) }; var row = new TableRow(); row.Cells.Add(cell); var group = new TableRowGroup(); group.Rows.Add(row);
        var table = new Table { CellSpacing = 0 }; table.RowGroups.Add(group); var doc = Doc(table, P("末段")); using var body = Body(doc);
        Assert.Equal(3, Bounds(body, 0).Select(box => Math.Round(box.X, 2)).Distinct().Count()); Assert.Equal(96, body.Paragraphs[1].TransformToAncestor(body).Y, 2);
        Css.SetStyle(row, "visibility:collapse;"); Layout(body); Assert.Equal(0, body.Paragraphs[1].TransformToAncestor(body).Y, 2); Assert.Equal(1, doc.ViewerPaginator.PageCount);
        Css.SetStyle(row, ""); Layout(body); Assert.Equal(96, body.Paragraphs[1].TransformToAncestor(body).Y, 2); Assert.Same(cell, section.Parent); Assert.Same(section, source.Parent);
    }

    private static Paragraph P(int lines) => P(string.Join('\n', Enumerable.Range(0, lines).Select(index => $"第{index:00}行")));
    private static Paragraph P(string text) => new(new Run(text));
    private static FlowDocument Doc(params Block[] blocks)
    {
        var doc = new FlowDocument { FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24, PagePadding = new(0), PageWidth = 400, PageHeight = 144, Foreground = Brushes.Black, Background = Brushes.White };
        foreach (var block in blocks) doc.Blocks.Add(block);
        Css.SetStyleSheet(doc, "FlowDocument{white-space:pre-wrap;text-align:start;}Paragraph,Section,List,ListItem{margin:0;}"); return doc;
    }
    private static FlowDocumentPresentation Body(FlowDocument doc) { var body = new FlowDocumentPresentation(); body.SetDocument(doc); Layout(body); return body; }
    private static void Layout(FlowDocumentPresentation body) { body.Measure(new(400, double.PositiveInfinity)); body.Arrange(new(0, 0, 400, body.DesiredSize.Height)); }
    private static Rect[] Bounds(FlowDocumentPresentation body, int index) { var text = body.Paragraphs[index]; var at = text.TransformToAncestor(body); return text.DocumentTextBounds(0, text.Text.Length).Select(box => new Rect(box.X + at.X, box.Y + at.Y, box.Width, box.Height)).ToArray(); }
    private static ITextProvider Provider(FlowDocumentPresentation body) => (ITextProvider)body.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
    private static Rect[] Rects(ITextRangeProvider range) { var v = range.GetBoundingRectangles(); return Enumerable.Range(0, v.Length / 4).Select(index => new Rect(v[index * 4], v[index * 4 + 1], v[index * 4 + 2], v[index * 4 + 3])).ToArray(); }
    private static Color Pixel(FrameworkElement view, int x, int y) { var bitmap = new RenderTargetBitmap(400, Math.Max(160, (int)Math.Ceiling(view.ActualHeight)), 96, 96, PixelFormats.Bgra32); bitmap.Render(view); var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); var at = (y * bitmap.PixelWidth + x) * 4; return Color.FromArgb(pixels[at + 3], pixels[at + 2], pixels[at + 1], pixels[at]); }
}
