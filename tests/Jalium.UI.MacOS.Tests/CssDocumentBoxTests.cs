using Jalium.UI.Automation.Peers;
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
public sealed class CssDocumentBoxTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    [Fact]
    public void NativeDefaultSpacingIsOverridableAndReturnsAfterCssRemoval()
    {
        var paragraph = P("原文"); var list = new DocList(); var item = new ListItem(paragraph); list.ListItems.Add(item);
        Assert.Equal(new Thickness(0, 0, 0, 10), paragraph.Margin); Assert.Equal(paragraph.Margin, list.Margin);
        Assert.Same(DependencyProperty.UnsetValue, paragraph.ReadLocalValue(Block.MarginProperty));
        Assert.Same(DependencyProperty.UnsetValue, list.ReadLocalValue(Block.MarginProperty));
        var doc = Doc(list); using var body = Body(doc); Assert.Equal(new Thickness(0), paragraph.Margin); Assert.Equal(new Thickness(0), list.Margin);
        Css.SetStyleSheet(doc, "FlowDocument{white-space:pre-wrap;}"); Layout(body);
        Assert.Equal(new Thickness(0, 0, 0, 10), paragraph.Margin); Assert.Equal(paragraph.Margin, list.Margin);
    }

    [Theory]
    [InlineData("margin:5%;padding:2.5%;")]
    [InlineData("margin-block:5%;margin-inline:5%;padding-block:2.5%;padding-inline:2.5%;")]
    [InlineData("margin:calc(2.5% + 10px);padding:calc(1.25% + 5px);")]
    public void AllRelativeEdgesResolveAgainstTheContainingWidth(string style)
    {
        var source = P("原文"); var doc = Doc(source); Css.SetStyle(source, style); using var body = Body(doc);
        var text = body.Paragraphs[0]; var at = text.TransformToAncestor(body);
        Assert.Equal(30, at.X, 2); Assert.Equal(30, at.Y, 2); Assert.Equal(340, text.ActualWidth, 2);
        Assert.Equal(text.ActualHeight + 60, body.ActualHeight, 2); Assert.Same(source.Inlines, text.Inlines);
    }

    [Theory]
    [InlineData("border-box", 176)] [InlineData("content-box", 200)]
    public void SpecifiedWidthsAccountForTheSelectedBoxSizing(string sizing, double width)
    {
        var source = P("原文"); Css.SetStyle(source, $"width:50%;box-sizing:{sizing};padding:10px;border:2px solid blue;");
        using var body = Body(Doc(source)); Assert.Equal(width, body.Paragraphs[0].ActualWidth, 2);
        Assert.Equal(12, body.Paragraphs[0].TransformToAncestor(body).X, 2);
    }

    [Theory]
    [InlineData("width:80%;max-width:150px;", 150)]
    [InlineData("width:20px;min-width:25%;", 100)]
    [InlineData("width:80%;min-width:60%;max-width:40%;", 240)]
    [InlineData("width:auto;max-width:50%;", 200)]
    [InlineData("inline-size:50%;min-inline-size:60%;max-inline-size:40%;", 240)]
    public void MinimumWidthsWinWhenMaximumsConflictAndAutoWidthsAreConstrained(string style, double expected)
    {
        var source = P("原文"); Css.SetStyle(source, style); using var body = Body(Doc(source)); Assert.Equal(expected, body.Paragraphs[0].ActualWidth, 2);
    }

    [Theory]
    [InlineData("width:50%;margin-inline:auto;", 100)]
    [InlineData("width:50%;margin-left:auto;margin-right:10%;", 160)]
    [InlineData("width:50%;margin-left:10%;margin-right:auto;", 40)]
    [InlineData("width:auto;max-width:50%;margin-inline:auto;", 100)]
    public void AutoMarginsUseTheRemainingSpaceAfterWidthConstraints(string style, double expected)
    {
        var source = P("原文"); Css.SetStyle(source, style); using var body = Body(Doc(source)); Assert.Equal(expected, body.Paragraphs[0].TransformToAncestor(body).X, 2);
    }

    [Theory]
    [InlineData(false, false, "width:80%;margin:0 20%;", 80)]
    [InlineData(true, false, "width:80%;margin:0 20%;", 0)]
    [InlineData(false, true, "width:50%;margin:0;", 0)]
    [InlineData(true, false, "width:120%;margin:0;", -80)]
    public void TheContainingDirectionControlsOverconstrainedMargins(bool parentRtl, bool childRtl, string style, double expected)
    {
        var source = P("原文"); var doc = Doc(source); doc.FlowDirection = parentRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Css.SetStyle(source, style + "direction:" + (childRtl ? "rtl" : "ltr") + ";"); using var body = Body(doc);
        Assert.Equal(expected, body.Paragraphs[0].TransformToAncestor(body).X, 2);
    }

    [Fact]
    public void NativeEdgesAndDefaultValuedOverridesCanBeClearedOnALivePresentation()
    {
        var source = P("原文"); source.Margin = new(0); source.Padding = new(0); var doc = Doc(source);
        Css.SetStyle(source, "margin:5%;padding:2.5%;"); using var body = Body(doc); Assert.Equal(0, body.Paragraphs[0].TransformToAncestor(body).X, 2);
        source.ClearValue(Block.MarginProperty); source.ClearValue(Block.PaddingProperty); Layout(body);
        Assert.Equal(30, body.Paragraphs[0].TransformToAncestor(body).X, 2);
        source.Margin = new(7); source.Padding = new(3); Layout(body); Assert.Equal(10, body.Paragraphs[0].TransformToAncestor(body).X, 2);
    }

    [Fact]
    public void LayoutOnlyStyleChangesUpdateBothConsumersAndTheCachedPaginator()
    {
        var source = P(string.Join('\n', Enumerable.Repeat("原文", 8))); var doc = Doc(source);
        Css.SetStyle(source, "width:50%;margin:2%;padding:2%;"); using var one = Body(doc); using var two = Body(doc);
        var pages = doc.ViewerPaginator.PageCount; var before = one.Paragraphs[0].ActualWidth;
        Css.SetStyle(source, "width:25%;margin:10%;padding:10%;"); Layout(one); Layout(two);
        Assert.True(one.Paragraphs[0].ActualWidth < before); Assert.Equal(80, one.Paragraphs[0].TransformToAncestor(one).X, 2);
        Assert.Equal(one.Paragraphs[0].ActualWidth, two.Paragraphs[0].ActualWidth, 2);
        Assert.True(doc.ViewerPaginator.PageCount > pages);
        Css.SetStyle(source, ""); Layout(one); Layout(two); Assert.Equal(400, one.Paragraphs[0].ActualWidth, 2);
    }

    [Fact]
    public void NativeAttachedWidthOverridesPercentageAndClearingAutoRevealsCssAgain()
    {
        var source = P("原文"); var doc = Doc(source); Css.SetStyle(source, "width:50%;"); source.SetValue(FrameworkElement.WidthProperty, 180d);
        using var body = Body(doc); Assert.Equal(180, body.Paragraphs[0].ActualWidth, 2);
        source.SetValue(FrameworkElement.WidthProperty, double.NaN); Layout(body); Assert.Equal(400, body.Paragraphs[0].ActualWidth, 2);
        source.ClearValue(FrameworkElement.WidthProperty); Layout(body); Assert.Equal(200, body.Paragraphs[0].ActualWidth, 2);
    }

    [Fact]
    public void RelativeInsetsUseTheCurrentIndependentColumnWidthAndRestoreTheOriginalFlow()
    {
        var source = P("甲\n乙\n丙\n丁\n戊\n己"); var run = source.Inlines[0]; var section = new Section(source); var doc = Doc(section);
        Css.SetStyle(section, "width:60%;padding:5%;columns:2;column-gap:20px;"); Css.SetStyle(source, "margin:10%;padding:5%;");
        using var body = Body(doc); var xs = Bounds(body, 0).Select(rect => Math.Round(rect.X, 2)).Distinct().Order().ToArray();
        Assert.Equal(new[] { 33.5, 143.5 }, xs); Assert.Equal(173, body.Paragraphs[0].ActualWidth, 2);
        Css.SetStyle(section, "width:60%;padding:5%;"); Layout(body); Assert.Equal(50, body.Paragraphs[0].TransformToAncestor(body).X, 2); Assert.Equal(140, body.Paragraphs[0].ActualWidth, 2);
        Assert.Same(run, body.Paragraphs[0].Inlines[0]); Assert.Same(section, source.Parent);
    }

    [Theory]
    [InlineData(false, 1, 123.8, 152.4)] [InlineData(true, 1, 104.54, 190.92)]
    [InlineData(false, 2, 91.8, 56.4)] [InlineData(true, 2, 64.64, 71.22)]
    public void TableCellsAndTheirBlocksUseTheirOwnResolvedContainingWidths(bool collapsed, int count, double left, double width)
    {
        var table = new Table { CellSpacing = 0 }; var row = new TableRow(); var group = new TableRowGroup(); group.Rows.Add(row); table.RowGroups.Add(group);
        for (var i = 0; i < count; i++)
        { var p = P("原文"); Css.SetStyle(p, "margin:10%;padding:10%;"); var cell = new TableCell(p); Css.SetStyle(cell, "padding:10%;vertical-align:top;"); row.Cells.Add(cell); }
        Css.SetStyle(table, "width:100%;padding:10%;table-layout:fixed;" + (collapsed ? "border-collapse:collapse;" : ""));
        using var body = Body(Doc(table)); Assert.Equal(left, body.Paragraphs[0].TransformToAncestor(body).X, 2); Assert.Equal(width, body.Paragraphs[0].ActualWidth, 2);
        Assert.Equal(count, body.TableCells.Count); Assert.Same(row, row.Cells[0].Parent); Assert.Equal(new Thickness(1), row.Cells[0].BorderThickness);
    }

    [Theory]
    [InlineData(false, 107)] [InlineData(true, 207)]
    public void ListItemFramesApplyRelativeEdgesAndTheInlineStartIndent(bool rtl, double left)
    {
        var source = P("原文"); var item = new ListItem(source); var list = new DocList(item); var doc = Doc(list); doc.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Css.SetStyle(list, "padding:10%;"); Css.SetStyle(item, "width:50%;margin:5%;padding:10%;border:2px solid red;background:blue;");
        using var body = Body(doc); Assert.Equal(left, body.Paragraphs[0].TransformToAncestor(body).X, 2); Assert.Equal(86, body.Paragraphs[0].ActualWidth, 2);
        var frameLeft = rtl ? 175 : 75; Assert.Equal(Colors.Red, Pixel(body, frameLeft, 55)); Assert.Equal(Colors.Blue, Pixel(body, frameLeft + 3, 58));
        Assert.Same(item, source.Parent); Assert.Same(list, item.Parent);
    }

    [Fact]
    public void ResolvedFramesPaintTheirBorderAndPaddingOutsideTheOriginalText()
    {
        var source = P("原文"); Css.SetStyle(source, "width:50%;box-sizing:content-box;margin:5%;padding:2.5%;border:2px solid red;background:blue;");
        using var body = Body(Doc(source)); Assert.Equal(Colors.Red, Pixel(body, 20, 20)); Assert.Equal(Colors.Blue, Pixel(body, 22, 22)); Assert.Equal(Colors.White, Pixel(body, 19, 19));
        Assert.Equal(32, body.Paragraphs[0].TransformToAncestor(body).X, 2);
    }

    [Theory]
    [InlineData("width:0;")] [InlineData("width:calc(10% - 100px);")]
    public void ZeroWidthAndClampedMathRetainVisibleOverflowAndSourceHitGeometry(string style)
    {
        var source = P("甲乙"); var doc = Doc(P("序"), source); Css.SetStyle(source, style + "margin-top:20px;"); using var body = Body(doc);
        Assert.Equal(0, body.Paragraphs[1].ActualWidth, 2); var bounds = Bounds(body, 1, 1, 1).Single();
        Assert.True(bounds.Width > 1); Assert.Equal(doc.GetDocumentOffset(source, 1), body.OffsetFromPoint(new(bounds.X + .1, bounds.Y + bounds.Height / 2)));
        var provider = (ITextProvider)body.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!; var range = provider.DocumentRange.FindText("乙", false, false)!;
        range.Select(); Assert.Equal("乙", body.Selection!.Text); Assert.Equal(4, range.GetBoundingRectangles().Length);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ASharedPaginationRefreshKeepsTheFoundOriginalRangeFullyVisible(bool rtlAndDpi)
    {
        var before = P(string.Join('\n', Enumerable.Range(0, 36).Select(i => $"第{i:00}行"))); var section = new Section(before);
        var tail = P("末段原文\n仍可选择"); var doc = Doc(section, tail);
        Css.SetStyle(section, "columns:2;column-gap:20px;"); Css.SetStyle(tail, "width:80%;margin:10% auto;padding:5%;");
        var scroll = new FlowDocumentScrollViewer { Document = doc, Height = 180, IsToolBarVisible = false, IsInactiveSelectionHighlightEnabled = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var root = new StackPanel(); root.Children.Add(scroll);
        void Arrange()
        {
            for (var i = 0; i < 4; i++) { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); root.Measure(new(600, 400)); root.Arrange(new(0, 0, 600, 400)); root.UpdateLayout(); }
        }
        Arrange(); if (rtlAndDpi) { Css.SetStyle(section, "columns:2;column-gap:20px;direction:rtl;"); VisualTreeHelper.SetRootDpi(root, new(2, 2)); Arrange(); }
        Assert.True(scroll.Find("末段")); Arrange();
        doc.NotifyPaginationStyleChanged(); Arrange();
        var body = Descendants(scroll).OfType<FlowDocumentPresentation>().Single();
        var provider = (ITextProvider)body.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
        var boxes = provider.DocumentRange.FindText("末段", false, false)!.GetBoundingRectangles();
        Assert.Equal(4, boxes.Length); Assert.Equal(24, boxes[3], 2); Assert.Equal("末段", scroll.Selection!.Text);
        scroll.Document = null;
    }

    private static IEnumerable<Visual> Descendants(Visual source)
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(source); i++) { if (VisualTreeHelper.GetChild(source, i) is Visual child) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } } }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TableDimensionsRespectNativeValuesAndClampRelativeMathAtZero(bool horizontal)
    {
        var cell = new TableCell(P("原文")); var row = new TableRow(); row.Cells.Add(cell); var group = new TableRowGroup(); group.Rows.Add(row); var table = new Table { CellSpacing = 0 }; table.RowGroups.Add(group); var doc = Doc(table);
        Css.SetStyle(cell, "width:50%;height:50%;"); using var body = Body(doc);
        var property = horizontal ? FrameworkElement.WidthProperty : FrameworkElement.HeightProperty;
        Assert.Equal(200, CssTableProperties.SizeOf(cell, horizontal, 400), 2);
        cell.SetValue(property, 120d); Assert.Equal(120, CssTableProperties.SizeOf(cell, horizontal, 400), 2);
        cell.SetValue(property, double.NaN); Assert.True(double.IsNaN(CssTableProperties.SizeOf(cell, horizontal, 400)));
        cell.ClearValue(property); Assert.Equal(200, CssTableProperties.SizeOf(cell, horizontal, 400), 2);
        Css.SetStyle(cell, "width:calc(10% - 100px);height:calc(10% - 100px);"); Layout(body); Assert.Equal(0, CssTableProperties.SizeOf(cell, horizontal, 400), 2);
    }

    [Fact]
    public void AListItemsSpecifiedWidthContributesToItsAutoTableCellMinimum()
    {
        var source = P("原文"); Css.SetStyle(source, "width:80px;"); var item = new ListItem(source); Css.SetStyle(item, "width:120px;padding:10px;border:1px solid blue;");
        var list = new DocList(item); var cell = new TableCell(list) { Padding = new(0), BorderThickness = new(0) };
        var row = new TableRow(); row.Cells.Add(cell); var group = new TableRowGroup(); group.Rows.Add(row); var table = new Table { CellSpacing = 0 }; table.RowGroups.Add(group);
        Css.SetStyle(table, "width:100px;table-layout:auto;"); using var body = Body(Doc(table));
        Assert.Equal(140, body.TableCells.Single().Bounds.Width, 2); Assert.Equal(80, body.Paragraphs[0].ActualWidth, 2); Assert.Same(item, source.Parent);
    }

    private static Paragraph P(string text) => new(new Run(text));
    private static FlowDocument Doc(params Block[] blocks)
    { var doc = new FlowDocument { FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24, Foreground = Brushes.Black, Background = Brushes.White, PagePadding = new(0), PageWidth = 400, PageHeight = 168 }; foreach (var b in blocks) doc.Blocks.Add(b); Css.SetStyleSheet(doc, "FlowDocument{white-space:pre-wrap;text-align:start;}Paragraph,List{margin:0;}"); return doc; }
    private static FlowDocumentPresentation Body(FlowDocument doc)
    { var body = new FlowDocumentPresentation(); body.SetDocument(doc); Layout(body); return body; }
    private static void Layout(FlowDocumentPresentation body)
    { for (var i = 0; i < 4; i++) { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); body.Measure(new(body.Document!.PageWidth, double.PositiveInfinity)); body.Arrange(new(0, 0, body.Document.PageWidth, body.DesiredSize.Height)); } }
    private static IReadOnlyList<Rect> Bounds(FlowDocumentPresentation body, int index, int start = 0, int length = -1)
    { var text = body.Paragraphs[index]; var at = text.TransformToAncestor(body); return text.DocumentTextBounds(start, length < 0 ? text.Text.Length : length).Select(rect => new Rect(at.X + rect.X, at.Y + rect.Y, rect.Width, rect.Height)).ToArray(); }
    private static Color Pixel(FrameworkElement root, int x, int y)
    { var w = (int)Math.Ceiling(root.ActualWidth); var h = (int)Math.Ceiling(root.ActualHeight); var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Bgra32); bitmap.Clear(Colors.White); bitmap.Render(root); var bytes = new byte[w * h * 4]; bitmap.CopyPixels(bytes, w * 4, 0); var at = (y * w + x) * 4; return Color.FromArgb(bytes[at + 3], bytes[at + 2], bytes[at + 1], bytes[at]); }
}
