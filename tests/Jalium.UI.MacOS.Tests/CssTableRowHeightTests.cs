using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssTableRowHeightTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData(false, "height:20%;", "", 40, 160)] [InlineData(true, "height:20%;", "", 40, 160)]
    [InlineData(false, "height:40px;", "", 40, 160)] [InlineData(true, "height:40px;", "", 40, 160)]
    [InlineData(false, "height:40px;", "height:60px;", 80, 120)] [InlineData(true, "height:40px;", "height:60px;", 80, 120)]
    [InlineData(false, "height:80%;", "height:80%;", 160, 40)] [InlineData(true, "height:80%;", "height:80%;", 160, 40)]
    [InlineData(false, "height:0%;", "", 20, 180)] [InlineData(true, "height:0%;", "", 20, 180)]
    [InlineData(false, "height:calc(50% + 20px);", "", 120, 80)] [InlineData(true, "height:calc(50% + 20px);", "", 120, 80)]
    public void RowRequestsUseTheTableHeightBeforeAllocatingRemainingSpace(bool document, string first, string second, double a, double b)
    {
        using var view = new Fixture(document, 20, 20); view.SetRow(0, first); view.SetRow(1, second); view.Layout();
        Assert.Equal(a, view.Bounds(0).Height, 2); Assert.Equal(b, view.Bounds(1).Height, 2); Assert.Equal(200, view.GridExtent, 2);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PercentageDeficitsShareInsufficientSpaceWithoutShrinkingFixedOrContentMinima(bool document)
    {
        using var view = new Fixture(document, 20, 20, 20); view.SetRow(0, "height:80%;"); view.SetRow(1, "height:80%;"); view.SetRow(2, "height:40px;"); view.Layout();
        Assert.Equal(125, view.Bounds(0).Height, 2); Assert.Equal(35, view.Bounds(1).Height, 2); Assert.Equal(40, view.Bounds(2).Height, 2);
        Assert.Equal(200, view.GridExtent, 2);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AutoRowsGrowInProportionToTheirNonemptyContentAndSkipEmptyRows(bool document)
    {
        using var view = new Fixture(document, 20, 60, 0); view.Layout();
        Assert.Equal(50, view.Bounds(0).Height, 2); Assert.Equal(150, view.Bounds(1).Height, 2); Assert.Equal(0, view.Bounds(2).Height, 2);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void EmptyAutoRowsReceiveSurplusWhenEveryNonemptyRowIsSpecified(bool document)
    {
        using var view = new Fixture(document, 20, 0, 0); view.SetRow(0, "height:40px;"); view.Layout();
        Assert.Equal(40, view.Bounds(0).Height, 2); Assert.Equal(80, view.Bounds(1).Height, 2); Assert.Equal(80, view.Bounds(2).Height, 2);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SingleRowCellPercentageJoinsTheMaximumRequestForItsRow(bool document)
    {
        using var view = new Fixture(document, 20, 20); view.SetRow(0, "height:20%;"); view.SetCell(0, "height:60%;"); view.Layout();
        Assert.Equal(120, view.Bounds(0).Height, 2); Assert.Equal(80, view.Bounds(1).Height, 2);
        view.SetCell(0, "height:10%;"); view.Layout(); Assert.Equal(40, view.Bounds(0).Height, 2); Assert.Equal(160, view.Bounds(1).Height, 2);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RowPercentagesUseTheGridHeightIncludingItsSpacing(bool document)
    {
        using var view = new Fixture(document, 20, 20); view.SetTable("height:200px;border-spacing:10px;"); view.SetRow(0, "height:50%;"); view.Layout();
        Assert.Equal(10, view.Bounds(0).Top, 2); Assert.Equal(100, view.Bounds(0).Height, 2); Assert.Equal(120, view.Bounds(1).Top, 2); Assert.Equal(70, view.Bounds(1).Height, 2); Assert.Equal(200, view.GridExtent, 2);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NativeAutoAndDefaultValuedOverridesAndClearReallocateLiveRows(bool document)
    {
        using var view = new Fixture(document, 20, 20); view.SetRow(0, "height:20%;"); view.Layout(); Assert.Equal(40, view.Bounds(0).Height, 2);
        view.Rows[0].SetValue(FrameworkElement.HeightProperty, 80d); view.Layout(); Assert.Equal(80, view.Bounds(0).Height, 2);
        view.Rows[0].SetValue(FrameworkElement.HeightProperty, double.NaN); view.Layout(); Assert.Equal(100, view.Bounds(0).Height, 2);
        view.Rows[0].SetValue(FrameworkElement.HeightProperty, 0d); view.Layout(); Assert.Equal(20, view.Bounds(0).Height, 2);
        view.Rows[0].ClearValue(FrameworkElement.HeightProperty); view.Layout(); Assert.Equal(40, view.Bounds(0).Height, 2);
        view.SetRow(0, ""); view.SetCell(0, "height:60%;"); view.Layout(); Assert.Equal(120, view.Bounds(0).Height, 2);
        view.Cells[0].SetValue(FrameworkElement.HeightProperty, double.NaN); view.Layout(); Assert.Equal(100, view.Bounds(0).Height, 2);
        view.Cells[0].ClearValue(FrameworkElement.HeightProperty); view.Layout(); Assert.Equal(120, view.Bounds(0).Height, 2);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void VisibilityProjectionFollowsFullPercentageDistributionAndRestoresTheSameSources(bool document)
    {
        using var view = new Fixture(document, 20, 20); view.SetRow(0, "height:20%;"); view.Layout(); var second = view.Bounds(1); var parent = view.ParentOfCell(0);
        view.SetRow(0, "height:20%;visibility:collapse;"); view.Layout(); Assert.Equal(0, view.Bounds(0).Height, 2); Assert.Equal(160, view.Bounds(1).Height, 2); Assert.Equal(0, view.Bounds(1).Top, 2);
        view.SetRow(0, "height:20%;"); view.Layout(); Assert.Equal(second, view.Bounds(1)); Assert.Same(parent, view.ParentOfCell(0));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PercentageContentBoxCellsIncludeTheirInsetsInTheRowRequest(bool document)
    {
        using var view = new Fixture(document, 20, 20); view.ClearCellInsets(0); view.SetCell(0, "height:50%;padding:10px;border:2px solid blue;box-sizing:content-box;"); view.Layout();
        Assert.Equal(124, view.Bounds(0).Height, 2); Assert.Equal(76, view.Bounds(1).Height, 2);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AutoTablePercentagesDoNotInventExtraHeightAndTallContentStaysAMinimum(bool document)
    {
        using var view = new Fixture(document, 20, 60); view.SetTable("height:auto;"); view.SetRow(0, "height:80%;"); view.Layout();
        Assert.Equal(20, view.Bounds(0).Height, 2); Assert.Equal(60, view.Bounds(1).Height, 2); Assert.Equal(80, view.GridExtent, 2);
        view.SetContentHeight(1, 240); view.SetTable("height:200px;"); view.Layout(); Assert.Equal(20, view.Bounds(0).Height, 2); Assert.Equal(240, view.Bounds(1).Height, 2); Assert.Equal(260, view.GridExtent, 2);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SpanningNaturalHeightGrowsTheUnspecifiedRowBeforeAConstrainedRow(bool percentage)
    {
        TableCell Cell(double height) { var content = new Section(); Css.SetStyle(content, $"height:{height}px;"); var cell = new TableCell(content) { Padding = new(0), BorderThickness = new(0) }; Css.SetStyle(cell, "vertical-align:top;"); return cell; }
        var span = Cell(percentage ? 20 : 120); span.RowSpan = 2; if (percentage) Css.SetStyle(span, "height:80%;vertical-align:top;");
        var a = Cell(20); var b = Cell(20); var first = new TableRow(); first.Cells.Add(span); first.Cells.Add(a); var second = new TableRow(); second.Cells.Add(b);
        var group = new TableRowGroup(); group.Rows.Add(first); group.Rows.Add(second); var table = new Table { CellSpacing = 0 }; table.RowGroups.Add(group);
        if (percentage) Css.SetStyle(table, "height:200px;"); else Css.SetStyle(first, "height:40px;");
        var doc = new FlowDocument(table) { PagePadding = new(0), PageWidth = 400, PageHeight = 144 }; Css.SetStyleSheet(doc, "Table,Section{margin:0;}");
        using var body = new FlowDocumentPresentation(); body.SetDocument(doc); body.Measure(new(400, double.PositiveInfinity)); body.Arrange(new(0, 0, 400, body.DesiredSize.Height));
        Assert.Equal(percentage ? 100 : 40, body.TableCells.Single(cell => ReferenceEquals(cell.Source, a)).Bounds.Height, 2);
        Assert.Equal(percentage ? 100 : 80, body.TableCells.Single(cell => ReferenceEquals(cell.Source, b)).Bounds.Height, 2);
        Assert.Equal(percentage ? 200 : 120, body.TableCells.Single(cell => ReferenceEquals(cell.Source, span)).Bounds.Height, 2); Assert.Same(first, span.Parent);
    }

    [Fact]
    public void ImmediateSearchAfterATableChangeUsesDocumentPositionsWithoutAnotherArrangePass()
    {
        var first = new Paragraph(new Run("前文")); var target = new Paragraph(new Run("目标原文"));
        var a = new TableRow(); a.Cells.Add(new TableCell(first) { Padding = new(0), BorderThickness = new(0) });
        var b = new TableRow(); b.Cells.Add(new TableCell(target) { Padding = new(0), BorderThickness = new(0) });
        var group = new TableRowGroup(); group.Rows.Add(a); group.Rows.Add(b); var table = new Table { CellSpacing = 0 }; table.RowGroups.Add(group);
        Css.SetStyle(a, "height:120px;"); Css.SetStyle(b, "height:120px;");
        var doc = new FlowDocument(table) { PagePadding = new(0), PageWidth = 400, PageHeight = 144, FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24 };
        Css.SetStyleSheet(doc, "Paragraph,Table{margin:0;}");
        var viewer = new FlowDocumentScrollViewer { Document = doc, Height = 180, IsToolBarVisible = false, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
        viewer.Measure(new(400, 180)); viewer.Arrange(new(0, 0, 400, 180));
        Css.SetStyle(table, "height:300px;"); Assert.True(viewer.Find("目标"));
        FlowDocumentPresentation? body = null;
        void Visit(Visual parent) { for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) if (VisualTreeHelper.GetChild(parent, index) is Visual child) { if (child is FlowDocumentPresentation presentation) body = presentation; Visit(child); } }
        Visit(viewer); Assert.NotNull(body); Assert.Equal(150, body.Paragraphs[1].TransformToAncestor(body).Y, 2); Assert.Equal("目标", viewer.Selection!.Text);
        Assert.Same(target.Inlines, body.Paragraphs[1].Inlines); viewer.Document = null;
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void EmptyRowHeightIsFragmentableSpaceInsteadOfAnOversizedTextLine(bool emptyCell)
    {
        var row = new TableRow(); if (emptyCell) row.Cells.Add(new TableCell { Padding = new(0), BorderThickness = new(0) }); Css.SetStyle(row, "height:300px;");
        var group = new TableRowGroup(); group.Rows.Add(row); var table = new Table { CellSpacing = 0 }; table.RowGroups.Add(group);
        var doc = new FlowDocument(table) { PagePadding = new(0), PageWidth = 400, PageHeight = 72 }; Css.SetStyleSheet(doc, "Table{margin:0;}");
        Assert.Equal(5, doc.ViewerPaginator.PageCount);
    }

    [Fact]
    public void DistributedRowWhitespaceDoesNotStretchTheFinalGlyphIntoExtraPages()
    {
        var group = new TableRowGroup(); var table = new Table { CellSpacing = 0 }; table.RowGroups.Add(group); Css.SetStyle(table, "height:480px;");
        for (var index = 0; index < 3; index++) { var paragraph = new Paragraph(new Run(string.Join('\n', Enumerable.Range(index * 4, 4).Select(line => $"第{line:00}行")))) { LineHeight = 24 };
            var row = new TableRow(); row.Cells.Add(new TableCell(paragraph) { Padding = new(0), BorderThickness = new(0) }); group.Rows.Add(row); if (index != 1) Css.SetStyle(row, "height:25%;"); }
        var doc = new FlowDocument(table) { PagePadding = new(0), PageWidth = 400, PageHeight = 144, FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24 };
        doc.Blocks.Add(new Paragraph(new Run("末段")) { LineHeight = 24 }); Css.SetStyleSheet(doc, "FlowDocument{white-space:pre-wrap;}Table,Paragraph{margin:0;}");
        Assert.Equal(4, doc.ViewerPaginator.PageCount);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly bool _document;
        private readonly FlowDocumentPresentation? _body;
        private readonly FrameworkElement _root;
        private readonly DependencyObject _table;
        private readonly DependencyObject[] _contents;
        internal readonly DependencyObject[] Rows, Cells;
        internal Fixture(bool document, params double[] heights)
        {
            _document = document; Rows = new DependencyObject[heights.Length]; Cells = new DependencyObject[heights.Length]; _contents = new DependencyObject[heights.Length];
            if (document)
            {
                var table = new Table { CellSpacing = 0 }; var group = new TableRowGroup(); table.RowGroups.Add(group); _table = table;
                for (var index = 0; index < heights.Length; index++)
                {
                    var content = new Section(); var cell = new TableCell(content) { Padding = new(0), BorderThickness = new(0) }; var row = new TableRow(); row.Cells.Add(cell); group.Rows.Add(row);
                    Rows[index] = row; Cells[index] = cell; _contents[index] = content; SetContentHeight(index, heights[index]);
                }
                var doc = new FlowDocument(table) { PageWidth = 400, PageHeight = 144, PagePadding = new(0), FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24 };
                Css.SetStyleSheet(doc, "Paragraph,Section,Table{margin:0;}"); _body = new(); _body.SetDocument(doc); _root = _body;
            }
            else
            {
                var table = new StackPanel(); _table = table; _root = table;
                for (var index = 0; index < heights.Length; index++)
                {
                    var content = new Border(); var cell = new Border { Child = content, Padding = new(0), BorderThickness = new(0) }; var row = new StackPanel(); row.Children.Add(cell); table.Children.Add(row);
                    Rows[index] = row; Cells[index] = cell; _contents[index] = content; SetContentHeight(index, heights[index]); SetRow(index, ""); SetCell(index, "");
                }
            }
            SetTable("height:200px;");
        }
        internal void SetTable(string style) => Css.SetStyle(_table, (_document ? "" : "display:table;width:400px;table-layout:fixed;") + style);
        internal void SetRow(int index, string style) => Css.SetStyle(Rows[index], (_document ? "" : "display:table-row;") + style);
        internal void SetCell(int index, string style) => Css.SetStyle(Cells[index], (_document ? "" : "display:table-cell;") + style);
        internal void SetContentHeight(int index, double height) => Css.SetStyle(_contents[index], $"height:{height}px;");
        internal void ClearCellInsets(int index) { Cells[index].ClearValue(_document ? TableCell.PaddingProperty : Border.PaddingProperty); Cells[index].ClearValue(_document ? TableCell.BorderThicknessProperty : Border.BorderThicknessProperty); }
        internal object? ParentOfCell(int index) => _document ? ((TableCell)Cells[index]).Parent : ((FrameworkElement)Cells[index]).VisualParent;
        internal void Layout() { for (var index = 0; index < 3; index++) { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); _root.Measure(new(400, double.PositiveInfinity)); _root.Arrange(new(0, 0, 400, Math.Max(1, _root.DesiredSize.Height))); _root.UpdateLayout(); } }
        internal Rect Bounds(int index) => _document ? _body!.TableCells.Single(cell => ReferenceEquals(cell.Source, Cells[index])).Bounds : new(((FrameworkElement)Cells[index]).TransformToAncestor(_root), ((FrameworkElement)Cells[index]).RenderSize);
        internal double GridExtent => _document ? _body!.ExtentHeight : _root.ActualHeight;
        public void Dispose() => _body?.Dispose();
    }
}
