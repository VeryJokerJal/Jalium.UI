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
public sealed class CssDocumentTableWidthTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void IntrinsicCellGrowthResizesAndCentersTheWholeFrame(bool collapse, bool rtl)
    {
        var (doc, table, source) = Example(140, "width:100px;margin:0 auto;", collapse); doc.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        using var body = Body(doc); var width = collapse ? 148 : 168; var left = (400 - width) / 2;
        Assert.Equal(130, body.Paragraphs[0].TransformToAncestor(body).X, 2); Assert.Equal(140, body.Paragraphs[0].ActualWidth, 2);
        Assert.Equal(Colors.Blue, Pixel(body, left + width - 1, 8)); Assert.Equal(Colors.White, Pixel(body, left - 1, 8));
        Assert.Same(source.Inlines, body.Paragraphs[0].Inlines); Assert.Same(table.RowGroups[0].Rows[0].Cells[0], source.Parent);
    }

    [Theory]
    [InlineData("width:auto;", 108)]
    [InlineData("width:auto;min-width:200px;", 200)]
    [InlineData("width:auto;max-width:100px;", 108)]
    [InlineData("width:auto;table-layout:fixed;", 108)]
    public void CssAutoWidthUsesIntrinsicSizeAndKeepsTheTableMinimum(string style, int width)
    {
        var (doc, _, _) = Example(80, style + "margin:0 auto;"); using var body = Body(doc);
        Assert.Equal(width - 28, body.TableCells.Single().Bounds.Width, 2);
        Assert.Equal(Colors.Blue, Pixel(body, (400 - width) / 2 + width - 1, 8));
    }

    [Theory]
    [InlineData(false, 14)] [InlineData(true, -114)]
    public void AnOverflowingIntrinsicWidthUsesTheContainingDirection(bool rtl, double x)
    {
        var (doc, _, _) = Example(500, "width:100px;margin:0;"); doc.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        using var body = Body(doc); Assert.Equal(x, body.Paragraphs[0].TransformToAncestor(body).X, 2); Assert.Equal(500, body.TableCells[0].Bounds.Width, 2);
    }

    [Theory]
    [InlineData(false, 140)] [InlineData(true, 180)]
    public void CollapsingAColumnRecentersAutoMarginsWithoutReflowingItsSource(bool rtl, double x)
    {
        var row = new TableRow(); for (var i = 0; i < 3; i++) row.Cells.Add(Cell(P("原文")));
        var table = Table(row); foreach (var width in new[] { 80, 60, 40 }) table.Columns.Add(new TableColumn { Width = new(width) });
        Css.SetStyle(table, "width:208px;margin:0 auto;padding:10px;border:4px solid blue;table-layout:fixed;" + (rtl ? "direction:rtl;" : "")); var doc = Doc(table);
        using var body = Body(doc); var first = body.Paragraphs[0].ActualWidth;
        Css.SetStyle(table.Columns[1], "visibility:collapse;"); Layout(body);
        Assert.Equal(x, body.Paragraphs[0].TransformToAncestor(body).X, 2); Assert.Equal(first, body.Paragraphs[0].ActualWidth, 2);
        Assert.Equal(Colors.Blue, Pixel(body, 273, 8)); Assert.Equal(Colors.White, Pixel(body, 125, 8));
        Css.SetStyle(table.Columns[1], ""); Layout(body); Assert.Equal(Colors.Blue, Pixel(body, 303, 8));
    }

    [Fact]
    public void EmptyAutoTablesKeepTheirExplicitMinimumFrame()
    {
        var table = Table(); Css.SetStyle(table, "width:auto;min-width:80px;height:40px;margin:0 auto;padding:10px;border:4px solid blue;"); using var body = Body(Doc(table));
        Assert.Empty(body.Paragraphs); Assert.Equal(40, body.ActualHeight, 2); Assert.Equal(Colors.Blue, Pixel(body, 239, 8)); Assert.Equal(Colors.White, Pixel(body, 159, 8));
    }

    [Fact]
    public void NativeTablesWithoutACssWidthRetainTheirStretchAllocation()
    {
        var (doc, _, _) = Example(80, ""); using var body = Body(doc); Assert.Equal(372, body.TableCells[0].Bounds.Width, 2);
    }

    [Fact]
    public void ResizingAndRemovingAutoWidthRestoresTheSameOriginalRanges()
    {
        var (doc, table, source) = Example(140, "width:auto;margin:0 auto;"); var run = source.Inlines[0]; using var body = Body(doc);
        var original = body.Paragraphs[0].TransformToAncestor(body).X;
        doc.PageWidth = 300; Layout(body); Assert.Equal(original - 50, body.Paragraphs[0].TransformToAncestor(body).X, 2);
        doc.PageWidth = 400; Css.SetStyle(table, "padding:10px;border:4px solid blue;"); Layout(body); Assert.Equal(14, body.Paragraphs[0].TransformToAncestor(body).X, 2);
        Css.SetStyle(table, "width:auto;margin:0 auto;padding:10px;border:4px solid blue;"); Layout(body); Assert.Equal(original, body.Paragraphs[0].TransformToAncestor(body).X, 2); Assert.Same(run, body.Paragraphs[0].Inlines[0]);
    }

    [Fact]
    public void ProviderSelectionAndPointLookupFollowTheExpandedCenteredFrame()
    {
        var (doc, _, source) = Example(140, "width:100px;margin:0 auto;"); using var body = Body(doc);
        var provider = (ITextProvider)body.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!; var range = provider.DocumentRange.FindText("原文", false, false)!; range.Select();
        var bounds = range.GetBoundingRectangles(); Assert.Equal(4, bounds.Length); Assert.Equal(130, bounds[0], 2); Assert.Equal("原文", body.Selection!.Text);
        Assert.Equal(doc.GetDocumentOffset(source, 0), body.OffsetFromPoint(new(bounds[0] + .1, bounds[1] + bounds[3] / 2)));
    }

    [Theory]
    [InlineData("width:100px;")] [InlineData("width:auto;")]
    public void PercentageCellPaddingUsesTheExpandedUncontractedGrid(string style)
    {
        var (doc, table, _) = Example(140, style + "margin:0 auto;"); var cell = table.RowGroups[0].Rows[0].Cells[0]; cell.ClearValue(TableCell.PaddingProperty); Css.SetStyle(cell, "padding:10%;");
        using var body = Body(doc); var box = body.TableCells[0].Bounds;
        Assert.Equal(box.X + box.Width * .1, body.Paragraphs[0].TransformToAncestor(body).X, 2);
        var at = body.Paragraphs[0].TransformToAncestor(body); Assert.Equal(box.Y + box.Width * .1, at.Y, 2);
    }

    [Fact]
    public void ACenteredExpandedFrameKeepsTheSameSourceGeometryOnLaterPages()
    {
        var source = P(string.Join('\n', Enumerable.Repeat("原文", 10))); Css.SetStyle(source, "width:140px;"); var row = new TableRow(); row.Cells.Add(Cell(source)); var table = Table(row);
        Css.SetStyle(table, "width:100px;margin:0 auto;padding:10px;border:4px solid blue;"); var doc = Doc(table); doc.PageHeight = 72;
        Assert.True(doc.ViewerPaginator.PageCount > 2);
        using var page = Assert.IsType<FlowDocumentPresentation>(doc.ViewerPaginator.GetPage(1).Visual);
        var provider = (ITextProvider)page.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
        var visible = provider.GetVisibleRanges().SelectMany(range => range.GetBoundingRectangles().Chunk(4)).ToArray(); Assert.NotEmpty(visible);
        Assert.All(visible, box => { Assert.Equal(130, box[0], 2); Assert.True(box[1] >= 0 && box[1] + box[3] <= 72); });
        Assert.Same(source.Inlines, page.Paragraphs[0].Inlines);
    }

    private static (FlowDocument Doc, Table Table, Paragraph Source) Example(double width, string style, bool collapse = false)
    { var source = P("原文"); Css.SetStyle(source, "width:" + width + "px;"); var table = Table(new TableRow()); table.RowGroups[0].Rows[0].Cells.Add(Cell(source)); Css.SetStyle(table, "padding:10px;border:4px solid blue;" + (collapse ? "border-collapse:collapse;" : "") + style); return (Doc(table), table, source); }
    private static Paragraph P(string text) => new(new Run(text));
    private static TableCell Cell(Paragraph p) => new(p) { Padding = new(0), BorderThickness = new(0) };
    private static Table Table(params TableRow[] rows) { var t = new Table { CellSpacing = 0 }; var g = new TableRowGroup(); foreach (var row in rows) g.Rows.Add(row); t.RowGroups.Add(g); return t; }
    private static FlowDocument Doc(params Block[] blocks)
    { var doc = new FlowDocument { FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24, Foreground = Brushes.Black, Background = Brushes.White, PagePadding = new(0), PageWidth = 400, PageHeight = 168 }; foreach (var b in blocks) doc.Blocks.Add(b); Css.SetStyleSheet(doc, "FlowDocument{white-space:pre-wrap;text-align:start;}Paragraph{margin:0;}"); return doc; }
    private static FlowDocumentPresentation Body(FlowDocument doc) { var b = new FlowDocumentPresentation(); b.SetDocument(doc); Layout(b); return b; }
    private static void Layout(FlowDocumentPresentation body) { for (var i = 0; i < 4; i++) { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); body.Measure(new(body.Document!.PageWidth, double.PositiveInfinity)); body.Arrange(new(0, 0, body.Document.PageWidth, body.DesiredSize.Height)); } }
    private static Color Pixel(FrameworkElement root, int x, int y) { var w = (int)Math.Ceiling(root.ActualWidth); var h = (int)Math.Ceiling(root.ActualHeight); var bitmap = new RenderTargetBitmap(w, Math.Max(1, h), 96, 96, PixelFormats.Bgra32); bitmap.Clear(Colors.White); bitmap.Render(root); var data = new byte[w * Math.Max(1, h) * 4]; bitmap.CopyPixels(data, w * 4, 0); var at = (y * w + x) * 4; return Color.FromArgb(data[at + 3], data[at + 2], data[at + 1], data[at]); }
}
