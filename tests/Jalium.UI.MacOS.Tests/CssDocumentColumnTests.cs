using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssDocumentColumnTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();
    private const string Six = "甲乙\n丙丁\n戊己\n庚辛\n壬癸\n子丑";

    [Theory]
    [InlineData("column-count:2;")] [InlineData("columns:100px 2;")] [InlineData("column-width:100px;")]
    public void ContinuousViewsFlowTheOriginalParagraphIntoWholeLineColumns(string style)
    {
        var source = P(Six); var doc = Doc(source); Css.SetStyle(doc, "column-gap:20px;" + style);
        using var body = Body(doc); var text = Assert.Single(body.Paragraphs); var rectangles = text.DocumentTextBounds(0, text.Text.Length);
        Assert.Equal(6, rectangles.Count); Assert.Equal(120, rectangles[3].X - rectangles[0].X, 3);
        Assert.Equal(rectangles[0].Y, rectangles[3].Y, 3); Assert.InRange(body.ActualHeight, 72, 78);
        Assert.Same(source, Assert.Single(doc.Blocks)); Assert.Same(source.Inlines, text.Inlines); Assert.Equal(Six, text.Text);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PagedViewsFillColumnsBeforeAdvancingToTheNextPage(bool rtl)
    {
        var source = P(Six + "\n" + Six); var doc = Doc(source); Css.SetStyle(doc, "columns:2;column-gap:20px;" + (rtl ? "direction:rtl;" : ""));
        var paginator = doc.ViewerPaginator; Assert.Equal(2, paginator.PageCount); var metrics = (IFlowDocumentPageMetrics)paginator;
        Assert.Equal(1, metrics.GetPageNumberForOffset(9)); Assert.Equal(2, metrics.GetPageNumberForOffset(18));
        for (var pageIndex = 0; pageIndex < 2; pageIndex++)
        {
            using var page = Assert.IsType<FlowDocumentPresentation>(paginator.GetPage(pageIndex).Visual);
            Assert.Equal(220, page.ActualWidth); Assert.Equal(72, page.ActualHeight);
            var text = Assert.Single(page.Paragraphs); var rect = text.DocumentTextBounds(pageIndex * 18 + 9, 2).Single();
            var at = text.TransformToAncestor(page);
            Assert.InRange(at.Y + rect.Y, 0, 24); Assert.InRange(at.X + rect.X, rtl ? 0 : 120, rtl ? 40 : 160);
        }
        Assert.Same(source, Assert.Single(doc.Blocks));
    }

    [Theory]
    [InlineData("balance")] [InlineData("balance-all")] [InlineData("auto")]
    public void ColumnFillKeepsSourceOffsetsAndProducesFinitePagePlans(string fill)
    {
        var source = P(Six + "\n" + Six + "\n" + Six); var doc = Doc(source); Css.SetStyle(doc, "columns:2;column-gap:20px;column-fill:" + fill + ";");
        Assert.Equal(3, doc.ViewerPaginator.PageCount);
        var metrics = (IFlowDocumentPageMetrics)doc.ViewerPaginator;
        Assert.Equal(new[] { 0, 18, 36 }, Enumerable.Range(1, 3).Select(metrics.GetPageStartOffset).ToArray());
        Assert.Equal(3, metrics.GetPageNumberForOffset(source.Inlines.OfType<Run>().Single().Text.Length - 1));
    }

    [Fact]
    public void AutoFillWithoutADefiniteHeightLeavesContinuousContentInTheFirstColumn()
    {
        var doc = Doc(P(Six)); Css.SetStyle(doc, "columns:2;column-gap:20px;column-fill:auto;"); using var body = Body(doc);
        var text = body.Paragraphs[0]; Assert.All(text.DocumentTextBounds(0, text.Text.Length), rect => Assert.Equal(0, rect.X, 3));
        Assert.InRange(body.ActualHeight, 144, 150);
    }

    [Fact]
    public void NativeDocumentWidthGapAndRulePropertiesParticipateInActualLayout()
    {
        var doc = Doc(P(Six)); doc.ColumnWidth = 100; doc.ColumnGap = 20; doc.ColumnRuleWidth = 4; doc.ColumnRuleBrush = Brushes.Red;
        using var body = Body(doc); var text = body.Paragraphs[0]; var rectangles = text.DocumentTextBounds(0, text.Text.Length);
        Assert.Equal(120, rectangles[3].X - rectangles[0].X, 3); Assert.Equal(Colors.Red, Pixel(body, 109, 10));
    }

    [Fact]
    public void LocalNativeValuesOutrankCssAndClearingThemRestoresCssColumns()
    {
        var doc = Doc(P(Six)); doc.ColumnWidth = 180; Css.SetStyle(doc, "columns:80px 2;column-gap:20px;"); using var body = Body(doc);
        Assert.Single(body.Paragraphs[0].DocumentTextBounds(0, Six.Length).Select(rect => rect.X).Distinct());
        doc.ClearValue(FlowDocument.ColumnWidthProperty); Relayout(body);
        Assert.Equal(2, body.Paragraphs[0].DocumentTextBounds(0, Six.Length).Select(rect => Math.Round(rect.X, 3)).Distinct().Count());
        doc.ColumnGap = 30; Relayout(body); var rectangles = body.Paragraphs[0].DocumentTextBounds(0, Six.Length);
        Assert.Equal(125, rectangles[3].X - rectangles[0].X, 3);
    }

    [Fact]
    public void FixedNativeColumnWidthDoesNotStretchToConsumeTheRemainingSpace()
    {
        var doc = Doc(P(Six)); doc.ColumnWidth = 80; doc.ColumnGap = 20; doc.IsColumnWidthFlexible = false;
        using var body = Body(doc); var text = body.Paragraphs[0]; var rectangles = text.DocumentTextBounds(0, text.Text.Length);
        Assert.Equal(100, rectangles[3].X - rectangles[0].X, 3);
    }

    [Fact]
    public void ASpanningHeadingSeparatesTheTwoBalancedColumnSets()
    {
        var first = P(Six); var heading = P("跨列标题"); var last = P(Six); var doc = Doc(first, heading, last);
        Css.SetStyle(doc, "columns:2;column-gap:20px;"); Css.SetStyle(heading, "column-span:all;background:blue;"); using var body = Body(doc);
        Assert.Equal(220, body.Paragraphs[1].ActualWidth, 3);
        Assert.True(body.Paragraphs[1].TransformToAncestor(body).Y >= 72);
        Assert.True(body.Paragraphs[2].TransformToAncestor(body).Y >= body.Paragraphs[1].TransformToAncestor(body).Y + 24);
        Assert.Equal(Colors.Blue, Pixel(body, 210, 78)); Assert.Same(heading, doc.Blocks[1]);
    }

    [Fact]
    public void NestedSectionHeadingsSpanWithoutMovingTheirOriginalDataParents()
    {
        var heading = P("嵌套跨列标题"); var section = new Section { Margin = new(0) }; section.Blocks.Add(P(Six)); section.Blocks.Add(heading); section.Blocks.Add(P(Six));
        var doc = Doc(section); Css.SetStyle(doc, "columns:2;column-gap:20px;"); Css.SetStyle(heading, "column-span:all;"); using var body = Body(doc);
        Assert.Equal(220, body.Paragraphs[1].ActualWidth, 3); Assert.Same(section, heading.Parent);
        Assert.Same(heading.Inlines, body.Paragraphs[1].Inlines);
    }

    [Theory]
    [InlineData("column", 1)] [InlineData("page", 2)] [InlineData("right", 3)] [InlineData("left", 2)]
    public void ColumnAndPageBreaksHaveDifferentEffects(string kind, int pages)
    {
        var first = P("甲"); var second = P("乙"); var doc = Doc(first, second); Css.SetStyle(doc, "columns:2;column-gap:20px;");
        Css.SetStyle(first, "break-after:" + kind + ";"); Assert.Equal(pages, doc.ViewerPaginator.PageCount);
        Assert.Equal(pages, ((IFlowDocumentPageMetrics)doc.ViewerPaginator).GetPageNumberForOffset(doc.GetDocumentOffset(second, 0)));
    }

    [Fact]
    public void TextProviderSelectionRectanglesAndPointLookupUseTheSameColumns()
    {
        var doc = Doc(P(Six)); Css.SetStyle(doc, "columns:2;column-gap:20px;"); using var body = Body(doc);
        var provider = Assert.IsAssignableFrom<ITextProvider>(body.GetAutomationPeer()!.GetPattern(PatternInterface.Text));
        var range = provider.DocumentRange.FindText("庚辛", false, false)!; range.Select(); Assert.Equal("庚辛", body.Selection!.Text);
        var coordinates = range.GetBoundingRectangles(); Assert.Equal(4, coordinates.Length);
        var rect = new Rect(coordinates[0], coordinates[1], coordinates[2], coordinates[3]); Assert.InRange(rect.X, 120, 140);
        Assert.Equal(9, body.OffsetFromPoint(new(rect.X + .1, rect.Y + rect.Height / 2)));
        Assert.Equal(doc.GetText(), provider.DocumentRange.GetText(-1));
        var key = new KeyEventArgs(UIElement.PreviewKeyDownEvent, Key.A, ModifierKeys.Control, true, false, 0); body.RaiseEvent(key);
        Assert.True(key.Handled); Assert.Equal(doc.GetText(), body.Selection.Text);
    }

    [Fact]
    public void RemovingColumnsRestoresContinuousLayoutAndSharedRunInstances()
    {
        var paragraph = P(Six); var run = (Run)paragraph.Inlines[0]; var doc = Doc(paragraph); Css.SetStyle(doc, "columns:2;column-gap:20px;"); using var body = Body(doc);
        var columnHeight = body.ActualHeight; Css.SetStyle(doc, "columns:auto;"); Relayout(body);
        Assert.True(body.ActualHeight > columnHeight * 1.9); Assert.Same(run, Assert.Single(body.Paragraphs[0].Inlines));
        Css.SetStyle(doc, "columns:2;column-gap:20px;"); Relayout(body); Assert.Equal(columnHeight, body.ActualHeight, 3);
        run.Text = "新文\n原文\n继续\n保留"; Relayout(body); Assert.Equal(run.Text, body.Paragraphs[0].Text); Assert.Same(paragraph, run.Parent);
    }

    [Fact]
    public void PageLookupAndRenderedGlyphGeometryAgreeAfterChangingWidthAndFont()
    {
        var source = P(string.Concat(Enumerable.Repeat("中文排版与原文选择", 30))); var doc = Doc(source); Css.SetStyle(doc, "columns:2;column-gap:20px;");
        var originalCount = doc.ViewerPaginator.PageCount; doc.PageWidth = 400; Assert.True(doc.ViewerPaginator.PageCount < originalCount);
        doc.FontSize = 22; Assert.True(doc.ViewerPaginator.PageCount >= 2);
        var metrics = (IFlowDocumentPageMetrics)doc.ViewerPaginator;
        foreach (var offset in new[] { 0, 50, 100, 150 })
        {
            var number = metrics.GetPageNumberForOffset(offset); using var page = Assert.IsType<FlowDocumentPresentation>(doc.ViewerPaginator.GetPage(number - 1).Visual);
            var text = page.Paragraphs[0]; var point = text.TransformToAncestor(page); var bounds = text.DocumentTextBounds(offset, 1).Single();
            Assert.True(new Rect(point.X + bounds.X, point.Y + bounds.Y, bounds.Width, bounds.Height).IntersectsWith(page.GetChildLayoutClip()!.Bounds));
        }
    }

    [Theory]
    [InlineData("balance", false)] [InlineData("balance-all", false)] [InlineData("auto", true)]
    public void LastPageBalancingAndSequentialFillUseDifferentColumnRuleHeights(string fill, bool reachesBottom)
    {
        var doc = Doc(P(Six)); doc.PageHeight = 120;
        Css.SetStyle(doc, "columns:2;column-gap:20px;column-fill:" + fill + ";column-rule:4px solid red;");
        Assert.Equal(1, doc.ViewerPaginator.PageCount); using var page = Assert.IsType<FlowDocumentPresentation>(doc.ViewerPaginator.GetPage(0).Visual);
        Assert.Equal(reachesBottom ? Colors.Red : Colors.White, Pixel(page, 109, 100));
    }

    [Fact]
    public void AParagraphAfterASpannerMovesToTheNextPageBeforeRelaxingItsOrphans()
    {
        var heading = P("页末标题"); var last = P("甲\n乙\n丙\n丁"); var doc = Doc(P(Six + "\n" + Six), heading, last); doc.PageHeight = 160;
        Css.SetStyle(doc, "columns:3;column-gap:20px;"); Css.SetStyle(heading, "column-span:all;");
        Assert.Equal(2, doc.ViewerPaginator.PageCount);
        Assert.Equal(2, ((IFlowDocumentPageMetrics)doc.ViewerPaginator).GetPageNumberForOffset(doc.GetDocumentOffset(last, 0)));
    }

    [Theory]
    [InlineData("before")] [InlineData("after")]
    public void ForcedPageBreaksOnSpanningHeadingsSeparateTheAdjacentColumnGroups(string side)
    {
        var heading = P("跨列标题"); var last = P(Six); var doc = Doc(P(Six), heading, last); doc.PageHeight = 240;
        Css.SetStyle(doc, "columns:2;column-gap:20px;"); Css.SetStyle(heading, "column-span:all;break-" + side + ":page;");
        Assert.Equal(2, doc.ViewerPaginator.PageCount);
        Assert.Equal(side == "before" ? 2 : 1, ((IFlowDocumentPageMetrics)doc.ViewerPaginator).GetPageNumberForOffset(doc.GetDocumentOffset(heading, 0)));
        Assert.Equal(2, ((IFlowDocumentPageMetrics)doc.ViewerPaginator).GetPageNumberForOffset(doc.GetDocumentOffset(last, 0)));
    }

    [Fact]
    public void AvoidPageKeepsASectionAcrossColumnsWithoutRequiringOneTallColumn()
    {
        var prefix = P("甲\n乙\n丙\n丁"); var section = new Section { Margin = new(0) }; var content = P("戊\n己\n庚\n辛"); section.Blocks.Add(content);
        var doc = Doc(prefix, section); Css.SetStyle(doc, "columns:2;column-gap:20px;"); Css.SetStyle(section, "break-inside:avoid-page;");
        Assert.Equal(2, doc.ViewerPaginator.PageCount);
        Assert.Equal(2, ((IFlowDocumentPageMetrics)doc.ViewerPaginator).GetPageNumberForOffset(doc.GetDocumentOffset(content, 0)));
        Assert.Same(section, content.Parent);
    }

    [Fact]
    public void AvoidColumnAndSingleLineWidowSettingsAffectActualContinuousFragments()
    {
        var source = P(Six); var doc = Doc(source); Css.SetStyle(doc, "columns:2;column-gap:20px;"); Css.SetStyle(source, "break-inside:avoid-column;");
        using var body = Body(doc); Assert.Single(body.Paragraphs[0].DocumentTextBounds(0, Six.Length).Select(rect => rect.X).Distinct());
        Css.SetStyle(source, "orphans:1;widows:1;"); Css.SetStyle(doc, "columns:3;column-gap:20px;"); Relayout(body);
        Assert.Equal(3, body.Paragraphs[0].DocumentTextBounds(0, Six.Length).Select(rect => rect.X).Distinct().Count());
    }

    [Fact]
    public void PageTextProviderClipsEveryVisibleRectangleToTheCurrentPage()
    {
        var doc = Doc(P(Six + "\n" + Six)); Css.SetStyle(doc, "columns:2;column-gap:20px;");
        using var page = Assert.IsType<FlowDocumentPresentation>(doc.ViewerPaginator.GetPage(1).Visual);
        var provider = Assert.IsAssignableFrom<ITextProvider>(page.GetAutomationPeer()!.GetPattern(PatternInterface.Text));
        Assert.NotEmpty(provider.GetVisibleRanges()); var range = provider.DocumentRange.FindText("庚辛", true, false)!;
        var boxes = range.GetBoundingRectangles(); Assert.Equal(4, boxes.Length); Assert.InRange(boxes[0], 120, 140);
        Assert.InRange(boxes[1], 0, 24); Assert.InRange(boxes[1] + boxes[3], 0, 72);
    }

    [Fact]
    public void RefreshingSeveralViewsPreservesTheSharedInlineCollectionAndIndependentGeometry()
    {
        var paragraph = P(Six); var run = (Run)paragraph.Inlines[0]; var doc = Doc(paragraph); Css.SetStyle(doc, "columns:2;column-gap:20px;");
        using var body = Body(doc); using var page = Assert.IsType<FlowDocumentPresentation>(doc.ViewerPaginator.GetPage(0).Visual); var originalHeight = body.ActualHeight;
        Css.SetStyle(doc, "columns:3;column-gap:20px;"); Relayout(body);
        Assert.True(((IFlowDocumentPagePresentation)doc.ViewerPaginator).TryRefreshPage(0, page, out var size)); page.Measure(size); page.Arrange(new Rect(size));
        Assert.True(body.ActualHeight < originalHeight); Assert.Same(run, body.Paragraphs[0].Inlines[0]); Assert.Same(run, page.Paragraphs[0].Inlines[0]);
        Assert.Equal(3, page.Paragraphs[0].DocumentTextBounds(0, Six.Length).Select(rect => rect.X).Distinct().Count());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TableFramesAndCellsRemainInTheirColumnsWithEitherBorderModel(bool collapsed)
    {
        var table = new Table { CellSpacing = 0 }; var group = new TableRowGroup(); table.RowGroups.Add(group);
        for (var row = 0; row < 6; row++)
        { var item = new TableRow(); item.Cells.Add(new TableCell(P("甲")) { Padding = new(0) }); item.Cells.Add(new TableCell(P("乙")) { Padding = new(0) }); group.Rows.Add(item); }
        var doc = Doc(table); Css.SetStyle(doc, "columns:2;column-gap:20px;");
        Css.SetStyleSheet(doc, "FlowDocument{white-space:pre-wrap;}Table{width:100%;box-sizing:border-box;table-layout:fixed;border:2px solid blue;}TableCell{padding:0;border:2px solid blue;}" + (collapsed ? "Table{border-collapse:collapse;}" : ""));
        using var body = Body(doc); Assert.True(body.TableCells.Count >= 12); Assert.All(body.TableCells, cell => Assert.InRange(cell.Bounds.Width, 1, 60));
        Assert.Equal(Colors.White, Pixel(body, 110, 10)); Assert.True(Count(body, Colors.Blue, new(0, 0, 100, body.ActualHeight)) > 0);
        Assert.True(Count(body, Colors.Blue, new(120, 0, 100, body.ActualHeight)) > 0);
        Assert.All(group.Rows.Cast<TableRow>(), row => Assert.Same(group, row.Parent));
    }

    [Fact]
    public void AnOversizedLineContinuesItsInkOnEveryRequiredPage()
    {
        var source = P("超高行"); source.LineHeight = 200; var doc = Doc(source); doc.PageHeight = 72; Css.SetStyle(doc, "columns:2;column-gap:20px;");
        Css.SetStyle(source, "background:blue;"); Assert.Equal(3, doc.ViewerPaginator.PageCount);
        for (var index = 0; index < 3; index++)
        { using var page = Assert.IsType<FlowDocumentPresentation>(doc.ViewerPaginator.GetPage(index).Visual); Assert.True(Count(page, Colors.Blue, new(0, 0, 100, 72)) > 100); }
    }

    [Fact]
    public void RichInlineImageFillsFollowBothColumnsWithoutReplacingTheirOriginalRun()
    {
        var source = P(Six); var run = (Run)source.Inlines[0]; var doc = Doc(source);
        Css.SetStyle(doc, "columns:2;column-gap:20px;"); Css.SetStyle(run, "border-image:image(blue) 0 fill / 0;");
        using var body = Body(doc);
        Assert.True(Count(body, Colors.Blue, new(0, 0, 100, body.ActualHeight)) > 50);
        Assert.True(Count(body, Colors.Blue, new(120, 0, 100, body.ActualHeight)) > 50);
        Assert.Equal(Colors.White, Pixel(body, 110, 10)); Assert.Same(run, body.Paragraphs[0].Inlines[0]); Assert.Same(source, run.Parent);
    }

    [Theory]
    [InlineData("balance", true)] [InlineData("balance-all", false)]
    public void BalanceAllAlsoBalancesANonFinalPageEndingAtAForcedPageBreak(string fill, bool reachesBottom)
    {
        var first = P("一\n二\n三\n四\n五\n六\n七"); var doc = Doc(first, P(Six)); doc.PageHeight = 120;
        Css.SetStyle(doc, "columns:2;column-gap:20px;column-fill:" + fill + ";column-rule:4px solid red;"); Css.SetStyle(first, "break-after:page;");
        Assert.Equal(2, doc.ViewerPaginator.PageCount); using var page = Assert.IsType<FlowDocumentPresentation>(doc.ViewerPaginator.GetPage(0).Visual);
        Assert.Equal(reachesBottom ? Colors.Red : Colors.White, Pixel(page, 109, 108));
    }

    [Fact]
    public void TheNormalCssGapUsesTheDocumentsCurrentFontSize()
    {
        var doc = Doc(P(Six)); Css.SetStyle(doc, "columns:2;"); using var body = Body(doc);
        var rectangles = body.Paragraphs[0].DocumentTextBounds(0, Six.Length); Assert.Equal(118, rectangles[3].X - rectangles[0].X, 3);
        doc.FontSize = 20; Relayout(body); rectangles = body.Paragraphs[0].DocumentTextBounds(0, Six.Length);
        Assert.Equal(120, rectangles[3].X - rectangles[0].X, 3);
    }

    [Fact]
    public void ReturningFromColumnsToSinglePageLayoutStartsWithTheFirstSourceLine()
    {
        var paragraph = P(Six + "\n寅卯\n辰巳\n午未\n申酉\n戌亥\n结束"); var doc = Doc(paragraph); doc.PageHeight = 144;
        Css.SetStyle(doc, "columns:2;column-gap:20px;");
        Assert.Equal(1, doc.ViewerPaginator.PageCount);
        using (var columns = Assert.IsType<FlowDocumentPresentation>(doc.ViewerPaginator.GetPage(0).Visual))
            Assert.Equal(2, columns.Paragraphs[0].DocumentTextBounds(0, paragraph.Inlines.OfType<Run>().Single().Text.Length).Select(rect => rect.X).Distinct().Count());
        Css.SetStyle(doc, "columns:auto;");
        Assert.True(doc.ViewerPaginator.PageCount >= 2);
        using var first = Assert.IsType<FlowDocumentPresentation>(doc.ViewerPaginator.GetPage(0).Visual);
        var provider = Assert.IsAssignableFrom<ITextProvider>(first.GetAutomationPeer()!.GetPattern(PatternInterface.Text));
        var coordinates = provider.DocumentRange.FindText("甲乙", false, false)!.GetBoundingRectangles();
        Assert.Equal(4, coordinates.Length); Assert.InRange(coordinates[1], 0, 24);
    }

    [Theory]
    [InlineData(FlowDocumentReaderViewingMode.Page)] [InlineData(FlowDocumentReaderViewingMode.TwoPage)] [InlineData(FlowDocumentReaderViewingMode.Scroll)]
    public void ReaderSearchAndModeChangesKeepTheOriginalRangeVisibleInColumns(FlowDocumentReaderViewingMode mode)
    {
        var source = P(Six + "\n" + Six + "\nneedle"); var run = source.Inlines[0]; var doc = Doc(source);
        Css.SetStyle(doc, "columns:2;column-gap:20px;");
        var reader = new FlowDocumentReader { Document = doc, ViewingMode = mode, SearchText = "needle", IsInactiveSelectionHighlightEnabled = true };
        Layout(reader, 240, 160); reader.Find(); Layout(reader, 240, 160);
        Assert.Equal("needle", reader.Selection!.Text); Assert.Equal(doc.ViewerPaginator.PageCount, reader.PageNumber);
        Assert.Contains(Bodies(reader).SelectMany(body => body.Paragraphs), paragraph => paragraph.SelectedText == "needle");
        reader.ViewingMode = FlowDocumentReaderViewingMode.Scroll; Layout(reader, 240, 160);
        Assert.Equal("needle", reader.Selection.Text); Assert.Same(run, source.Inlines[0]);
        reader.Document = null;
    }

    [Fact]
    public void SharedScrollAndPageViewsReturnToTheFirstSingleColumnPageAfterSearch()
    {
        var before = P("按行流入下一列\n阅读顺序保持\n保留原文字体\n间距留出呼吸\n分隔线不占列宽\n按钮之外仍是原文\n窄屏使用双列\n宽屏容纳三列\n逐列读完再换页\n页码对应原偏移\n查找可以定位\n焦点仍有归属");
        var heading = P("一个标题，跨越所有列"); var after = P("标题后重新分列\n末段仍可选择\n内容继续流动\n恢复原始列序");
        var doc = Doc(before, heading, after); doc.PageWidth = 315; doc.PageHeight = 160; doc.PagePadding = new(8);
        Css.SetStyle(doc, "columns:140px 3;column-gap:16px;"); Css.SetStyle(heading, "column-span:all;margin:8px 0;line-height:30px;");
        var scroll = new FlowDocumentScrollViewer { Document = doc, Height = 200, IsInactiveSelectionHighlightEnabled = true };
        Layout(scroll, 335, 200); Assert.True(scroll.Find("末段")); Layout(scroll, 335, 200);
        using (var second = Assert.IsType<FlowDocumentPresentation>(doc.ViewerPaginator.GetPage(1).Visual)) Assert.True(second.HasColumns);
        Css.SetStyle(doc, "columns:auto;"); Layout(scroll, 335, 200); Assert.True(scroll.Find("末段"));
        using var first = Assert.IsType<FlowDocumentPresentation>(doc.ViewerPaginator.GetPage(0).Visual); Layout(first, 315, 160);
        var provider = Assert.IsAssignableFrom<ITextProvider>(first.GetAutomationPeer()!.GetPattern(PatternInterface.Text));
        var coordinates = provider.DocumentRange.FindText("按行流入下一列", false, false)!.GetBoundingRectangles();
        Assert.True(coordinates.Length > 0, $"First={first.Lines[0]}; text={first.Paragraphs[0].DocumentTextBounds(0, 8)[0]}; origin={first.Paragraphs[0].TransformToAncestor(first)}; starts={string.Join(',', Enumerable.Range(1, doc.ViewerPaginator.PageCount).Select(((IFlowDocumentPageMetrics)doc.ViewerPaginator).GetPageStartOffset))}");
        scroll.Document = null;
    }

    private static void Layout(FrameworkElement view, double width, double height)
    { for (var i = 0; i < 4; i++) { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); view.Measure(new(width, height)); view.Arrange(new(0, 0, width, height)); view.UpdateLayout(); } }
    private static IEnumerable<FlowDocumentPresentation> Bodies(Visual root)
    {
        if (root is FlowDocumentPresentation body) yield return body;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (VisualTreeHelper.GetChild(root, i) is Visual child) foreach (var item in Bodies(child)) yield return item;
    }

    private static int Count(FrameworkElement root, Color color, Rect area)
    {
        var width = Math.Max(1, (int)Math.Ceiling(root.ActualWidth)); var height = Math.Max(1, (int)Math.Ceiling(root.ActualHeight)); var count = 0;
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Bgra32); var drawing = new SoftwareDrawingContext(bitmap); drawing.DrawRectangle(Brushes.White, null, new(0, 0, width, height)); root.Render(drawing); drawing.Close();
        var pixels = new byte[width * height * 4]; bitmap.CopyPixels(pixels, width * 4, 0);
        for (var y = Math.Max(0, (int)area.Y); y < Math.Min(height, area.Bottom); y++) for (var x = Math.Max(0, (int)area.X); x < Math.Min(width, area.Right); x++)
        { var i = (y * width + x) * 4; if (Color.FromArgb(pixels[i + 3], pixels[i + 2], pixels[i + 1], pixels[i]) == color) count++; }
        return count;
    }

    private static Paragraph P(string text) => new(new Run(text)) { Margin = new(0) };
    private static FlowDocument Doc(params Block[] blocks)
    { var doc = new FlowDocument { FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24, Foreground = Brushes.Black, Background = Brushes.White, PagePadding = new(0), PageWidth = 220, PageHeight = 72 }; foreach (var block in blocks) doc.Blocks.Add(block); Css.SetStyleSheet(doc, "FlowDocument{white-space:pre-wrap;}"); return doc; }
    private static FlowDocumentPresentation Body(FlowDocument doc) { var body = new FlowDocumentPresentation(); body.SetDocument(doc); Relayout(body); return body; }
    private static void Relayout(FlowDocumentPresentation body)
    { for (var i = 0; i < 3; i++) { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); body.Measure(new(body.Document!.PageWidth, double.PositiveInfinity)); body.Arrange(new(0, 0, body.Document.PageWidth, Math.Max(1, body.DesiredSize.Height))); } }
    private static Color Pixel(FrameworkElement root, int x, int y)
    {
        var width = Math.Max(1, (int)Math.Ceiling(root.ActualWidth)); var height = Math.Max(1, (int)Math.Ceiling(root.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Bgra32); var drawing = new SoftwareDrawingContext(bitmap);
        drawing.DrawRectangle(Brushes.White, null, new(0, 0, width, height)); root.Render(drawing); drawing.Close(); var pixels = new byte[width * height * 4]; bitmap.CopyPixels(pixels, width * 4, 0);
        var at = (y * width + x) * 4; return Color.FromArgb(pixels[at + 3], pixels[at + 2], pixels[at + 1], pixels[at]);
    }
}
