using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssTablePercentColumnTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData(false, false, 25, 20, 60)] [InlineData(true, false, 25, 20, 60)]
    [InlineData(false, true, 25, 20, 60)] [InlineData(true, true, 25, 20, 60)]
    [InlineData(false, false, 50, 60, 60)] [InlineData(true, false, 50, 60, 60)]
    [InlineData(false, true, 50, 60, 60)] [InlineData(true, true, 50, 60, 60)]
    [InlineData(false, false, 75, 180, 60)] [InlineData(true, false, 75, 180, 60)]
    [InlineData(false, true, 75, 180, 60)] [InlineData(true, true, 75, 180, 60)]
    [InlineData(false, false, 100, 340, 60)] [InlineData(true, false, 100, 340, 60)]
    [InlineData(false, true, 100, 340, 60)] [InlineData(true, true, 100, 340, 60)]
    public void AutoTableIntrinsicPercentagesDetermineShrinkWidthBeforePixelAllocation(bool document, bool column, double percent, double a, double b)
    {
        using var view = new Fixture(document, 20, 60); view.Request(column, 0, $"width:{percent}%;"); view.Layout();
        view.AssertColumns(a, b); Assert.Equal(a + b, view.GridExtent, 2);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public void SpecifiedTableWidthGrowsPercentThenAutomaticColumns(bool document, bool column)
    {
        using var view = new Fixture(document, 20, 60); view.SetTable("width:200px;"); view.Request(column, 0, "width:50%;"); view.Layout(); view.AssertColumns(100, 100);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public void OrderedPercentageBudgetKeepsOverconstrainedColumnsWithinTheirNaturalMinimums(bool document, bool column)
    {
        using var view = new Fixture(document, 20, 20); view.Request(column, 0, "width:80%;"); view.Request(column, 1, "width:80%;"); view.Layout();
        view.AssertColumns(80, 20); Assert.Equal(100, view.GridExtent, 2);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ZeroPercentColumnsKeepContentAndGiveSurplusToAutomaticColumns(bool document)
    {
        using var view = new Fixture(document, 20, 60); view.Request(false, 0, "width:0%;"); view.Layout(); view.AssertColumns(20, 60);
        view.SetTable("width:200px;"); view.Layout(); view.AssertColumns(20, 180);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AutomaticSurplusIsProportionalAndSpecifiedLengthsDoNotStealIt(bool document)
    {
        using var view = new Fixture(document, 20, 60); view.SetTable("width:200px;"); view.Layout(); view.AssertColumns(50, 150);
        view.Request(false, 0, "width:40px;"); view.Layout(); view.AssertColumns(40, 160);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void IntrinsicPercentageWidthsCountEveryOuterAndInnerGapExactlyOnce(bool document)
    {
        using var view = new Fixture(document, 20, 60); view.SetTable("width:auto;border-spacing:10px;"); view.Request(true, 0, "width:50%;"); view.Layout();
        view.AssertColumns(60, 60); Assert.Equal(10, view.Bounds(0).Left, 2); Assert.Equal(80, view.Bounds(1).Left, 2); Assert.Equal(150, view.GridExtent, 2);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AutomaticCellPercentageUsesBorderBoxButFixedContentBoxAddsInsets(bool document)
    {
        using var view = new Fixture(document, 20, 60); view.ClearCellInsets(0); view.Request(false, 0, "width:50%;box-sizing:content-box;padding:10px;border:2px solid green;"); view.Layout();
        view.AssertColumns(60, 60); Assert.Equal(120, view.GridExtent, 2);
        view.SetTable("width:200px;table-layout:fixed;"); view.Layout(); view.AssertColumns(124, 76);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ZeroPercentColumnDefinitionActsAsAutoInBothTableLayoutModes(bool document)
    {
        using var view = new Fixture(document, 20, 60); view.Request(true, 0, "width:0%;"); view.SetTable("width:200px;"); view.Layout(); view.AssertColumns(50, 150);
        view.SetTable("width:200px;table-layout:fixed;"); view.Layout(); view.AssertColumns(100, 100);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public void CssLengthConstraintsCanInterpolateBetweenContentMinimumAndSpecifiedMaximum(bool document, bool column)
    {
        using var view = new Fixture(document, 20, 60); view.Request(column, 0, "width:200px;"); view.SetTable("width:100px;"); view.Layout(); view.AssertColumns(40, 60);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public void PercentageMaxWidthLimitsTheContributionAndNativeInfinityCanClearIt(bool document, bool column)
    {
        using var view = new Fixture(document, 20, 60); var source = view.Request(column, 0, "width:50%;max-width:25%;"); view.Layout(); view.AssertColumns(20, 60);
        source.SetValue(FrameworkElement.MaxWidthProperty, double.PositiveInfinity); view.Layout(); view.AssertColumns(60, 60);
        source.ClearValue(FrameworkElement.MaxWidthProperty); view.Layout(); view.AssertColumns(20, 60);
    }

    [Fact]
    public void NativeGridLengthColumnOverridesCssIncludingAnUnchangedDefaultStar()
    {
        using var view = new Fixture(true, 20, 60); view.SetTable("width:200px;"); var column = (TableColumn)view.Request(true, 0, "width:40%;"); view.Layout(); view.AssertColumns(80, 120);
        column.Width = new GridLength(1, GridUnitType.Star); view.Layout(); view.AssertColumns(140, 60);
        column.Width = GridLength.Auto; view.Layout(); view.AssertColumns(50, 150);
        column.Width = new GridLength(40); view.Layout(); view.AssertColumns(40, 160);
        column.ClearValue(TableColumn.WidthProperty); view.Layout(); view.AssertColumns(80, 120);
    }

    [Fact]
    public void ColumnGroupWidthIsTheDefaultForEachChildColumn()
    {
        using var view = new Fixture(false, 20, 60, 40); view.GroupFirstColumns("width:40px;", 2); view.Layout(); view.AssertColumns(40, 60, 40);
        view.GroupFirstColumns("width:50%;", 2); view.Layout(); view.AssertColumns(177.5, 182.5, 40); Assert.Equal(400, view.GridExtent, 2);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public void NativeAutoZeroAndClearReplaceTheRawPercentageRequest(bool document, bool column)
    {
        using var view = new Fixture(document, 20, 60); view.SetTable("width:200px;"); var source = view.Request(column, 0, "width:40%;"); view.Layout(); view.AssertColumns(80, 120);
        source.SetValue(FrameworkElement.WidthProperty, double.NaN); view.Layout(); view.AssertColumns(50, 150);
        source.ClearValue(FrameworkElement.WidthProperty); view.Layout(); view.AssertColumns(80, 120);
        source.SetValue(FrameworkElement.WidthProperty, 40d); view.Layout(); view.AssertColumns(40, 160);
        source.SetValue(FrameworkElement.WidthProperty, 0d); view.Layout(); view.AssertColumns(20, 180);
        source.ClearValue(FrameworkElement.WidthProperty); view.Layout(); view.AssertColumns(80, 120);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PercentageAndContentChangesRecomputeTheSameSourceCells(bool document)
    {
        using var view = new Fixture(document, 20, 60); var parent = view.ParentOfCell(0);
        view.Request(false, 0, "width:25%;"); view.Layout(); view.AssertColumns(20, 60);
        view.Request(false, 0, "width:50%;"); view.Layout(); view.AssertColumns(60, 60);
        view.Request(false, 0, "width:75%;"); view.Layout(); view.AssertColumns(180, 60);
        view.Request(false, 0, "width:50%;"); view.SetContentWidth(0, 200); view.Layout(); view.AssertColumns(200, 200);
        view.SetContentWidth(0, 20); view.Layout(); view.AssertColumns(60, 60); Assert.Same(parent, view.ParentOfCell(0));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AvailableWidthLimitsIntrinsicMaximumButCannotShrinkNaturalMinimums(bool document)
    {
        using var view = new Fixture(document, 200, 60); view.Request(false, 0, "width:50%;"); view.Layout(); view.AssertColumns(200, 200);
        view.Available = 250; view.Layout(); view.AssertColumns(200, 60); Assert.Equal(260, view.GridExtent, 2);
        view.Available = 400; view.Layout(); view.AssertColumns(200, 200);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CollapsedPercentageColumnProjectsTheFullAllocationAndRestoresIt(bool document)
    {
        using var view = new Fixture(document, 20, 60); view.Request(true, 0, "width:50%;"); view.Layout(); var original = view.Bounds(1);
        view.Request(true, 0, "width:50%;visibility:collapse;"); view.Layout(); view.AssertColumns(0, 60); Assert.Equal(0, view.Bounds(1).Left, 2); Assert.Equal(60, view.GridExtent, 2);
        view.Request(true, 0, "width:50%;"); view.Layout(); Assert.Equal(original, view.Bounds(1));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public void SpanningPercentageContributionUsesNonspanningMaximumContentWeights(bool document, bool asymmetric)
    {
        using var view = new Fixture(document, 20, asymmetric ? 60 : 20, 40); view.PrependSpan("width:60%;", 2); view.Layout();
        if (asymmetric) { view.AssertColumns(20, 60, 160d / 3); Assert.Equal(80, view.SpanBounds.Width, 2); }
        else { view.AssertColumns(30, 30, 40); Assert.Equal(60, view.SpanBounds.Width, 2); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AffineCalcRetainsLengthAndPercentageUntilTheGridWidthIsKnown(bool document)
    {
        using var view = new Fixture(document, 20, 60); view.Request(false, 0, "width:calc(50% + 20px);"); view.Layout(); view.AssertColumns(100, 60);
        view.SetTable("width:200px;"); view.Layout(); view.AssertColumns(120, 80);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FiniteGridResolvesNonlinearMathWithoutInventingAPercentageCoefficient(bool document)
    {
        using var view = new Fixture(document, 20, 60); view.SetTable("width:200px;"); view.Request(false, 0, "width:clamp(20px,50%,120px);"); view.Layout(); view.AssertColumns(100, 100);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CancelledPercentageMathRetainsItsConstantWidthContribution(bool document)
    {
        using var view = new Fixture(document, 0, 0); view.Request(false, 0, "width:calc(0% + 20px);"); view.Request(false, 1, "width:calc(50% - 50% + 40px);");
        view.Layout(); view.AssertColumns(20, 40); Assert.Equal(60, view.GridExtent, 2);
        view.SetTable("width:200px;"); view.Layout(); view.AssertColumns(200d / 3, 400d / 3);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CompetingAffineRequestsKeepTheirOwnOffsetsAndCoefficients(bool document)
    {
        using var view = new Fixture(document, 20, 60); view.Available = 1000; view.Request(false, 0, "width:90%;"); var competing = view.AddCompetingRow("width:calc(10% + 100px);");
        view.Layout(); view.AssertColumns(540, 60); Assert.Equal(600, view.GridExtent, 2);
        Css.SetStyle(competing, (document ? "" : "display:table-cell;") + "width:calc(10% + 700px);"); view.Layout(); view.AssertColumns(7060d / 9, 60); Assert.Equal(7600d / 9, view.GridExtent, 2);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NativeStretchingHostsDoNotStretchAnAutomaticCssTableDuringArrange(bool scroll)
    {
        using var view = new Fixture(false, 20, 60); view.Request(true, 0, "width:50%;");
        FrameworkElement host = scroll ? new ScrollViewer { Content = view.Root, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden }
            : new Border { Child = view.Root };
        for (var pass = 0; pass < 3; pass++) { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); host.Measure(new(400, 200)); host.Arrange(new(0, 0, 400, 200)); host.UpdateLayout(); }
        view.AssertColumns(60, 60); Assert.Equal(120, view.Root.ActualWidth, 2); Assert.Equal(400, host.ActualWidth, 2);
        view.SetTable("width:auto;min-width:50%;");
        for (var pass = 0; pass < 3; pass++) { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); host.Measure(new(400, 200)); host.Arrange(new(0, 0, 400, 200)); host.UpdateLayout(); }
        view.AssertColumns(100, 100); Assert.Equal(200, view.Root.ActualWidth, 2);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AutomaticWidthStillHonorsTableBoundsAndContentBoxInsets(bool document)
    {
        using var view = new Fixture(document, 20, 60); view.Request(true, 0, "width:50%;"); view.SetTable("width:auto;min-width:200px;"); view.Layout(); view.AssertColumns(100, 100);
        view.SetTable("width:auto;max-width:100px;"); view.Layout(); view.AssertColumns(40, 60);
        view.SetTable("width:auto;max-width:30px;"); view.Layout(); view.AssertColumns(20, 60);
        view.SetTable("width:auto;min-width:200px;box-sizing:content-box;padding:10px;border:2px solid green;"); view.Layout(); view.AssertColumns(100, 100); Assert.Equal(12, view.Bounds(0).Left, 2);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly bool _document;
        private readonly FrameworkElement _root;
        private readonly DependencyObject _table;
        private readonly DependencyObject[] _contents;
        private readonly FlowDocumentPresentation? _body;
        private readonly FlowDocument? _doc;
        private readonly List<DependencyObject> _columns = [];
        private DependencyObject? _span;
        private StackPanel? _columnGroup;
        private double _gap;
        internal readonly DependencyObject[] Cells;
        internal double Available = 400;
        internal FrameworkElement Root => _root;
        internal Fixture(bool document, params double[] widths)
        {
            _document = document; Cells = new DependencyObject[widths.Length]; _contents = new DependencyObject[widths.Length];
            if (document)
            {
                var table = new Table { CellSpacing = 0 }; _table = table; var group = new TableRowGroup(); var row = new TableRow(); group.Rows.Add(row); table.RowGroups.Add(group);
                for (var index = 0; index < widths.Length; index++) { var content = new Section(); var cell = new TableCell(content) { Padding = new(0), BorderThickness = new(0) }; row.Cells.Add(cell); Cells[index] = cell; _contents[index] = content; }
                _doc = new FlowDocument(table) { PagePadding = new(0), PageWidth = Available, PageHeight = 144 }; Css.SetStyleSheet(_doc, "Table,Section,Paragraph{margin:0;}");
                _body = new(); _body.SetDocument(_doc); _root = _body;
            }
            else
            {
                var table = new StackPanel(); _table = table; _root = table; var row = new StackPanel(); Css.SetStyle(row, "display:table-row;"); table.Children.Add(row);
                for (var index = 0; index < widths.Length; index++) { var content = new Border(); var cell = new Border { Child = content, Padding = new(0), BorderThickness = new(0) }; row.Children.Add(cell); Cells[index] = cell; _contents[index] = content; }
            }
            for (var index = 0; index < widths.Length; index++) { SetContentWidth(index, widths[index]); Request(false, index, ""); } SetTable("width:auto;");
        }
        internal void SetTable(string style) { _gap = style.Contains("border-spacing:10px") ? 10 : 0; Css.SetStyle(_table, (_document ? "" : "display:table;") + "table-layout:auto;" + style); }
        internal void SetContentWidth(int cell, double width) => Css.SetStyle(_contents[cell], $"width:{width}px;height:20px;");
        internal void ClearCellInsets(int cell) { Cells[cell].ClearValue(_document ? TableCell.PaddingProperty : Border.PaddingProperty); Cells[cell].ClearValue(_document ? TableCell.BorderThicknessProperty : Border.BorderThicknessProperty); }
        internal DependencyObject Request(bool column, int index, string style)
        {
            if (!column) { Css.SetStyle(Cells[index], (_document ? "" : "display:table-cell;") + "vertical-align:top;" + style); return Cells[index]; }
            while (_columns.Count <= index)
            {
                if (_document) { var source = new TableColumn(); ((Table)_table).Columns.Add(source); _columns.Add(source); }
                else { var source = new Border(); ((StackPanel)_table).Children.Insert(_columns.Count, source); _columns.Add(source); }
            }
            Css.SetStyle(_columns[index], (_document ? "" : "display:table-column;") + style); return _columns[index];
        }
        internal void PrependSpan(string style, int count)
        {
            if (_document)
            {
                var content = new Section(); Css.SetStyle(content, "width:20px;height:20px;"); var cell = new TableCell(content) { ColumnSpan = count, Padding = new(0), BorderThickness = new(0) }; Css.SetStyle(cell, "vertical-align:top;" + style);
                var row = new TableRow(); row.Cells.Add(cell); ((Table)_table).RowGroups[0].Rows.Insert(0, row); _span = cell;
            }
            else
            {
                var content = new Border(); Css.SetStyle(content, "width:20px;height:20px;"); var cell = new Border { Child = content, Padding = new(0), BorderThickness = new(0) }; Css.SetStyle(cell, "display:table-cell;vertical-align:top;" + style); Grid.SetColumnSpan(cell, count);
                var row = new StackPanel(); Css.SetStyle(row, "display:table-row;"); row.Children.Add(cell); ((StackPanel)_table).Children.Insert(_columns.Count, row); _span = cell;
            }
        }
        internal void GroupFirstColumns(string style, int count)
        {
            if (_columnGroup is null)
            {
                for (var index = 0; index < count; index++) Request(true, index, "");
                _columnGroup = new StackPanel(); var table = (StackPanel)_table;
                foreach (var column in _columns.Take(count)) { table.Children.Remove((UIElement)column); _columnGroup.Children.Add((UIElement)column); }
                table.Children.Insert(0, _columnGroup);
            }
            Css.SetStyle(_columnGroup, "display:table-column-group;" + style);
        }
        internal DependencyObject AddCompetingRow(string style)
        {
            if (_document)
            {
                var row = new TableRow(); TableCell? source = null;
                for (var column = 0; column < 2; column++) { var block = new Section(); Css.SetStyle(block, "width:0;height:20px;"); var cell = new TableCell(block) { Padding = new(0), BorderThickness = new(0) }; row.Cells.Add(cell); if (column == 0) { source = cell; Css.SetStyle(cell, style); } }
                ((Table)_table).RowGroups[0].Rows.Add(row); return source!;
            }
            else
            {
                var row = new StackPanel(); Css.SetStyle(row, "display:table-row;"); Border? source = null;
                for (var column = 0; column < 2; column++) { var block = new Border(); Css.SetStyle(block, "width:0;height:20px;"); var cell = new Border { Child = block, Padding = new(0), BorderThickness = new(0) }; Css.SetStyle(cell, "display:table-cell;" + (column == 0 ? style : "")); row.Children.Add(cell); if (column == 0) source = cell; }
                ((StackPanel)_table).Children.Add(row); return source!;
            }
        }
        internal void Layout()
        {
            if (_doc is not null) _doc.PageWidth = Available;
            for (var pass = 0; pass < 3; pass++) { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); _root.Measure(new(Available, double.PositiveInfinity)); _root.Arrange(new(0, 0, _document ? Available : _root.DesiredSize.Width, Math.Max(1, _root.DesiredSize.Height))); _root.UpdateLayout(); }
        }
        private Rect Bounds(DependencyObject source) => _document ? _body!.TableCells.Single(cell => ReferenceEquals(cell.Source, source)).Bounds : new(((FrameworkElement)source).TransformToAncestor(_root), ((FrameworkElement)source).RenderSize);
        internal Rect Bounds(int cell) => Bounds(Cells[cell]);
        internal Rect SpanBounds => Bounds(_span!);
        internal double GridExtent => _document ? _body!.TableCells.Max(cell => cell.Bounds.Right) + _gap : _root.ActualWidth;
        internal object? ParentOfCell(int cell) => _document ? ((TableCell)Cells[cell]).Parent : ((FrameworkElement)Cells[cell]).VisualParent;
        internal void AssertColumns(params double[] expected) { Assert.Equal(Cells.Length, expected.Length); for (var index = 0; index < Cells.Length; index++) Assert.Equal(expected[index], Bounds(index).Width, 2); }
        public void Dispose() => _body?.Dispose();
    }
}
