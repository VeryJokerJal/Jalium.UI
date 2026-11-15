using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssNestedTableIntrinsicTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OuterCellCombinesInnerColumnMaximaAcrossDifferentRows(bool document)
    {
        using var view = CrossRows(document); view.Layout(); view.AssertOuter(200, 40); view.AssertRow(0, 100, 100); view.AssertRow(1, 100, 100);
        var parent = view.Parent(view.InnerCells[0]); view.ContentWidth(view.InnerCells[2], 140); view.Layout(); view.AssertOuter(240, 40); view.AssertRow(0, 140, 100);
        view.ContentWidth(view.InnerCells[2], 20); view.Layout(); view.AssertOuter(120, 40); view.AssertRow(0, 20, 100); Assert.Same(parent, view.Parent(view.InnerCells[0]));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NativeAndCssColumnDefinitionsReachTheOuterIntrinsicMaximum(bool document)
    {
        using var view = new Fixture(document); view.Row(20, 20); view.ColumnStyle(0, "width:40px;"); view.ColumnStyle(1, "width:60px;"); view.Layout(); view.AssertOuter(100, 40); view.AssertRow(0, 40, 60);
        view.ColumnStyle(0, "width:100px;"); view.Layout(); view.AssertOuter(160, 40); view.AssertRow(0, 100, 60);
        view.NativeColumnWidth(0, 140); view.Layout(); view.AssertOuter(200, 40); view.AssertRow(0, 140, 60);
        view.ClearNativeColumnWidth(0); view.Layout(); view.AssertOuter(160, 40);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void EmptyDeclaredColumnsStillContributeTheirOwnLengthConstraints(bool document)
    {
        using var view = new Fixture(document); view.Row(20, 20); view.ColumnStyle(0, "width:40px;"); view.ColumnStyle(1, "width:60px;"); view.ColumnStyle(2, "width:80px;"); view.Layout(); view.AssertOuter(180, 40); view.AssertRow(0, 40, 60);
        view.ColumnStyle(2, "width:auto;"); view.Layout(); view.AssertOuter(100, 40);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RowspansUseTheSamePlacedColumnsAsTheInnerLayout(bool document)
    {
        using var view = new Fixture(document); view.AddRow((100, 1, 2), (20, 1, 1)); view.Row(100); view.Layout(); view.AssertOuter(200, 40);
        Assert.Equal(100, view.Bounds(view.InnerCells[0]).Width, 2); Assert.Equal(100, view.Bounds(view.InnerCells[1]).Width, 2); Assert.Equal(100, view.Bounds(view.InnerCells[2]).Width, 2);
        Assert.Equal(view.Bounds(view.InnerCells[1]).Left, view.Bounds(view.InnerCells[2]).Left, 2);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void OverlappingSpansContributeTheResolvedGridRatherThanTheWidestRow(bool document, bool reverse)
    {
        using var view = new Fixture(document); view.Row(20, 60, 40);
        if (reverse) { view.AddRow((0, 1, 1), (240, 2, 1)); view.AddRow((160, 2, 1)); }
        else { view.AddRow((160, 2, 1)); view.AddRow((0, 1, 1), (240, 2, 1)); }
        view.Layout(); view.AssertOuter(280, 40); view.AssertRow(0, 40, 180, 60);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void InnerSpacingAndTableInsetsAreCountedExactlyOnce(bool document)
    {
        using var view = CrossRows(document); view.InnerStyle("width:auto;border-spacing:10px;"); view.Layout(); view.AssertOuter(230, 40);
        view.InnerStyle("width:auto;padding:8px;border:2px solid green;margin:3px;"); view.Layout(); view.AssertOuter(226, 40); Assert.Equal(13, view.Bounds(view.InnerCells[0]).Left - view.Bounds(view.Container).Left, 2);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void InnerCellPaddingAndBordersContributeToTheirPlacedColumns(bool document)
    {
        using var view = CrossRows(document); foreach (var cell in view.InnerCells) view.CellStyle(cell, "padding:10px;border:2px solid green;", clearInsets: true);
        view.Layout(); view.AssertOuter(248, 40); view.AssertRow(0, 124, 124);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FixedAndMaximumTableWidthsCannotDiscardInnerContentMinimums(bool document)
    {
        using var view = CrossRows(document); view.InnerStyle("width:240px;"); view.Layout(); view.AssertOuter(240, 40); view.AssertRow(0, 120, 120);
        view.InnerStyle("width:auto;max-width:120px;"); view.Layout(); view.AssertOuter(200, 40); view.AssertRow(0, 100, 100);
        view.InnerStyle("width:120px;"); view.Layout(); view.AssertOuter(200, 40);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NestedCollapsedBordersUseTheResolvedHalfEdgesInTheirIntrinsicContribution(bool document)
    {
        using var view = CrossRows(document); foreach (var cell in view.InnerCells) view.CellStyle(cell, "padding:0;border:2px solid green;", clearInsets: true);
        view.InnerStyle("width:auto;border:2px solid green;border-collapse:collapse;"); view.Layout(); view.AssertOuter(206, 40); view.AssertRow(0, 102, 102);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ContentBoxWidthAddsItsOwnTableChromeOnce(bool document)
    {
        using var view = CrossRows(document); view.InnerStyle("width:240px;box-sizing:content-box;padding:8px;border:2px solid green;"); view.Layout(); view.AssertOuter(260, 40); view.AssertRow(0, 120, 120);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CyclicColumnPercentagesResolveOnlyAfterTheOuterAllocation(bool document)
    {
        using var view = new Fixture(document); view.Row(20, 60); view.ColumnStyle(0, "width:75%;"); view.ColumnStyle(1, "width:auto;"); view.Layout(); view.AssertOuter(80, 40); view.AssertRow(0, 20, 60);
        view.OuterStyle("width:400px;"); view.Layout(); view.AssertOuter(800d / 3, 400d / 3); view.AssertRow(0, 180, 60);
        view.OuterStyle("width:auto;"); view.Layout(); view.AssertOuter(80, 40); view.AssertRow(0, 20, 60);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CyclicTableWidthDoesNotFeedBackItsUsedOverflowIntoTheOuterIntrinsicWidth(bool document)
    {
        using var view = new Fixture(document); view.Row(20, 60); view.InnerStyle("width:200%;"); view.Layout(); view.AssertOuter(80, 40); view.AssertRow(0, 40, 120);
        view.InnerStyle("width:50%;"); view.Layout(); view.AssertOuter(80, 40); view.AssertRow(0, 20, 60);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CollapsedInnerColumnsProjectTheContributionWithoutReflowingRemainingCells(bool document)
    {
        using var view = CrossRows(document); view.ColumnStyle(0, "width:auto;"); view.ColumnStyle(1, "width:auto;"); view.Layout(); view.AssertOuter(200, 40);
        view.ColumnStyle(0, "width:auto;visibility:collapse;"); view.Layout(); view.AssertOuter(100, 40); Assert.Equal(100, view.Bounds(view.InnerCells[1]).Width, 2);
        view.ColumnStyle(0, "width:auto;visibility:visible;"); view.Layout(); view.AssertOuter(200, 40); view.AssertRow(0, 100, 100);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SeveralNestedTablesCarryTheirColumnMeasuresThroughEveryWrapper(bool document)
    {
        using var view = CrossRows(document); view.Wrap(3); view.Layout(); view.AssertOuter(218, 40); view.AssertRow(0, 100, 100);
        view.ContentWidth(view.InnerCells[2], 140); view.Layout(); view.AssertOuter(258, 40);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RtlPlacesNestedOriginalCellsWithinTheResolvedOuterColumn(bool document)
    {
        using var view = CrossRows(document); view.OuterStyle("width:auto;direction:rtl;"); view.Layout(); view.AssertOuter(200, 40); view.AssertRow(0, 100, 100);
        Assert.True(view.Bounds(view.InnerCells[0]).Left > view.Bounds(view.InnerCells[1]).Left); Assert.True(view.Bounds(view.Container).Left > view.Bounds(view.Side).Left);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AddingAndRemovingAnInnerRowInvalidatesExistingAncestorMeasures(bool document)
    {
        using var view = new Fixture(document); view.Row(20, 100); view.Layout(); view.AssertOuter(120, 40);
        view.Row(100, 20); view.Layout(); view.AssertOuter(200, 40); view.RemoveLastRow(); view.Layout(); view.AssertOuter(120, 40);
    }

    private static Fixture CrossRows(bool document) { var view = new Fixture(document); view.Row(20, 100); view.Row(100, 20); return view; }

    private sealed class Fixture : IDisposable
    {
        private readonly bool _document;
        private readonly FrameworkElement _root;
        private readonly FlowDocumentPresentation? _body;
        private readonly DependencyObject _outer, _inner;
        private readonly Dictionary<DependencyObject, DependencyObject> _contents = [];
        private readonly List<DependencyObject> _columns = [], _rows = [];
        private readonly List<DependencyObject[]> _rowCells = [];
        internal readonly List<DependencyObject> InnerCells = [];
        internal readonly DependencyObject Container, Side;
        internal Fixture(bool document)
        {
            _document = document; _inner = Table(); _outer = Table();
            Container = Cell(_inner); Side = ContentCell(40); AddTableRow(_outer, [Container, Side]);
            if (document) { var doc = new FlowDocument((Block)_outer) { PagePadding = new(0), PageWidth = 600 }; Css.SetStyleSheet(doc, "Table,Section,Paragraph{margin:0;}"); _body = new(); _body.SetDocument(doc); _root = _body; }
            else _root = (FrameworkElement)_outer;
        }
        private DependencyObject Table()
        {
            DependencyObject table = _document ? new Table { CellSpacing = 0 } : new StackPanel();
            Css.SetStyle(table, (_document ? "" : "display:table;") + "width:auto;table-layout:auto;"); return table;
        }
        private DependencyObject Cell(DependencyObject content, int columns = 1, int rows = 1)
        {
            DependencyObject cell;
            if (_document) cell = new TableCell((Block)content) { Padding = new(0), BorderThickness = new(0), ColumnSpan = columns, RowSpan = rows };
            else { var border = new Border { Child = (UIElement)content, Padding = new(0), BorderThickness = new(0) }; Grid.SetColumnSpan(border, columns); Grid.SetRowSpan(border, rows); cell = border; }
            _contents[cell] = content; CellStyle(cell, ""); return cell;
        }
        private DependencyObject ContentCell(double width, int columns = 1, int rows = 1)
        { DependencyObject content = _document ? new Section() : new Border(); var cell = Cell(content, columns, rows); ContentWidth(cell, width); return cell; }
        private DependencyObject AddTableRow(DependencyObject table, DependencyObject[] cells)
        {
            if (_document) { var source = (Table)table; if (source.RowGroups.Count == 0) source.RowGroups.Add(new()); var row = new TableRow(); foreach (var cell in cells) row.Cells.Add((TableCell)cell); source.RowGroups[0].Rows.Add(row); return row; }
            else { var row = new StackPanel(); Css.SetStyle(row, "display:table-row;"); foreach (var cell in cells) row.Children.Add((UIElement)cell); ((StackPanel)table).Children.Add(row); return row; }
        }
        internal void Row(params double[] widths) => AddRow(widths.Select(width => (width, 1, 1)).ToArray());
        internal void AddRow(params (double Width, int Columns, int Rows)[] specs)
        { var cells = specs.Select(spec => ContentCell(spec.Width, spec.Columns, spec.Rows)).ToArray(); InnerCells.AddRange(cells); _rowCells.Add(cells); _rows.Add(AddTableRow(_inner, cells)); }
        internal void RemoveLastRow()
        { var index = _rows.Count - 1; if (_document) ((Table)_inner).RowGroups[0].Rows.Remove((TableRow)_rows[index]); else ((StackPanel)_inner).Children.Remove((UIElement)_rows[index]); foreach (var cell in _rowCells[index]) InnerCells.Remove(cell); _rows.RemoveAt(index); _rowCells.RemoveAt(index); }
        internal void InnerStyle(string style) => Css.SetStyle(_inner, (_document ? "" : "display:table;") + "table-layout:auto;" + style);
        internal void OuterStyle(string style) => Css.SetStyle(_outer, (_document ? "" : "display:table;") + "table-layout:auto;" + style);
        internal void ContentWidth(DependencyObject cell, double width) => Css.SetStyle(_contents[cell], $"width:{width}px;height:20px;");
        internal void CellStyle(DependencyObject cell, string style, bool clearInsets = false)
        { if (clearInsets) { cell.ClearValue(_document ? TableCell.PaddingProperty : Border.PaddingProperty); cell.ClearValue(_document ? TableCell.BorderThicknessProperty : Border.BorderThicknessProperty); } Css.SetStyle(cell, (_document ? "" : "display:table-cell;") + "vertical-align:top;" + style); }
        internal void ColumnStyle(int index, string style)
        {
            while (_columns.Count <= index)
            {
                DependencyObject column = _document ? new TableColumn() : new Border(); Css.SetStyle(column, (_document ? "" : "display:table-column;") + "width:auto;");
                if (_document) ((Table)_inner).Columns.Add((TableColumn)column); else ((StackPanel)_inner).Children.Insert(_columns.Count, (UIElement)column); _columns.Add(column);
            }
            Css.SetStyle(_columns[index], (_document ? "" : "display:table-column;") + style);
        }
        internal void NativeColumnWidth(int index, double width)
        { if (_document) ((TableColumn)_columns[index]).Width = new(width); else _columns[index].SetValue(FrameworkElement.WidthProperty, width); }
        internal void ClearNativeColumnWidth(int index)
        { _columns[index].ClearValue(_document ? TableColumn.WidthProperty : FrameworkElement.WidthProperty); }
        internal void Wrap(int levels)
        {
            if (_document) ((TableCell)Container).Blocks.Clear(); else ((Border)Container).Child = null;
            var child = _inner;
            for (var level = 0; level < levels; level++) { var table = Table(); Css.SetStyle(table, (_document ? "" : "display:table;") + "width:auto;padding:3px;"); AddTableRow(table, [Cell(child)]); child = table; }
            if (_document) ((TableCell)Container).Blocks.Add((Block)child); else ((Border)Container).Child = (UIElement)child;
        }
        internal void Layout()
        { for (var pass = 0; pass < 3; pass++) { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); _root.Measure(new(600, double.PositiveInfinity)); _root.Arrange(new(0, 0, _document ? 600 : _root.DesiredSize.Width, Math.Max(1, _root.DesiredSize.Height))); _root.UpdateLayout(); } }
        internal Rect Bounds(DependencyObject source) => _document ? _body!.TableCells.Single(cell => ReferenceEquals(cell.Source, source)).Bounds : new(((FrameworkElement)source).TransformToAncestor(_root), ((FrameworkElement)source).RenderSize);
        internal object? Parent(DependencyObject source) => _document ? ((TableCell)source).Parent : ((FrameworkElement)source).VisualParent;
        internal void AssertOuter(double first, double side) { Assert.Equal(first, Bounds(Container).Width, 2); Assert.Equal(side, Bounds(Side).Width, 2); }
        internal void AssertRow(int row, params double[] widths) { Assert.Equal(widths.Length, _rowCells[row].Length); for (var index = 0; index < widths.Length; index++) Assert.Equal(widths[index], Bounds(_rowCells[row][index]).Width, 2); }
        public void Dispose() => _body?.Dispose();
    }
}
