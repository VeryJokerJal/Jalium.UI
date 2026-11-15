using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssDocumentCellHeightTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData("height:200px;", "", true)]
    [InlineData("height:50%;", "", true)]
    [InlineData("", "height:200px;", true)]
    [InlineData("", "height:0;", true)]
    [InlineData("", "height:50%;", false)]
    [InlineData("", "", false)]
    [InlineData("height:auto;", "min-height:200px;", false)]
    public void OnlyExplicitCellOrTableHeightsEnablePercentageContent(string tableStyle, string cellStyle, bool definite)
    {
        var first = P("原文"); var cell = Cell(first, P("后文")); var row = Row(cell); var table = Table(row); var doc = Doc(table);
        Css.SetStyle(table, tableStyle); Css.SetStyle(cell, cellStyle); Css.SetStyle(row, "height:200px;"); Css.SetStyle(first, "height:50%;");
        using var body = Body(doc); Assert.Equal(definite ? 100 : body.Paragraphs[0].ActualHeight, Y(body, 1), 2); Assert.Equal(200, body.TableCells.Single().Bounds.Height, 2);
    }

    [Theory]
    [InlineData("border-box", 200, 100)] [InlineData("content-box", 224, 112)]
    public void CellPercentageBasisUsesTheAllocatedContentBox(string sizing, double rowHeight, double after)
    {
        var first = P("原文"); var cell = Cell(first, P("后文")); cell.ClearValue(TableCell.PaddingProperty); cell.ClearValue(TableCell.BorderThicknessProperty);
        Css.SetStyle(cell, $"height:200px;padding:10px;border:2px solid blue;box-sizing:{sizing};"); Css.SetStyle(first, "height:50%;");
        using var body = Body(Doc(Table(Row(cell)))); Assert.Equal(rowHeight, body.TableCells.Single().Bounds.Height, 2); Assert.Equal(after, Y(body, 1), 2);
    }

    [Theory]
    [InlineData("height:calc(50% - 4px);", 96)]
    [InlineData("height:90%;max-height:50%;", 100)]
    [InlineData("height:auto;min-height:50%;max-height:25%;", 100)]
    [InlineData("block-size:50%;min-block-size:60%;max-block-size:40%;", 120)]
    public void ChildHeightAndMinMaxResolveAfterRowAllocation(string style, double after)
    {
        var first = P("原文"); var cell = Cell(first, P("后文")); var table = Table(Row(cell)); Css.SetStyle(table, "height:200px;"); Css.SetStyle(first, style);
        using var body = Body(Doc(table)); Assert.Equal(after, Y(body, 1), 2); Assert.Equal(200, body.TableCells.Single().Bounds.Height, 2);
    }

    [Theory]
    [InlineData("top", 0d)] [InlineData("middle", .5)] [InlineData("bottom", 1d)]
    public void VerticalAlignmentUsesTheRelayoutContentHeight(string alignment, double fraction)
    {
        var first = P("原文"); var cell = Cell(first, P("后文")); var table = Table(Row(cell)); Css.SetStyle(table, "height:200px;"); Css.SetStyle(cell, "vertical-align:" + alignment + ";"); Css.SetStyle(first, "height:50%;");
        using var body = Body(Doc(table)); var slack = 200 - 100 - body.Paragraphs[1].ActualHeight;
        Assert.Equal(slack * fraction, Y(body, 0), 2); Assert.Equal(slack * fraction + 100, Y(body, 1), 2);
    }

    [Fact]
    public void PercentageOverflowDoesNotFeedBackIntoRowMinimaOrLoseOriginalText()
    {
        var first = P("原文"); var last = P("单元格末段"); var cell = Cell(first, last); var outside = P("表格后文"); var table = Table(Row(cell)); var doc = Doc(table, outside);
        Css.SetStyle(table, "height:200px;"); Css.SetStyle(first, "height:150%;"); using var body = Body(doc);
        for (var index = 0; index < 4; index++)
        { Layout(body); Assert.Equal(200, body.TableCells.Single().Bounds.Height, 2); Assert.Equal(300, Y(body, 1), 2); Assert.Equal(200, Y(body, 2), 2); }
        Assert.True(body.ExtentHeight >= Y(body, 1) + body.Paragraphs[1].ActualHeight); var range = Provider(body).DocumentRange.FindText("单元格末段", false, false)!; range.Select(); Assert.Equal("单元格末段", body.Selection!.Text);
        var box = Rects(range).Single(); var offset = doc.GetDocumentOffset(last, 0); Assert.InRange(body.OffsetFromPoint(new(box.X + 1, box.Y + 12)), offset, offset + 1); Assert.Same(cell, last.Parent);
    }

    [Fact]
    public void NativeCellAndChildAutoOverridesAndClearRefreshBothViewsAndCachedPages()
    {
        var first = P("原文"); var cell = Cell(first, P("后文")); var row = Row(cell); var table = Table(row); Css.SetStyle(row, "height:200px;"); Css.SetStyle(cell, "height:200px;"); Css.SetStyle(first, "height:50%;"); var doc = Doc(table);
        using var one = Body(doc); using var two = Body(doc); var paginator = doc.ViewerPaginator; _ = paginator.PageCount;
        void Check(double after) { Layout(one); Layout(two); Assert.Equal(after, Y(one, 1), 2); Assert.Equal(after, Y(two, 1), 2); }
        Check(100); cell.SetValue(FrameworkElement.HeightProperty, double.NaN); Check(one.Paragraphs[0].ActualHeight);
        cell.ClearValue(FrameworkElement.HeightProperty); Check(100); first.SetValue(FrameworkElement.HeightProperty, double.NaN); Check(one.Paragraphs[0].ActualHeight);
        first.ClearValue(FrameworkElement.HeightProperty); Check(100); Css.SetStyle(first, "height:150%;"); Check(300); Assert.True(paginator.PageCount > 2);
        Css.SetStyle(first, "height:50%;"); Check(100); Assert.Same(cell, first.Parent);
    }

    [Fact]
    public void RowSpanUsesTheAllocatedSpanBeforeVisibilityProjectionAndRestoresAfterCollapse()
    {
        var first = P("原文"); var cell = Cell(first, P("跨行末段")); cell.RowSpan = 2; var firstRow = Row(cell, Cell(P("右上"))); var secondRow = Row(Cell(P("右下"))); var table = Table(firstRow, secondRow); var doc = Doc(table);
        Css.SetStyle(table, "height:200px;"); Css.SetStyle(firstRow, "height:100px;"); Css.SetStyle(secondRow, "height:100px;"); Css.SetStyle(first, "height:50%;"); using var body = Body(doc);
        Assert.Equal(100, Y(body, 1), 2); Assert.Equal(200, body.TableCells.Single(item => ReferenceEquals(item.Source, cell)).Bounds.Height, 2);
        Css.SetStyle(secondRow, "height:100px;visibility:collapse;"); Layout(body); Assert.Equal(100, body.TableCells.Single(item => ReferenceEquals(item.Source, cell)).Bounds.Height, 2);
        Assert.Empty(Rects(Provider(body).DocumentRange.FindText("跨行末段", false, false)!));
        Css.SetStyle(secondRow, "height:100px;"); Layout(body); Assert.Equal(100, Y(body, 1), 2); Assert.Single(Rects(Provider(body).DocumentRange.FindText("跨行末段", false, false)!));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PercentPaddingAndCollapsedBordersUseTheirActualInsets(bool collapsed)
    {
        var first = P("原文"); var cell = Cell(first, P("后文")); cell.ClearValue(TableCell.PaddingProperty); cell.ClearValue(TableCell.BorderThicknessProperty);
        Css.SetStyle(cell, "padding:5%;border:2px solid blue;"); Css.SetStyle(first, "height:50%;"); var table = Table(Row(cell)); Css.SetStyle(table, "height:200px;" + (collapsed ? "border-collapse:collapse;" : "")); using var body = Body(Doc(table));
        var frame = body.TableCells.Single().Bounds; var inset = body.Paragraphs[0].TransformToAncestor(body).Y - frame.Top;
        Assert.Equal(frame.Top + inset + (frame.Height - 2 * inset) / 2, Y(body, 1), 2); Assert.True(inset >= 20);
    }

    [Fact]
    public void NestedTablesResolveTheirOwnPercentageHeightFromTheFinalOuterCell()
    {
        var first = P("内表原文"); var inner = Table(Row(Cell(first, P("内表后文")))); var outerCell = Cell(inner, P("外格后文")); var outer = Table(Row(outerCell)); var doc = Doc(outer);
        Css.SetStyle(outer, "height:240px;"); Css.SetStyle(inner, "height:50%;"); Css.SetStyle(first, "height:50%;"); using var body = Body(doc);
        Assert.Equal(60, Y(body, 1), 2); Assert.Equal(120, Y(body, 2), 2); Assert.Equal(240, body.TableCells.Single(item => ReferenceEquals(item.Source, outerCell)).Bounds.Height, 2);
        Assert.Same(outerCell, inner.Parent); Assert.Equal(2, body.TableCells.Count);
    }

    [Fact]
    public void DefiniteSectionsInsideCellsSupplyTheirOwnPercentageChains()
    {
        var first = P("原文"); var section = new Section(first); section.Blocks.Add(P("章节后文")); var cell = Cell(section, P("单元格后文")); var table = Table(Row(cell));
        Css.SetStyle(table, "height:240px;"); Css.SetStyle(section, "height:50%;"); Css.SetStyle(first, "height:50%;"); using var body = Body(Doc(table));
        Assert.Equal(60, Y(body, 1), 2); Assert.Equal(120, Y(body, 2), 2); Assert.Same(section, first.Parent);
    }

    [Fact]
    public void PercentageColumnOwnersAreRebuiltWithoutRetainingFirstPassFragments()
    {
        var source = P(12); var section = new Section(source); var cell = Cell(section, P("末段")); var table = Table(Row(cell)); var doc = Doc(table);
        Css.SetStyle(table, "height:240px;"); Css.SetStyle(section, "height:50%;columns:2;column-gap:20px;"); using var body = Body(doc);
        var boxes = Rects(Provider(body).DocumentRange.FindText("第00行", false, false)!); Assert.Single(boxes); Assert.Equal(120, Y(body, 1), 2);
        Assert.Equal(12, Enumerable.Range(0, 12).Sum(index => Rects(Provider(body).DocumentRange.FindText($"第{index:00}行", false, false)!).Length));
        Assert.Equal(3, TextBounds(body, 0).Select(box => Math.Round(box.X, 2)).Distinct().Count());
        Css.SetStyle(section, "height:25%;columns:2;column-gap:20px;"); Layout(body); Assert.Equal(60, Y(body, 1), 2); Assert.Equal(6, TextBounds(body, 0).Select(box => Math.Round(box.X, 2)).Distinct().Count());
        Css.SetStyle(section, "height:50%;columns:2;column-gap:20px;"); Layout(body); Assert.Equal(120, Y(body, 1), 2); Assert.Same(source.Inlines, body.Paragraphs[0].Inlines);
    }

    [Theory]
    [InlineData(72)] [InlineData(144)]
    public void PaginatedCellPercentageSpaceKeepsAllOriginalSourceLines(int pageHeight)
    {
        var source = P(8); var cell = Cell(source, P("末段原文")); var table = Table(Row(cell)); var doc = Doc(table); doc.PageHeight = pageHeight;
        Css.SetStyle(table, "height:240px;"); Css.SetStyle(source, "height:100%;"); var seen = new HashSet<int>(); var footer = false;
        for (var index = 0; index < doc.ViewerPaginator.PageCount; index++)
        {
            using var page = (FlowDocumentPresentation)doc.ViewerPaginator.GetPage(index).Visual!;
            for (var line = 0; line < 8; line++)
            {
                var boxes = Rects(Provider(page).DocumentRange.FindText($"第{line:00}行", false, false)!); if (boxes.Length > 0) seen.Add(line);
                Assert.All(boxes, box => Assert.True(box.Y >= 0 && box.Bottom <= pageHeight + .02));
            }
            footer |= Rects(Provider(page).DocumentRange.FindText("末段原文", false, false)!).Length > 0;
        }
        Assert.Equal(8, seen.Count); Assert.True(footer); Assert.Same(cell, source.Parent);
    }

    [Fact]
    public void PercentageTableHeightUsesTheDefiniteParentBeforeItsCellRelayout()
    {
        var first = P("原文"); var table = Table(Row(Cell(first, P("后文")))); var section = new Section(table); var doc = Doc(section);
        Css.SetStyle(section, "height:400px;"); Css.SetStyle(table, "height:50%;"); Css.SetStyle(first, "height:50%;"); using var body = Body(doc);
        Assert.Equal(200, body.TableCells.Single().Bounds.Height, 2); Assert.Equal(100, Y(body, 1), 2);
    }

    [Fact]
    public void NativeTableHeightKeepsItsBorderBoxMeaningUnderContentBoxCss()
    {
        var first = P("原文"); var table = Table(Row(Cell(first, P("后文")))); var doc = Doc(table, P("表格后文"));
        Css.SetStyle(table, "height:200px;box-sizing:content-box;padding:10px;border:2px solid blue;"); Css.SetStyle(first, "height:50%;");
        table.SetValue(FrameworkElement.HeightProperty, 240d); using var body = Body(doc);
        Assert.Equal(216, body.TableCells.Single().Bounds.Height, 2); Assert.Equal(120, Y(body, 1), 2); Assert.Equal(240, Y(body, 2), 2);
        table.ClearValue(FrameworkElement.HeightProperty); Layout(body); Assert.Equal(200, body.TableCells.Single().Bounds.Height, 2); Assert.Equal(112, Y(body, 1), 2); Assert.Equal(224, Y(body, 2), 2);
    }

    [Fact]
    public void ALengthOnTheOtherCellDoesNotGiveAnAutoCellAPercentageBasis()
    {
        var one = P("左原文"); var two = P("右原文"); var left = Cell(one, P("左后文")); var right = Cell(two, P("右后文")); var table = Table(Row(left, right));
        Css.SetStyle(left, "height:200px;"); Css.SetStyle(one, "height:50%;"); Css.SetStyle(two, "height:50%;"); using var body = Body(Doc(table));
        Assert.Equal(100, Y(body, 1), 2); Assert.Equal(body.Paragraphs[2].ActualHeight, Y(body, 3), 2); Assert.Equal(2, body.TableCells.Count);
    }

    [Fact]
    public void AutoSectionsInsideExplicitCellsRemainIndefiniteForTheirChildren()
    {
        var first = P("原文"); var section = new Section(first); section.Blocks.Add(P("章内后文")); var table = Table(Row(Cell(section))); var doc = Doc(table);
        Css.SetStyle(table, "height:200px;"); Css.SetStyle(first, "height:90%;"); using var body = Body(doc);
        Assert.Equal(body.Paragraphs[0].ActualHeight, Y(body, 1), 2);
    }

    [Fact]
    public void CellOwnedColumnsUseTheAllocatedHeightWithoutShrinkingTheirRuleOrDuplicatingCells()
    {
        var source = P(12); var cell = Cell(source); var table = Table(Row(cell)); var doc = Doc(table);
        Css.SetStyle(table, "height:240px;"); Css.SetStyle(cell, "columns:2;column-gap:20px;column-rule:2px solid blue;"); using var body = Body(doc);
        Assert.Single(body.TableCells); Assert.Equal(240, body.TableCells.Single().Bounds.Height, 2); Assert.Equal(240, body.ExtentHeight, 2);
        Assert.Equal(12, TextBounds(body, 0).Length); Assert.Equal(2, TextBounds(body, 0).Select(box => Math.Round(box.X, 2)).Distinct().Count());
        Css.SetStyle(cell, ""); Layout(body); Assert.Single(body.TableCells); Assert.Single(TextBounds(body, 0).Select(box => Math.Round(box.X, 2)).Distinct()); Assert.Same(cell, source.Parent);
    }

    [Fact]
    public void NestedCollapsedTableFramesSurviveOuterCellRelayoutAndStyleRestoration()
    {
        var first = P("原文"); var innerCell = Cell(first, P("内表后文")); innerCell.ClearValue(TableCell.BorderThicknessProperty);
        var inner = Table(Row(innerCell)); var outer = Table(Row(Cell(inner))); var doc = Doc(outer);
        Css.SetStyle(outer, "height:240px;"); Css.SetStyle(inner, "height:50%;border-collapse:collapse;"); Css.SetStyle(innerCell, "border:2px solid blue;"); Css.SetStyle(first, "height:50%;");
        using var body = Body(doc); var initial = Y(body, 1); Assert.Equal(2, body.TableCells.Count); Assert.True(initial >= 60 && initial < 64);
        Css.SetStyle(outer, "height:320px;"); Layout(body); Assert.True(Y(body, 1) > initial);
        Css.SetStyle(outer, "height:240px;"); Layout(body); Assert.Equal(initial, Y(body, 1), 2); Assert.Equal(2, body.TableCells.Count);
    }

    [Theory]
    [InlineData(72)] [InlineData(144)]
    public void PercentageColumnsRetainTheOriginalFooterInPagedGeometry(int pageHeight)
    {
        var source = P(12); var section = new Section(source); var cell = Cell(section, P("末段")); var table = Table(Row(cell)); var doc = Doc(table); doc.PageHeight = pageHeight;
        Css.SetStyle(table, "height:240px;"); Css.SetStyle(section, "columns:2;column-gap:20px;height:50%;"); var seen = false; var details = new List<string>();
        for (var index = 0; index < doc.ViewerPaginator.PageCount; index++)
        {
            using var page = (FlowDocumentPresentation)doc.ViewerPaginator.GetPage(index).Visual!; var boxes = Rects(Provider(page).DocumentRange.FindText("末段", false, false)!);
            var footer = page.Paragraphs[1]; details.Add($"page={index}, footer={footer.TransformToAncestor(page)}/{footer.RenderSize}, bounds={string.Join(';', TextBounds(page, 1))}, pages={string.Join(';', page.ColumnPages)}, pieces={string.Join(';', footer.CssColumnBox?.Pieces ?? [])}, clips={footer.CssColumnBox?.Clip}, line={page.TopForOffset(doc.GetDocumentOffset(cell.Blocks[1], 0))}");
            object? Field(object value, string name) => value.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)?.GetValue(value);
            string Items(object? values) => values is System.Collections.IEnumerable list ? string.Join(';', list.Cast<object>()) : "null";
            var views = ((System.Collections.IEnumerable)Field(page, "_paragraphs")!).Cast<object>().ToArray(); var lastView = views[1];
            details.Add($"view={Field(lastView, "Bounds")}, colClips={Items(Field(lastView, "ColumnClips"))}, suppressed={Field(lastView, "TableSuppressed")}, tableClip={Field(lastView, "TableClip")}, page={Field(page, "_pageTop")}/{Field(page, "_pageBottom")}, cssBoxNull={footer.CssColumnBox is null}, slices={Items(Field(Field(page, "_rootColumnPlan")!, "Slices"))}");
            seen |= boxes.Length > 0;
        }
        Assert.True(seen, string.Join("\n", details));
    }

    private static Paragraph P(int lines) => P(string.Join('\n', Enumerable.Range(0, lines).Select(index => $"第{index:00}行")));
    private static Paragraph P(string text) => new(new Run(text));
    private static TableCell Cell(params Block[] blocks) { var cell = new TableCell { Padding = new(0), BorderThickness = new(0) }; foreach (var block in blocks) cell.Blocks.Add(block); return cell; }
    private static TableRow Row(params TableCell[] cells) { var row = new TableRow(); foreach (var cell in cells) row.Cells.Add(cell); return row; }
    private static Table Table(params TableRow[] rows) { var table = new Table { CellSpacing = 0 }; var group = new TableRowGroup(); foreach (var row in rows) group.Rows.Add(row); table.RowGroups.Add(group); return table; }
    private static FlowDocument Doc(params Block[] blocks)
    {
        var doc = new FlowDocument { FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24, PagePadding = new(0), PageWidth = 400, PageHeight = 144, Foreground = Brushes.Black, Background = Brushes.White };
        foreach (var block in blocks) doc.Blocks.Add(block);
        Css.SetStyleSheet(doc, "FlowDocument{white-space:pre-wrap;text-align:start;}Paragraph,Section,List,ListItem,Table{margin:0;}"); return doc;
    }
    private static FlowDocumentPresentation Body(FlowDocument doc) { var body = new FlowDocumentPresentation(); body.SetDocument(doc); Layout(body); return body; }
    private static void Layout(FlowDocumentPresentation body) { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); body.Measure(new(400, double.PositiveInfinity)); body.Arrange(new(0, 0, 400, body.DesiredSize.Height)); }
    private static double Y(FlowDocumentPresentation body, int index) => body.Paragraphs[index].TransformToAncestor(body).Y;
    private static Rect[] TextBounds(FlowDocumentPresentation body, int index) { var text = body.Paragraphs[index]; var at = text.TransformToAncestor(body); return text.DocumentTextBounds(0, text.Text.Length).Select(box => new Rect(box.X + at.X, box.Y + at.Y, box.Width, box.Height)).ToArray(); }
    private static ITextProvider Provider(FlowDocumentPresentation body) => (ITextProvider)body.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
    private static Rect[] Rects(ITextRangeProvider range) { var values = range.GetBoundingRectangles(); return Enumerable.Range(0, values.Length / 4).Select(index => new Rect(values[index * 4], values[index * 4 + 1], values[index * 4 + 2], values[index * 4 + 3])).ToArray(); }
}
