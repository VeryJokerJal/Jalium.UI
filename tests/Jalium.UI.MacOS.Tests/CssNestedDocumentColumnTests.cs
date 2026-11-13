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
public sealed class CssNestedDocumentColumnTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();
    private const string TwoColumns = "columns:2;column-gap:20px;";
    private static string Lines(int count) => string.Join('\n', Enumerable.Range(0, count).Select(i => $"第{i:00}行"));

    [Theory]
    [InlineData("paragraph")] [InlineData("section")] [InlineData("list")] [InlineData("item")]
    public void DocumentContainersEstablishTheirOwnColumnsWithoutRootColumnDeclarations(string kind)
    {
        var source = P(Lines(6)); Block block = source; DependencyObject owner = source;
        if (kind == "section") { var section = new Section { Margin = new(0) }; section.Blocks.Add(source); owner = block = section; }
        if (kind is "list" or "item")
        {
            var list = new Documents.List { Margin = new(0), Padding = new(0) }; var item = new ListItem(source); list.ListItems.Add(item);
            block = list; owner = kind == "list" ? list : item;
        }
        var doc = Doc(block); Css.SetStyle(owner, TwoColumns); using var body = Body(doc);
        var text = Assert.Single(body.Paragraphs); var rectangles = text.DocumentTextBounds(0, text.Text.Length);
        Assert.Equal(2, rectangles.Select(rect => Math.Round(rect.X, 2)).Distinct().Count());
        Assert.Equal(rectangles[0].Y, rectangles[3].Y, 2); Assert.Same(source.Inlines, text.Inlines);
        Assert.InRange(body.ActualHeight, 72, 74); Assert.Same(block, doc.Blocks[0]);
    }

    [Theory]
    [InlineData("column-count:2;")] [InlineData("column-width:100px;")] [InlineData("columns:100px 2;")]
    public void AParagraphUsesItsOwnWidthAndColumnSizing(string style)
    {
        var source = P(Lines(6)); var doc = Doc(source); Css.SetStyle(source, style + "column-gap:20px;"); using var body = Body(doc);
        var rectangles = body.Paragraphs[0].DocumentTextBounds(0, source.Inlines.OfType<Run>().Single().Text.Length);
        Assert.Equal(120, rectangles[3].X - rectangles[0].X, 2); Assert.Equal(72, body.ActualHeight, 2);
    }

    [Fact]
    public void TwoIndependentSectionsKeepTheirOwnRulesGapsAndFollowingBlockPositions()
    {
        var first = new Section { Margin = new(0) }; first.Blocks.Add(P(Lines(6)));
        var second = new Section { Margin = new(0) }; second.Blocks.Add(P(Lines(6))); var last = P("结束"); var doc = Doc(first, second, last);
        Css.SetStyle(first, TwoColumns + "column-rule:4px solid red;"); Css.SetStyle(second, "columns:3;column-gap:10px;column-rule:4px solid blue;");
        using var body = Body(doc); Assert.Equal(Colors.Red, Pixel(body, 110, 10));
        var secondTop = body.Paragraphs[1].TransformToAncestor(body).Y;
        Assert.InRange(secondTop, 72, 73); Assert.Equal(Colors.Blue, Pixel(body, 71, (int)secondTop + 10));
        Assert.Equal(120, body.Paragraphs[2].TransformToAncestor(body).Y, 2);
    }

    [Fact]
    public void NestedColumnContainersComposeFourColumnsInOriginalReadingOrder()
    {
        var source = P(Lines(12)); var section = new Section { Margin = new(0) }; section.Blocks.Add(source); var doc = Doc(section); doc.PageWidth = 460;
        Css.SetStyle(doc, TwoColumns + "column-rule:4px solid blue;"); Css.SetStyle(section, TwoColumns + "column-rule:4px solid red;"); using var body = Body(doc);
        Assert.Equal(new[] { 0d, 120, 240, 360 }, TextBounds(body, 0).Select(rect => Math.Round(rect.X, 2)).Distinct().ToArray());
        Assert.Equal(72, body.ActualHeight, 2);
        Assert.Equal(Colors.Red, Pixel(body, 110, 10)); Assert.Equal(Colors.Blue, Pixel(body, 230, 10)); Assert.Equal(Colors.Red, Pixel(body, 350, 10));
        var offset = source.Inlines.OfType<Run>().Single().Text.IndexOf("第06行", StringComparison.Ordinal);
        var rectangle = TextBounds(body, 0, offset, 4).Single(); Assert.Equal(240, rectangle.X, 2);
        Assert.Equal(offset, body.OffsetFromPoint(new(rectangle.X + .1, rectangle.Y + rectangle.Height / 2)));
    }

    [Fact]
    public void ThreeColumnLevelsResolveTheirAncestorCapacityWithoutLosingAnyLine()
    {
        var source = P(Lines(24)); var section = new Section { Margin = new(0) }; section.Blocks.Add(source); var doc = Doc(section); doc.PageWidth = 940;
        Css.SetStyle(doc, TwoColumns); Css.SetStyle(section, TwoColumns); Css.SetStyle(source, TwoColumns);
        using var body = Body(doc); var boxes = TextBounds(body, 0); Assert.Equal(24, boxes.Count);
        Assert.Equal(8, boxes.Select(rect => Math.Round(rect.X, 2)).Distinct().Count()); Assert.Equal(72, body.ActualHeight, 2);
    }

    [Fact]
    public void ASpanningHeadingOnlySpansItsNearestColumnContainer()
    {
        var heading = P("内层标题"); var section = new Section { Margin = new(0) }; section.Blocks.Add(P(Lines(6))); section.Blocks.Add(heading); section.Blocks.Add(P(Lines(6)));
        var outer = P("外层标题"); var doc = Doc(section, outer); doc.PageWidth = 460;
        Css.SetStyle(doc, TwoColumns); Css.SetStyle(section, TwoColumns); Css.SetStyle(heading, "column-span:all;"); Css.SetStyle(outer, "column-span:all;");
        using var body = Body(doc); Assert.Equal(220, body.Paragraphs[1].ActualWidth, 2); Assert.Equal(460, body.Paragraphs[3].ActualWidth, 2);
        Assert.Same(section, heading.Parent); Assert.Same(outer, doc.Blocks[1]);
    }

    [Fact]
    public void AParagraphAfterAnInnerSpannerKeepsItsBalancedLineOrderAcrossFeedbackPasses()
    {
        var heading = P("局部标题"); heading.ClearValue(Block.MarginProperty);
        var section = new Section { Margin = new(0) }; section.Blocks.Add(P(Lines(36))); section.Blocks.Add(heading); section.Blocks.Add(P("甲\n乙\n丙\n丁"));
        var doc = Doc(section); doc.PageWidth = 335; Css.SetStyle(doc, "columns:300px 2;column-gap:20px;"); Css.SetStyle(section, "columns:2;column-gap:16px;");
        Css.SetStyle(heading, "column-span:all;font-size:18px;line-height:30px;margin:8px 0;"); using var body = Body(doc);
        var boxes = TextBounds(body, 2); Assert.Equal(4, boxes.Count);
        Assert.Equal(boxes[0].X, boxes[1].X, 2); Assert.Equal(boxes[2].X, boxes[3].X, 2);
        Assert.Equal(boxes[0].Y, boxes[2].Y, 2); Assert.Equal(boxes[1].Y, boxes[3].Y, 2);
        Assert.True(boxes[2].X > boxes[0].X + 100);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void IndependentColumnsGenerateNewColumnRowsOnEveryPage(bool outerColumns)
    {
        var source = P(Lines(outerColumns ? 24 : 12)); var section = new Section { Margin = new(0) }; section.Blocks.Add(source); var doc = Doc(section);
        if (outerColumns) { doc.PageWidth = 460; Css.SetStyle(doc, TwoColumns); }
        Css.SetStyle(section, TwoColumns); Assert.Equal(2, doc.ViewerPaginator.PageCount);
        var index = outerColumns ? 12 : 6; var needle = $"第{index:00}行"; var offset = source.Inlines.OfType<Run>().Single().Text.IndexOf(needle, StringComparison.Ordinal);
        Assert.Equal(2, ((IFlowDocumentPageMetrics)doc.ViewerPaginator).GetPageNumberForOffset(offset));
        using var page = Assert.IsType<FlowDocumentPresentation>(doc.ViewerPaginator.GetPage(1).Visual);
        var provider = Assert.IsAssignableFrom<ITextProvider>(page.GetAutomationPeer()!.GetPattern(PatternInterface.Text));
        var bounds = provider.DocumentRange.FindText(needle, false, false)!.GetBoundingRectangles();
        Assert.Equal(4, bounds.Length); Assert.InRange(bounds[0], 0, 20); Assert.InRange(bounds[1], 0, 24);
    }

    [Fact]
    public void ASectionUsesTheSpaceRemainingOnTheFirstPageBeforeContinuingItsOwnColumns()
    {
        var source = P(Lines(12)); var section = new Section { Margin = new(0) }; section.Blocks.Add(source); var doc = Doc(P("序"), section);
        Css.SetStyle(section, TwoColumns); Assert.Equal(3, doc.ViewerPaginator.PageCount);
        var offset = doc.GetDocumentOffset(source, 20); Assert.Equal(2, ((IFlowDocumentPageMetrics)doc.ViewerPaginator).GetPageNumberForOffset(offset));
        using var page = Assert.IsType<FlowDocumentPresentation>(doc.ViewerPaginator.GetPage(0).Visual);
        Assert.Equal(2, TextBounds(page, 1).Where(rect => rect.Y >= 0 && rect.Bottom <= 72).Select(rect => rect.X).Distinct().Count());
    }

    [Theory]
    [InlineData("column", 1)] [InlineData("page", 2)] [InlineData("right", 3)]
    public void ForcedBreaksInsideAnInnerContextKeepTheirColumnOrPageType(string value, int pages)
    {
        var first = P("甲"); var second = P("乙"); var section = new Section { Margin = new(0) }; section.Blocks.Add(first); section.Blocks.Add(second);
        var doc = Doc(section); doc.PageWidth = 460; Css.SetStyle(doc, TwoColumns); Css.SetStyle(section, TwoColumns); Css.SetStyle(first, "break-after:" + value + ";");
        Assert.Equal(pages, doc.ViewerPaginator.PageCount);
        Assert.Equal(pages, ((IFlowDocumentPageMetrics)doc.ViewerPaginator).GetPageNumberForOffset(doc.GetDocumentOffset(second, 0)));
        using var page = Assert.IsType<FlowDocumentPresentation>(doc.ViewerPaginator.GetPage(pages - 1).Visual);
        var provider = Assert.IsAssignableFrom<ITextProvider>(page.GetAutomationPeer()!.GetPattern(PatternInterface.Text));
        var bounds = provider.DocumentRange.FindText("乙", false, false)!.GetBoundingRectangles(); Assert.Equal(4, bounds.Length);
        Assert.Equal(value == "column" ? 120 : 0, bounds[0], 2); Assert.InRange(bounds[1], 0, 24);
    }

    [Fact]
    public void InheritedStartAlignmentRespectsAnExplicitParagraphOverrideAndItsRemoval()
    {
        var source = P(Lines(6)); source.TextAlignment = TextAlignment.Left; var section = new Section { Margin = new(0) }; section.Blocks.Add(source); var doc = Doc(section);
        Css.SetStyle(section, TwoColumns + "direction:rtl;text-align:start;"); using var body = Body(doc);
        Assert.Equal(120, TextBounds(body, 0)[0].X, 2);
        source.ClearValue(Block.TextAlignmentProperty); Layout(body); Assert.Equal(220, TextBounds(body, 0)[0].Right, 2);
        section.TextAlignment = TextAlignment.Center; Layout(body);
        var box = TextBounds(body, 0)[0]; Assert.Equal(170, box.X + box.Width / 2, 2);
    }

    [Fact]
    public void AnInheritedJustifiedLastLineIsNotSuppressedByTheProxysNativeFallback()
    {
        var section = new Section { Margin = new(0) }; section.Blocks.Add(P("甲乙丙丁")); var doc = Doc(section);
        Css.SetStyle(section, TwoColumns + "text-align:justify-all;"); using var body = Body(doc);
        Assert.Equal(100, TextBounds(body, 0).Single().Width, 2);
    }

    [Fact]
    public void ClearingNativeColumnValuesEqualToTheirDefaultsRestoresCssPrecedence()
    {
        var doc = Doc(P(Lines(6))); Css.SetStyle(doc, TwoColumns + "column-rule:4px solid red;");
        doc.ColumnGap = 10; doc.ColumnRuleWidth = 0; using var body = Body(doc);
        Assert.Equal(115, TextBounds(body, 0)[3].X, 2); Assert.Equal(Colors.White, Pixel(body, 110, 10));
        doc.ClearValue(FlowDocument.ColumnGapProperty); doc.ClearValue(FlowDocument.ColumnRuleWidthProperty); Layout(body);
        Assert.Equal(120, TextBounds(body, 0)[3].X, 2); Assert.Equal(Colors.Red, Pixel(body, 110, 10));
    }

    [Fact]
    public void AForcedPageAtTheStartOfAnInnerContextPropagatesPastTheOuterColumns()
    {
        var source = P("原文"); var section = new Section { Margin = new(0) }; section.Blocks.Add(source); var doc = Doc(P("序"), section); doc.PageWidth = 460;
        Css.SetStyle(doc, TwoColumns); Css.SetStyle(section, TwoColumns); Css.SetStyle(source, "break-before:page;");
        Assert.Equal(2, doc.ViewerPaginator.PageCount);
        Assert.Equal(2, ((IFlowDocumentPageMetrics)doc.ViewerPaginator).GetPageNumberForOffset(doc.GetDocumentOffset(source, 0)));
    }

    [Fact]
    public void InnerRtlColumnsDoNotReverseTheOuterLtrColumnOrder()
    {
        var source = P(Lines(12)); var section = new Section { Margin = new(0) }; section.Blocks.Add(source); var doc = Doc(section); doc.PageWidth = 460;
        Css.SetStyle(doc, TwoColumns); Css.SetStyle(section, TwoColumns + "direction:rtl;text-align:start;"); using var body = Body(doc);
        var boxes = TextBounds(body, 0);
        Assert.True(Math.Abs(boxes[0].Right - 220) < .01, $"boxes={string.Join(';', boxes)}; source={source.FlowDirection}/{source.GetValue(CssFlowProperties.TextAlignProperty)}/{source.GetValueSourceInternal(CssFlowProperties.TextAlignProperty).BaseValueSource}; proxy={body.Paragraphs[0].FlowDirection}/{body.Paragraphs[0].GetValue(CssFlowProperties.TextAlignProperty)}/{body.Paragraphs[0].GetValueSourceInternal(CssFlowProperties.TextAlignProperty).BaseValueSource}");
        Assert.Equal(100, boxes[3].Right, 2); Assert.Equal(460, boxes[6].Right, 2);
    }

    [Fact]
    public void RemovingAnInnerContextRestoresItsTextWithoutDisturbingTheOuterColumns()
    {
        var source = P(Lines(12)); var run = source.Inlines[0]; var section = new Section { Margin = new(0) }; section.Blocks.Add(source); var doc = Doc(section); doc.PageWidth = 460;
        Css.SetStyle(doc, TwoColumns); Css.SetStyle(section, TwoColumns); using var body = Body(doc); var original = TextBounds(body, 0).ToArray();
        Css.SetStyle(section, "columns:auto;"); Layout(body); Assert.Equal(2, TextBounds(body, 0).Select(rect => rect.X).Distinct().Count()); Assert.Same(run, body.Paragraphs[0].Inlines[0]);
        Css.SetStyle(section, TwoColumns); Layout(body); Assert.Equal(original, TextBounds(body, 0));
        Css.SetStyle(doc, "columns:auto;"); Css.SetStyle(section, "columns:auto;"); Layout(body); Assert.Single(TextBounds(body, 0).Select(rect => rect.X).Distinct());
        Assert.True(body.ActualHeight > 280); Assert.Same(source, run.Parent);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TableCellsKeepTheirOwnColumnsRulesAndSourceParentsWithEitherBorderModel(bool collapse)
    {
        var row = new TableRow(); var group = new TableRowGroup(); group.Rows.Add(row); var table = new Table { CellSpacing = 0 }; table.RowGroups.Add(group);
        var left = new TableCell(P(Lines(6))) { Padding = new(0) }; var right = new TableCell(P(Lines(6))) { Padding = new(0) }; row.Cells.Add(left); row.Cells.Add(right);
        var doc = Doc(table); doc.PageWidth = 460;
        Css.SetStyleSheet(doc, "FlowDocument{white-space:pre-wrap;}Table{width:100%;table-layout:fixed;border:2px solid blue;" + (collapse ? "border-collapse:collapse;" : "") + "}TableCell{columns:2;column-gap:20px;column-rule:4px solid red;border:2px solid blue;padding:0;}");
        using var body = Body(doc); Assert.Equal(2, body.Paragraphs.Count);
        Assert.All(body.Paragraphs, paragraph => Assert.Equal(2, paragraph.DocumentTextBounds(0, paragraph.Text.Length).Select(rect => rect.X).Distinct().Count()));
        Assert.Equal(Colors.Red, Pixel(body, 115, 10)); Assert.Equal(Colors.Red, Pixel(body, 345, 10));
        Assert.Same(row, left.Parent); Assert.Same(row, right.Parent); Assert.InRange(body.ActualHeight, 72, 82);
    }

    [Fact]
    public void RichInlineImagesAndProviderSelectionsFollowEveryComposedColumn()
    {
        var source = P(Lines(12)); var run = (Run)source.Inlines[0]; var section = new Section { Margin = new(0) }; section.Blocks.Add(source); var doc = Doc(section); doc.PageWidth = 460;
        Css.SetStyle(doc, TwoColumns); Css.SetStyle(section, TwoColumns); Css.SetStyle(run, "border-image:image(blue) 0 fill / 0;"); using var body = Body(doc);
        foreach (var x in new[] { 1, 121, 241, 361 }) Assert.Equal(Colors.Blue, Pixel(body, x, 1));
        Assert.Equal(Colors.White, Pixel(body, 110, 1));
        var provider = Assert.IsAssignableFrom<ITextProvider>(body.GetAutomationPeer()!.GetPattern(PatternInterface.Text));
        var range = provider.DocumentRange.FindText("第06行", false, false)!; range.Select(); Assert.Equal("第06行", body.Selection!.Text);
        var boxes = range.GetBoundingRectangles(); Assert.Equal(4, boxes.Length); Assert.Equal(240, boxes[0], 2);
        Assert.Same(run, source.Inlines[0]);
    }

    private static Paragraph P(string text) => new(new Run(text)) { Margin = new(0) };
    private static FlowDocument Doc(params Block[] blocks)
    { var doc = new FlowDocument { FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24, Foreground = Brushes.Black, Background = Brushes.White, PagePadding = new(0), PageWidth = 220, PageHeight = 72 }; foreach (var block in blocks) doc.Blocks.Add(block); Css.SetStyleSheet(doc, "FlowDocument{white-space:pre-wrap;}"); return doc; }
    private static FlowDocumentPresentation Body(FlowDocument doc) { var body = new FlowDocumentPresentation(); body.SetDocument(doc); Layout(body); return body; }
    private static void Layout(FlowDocumentPresentation body)
    { for (var i = 0; i < 3; i++) { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); body.Measure(new(body.Document!.PageWidth, double.PositiveInfinity)); body.Arrange(new(0, 0, body.Document.PageWidth, Math.Max(1, body.DesiredSize.Height))); } }
    private static IReadOnlyList<Rect> TextBounds(FlowDocumentPresentation body, int index, int start = 0, int length = -1)
    { var text = body.Paragraphs[index]; var at = text.TransformToAncestor(body); return text.DocumentTextBounds(start, length < 0 ? text.Text.Length : length).Select(rect => new Rect(at.X + rect.X, at.Y + rect.Y, rect.Width, rect.Height)).ToArray(); }
    private static Color Pixel(FrameworkElement root, int x, int y)
    {
        var width = Math.Max(1, (int)Math.Ceiling(root.ActualWidth)); var height = Math.Max(1, (int)Math.Ceiling(root.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Bgra32); var drawing = new SoftwareDrawingContext(bitmap);
        drawing.DrawRectangle(Brushes.White, null, new(0, 0, width, height)); root.Render(drawing); drawing.Close(); var pixels = new byte[width * height * 4]; bitmap.CopyPixels(pixels, width * 4, 0);
        var at = (y * width + x) * 4; return Color.FromArgb(pixels[at + 3], pixels[at + 2], pixels[at + 1], pixels[at]);
    }
}
