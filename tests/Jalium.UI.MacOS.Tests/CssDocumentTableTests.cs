using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssDocumentTableTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData("0", 0, 0)] [InlineData("4px", 4, 4)] [InlineData("3px 7px", 3, 7)]
    [InlineData("1em 2em", 16, 32)] [InlineData("calc(-2px) 3px", 0, 3)]
    public void BorderSpacingUsesTheSharedLengthGrammar(string value, double x, double y)
    {
        var table = Table(Row(Cell("甲"), Cell("乙"))); var doc = Doc(table); Css.SetStyle(table, "border-spacing:" + value);
        using var body = Body(doc); Assert.Equal(new Size(x, y), table.GetValue(CssTableProperties.SpacingProperty));
        Assert.Equal(x, body.TableCells[0].Bounds.X, 3); Assert.Equal(y, body.TableCells[0].Bounds.Y, 3);
    }

    [Theory]
    [InlineData("-1px")] [InlineData("1px -2px")] [InlineData("10%")] [InlineData("calc(10% + 1px)")]
    [InlineData("1px 2px 3px")] [InlineData("auto")]
    public void InvalidBorderSpacingIsRejected(string value) => Assert.False(CssAuthoring.IsValueSupported("border-spacing", value));

    [Fact]
    public void AdjacentCellsShareRowsAndColumnEdgesWithoutChangingOriginalNodes()
    {
        var a = Cell("甲", 2); var b = Cell("乙"); var c = Cell("丙"); var d = Cell("丁");
        var table = Table(Row(a, b), Row(c, d)); var doc = Doc(table); var original = doc.GetText();
        using var body = Body(doc); var boxes = body.TableCells;
        Assert.Equal(boxes[0].Bounds.Top, boxes[1].Bounds.Top); Assert.Equal(boxes[2].Bounds.Top, boxes[3].Bounds.Top);
        Assert.Equal(boxes[0].Bounds.Left, boxes[2].Bounds.Left); Assert.Equal(boxes[1].Bounds.Left, boxes[3].Bounds.Left);
        Assert.Equal(boxes[0].Bounds.Bottom, boxes[1].Bounds.Bottom); Assert.True(boxes[2].Bounds.Top >= boxes[0].Bounds.Bottom);
        Assert.Equal(original, doc.GetText()); Assert.Same(a, table.RowGroups[0].Rows[0].Cells[0]); Assert.Same(table.RowGroups[0].Rows[0], a.Parent); Assert.Null(doc.Parent);
        Assert.Equal(body.Paragraphs[0].TransformToAncestor(body).Y, body.Paragraphs[1].TransformToAncestor(body).Y);
    }

    [Fact]
    public void FixedColumnsAndWeightedNativeColumnsKeepTheirAllocatedWidths()
    {
        var table = Table(Row(Cell("甲"), Cell("乙"), Cell("丙"))); table.CellSpacing = 4;
        table.Columns.Add(new() { Width = new(80) }); table.Columns.Add(new() { Width = new(1, GridUnitType.Star) }); table.Columns.Add(new() { Width = new(2, GridUnitType.Star) });
        var doc = Doc(table); Css.SetStyle(table, "table-layout:fixed;"); using var body = Body(doc);
        var boxes = body.TableCells; Assert.Equal(80, boxes[0].Bounds.Width, 3);
        Assert.Equal(2 * boxes[1].Bounds.Width, boxes[2].Bounds.Width, 3); Assert.Equal(316, boxes[2].Bounds.Right, 3);
    }

    [Fact]
    public void FixedLayoutUsesTheFirstRowAndAutoLayoutIncludesLaterIntrinsicWidths()
    {
        var table = Table(Row(Cell("甲"), Cell("乙")), Row(Cell(new string('W', 50)), Cell("丙"))); var doc = Doc(table);
        Css.SetStyle(table, "table-layout:fixed;"); using var body = Body(doc); var fixedWidth = body.TableCells[0].Bounds.Width;
        Css.SetStyle(table, "table-layout:auto;"); Relayout(body); Assert.True(body.TableCells[0].Bounds.Width > fixedWidth + 100);
    }

    [Fact]
    public void PercentCellWidthsAndAComponentTableWidthUseTheContainingBlock()
    {
        var a = Cell("甲"); var table = Table(Row(a, Cell("乙"))); var doc = Doc(table);
        Css.SetStyle(table, "width:80%;table-layout:fixed;border-spacing:0;"); Css.SetStyle(a, "width:25%;"); using var body = Body(doc);
        Assert.Equal(64, body.TableCells[0].Bounds.Width, 3); Assert.Equal(256, body.TableCells[1].Bounds.Right, 3);
    }

    [Fact]
    public void RowAndColumnSpansOccupyTheirActualTracksAndStopAtTheRowGroup()
    {
        var a = Cell("跨行", 4); a.RowSpan = 10;
        var b = Cell("跨列"); b.ColumnSpan = 2;
        var table = Table(Row(a, b), Row(Cell("乙"), Cell("丙"))); var second = new TableRowGroup(); second.Rows.Add(Row(Cell("下一组"))); table.RowGroups.Add(second);
        using var body = Body(Doc(table)); var boxes = body.TableCells;
        Assert.Equal(2, boxes[0].RowSpan); Assert.Equal(2, boxes[1].ColumnSpan);
        Assert.Equal(1, boxes[2].Column); Assert.Equal(2, boxes[3].Column); Assert.Equal(0, boxes[4].Column);
        Assert.True(boxes[0].Bounds.Height >= 96); Assert.Equal(boxes[0].Bounds.Bottom, boxes[2].Bounds.Bottom, 3);
        Assert.True(boxes[4].Bounds.Top >= boxes[0].Bounds.Bottom);
    }

    [Theory]
    [InlineData("top", 0d)] [InlineData("middle", .5)] [InlineData("bottom", 1d)]
    public void VerticalAlignmentUsesTheSharedCellHeight(string alignment, double fraction)
    {
        var a = Cell("多行", 4); var b = Cell("短行"); var table = Table(Row(a, b)); var doc = Doc(table); Css.SetStyle(b, "vertical-align:" + alignment);
        using var body = Body(doc); var text = body.Paragraphs[1]; var box = body.TableCells[1].Bounds;
        Assert.Equal(box.Top + (box.Height - text.ActualHeight) * fraction, text.TransformToAncestor(body).Y, 3);
    }

    [Fact]
    public void BaselinesAlignUsingTheFirstShapedLineWithDifferentFontSizes()
    {
        var a = Cell("大字", 2); var b = Cell("小字"); ((Paragraph)a.Blocks[0]).FontSize = 28;
        using var body = Body(Doc(Table(Row(a, b))));
        var lines = body.Paragraphs.Select(p => p.TransformToAncestor(body).Y + p.GetFirstBaselineOffset(p.ActualHeight)).ToArray(); Assert.Equal(lines[0], lines[1], 3);
    }

    [Fact]
    public void InheritedSpacingAndRemovalOverrideThenRestoreNativeSpacing()
    {
        var table = Table(Row(Cell("甲"), Cell("乙"))); table.CellSpacing = 5; var doc = Doc(table);
        using var body = Body(doc); Assert.Equal(5, body.TableCells[0].Bounds.X);
        Css.SetStyle(doc, "border-spacing:0;"); Relayout(body); Assert.Equal(0, body.TableCells[0].Bounds.X);
        Css.SetStyle(doc, ""); Relayout(body); Assert.Equal(5, body.TableCells[0].Bounds.X);
    }

    [Fact]
    public void CellPaddingBordersAndNativeCellTypographyParticipateInLayout()
    {
        var cell = Cell("正文", 2); cell.ClearValue(TableCell.PaddingProperty); cell.BorderThickness = new(2); cell.LineHeight = 32;
        var table = Table(Row(cell)); var doc = Doc(table); Css.SetStyle(cell, "padding:10%;"); using var body = Body(doc);
        var text = body.Paragraphs[0]; Assert.Equal(34, text.TransformToAncestor(body).X, 3); Assert.Equal(34, text.TransformToAncestor(body).Y, 3);
        Assert.Equal(32, text.LineHeight); Assert.True(body.TableCells[0].Bounds.Height >= 128);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RightToLeftReversesColumnPositionsAndHitTestingUsesBothCoordinates(bool rtl)
    {
        var a = Cell("左列"); var b = Cell("右列"); var table = Table(Row(a, b)); table.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        var doc = Doc(table); using var body = Body(doc); var first = body.Paragraphs[0]; var second = body.Paragraphs[1];
        Assert.Equal(!rtl, first.TransformToAncestor(body).X < second.TransformToAncestor(body).X);
        foreach (var pair in new[] { (Paragraph: (Paragraph)a.Blocks[0], Text: first), (Paragraph: (Paragraph)b.Blocks[0], Text: second) })
        {
            var origin = pair.Text.TransformToAncestor(body); var offset = body.OffsetFromPoint(new(origin.X + 1, origin.Y + 8));
            Assert.InRange(offset, doc.GetDocumentOffset(pair.Paragraph, 0), doc.GetDocumentOffset(pair.Paragraph, 0) + pair.Text.Text.Length);
        }
    }

    [Fact]
    public void EmptyCellsHideTheirLayersAndShowRestoresBordersAndBackground()
    {
        var empty = new TableCell { Background = Brushes.Red, BorderBrush = Brushes.Red, Padding = new(6), BorderThickness = new(2) };
        var table = Table(Row(empty)); table.Columns.Add(new() { Background = Brushes.Red }); var doc = Doc(table);
        using var body = Body(doc); Assert.True(Count(body, Colors.Red) > 100);
        Css.SetStyle(table, "empty-cells:hide;"); Relayout(body); Assert.Equal(0, Count(body, Colors.Red));
        Css.SetStyle(table, "empty-cells:show;"); Relayout(body); Assert.True(Count(body, Colors.Red) > 100);
    }

    [Fact]
    public void NestedTablesKeepCellGeometryInTheSameCoordinateSpaceAsTheirText()
    {
        var inner = Cell("内表"); var nested = Table(Row(inner, Cell("乙"))); var outer = new TableCell(nested) { Padding = new(10), BorderThickness = new(2) };
        var doc = Doc(Table(Row(Cell("前列"), outer))); using var body = Body(doc);
        var box = body.TableCells.Single(b => ReferenceEquals(b.Source, inner)).Bounds;
        var text = body.Paragraphs[1]; Assert.Equal(box.X, text.TransformToAncestor(body).X, 3); Assert.Equal(box.Y, text.TransformToAncestor(body).Y, 3);
        Assert.True(box.X > body.TableCells.Single(b => ReferenceEquals(b.Source, outer)).Bounds.X);
    }

    [Fact]
    public void MutatingColumnsRowsAndOriginalRunsInvalidatesExistingViewsAndPaginator()
    {
        var table = Table(Row(Cell("甲"), Cell("乙"))); table.Columns.Add(new() { Width = new(80) }); table.Columns.Add(new() { Width = new(1, GridUnitType.Star) });
        var doc = Doc(table); Css.SetStyle(table, "table-layout:fixed;"); using var body = Body(doc); var paginator = doc.ViewerPaginator;
        Assert.Equal(80, body.TableCells[0].Bounds.Width); table.Columns[0].Width = new(120); Relayout(body); Assert.Equal(120, body.TableCells[0].Bounds.Width);
        var before = paginator.PageCount; var added = Cell("新增", 12); table.RowGroups[0].Rows.Add(Row(added)); Relayout(body);
        Assert.Equal(3, body.Paragraphs.Count); Assert.True(paginator.PageCount > before);
        ((Run)((Paragraph)added.Blocks[0]).Inlines[0]).Text = "更改后的原文"; Relayout(body); Assert.Contains("更改后的原文", body.Paragraphs[^1].Text);
        table.RowGroups[0].Rows.RemoveAt(1); Relayout(body); Assert.Equal(2, body.Paragraphs.Count); Assert.Equal(before, paginator.PageCount);
    }

    [Fact]
    public void ParallelCellLinesNavigateToThePageWhereTheirOriginalGlyphsAreVisible()
    {
        var left = Cell("甲", 11); var right = Cell("乙", 7); var table = Table(Row(left, right)); var doc = Doc(table); doc.PageHeight = 100;
        using var body = Body(doc); var paginator = doc.ViewerPaginator;
        foreach (var cell in new[] { left, right })
        {
            var paragraph = (Paragraph)cell.Blocks[0]; var local = 0;
            foreach (var inline in paragraph.Inlines)
            {
                if (inline is LineBreak) { local++; continue; }
                var run = (Run)inline; var offset = doc.GetDocumentOffset(paragraph, local); var pageNumber = ((IFlowDocumentPageMetrics)paginator).GetPageNumberForOffset(offset);
                var page = Assert.IsType<FlowDocumentPresentation>(paginator.GetPage(pageNumber - 1).Visual);
                var text = page.Paragraphs[cell == left ? 0 : 1]; var rect = text.DocumentTextBounds(local, 1)[0]; var origin = text.TransformToAncestor(page);
                var bounds = new Rect(origin.X + rect.X, origin.Y + rect.Y, rect.Width, rect.Height);
                Assert.True(bounds.IntersectsWith(page.GetChildLayoutClip()!.Bounds), $"{run.Text} was mapped to invisible page {pageNumber}");
                local += run.Text.Length;
            }
        }
        Assert.InRange(paginator.PageCount, 3, 5);
    }

    [Fact]
    public void RowBreaksAndKeepRulesUsePhysicalRowsInsteadOfTheOrderOfCells()
    {
        var first = Row(Cell("甲"), Cell("乙")); var second = Row(Cell("丙", 2), Cell("丁", 2)); var table = Table(first, second); var doc = Doc(table);
        Css.SetStyle(second, "break-before:right;break-inside:avoid;"); Flush();
        var p = (Paragraph)second.Cells[1].Blocks[0]; Assert.Equal(3, ((IFlowDocumentPageMetrics)doc.ViewerPaginator).GetPageNumberForOffset(doc.GetDocumentOffset(p, 0)));
        Assert.Equal(3, doc.ViewerPaginator.PageCount);
    }

    [Fact]
    public void TableFollowingContentStartsAfterTheTallestCellAndSelectionRetainsTabSeparators()
    {
        var table = Table(Row(Cell("甲", 4), Cell("乙"))); var after = Paragraph("后文", 1); var doc = Doc(table, after); using var body = Body(doc);
        Assert.True(body.Paragraphs[^1].TransformToAncestor(body).Y >= body.TableCells.Max(c => c.Bounds.Bottom));
        var peer = body.GetAutomationPeer()!; var provider = Assert.IsAssignableFrom<ITextProvider>(peer.GetPattern(PatternInterface.Text));
        Assert.Equal(doc.GetText(), provider.DocumentRange.GetText(-1)); Assert.Contains('\t', doc.GetText());
        var key = new Jalium.UI.Input.KeyEventArgs(UIElement.PreviewKeyDownEvent, Jalium.UI.Input.Key.A, Jalium.UI.Input.ModifierKeys.Control, true, false, 0);
        body.RaiseEvent(key); Assert.True(key.Handled); Assert.Equal(doc.GetText(), body.Selection!.Text);
    }

    [Fact]
    public void ALineTallerThanAPageContinuesUntilAllItsInkCanBePresented()
    {
        var p = Paragraph("超高单行", 1); p.LineHeight = 280; var doc = Doc(p); doc.PageHeight = 100;
        Assert.Equal(3, doc.ViewerPaginator.PageCount);
        var pages = Enumerable.Range(0, 3).Select(i => Assert.IsType<FlowDocumentPresentation>(doc.ViewerPaginator.GetPage(i).Visual)).ToArray();
        Assert.Equal(-100, pages[1].Paragraphs[0].TransformToAncestor(pages[1]).Y, 3); Assert.Equal(-200, pages[2].Paragraphs[0].TransformToAncestor(pages[2]).Y, 3);
    }

    [Fact]
    public void MetadataExposesActualTableGrammarDefaultsAndInheritance()
    {
        var layout = CssAuthoring.GetProperty("table-layout"); Assert.False(layout.IsInherited); Assert.Equal("auto", layout.InitialValue); Assert.Contains("fixed", layout.Values);
        Assert.True(CssAuthoring.GetProperty("border-spacing").IsInherited); Assert.True(CssAuthoring.GetProperty("empty-cells").IsInherited); Assert.Equal("show", CssAuthoring.GetProperty("empty-cells").InitialValue);
    }

    [Fact]
    public void EveryTableSeparatorAndNestedRunRoundTripsThroughPointersAndRanges()
    {
        var nested = Table(Row(Cell("内甲"), Cell("内乙"))); var second = Cell("末格");
        var outer = Table(Row(new TableCell(nested), second)); var doc = Doc(outer, Paragraph("表后", 1)); var text = doc.GetText();
        for (var offset = 0; offset <= text.Length; offset++)
        {
            var forward = doc.GetPositionAtOffset(offset, LogicalDirection.Forward); var backward = doc.GetPositionAtOffset(offset, LogicalDirection.Backward);
            Assert.NotNull(forward); Assert.NotNull(backward); Assert.Equal(offset, forward.DocumentOffset); Assert.Equal(offset, backward.DocumentOffset);
            for (var length = 0; length <= Math.Min(4, text.Length - offset); length++)
                Assert.Equal(text.Substring(offset, length), new TextRange(forward, doc.GetPositionAtOffset(offset + length, LogicalDirection.Backward)!).Text);
        }
        Assert.Equal(text.Length, doc.ContentEnd.DocumentOffset); Assert.Equal("末格0\n", new TextRange(second.ContentStart, second.ContentEnd).Text);
        Assert.Equal(text.IndexOf("末格", StringComparison.Ordinal), doc.GetDocumentOffset((Paragraph)second.Blocks[0], 0));
    }

    [Fact]
    public void EmptyParagraphsHaveAFiniteBaselineAndHiddenEmptyRowsCollapseSpacing()
    {
        var empty = new TableCell(new Paragraph()); var table = Table(Row(empty, Cell("正文"))); using var body = Body(Doc(table));
        Assert.All(body.TableCells, cell => { Assert.True(double.IsFinite(cell.Bounds.Height)); Assert.True(cell.Bounds.Height > 0); });
        var hidden = new TableCell(); var hiddenTable = Table(Row(hidden)); var doc = Doc(hiddenTable); Css.SetStyle(hiddenTable, "border-spacing:4px 6px;empty-cells:hide;");
        using var hiddenBody = Body(doc); Assert.Equal(6, hiddenBody.ExtentHeight, 3); Assert.Equal(0, hiddenBody.TableCells[0].Bounds.Height);
    }

    [Fact]
    public void LocalPaddingKeepsItsNativePrecedenceOverPercentCssAndClearingRestoresCss()
    {
        var cell = Cell("正文"); cell.Padding = new(4); var table = Table(Row(cell)); var doc = Doc(table); Css.SetStyle(cell, "padding:10%;");
        using var body = Body(doc); Assert.Equal(4, body.Paragraphs[0].TransformToAncestor(body).X, 3);
        cell.ClearValue(TableCell.PaddingProperty); Relayout(body); Assert.Equal(32, body.Paragraphs[0].TransformToAncestor(body).X, 3);
    }

    [Fact]
    public void CssBordersOverrideTheNativeDefaultAndRemovalRestoresIt()
    {
        var cell = new TableCell(); var table = Table(Row(cell)); var doc = Doc(table); Css.SetStyle(cell, "border:2px solid red;"); using var body = Body(doc);
        Assert.True(Count(body, Colors.Red) > 50); Css.SetStyle(cell, ""); Relayout(body);
        Assert.Equal(0, Count(body, Colors.Red)); Assert.Equal(Color.FromRgb(200, 200, 200), Assert.IsType<SolidColorBrush>(cell.BorderBrush).Color);
    }

    [Fact]
    public void CellBorderImagesUseTheOriginalCellAndAreRemovedWithoutStalePaint()
    {
        var cell = new TableCell(Paragraph("正文", 2)); var doc = Doc(Table(Row(cell))); Css.SetStyle(cell, "border-image:image(red) 0 fill / 0;");
        using var body = Body(doc); Assert.True(Count(body, Colors.Red) > 100);
        Css.SetStyle(cell, "border-image:none;"); Relayout(body); Assert.Equal(0, Count(body, Colors.Red)); Assert.Same(cell, body.TableCells[0].Source);
    }

    [Fact]
    public void OversizedLineContinuationPreservesTheWholePaintedLineBox()
    {
        var p = Paragraph("超高行", 1); p.LineHeight = 280; p.Background = Brushes.Red; var doc = Doc(p); doc.PageHeight = 100;
        using var reference = Body(doc); var expected = Count(reference, Colors.Red); var actual = 0;
        for (var i = 0; i < doc.ViewerPaginator.PageCount; i++)
        { using var page = Assert.IsType<FlowDocumentPresentation>(doc.ViewerPaginator.GetPage(i).Visual); actual += Count(page, Colors.Red); }
        Assert.True(expected > 1000); Assert.Equal(expected, actual);
    }

    [Fact]
    public void ProgrammaticTablesMatchTypesAndBaseTypesWithoutLoadingXaml()
    {
        var resolver = TypeResolver.ResolveTypeByName;
        try
        {
            TypeResolver.ResolveTypeByName = null;
            var cell = new TableCell(Paragraph("原文", 1)); var table = Table(Row(cell, new TableCell(Paragraph("乙", 1))));
            table.Columns.Add(new DerivedColumn()); table.Columns.Add(new TableColumn()); var doc = Doc(table);
            Css.SetStyleSheet(doc, "Table{width:132px;table-layout:fixed;border-spacing:4px;}TableColumn{width:60px;}TableCell{padding:8px;border:2px solid red;}");
            using var body = Body(doc); Assert.Equal(60, body.TableCells[0].Bounds.Width, 3); Assert.Equal(new Thickness(8), cell.Padding);
            Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(cell.BorderBrush).Color); Assert.Equal(4, body.TableCells[0].Bounds.Left);
        }
        finally { TypeResolver.ResolveTypeByName = resolver; }
    }
    private sealed class DerivedColumn : TableColumn;

    [Fact]
    public void TableContentBoxWidthIncludesItsOwnPaddingAndBorderOutsideTheSpecifiedSize()
    {
        var table = Table(Row(Cell("甲"), Cell("乙"))); var doc = Doc(table);
        Css.SetStyle(table, "width:200px;box-sizing:content-box;padding:10px;border:2px solid green;table-layout:fixed;");
        using var body = Body(doc); Assert.Equal(12, body.TableCells[0].Bounds.Left, 3); Assert.Equal(100, body.TableCells[0].Bounds.Width, 3); Assert.Equal(212, body.TableCells[1].Bounds.Right, 3);
    }

    [Theory]
    [InlineData("border-box", 200d, 88d)] [InlineData("content-box", 224d, 100d)]
    public void TableHeightIsAMinimumDistributedAcrossRowsAndIncludesTheCorrectInsets(string sizing, double expected, double rowHeight)
    {
        var table = Table(Row(Cell("甲")), Row(Cell("乙"))); var doc = Doc(table);
        Css.SetStyle(table, $"height:200px;box-sizing:{sizing};padding:10px;border:2px solid green;table-layout:fixed;");
        using var body = Body(doc); Assert.Equal(expected, body.ExtentHeight, 3); Assert.All(body.TableCells, cell => Assert.Equal(rowHeight, cell.Bounds.Height, 3));
    }

    [Fact]
    public void TableOuterInsetsArePreservedAcrossAllPages()
    {
        var table = Table(Row(Cell("甲", 3)), Row(Cell("乙", 3))); var doc = Doc(table); doc.PageHeight = 100;
        Css.SetStyle(table, "height:224px;box-sizing:border-box;padding:10px;border:2px solid red;background-color:red;");
        using var reference = Body(doc); var expected = Count(reference, Colors.Red); var actual = 0;
        for (var i = 0; i < doc.ViewerPaginator.PageCount; i++)
        { using var page = Assert.IsType<FlowDocumentPresentation>(doc.ViewerPaginator.GetPage(i).Visual); actual += Count(page, Colors.Red); }
        Assert.True(expected > 1000); Assert.Equal(expected, actual);
    }

    private static FlowDocument Doc(params Block[] blocks)
    { var doc = new FlowDocument { FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24, PageWidth = 320, PageHeight = 180, PagePadding = new(0), Foreground = Brushes.Black }; foreach (var block in blocks) doc.Blocks.Add(block); return doc; }
    private static Paragraph Paragraph(string text, int lines)
    { var p = new Paragraph { Margin = new(0) }; for (var i = 0; i < lines; i++) { if (i > 0) p.Inlines.Add(new LineBreak()); p.Inlines.Add(new Run(text + i)); } return p; }
    private static TableCell Cell(string text, int lines = 1) => new(Paragraph(text, lines)) { Padding = new(0), BorderThickness = new(0) };
    private static TableRow Row(params TableCell[] cells) { var row = new TableRow(); foreach (var cell in cells) row.Cells.Add(cell); return row; }
    private static Table Table(params TableRow[] rows) { var table = new Table { CellSpacing = 0 }; var group = new TableRowGroup(); foreach (var row in rows) group.Rows.Add(row); table.RowGroups.Add(group); return table; }
    private static FlowDocumentPresentation Body(FlowDocument doc) { var body = new FlowDocumentPresentation(); body.SetDocument(doc); Relayout(body); return body; }
    private static void Relayout(FlowDocumentPresentation body) { Flush(); body.Measure(new(320, 1500)); body.Arrange(new(0, 0, 320, Math.Max(1, body.DesiredSize.Height))); }
    private static void Flush() { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); }
    private static int Count(FrameworkElement view, Color color)
    {
        var width = (int)Math.Ceiling(view.ActualWidth); var height = Math.Max(1, (int)Math.Ceiling(view.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Bgra32); var drawing = new SoftwareDrawingContext(bitmap); view.Render(drawing); drawing.Close();
        var pixels = new byte[width * height * 4]; bitmap.CopyPixels(pixels, width * 4, 0); var count = 0;
        for (var i = 0; i < pixels.Length; i += 4) if (pixels[i] == color.B && pixels[i + 1] == color.G && pixels[i + 2] == color.R && pixels[i + 3] == color.A) count++;
        return count;
    }
}
