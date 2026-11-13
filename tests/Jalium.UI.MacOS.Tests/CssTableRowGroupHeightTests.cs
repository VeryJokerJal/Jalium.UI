using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssTableRowGroupHeightTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FixedGroupRaisesAutoTableMinimumAndDistributesItsOwnRows(bool document)
    {
        using var view = new Fixture(document, [20, 60], [20]); view.SetGroup(0, "height:160px;"); view.Layout();
        view.AssertRows(40, 120, 20); Assert.Equal(180, view.GridExtent, 2);
        view.SetGroup(0, "height:20px;"); view.Layout(); view.AssertRows(20, 60, 20); Assert.Equal(100, view.GridExtent, 2);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PercentageGroupsGrowBeforeAutoGroupsAndFixedGroupsRemainMinima(bool document)
    {
        using var view = new Fixture(document, [20], [20], [20]); view.SetTable("height:300px;");
        view.SetGroup(0, "height:80px;"); view.SetGroup(2, "height:50%;"); view.Layout(); view.AssertRows(80, 70, 150);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OverconstrainedGroupPercentagesShareDeficitsWithoutRowPercentageCapping(bool document)
    {
        using var view = new Fixture(document, [20], [20], [20]); view.SetTable("height:200px;");
        view.SetGroup(0, "height:80%;"); view.SetGroup(1, "height:80%;"); view.SetGroup(2, "height:40px;"); view.Layout();
        view.AssertRows(80, 80, 40); Assert.Equal(200, view.GridExtent, 2);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void BodyGroupsReceiveSurplusBeforeHeaderAndFooterGroups(bool document)
    {
        using var view = new Fixture(document, [20], [20], [20]); view.SetTable("height:300px;");
        view.SetRole(0, "table-header-group"); view.SetRole(2, "table-footer-group"); view.Layout(); view.AssertRows(20, 260, 20);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void HeaderAndFooterPercentageDeficitsRespectTheBodyContentMinimum(bool document)
    {
        using var view = new Fixture(document, [0], [60], [0]); view.SetTable("height:100px;");
        view.SetRole(0, "table-header-group"); view.SetRole(2, "table-footer-group");
        view.SetGroup(0, "height:50%;"); view.SetGroup(1, "height:50%;"); view.SetGroup(2, "height:50%;"); view.Layout(); view.AssertRows(20, 60, 20);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TablesWithOnlyHeaderAndFooterGroupsStillDistributeSurplus(bool document)
    {
        using var view = new Fixture(document, [20], [60]); view.SetTable("height:300px;");
        view.SetRole(0, "table-header-group"); view.SetRole(1, "table-footer-group"); view.Layout(); view.AssertRows(75, 225);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AdditionalHeaderGroupsAreTreatedAsBodyGroups(bool document)
    {
        using var view = new Fixture(document, [20], [20], [20]); view.SetTable("height:200px;");
        view.SetRole(0, "table-header-group"); view.SetRole(1, "table-header-group"); view.SetRole(2, "table-footer-group"); view.Layout(); view.AssertRows(20, 160, 20);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void GroupPercentageBasisExcludesOuterAndInterGroupGapsAndCountsInternalGapsOnce(bool document)
    {
        using var view = new Fixture(document, [20, 20], [20]); view.SetTable("height:200px;border-spacing:10px;"); view.SetGroup(0, "height:50%;"); view.Layout();
        view.AssertRows(37.5, 37.5, 85); Assert.Equal(10, view.Bounds(0).Top, 2); Assert.Equal(105, view.Bounds(2).Top, 2); Assert.Equal(200, view.GridExtent, 2);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CalcGroupHeightIncludesItsLengthComponent(bool document)
    {
        using var view = new Fixture(document, [20], [20]); view.SetTable("height:200px;"); view.SetGroup(0, "height:calc(50% + 20px);"); view.Layout(); view.AssertRows(120, 80);
        view.SetTable("height:auto;"); view.SetGroup(0, "height:calc(50% + 100px);"); view.Layout(); view.AssertRows(100, 20);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NativeGroupHeightAutoDefaultAndClearReplaceTheCssRequest(bool document)
    {
        using var view = new Fixture(document, [20], [20]); view.SetTable("height:200px;"); view.SetGroup(0, "height:25%;"); view.Layout(); view.AssertRows(50, 150);
        view.Groups[0].SetValue(FrameworkElement.HeightProperty, double.NaN); view.Layout(); view.AssertRows(100, 100);
        view.Groups[0].ClearValue(FrameworkElement.HeightProperty); view.Layout(); view.AssertRows(50, 150);
        view.Groups[0].SetValue(FrameworkElement.HeightProperty, 80d); view.Layout(); view.AssertRows(80, 120);
        view.Groups[0].SetValue(FrameworkElement.HeightProperty, double.NaN); view.Layout(); view.AssertRows(100, 100);
        view.Groups[0].SetValue(FrameworkElement.HeightProperty, 0d); view.Layout(); view.AssertRows(20, 180);
        view.Groups[0].ClearValue(FrameworkElement.HeightProperty); view.Layout(); view.AssertRows(50, 150);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RowPercentagesKeepTheFullGridReferenceWithinTheirGroupAllocation(bool document)
    {
        using var view = new Fixture(document, [20, 20], [20]); view.SetTable("height:200px;"); view.SetGroup(0, "height:120px;"); view.SetRow(0, "height:40%;"); view.Layout();
        view.AssertRows(80, 40, 80); Assert.Equal(200, view.GridExtent, 2);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AutoTableGroupPercentagesDoNotInventDefiniteHeight(bool document)
    {
        using var view = new Fixture(document, [20], [60]); view.SetGroup(0, "height:80%;"); view.SetGroup(1, "height:80%;"); view.Layout(); view.AssertRows(20, 60); Assert.Equal(80, view.GridExtent, 2);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void EmptyGroupsIgnoreSpecifiedHeightAndZeroHeightRowsCanReceiveSurplus(bool document)
    {
        using var view = new Fixture(document, [], [0], [0]); view.SetGroup(0, "height:600px;"); view.SetTable("height:200px;"); view.Layout(); view.AssertRows(100, 100); Assert.Equal(200, view.GridExtent, 2);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CollapsedGroupProjectsAlreadyAllocatedRowsAndRestoresTheOriginalSources(bool document)
    {
        using var view = new Fixture(document, [20], [20]); view.SetTable("height:200px;"); view.SetGroup(0, "height:25%;"); view.Layout(); var original = view.Bounds(1); var parent = view.ParentOfCell(1);
        view.SetGroup(0, "height:25%;visibility:collapse;"); view.Layout(); view.AssertRows(0, 150); Assert.Equal(0, view.Bounds(1).Top, 2); Assert.Equal(150, view.GridExtent, 2);
        view.SetGroup(0, "height:25%;"); view.Layout(); Assert.Equal(original, view.Bounds(1)); Assert.Same(parent, view.ParentOfCell(1));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void StylesheetChangesRecomputeCachedAutoTableGroupHeights(bool document)
    {
        using var view = new Fixture(document, [20, 20], [20]); var parent = view.ParentOfCell(0);
        view.SetSheet(".g0{height:120px;}"); view.Layout(); view.AssertRows(60, 60, 20);
        view.SetSheet(".g0{height:200px;}"); view.Layout(); view.AssertRows(100, 100, 20);
        view.SetSheet(""); view.Layout(); view.AssertRows(20, 20, 20); Assert.Same(parent, view.ParentOfCell(0));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ReorderingSourceGroupsKeepsEachGroupsOwnAllocationAndOriginalCellParents(bool document)
    {
        using var view = new Fixture(document, [20], [20]); view.SetTable("height:200px;"); view.SetGroup(0, "height:80px;"); view.Layout();
        var parent = view.ParentOfCell(0); view.MoveFirstGroupToEnd(); view.Layout(); view.AssertRows(80, 120);
        Assert.Equal(120, view.Bounds(0).Top, 2); Assert.Equal(0, view.Bounds(1).Top, 2); Assert.Same(parent, view.ParentOfCell(0));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OriginalContentChangesCanExceedGroupHeightAndRestoreItsPreviousDistribution(bool document)
    {
        using var view = new Fixture(document, [20, 60], [20]); view.SetGroup(0, "height:120px;"); view.Layout(); view.AssertRows(30, 90, 20);
        view.SetContentHeight(1, 200); view.Layout(); view.AssertRows(20, 200, 20); Assert.Equal(240, view.GridExtent, 2);
        view.SetContentHeight(1, 60); view.Layout(); view.AssertRows(30, 90, 20); Assert.Equal(140, view.GridExtent, 2);
    }

    [Fact]
    public void GroupHeightAloneDoesNotMakeAutoCellContentDefiniteButATableHeightDoes()
    {
        var chapter = new Section(); Css.SetStyle(chapter, "height:50%;"); var footer = new Paragraph(new Run("组内原文"));
        var cell = new TableCell { Padding = new(0), BorderThickness = new(0) }; cell.Blocks.Add(chapter); cell.Blocks.Add(footer);
        var row = new TableRow(); row.Cells.Add(cell); var group = new TableRowGroup(); group.Rows.Add(row); Css.SetStyle(group, "height:200px;");
        var table = new Table { CellSpacing = 0 }; table.RowGroups.Add(group);
        var doc = new FlowDocument(table) { PagePadding = new(0), PageWidth = 400, PageHeight = 144, FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24 };
        Css.SetStyleSheet(doc, "Paragraph,Section,Table{margin:0;}"); using var body = new FlowDocumentPresentation(); body.SetDocument(doc);
        body.Measure(new(400, double.PositiveInfinity)); body.Arrange(new(0, 0, 400, body.DesiredSize.Height));
        Assert.Equal(200, body.TableCells.Single().Bounds.Height, 2); Assert.Equal(0, body.Paragraphs.Single().TransformToAncestor(body).Y, 2);
        Css.SetStyle(table, "height:200px;"); body.Measure(new(400, double.PositiveInfinity)); body.Arrange(new(0, 0, 400, body.DesiredSize.Height));
        Assert.Equal(100, body.Paragraphs.Single().TransformToAncestor(body).Y, 2);
        Assert.Same(footer.Inlines, body.Paragraphs.Single().Inlines); Assert.Equal(2, doc.ViewerPaginator.PageCount);
    }

    [Fact]
    public void SpecifiedEmptyGroupRowsRemainFragmentableAcrossPages()
    {
        var group = new TableRowGroup(); group.Rows.Add(new TableRow()); Css.SetStyle(group, "height:300px;"); var table = new Table { CellSpacing = 0 }; table.RowGroups.Add(group);
        var doc = new FlowDocument(table) { PagePadding = new(0), PageWidth = 400, PageHeight = 72 }; Css.SetStyleSheet(doc, "Table{margin:0;}");
        Assert.Equal(5, doc.ViewerPaginator.PageCount);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly bool _document;
        private readonly FlowDocumentPresentation? _body;
        private readonly FlowDocument? _doc;
        private readonly FrameworkElement _root;
        private readonly DependencyObject _table;
        private readonly string[] _roles;
        internal readonly DependencyObject[] Groups, Rows, Cells;
        private readonly DependencyObject[] _contents;
        private double _gap;
        internal Fixture(bool document, params double[][] groups)
        {
            _document = document; Groups = new DependencyObject[groups.Length]; Rows = new DependencyObject[groups.Sum(g => g.Length)]; Cells = new DependencyObject[Rows.Length];
            _contents = new DependencyObject[Rows.Length];
            _roles = Enumerable.Repeat("table-row-group", groups.Length).ToArray(); var index = 0;
            if (document)
            {
                var table = new Table { CellSpacing = 0 }; _table = table;
                for (var g = 0; g < groups.Length; g++)
                {
                    var group = new TableRowGroup(); Groups[g] = group; table.RowGroups.Add(group); Css.SetClass(group, "g" + g);
                    foreach (var height in groups[g])
                    {
                        var content = new Section(); Css.SetStyle(content, $"height:{height}px;"); var cell = new TableCell(content) { Padding = new(0), BorderThickness = new(0) }; var row = new TableRow();
                        row.Cells.Add(cell); group.Rows.Add(row); Rows[index] = row; _contents[index] = content; Cells[index++] = cell;
                    }
                }
                _doc = new FlowDocument(table) { PagePadding = new(0), PageWidth = 400, PageHeight = 144 }; _body = new(); _body.SetDocument(_doc); _root = _body;
            }
            else
            {
                var table = new StackPanel(); _table = table; _root = table;
                for (var g = 0; g < groups.Length; g++)
                {
                    var group = new StackPanel(); Groups[g] = group; table.Children.Add(group); Css.SetClass(group, "g" + g);
                    foreach (var height in groups[g])
                    {
                        var content = new Border(); Css.SetStyle(content, $"height:{height}px;"); var cell = new Border { Child = content, Padding = new(0), BorderThickness = new(0) }; Css.SetStyle(cell, "display:table-cell;vertical-align:top;");
                        var row = new StackPanel(); Css.SetStyle(row, "display:table-row;"); row.Children.Add(cell); group.Children.Add(row); Rows[index] = row; _contents[index] = content; Cells[index++] = cell;
                    }
                }
            }
            for (var g = 0; g < groups.Length; g++) SetGroup(g, ""); SetSheet(""); SetTable("height:auto;");
        }
        internal void SetTable(string style) { _gap = style.Contains("border-spacing:10px") ? 10 : 0; Css.SetStyle(_table, (_document ? "" : "display:table;width:400px;table-layout:fixed;") + style); }
        internal void SetGroup(int group, string style) => Css.SetStyle(Groups[group], "display:" + _roles[group] + ";" + style);
        internal void SetRole(int group, string role) { _roles[group] = role; SetGroup(group, ""); }
        internal void SetRow(int row, string style) => Css.SetStyle(Rows[row], (_document ? "" : "display:table-row;") + style);
        internal void SetSheet(string rules) => Css.SetStyleSheet(_doc ?? (DependencyObject)_root, "Paragraph,Section,Table{margin:0;}" + rules);
        internal void SetContentHeight(int row, double height) => Css.SetStyle(_contents[row], $"height:{height}px;");
        internal void MoveFirstGroupToEnd()
        {
            if (_document) { var table = (Table)_table; table.RowGroups.Remove((TableRowGroup)Groups[0]); table.RowGroups.Add((TableRowGroup)Groups[0]); }
            else { var table = (StackPanel)_table; table.Children.Remove((UIElement)Groups[0]); table.Children.Add((UIElement)Groups[0]); }
        }
        internal void Layout() { for (var pass = 0; pass < 3; pass++) { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); _root.Measure(new(400, double.PositiveInfinity)); _root.Arrange(new(0, 0, 400, Math.Max(1, _root.DesiredSize.Height))); _root.UpdateLayout(); } }
        internal Rect Bounds(int row)
        {
            if (_document) return _body!.TableCells.Single(cell => ReferenceEquals(cell.Source, Cells[row])).Bounds;
            var element = (FrameworkElement)Cells[row]; var at = element.TransformToAncestor(_root); return new(at.X, at.Y, element.ActualWidth, element.ActualHeight);
        }
        internal double GridExtent => Cells.Length == 0 ? 0 : Enumerable.Range(0, Cells.Length).Max(row => Bounds(row).Bottom) + _gap;
        internal DependencyObject? ParentOfCell(int index) => _document ? ((TableCell)Cells[index]).Parent : ((FrameworkElement)Cells[index]).VisualParent;
        internal void AssertRows(params double[] expected) { Assert.Equal(Cells.Length, expected.Length); for (var row = 0; row < expected.Length; row++) Assert.Equal(expected[row], Bounds(row).Height, 2); }
        public void Dispose() => _body?.Dispose();
    }
}
