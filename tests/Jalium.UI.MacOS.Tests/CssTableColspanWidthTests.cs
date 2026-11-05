using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssTableColspanWidthTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SpanningMinimumGrowsColumnsInProportionToTheirContent(bool document)
    {
        using var view = new Fixture(document, 20, 60); var span = view.AddSpan(0, 2, 160); view.Layout();
        view.AssertColumns(40, 120); Assert.Equal(160, view.Bounds(span).Width, 2);
        view.ContentWidth(span, 240); view.Layout(); view.AssertColumns(60, 180);
        view.ContentWidth(span, 40); view.Layout(); view.AssertColumns(20, 60);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SpecifiedSpanningMaximumInterpolatesAboveNaturalMinimums(bool document)
    {
        using var view = new Fixture(document, 20, 60); var span = view.AddSpan(0, 2, 20, "width:240px;"); view.Layout(); view.AssertColumns(60, 180);
        view.TableStyle("width:160px;"); view.Layout(); view.AssertColumns(40, 120);
        view.TableStyle("width:40px;"); view.Layout(); view.AssertColumns(20, 60);
        view.NativeWidth(span, 240); view.Layout(); view.AssertColumns(60, 180);
        span.ClearValue(FrameworkElement.WidthProperty); view.Layout(); view.AssertColumns(20, 60);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SpanningMinimumGrowsSpecifiedColumnsBeforeAutomaticSurplus(bool document)
    {
        using var view = new Fixture(document, 20, 60); view.ColumnStyle(0, "width:40px;"); view.AddSpan(0, 2, 160); view.Layout(); view.AssertColumns(40, 120);
        view.ColumnStyle(0, "width:auto;"); view.Layout(); view.AssertColumns(40, 120);
        view.ColumnStyle(0, "width:80px;"); view.Layout(); view.AssertColumns(80, 80);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PercentageColumnsParticipateInSpanningMinimumDistribution(bool document)
    {
        using var view = new Fixture(document, 20, 60); view.ColumnStyle(0, "width:50%;"); view.AddSpan(0, 2, 160); view.Layout(); view.AssertColumns(80, 80);
        view.ColumnStyle(0, "width:25%;"); view.Layout(); view.AssertColumns(40, 120);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SpanningPercentageWeightsUseTheColumnsBeforeTheirContentGrowth(bool document)
    {
        using var view = new Fixture(document, 20, 60, 40); view.AddSpan(0, 2, 160, "width:60%;"); view.Layout(); view.AssertColumns(40, 120, 320d / 3);
        view.TableStyle("width:200px;"); view.Layout(); view.AssertColumns(40, 120, 40);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void OverlappingSpansUseStableStartingColumnOrder(bool document, bool reverse)
    {
        using var view = new Fixture(document, 20, 60, 40);
        if (reverse) { view.AddSpan(1, 2, 240); view.AddSpan(0, 2, 160); }
        else { view.AddSpan(0, 2, 160); view.AddSpan(1, 2, 240); }
        view.Layout(); view.AssertColumns(40, 180, 60);
        view.TableStyle("width:400px;"); view.Layout(); view.AssertColumns(400d / 7, 1800d / 7, 600d / 7);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ShorterSpansContributeBeforeLongerSpansRegardlessOfRowOrder(bool document)
    {
        using var view = new Fixture(document, 20, 60, 40); view.AddSpan(0, 3, 400); view.AddSpan(0, 2, 160); view.Layout(); view.AssertColumns(80, 240, 80);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SpanningWidthsSubtractOnlyInternalSpacing(bool document)
    {
        using var view = new Fixture(document, 20, 60); var span = view.AddSpan(0, 2, 170); view.TableStyle("width:auto;border-spacing:10px;"); view.Layout(); view.AssertColumns(40, 120);
        Assert.Equal(170, view.Bounds(span).Width, 2); Assert.Equal(10, view.Bounds(view.Cells[0]).Left, 2); Assert.Equal(60, view.Bounds(view.Cells[1]).Left, 2);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SpanningCellContentBoxInsetsContributeExactlyOnce(bool document)
    {
        using var view = new Fixture(document, 20, 60); var span = view.AddSpan(0, 2, 140, "box-sizing:content-box;padding:8px;border:2px solid green;"); view.ClearInsets(span); view.Layout(); view.AssertColumns(40, 120); Assert.Equal(160, view.Bounds(span).Width, 2);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void EmptyColumnsShareSpanningContentAndSmallSpansDoNotShrinkThem(bool document)
    {
        using var view = new Fixture(document, 0, 0); var span = view.AddSpan(0, 2, 160); view.Layout(); view.AssertColumns(80, 80);
        view.ContentWidth(span, 0); view.Layout(); view.AssertColumns(0, 0);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CollapsingAColumnProjectsSpanningAllocationWithoutChangingItsSources(bool document)
    {
        using var view = new Fixture(document, 20, 60); view.ColumnStyle(0, "width:auto;"); view.ColumnStyle(1, "width:auto;"); var span = view.AddSpan(0, 2, 160); view.Layout(); var parent = view.Parent(span); view.AssertColumns(40, 120);
        view.ColumnStyle(0, "width:auto;visibility:collapse;"); view.Layout(); Assert.Equal(120, view.Bounds(span).Width, 2); Assert.Equal(120, view.Bounds(view.Cells[1]).Width, 2);
        view.ColumnStyle(0, "width:auto;visibility:visible;"); view.Layout(); view.AssertColumns(40, 120); Assert.Same(parent, view.Parent(span));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RtlMirrorsColumnsAfterSpanningConstraintsAreResolved(bool document)
    {
        using var view = new Fixture(document, 20, 60); view.AddSpan(0, 2, 160); view.TableStyle("width:auto;direction:rtl;"); view.Layout(); view.AssertColumns(40, 120);
        Assert.True(view.Bounds(view.Cells[0]).Left > view.Bounds(view.Cells[1]).Left);
        view.TableStyle("width:auto;"); view.Layout(); view.AssertColumns(40, 120); Assert.Equal(0, view.Bounds(view.Cells[0]).Left, 2);
    }

    [Fact]
    public void NativeStarColumnWeightsStillControlSpanningSurplus()
    {
        using var view = new Fixture(true, 20, 60); view.NativeColumn(0, new GridLength(3, GridUnitType.Star)); view.NativeColumn(1, new GridLength(1, GridUnitType.Star)); view.AddSpan(0, 2, 160);
        view.Layout(); view.AssertColumns(80, 80);
    }

    [Fact]
    public void UnconstrainedSpanningMaximumDoesNotInflateOnlySpecifiedColumns()
    {
        var cells = new[] { Track(0, 1, 20, 20), Track(1, 1, 20, 20), Track(0, 2, 20, 200) };
        var columns = new[] { new CssTableTracks.Column(40, Request: new(CssLayoutLength.Px(40))), new CssTableTracks.Column(60, Request: new(CssLayoutLength.Px(60))) };
        Assert.Equal((40d, 100d), CssTableTracks.IntrinsicColumns(2, 0, columns, cells));
        cells[2].Preferred = 200; cells[2].SpecifiedWidth = new(CssLayoutLength.Px(200));
        Assert.Equal((40d, 200d), CssTableTracks.IntrinsicColumns(2, 0, columns, cells));
        var used = CssTableTracks.Columns(2, 200, 0, false, columns, cells); Assert.Equal(80, used.Sizes[0], 3); Assert.Equal(120, used.Sizes[1], 3);
    }

    [Fact]
    public void FlexibleSpanningMinimumInterpolatesAcrossAvailableContentGrowth()
    {
        var cells = new[] { Track(0, 1, 10, 30), Track(1, 1, 20, 100), Track(0, 2, 80, 200) };
        var intrinsic = CssTableTracks.IntrinsicColumns(2, 0, [], cells); Assert.Equal(80, intrinsic.Min, 3); Assert.Equal(200, intrinsic.Max, 3);
        var min = CssTableTracks.Columns(2, 80, 0, false, [], cells); Assert.Equal(20, min.Sizes[0], 3); Assert.Equal(60, min.Sizes[1], 3);
        var max = CssTableTracks.Columns(2, 200, 0, false, [], cells); Assert.Equal(600d / 13, max.Sizes[0], 3); Assert.Equal(2000d / 13, max.Sizes[1], 3);
    }

    private static CssTableTracks.Cell Track(int start, int span, double min, double max) => new(new Border(), span == 1 ? 0 : 1, start, 1, span) { Min = min, Max = max };

    private sealed class Fixture : IDisposable
    {
        private readonly bool _document;
        private readonly DependencyObject _table;
        private readonly FrameworkElement _root;
        private readonly FlowDocument? _doc;
        private readonly FlowDocumentPresentation? _body;
        private readonly Dictionary<DependencyObject, DependencyObject> _contents = [];
        private readonly List<DependencyObject> _columns = [];
        internal readonly List<DependencyObject> Cells = [];
        internal Fixture(bool document, params double[] widths)
        {
            _document = document;
            if (document)
            {
                var table = new Table { CellSpacing = 0 }; _table = table; var group = new TableRowGroup(); table.RowGroups.Add(group); var row = new TableRow(); group.Rows.Add(row);
                foreach (var width in widths) row.Cells.Add((TableCell)Cell(width));
                _doc = new FlowDocument(table) { PagePadding = new(0), PageWidth = 600 }; Css.SetStyleSheet(_doc, "Table,Section,Paragraph{margin:0;}");
                _body = new(); _body.SetDocument(_doc); _root = _body;
            }
            else
            {
                var table = new StackPanel(); _table = table; _root = table; var row = new StackPanel(); Css.SetStyle(row, "display:table-row;"); table.Children.Add(row);
                foreach (var width in widths) row.Children.Add((UIElement)Cell(width));
            }
            Cells.AddRange(_contents.Keys); TableStyle("width:auto;");
        }
        private DependencyObject Cell(double width, int span = 1, string style = "")
        {
            DependencyObject content, cell;
            if (_document) { content = new Section(); cell = new TableCell((Block)content) { ColumnSpan = span, Padding = new(0), BorderThickness = new(0) }; }
            else { content = new Border(); cell = new Border { Child = (UIElement)content, Padding = new(0), BorderThickness = new(0) }; Grid.SetColumnSpan((UIElement)cell, span); }
            _contents.Add(cell, content); ContentWidth(cell, width); Css.SetStyle(cell, (_document ? "" : "display:table-cell;") + "vertical-align:top;" + style); return cell;
        }
        internal DependencyObject AddSpan(int start, int count, double width, string style = "")
        {
            var cell = Cell(width, count, style);
            if (_document) { var row = new TableRow(); for (var index = 0; index < start; index++) row.Cells.Add((TableCell)Cell(0)); row.Cells.Add((TableCell)cell); ((Table)_table).RowGroups[0].Rows.Add(row); }
            else { var row = new StackPanel(); Css.SetStyle(row, "display:table-row;"); for (var index = 0; index < start; index++) row.Children.Add((UIElement)Cell(0)); row.Children.Add((UIElement)cell); ((StackPanel)_table).Children.Add(row); }
            return cell;
        }
        internal void TableStyle(string style) => Css.SetStyle(_table, (_document ? "" : "display:table;") + "table-layout:auto;" + style);
        internal void ContentWidth(DependencyObject cell, double width) => Css.SetStyle(_contents[cell], $"width:{width}px;height:20px;");
        internal void NativeWidth(DependencyObject cell, double width) => cell.SetValue(FrameworkElement.WidthProperty, width);
        internal void ClearInsets(DependencyObject cell) { cell.ClearValue(_document ? TableCell.PaddingProperty : Border.PaddingProperty); cell.ClearValue(_document ? TableCell.BorderThicknessProperty : Border.BorderThicknessProperty); }
        internal void ColumnStyle(int index, string style)
        {
            while (_columns.Count <= index)
            {
                if (_document) { var column = new TableColumn(); ((Table)_table).Columns.Add(column); _columns.Add(column); }
                else { var column = new Border(); ((StackPanel)_table).Children.Insert(_columns.Count, column); _columns.Add(column); }
            }
            Css.SetStyle(_columns[index], (_document ? "" : "display:table-column;") + style);
        }
        internal void NativeColumn(int index, GridLength width)
        {
            while (_columns.Count <= index) { var column = new TableColumn(); ((Table)_table).Columns.Add(column); _columns.Add(column); }
            ((TableColumn)_columns[index]).Width = width;
        }
        internal void Layout()
        {
            for (var pass = 0; pass < 3; pass++) { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); _root.Measure(new(600, double.PositiveInfinity)); _root.Arrange(new(0, 0, _document ? 600 : _root.DesiredSize.Width, Math.Max(1, _root.DesiredSize.Height))); _root.UpdateLayout(); }
        }
        internal Rect Bounds(DependencyObject source) => _document ? _body!.TableCells.Single(cell => ReferenceEquals(cell.Source, source)).Bounds : new(((FrameworkElement)source).TransformToAncestor(_root), ((FrameworkElement)source).RenderSize);
        internal object? Parent(DependencyObject source) => _document ? ((TableCell)source).Parent : ((FrameworkElement)source).VisualParent;
        internal void AssertColumns(params double[] expected) { Assert.Equal(Cells.Count, expected.Length); for (var index = 0; index < expected.Length; index++) Assert.Equal(expected[index], Bounds(Cells[index]).Width, 2); }
        public void Dispose() => _body?.Dispose();
    }
}
