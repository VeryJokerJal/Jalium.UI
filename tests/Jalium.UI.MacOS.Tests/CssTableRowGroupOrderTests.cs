using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Automation.Text;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssTableRowGroupOrderTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();
    public static IEnumerable<object[]> Permutations()
    {
        foreach (var order in new[] { "HBF", "HFB", "BHF", "BFH", "FHB", "FBH" })
        foreach (var document in new[] { false, true }) foreach (var rtl in new[] { false, true }) yield return [order, document, rtl];
    }
    public static IEnumerable<object[]> Modes()
    { foreach (var document in new[] { false, true }) foreach (var rtl in new[] { false, true }) yield return [document, rtl]; }

    [Theory]
    [MemberData(nameof(Permutations))]
    public void FirstHeaderAndFooterHaveTheSameVisualOrderForEverySourceOrder(string order, bool document, bool rtl)
    {
        using var view = new Fixture(document, rtl, order); view.Layout();
        view.AssertOrder(order.IndexOf('H'), order.IndexOf('B'), order.IndexOf('F')); view.AssertOriginal();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void AdditionalFootersRemainBodyGroupsWhenTheFirstFooterMovesPastThem(bool document, bool rtl)
    {
        using var view = new Fixture(document, rtl, "FBFH"); view.SetTable("height:300px;"); view.Layout();
        view.AssertOrder(3, 1, 2, 0); view.AssertHeight(0, 40); view.AssertHeight(1, 110); view.AssertHeight(2, 110); view.AssertHeight(3, 40); view.AssertOriginal();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void EmptyFirstHeadersKeepLaterHeaderRowsInTheBodyHeightDistribution(bool document, bool rtl)
    {
        using var view = new Fixture(document, rtl, "HHBF", empty: 0); view.SetTable("height:300px;"); view.Layout();
        view.AssertOrder(1, 2, 3); view.AssertHeight(1, 130); view.AssertHeight(2, 130); view.AssertHeight(3, 40); view.AssertGroup(0, 0); view.AssertOriginal();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void EmptyFirstFootersDoNotPromoteALaterFooterForHeightAllocation(bool document, bool rtl)
    {
        using var view = new Fixture(document, rtl, "FBFH", empty: 0); view.SetTable("height:300px;"); view.Layout();
        view.AssertOrder(3, 1, 2); view.AssertHeight(3, 40); view.AssertHeight(1, 130); view.AssertHeight(2, 130); view.AssertGroup(0, 300); view.AssertOriginal();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void EmptyBodyGroupsKeepTheirBoundaryBeforeThePromotedFooter(bool document, bool rtl)
    {
        using var view = new Fixture(document, rtl, "FBHB", empty: 3); view.Layout(); view.AssertOrder(2, 1, 0); view.AssertGroup(3, 80); view.AssertOriginal();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void CollapsingAndRestoringThePromotedFooterKeepsTheOtherFooterInTheBody(bool document, bool rtl)
    {
        using var view = new Fixture(document, rtl, "FBFH"); view.SetTable("height:300px;"); view.SetRole(0, 'F', "visibility:collapse;"); view.Layout();
        view.AssertOrder(3, 1, 2); view.AssertHeight(0, 0); view.AssertHeight(1, 110); view.AssertHeight(2, 110);
        view.SetRole(0, 'F', "visibility:hidden;"); view.Layout(); view.AssertHeight(0, 40); view.AssertHeight(1, 110); view.AssertHeight(2, 110);
        view.SetRole(0, 'F'); view.Layout(); view.AssertOrder(3, 1, 2, 0); view.AssertOriginal();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void RoleChangesRemovalAndRestorationRecomputeSourcePromotion(bool document, bool rtl)
    {
        using var view = new Fixture(document, rtl, "FBHHF"); view.Layout(); view.AssertOrder(2, 1, 3, 4, 0);
        view.SetRole(0, 'B'); view.Layout(); view.AssertOrder(2, 0, 1, 3, 4);
        view.SetRole(2, 'B'); view.Layout(); view.AssertOrder(3, 0, 1, 2, 4);
        view.RemoveGroup(3); view.Layout(); view.AssertOrder(0, 1, 2, 4);
        view.RestoreGroup(3); view.SetRole(0, 'F'); view.SetRole(2, 'H'); view.Layout(); view.AssertOrder(2, 1, 3, 4, 0); view.AssertOriginal();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void FixedLayoutTakesItsFirstRowWidthsFromThePromotedHeader(bool document, bool rtl)
    {
        using var view = new Fixture(document, rtl, "FBH"); view.SetWidths(0, 40, 120); view.SetWidths(1, 80, 80); view.SetWidths(2, 100, 60);
        view.SetTable("table-layout:fixed;"); view.Layout(); view.AssertOrder(2, 1, 0);
        for (var group = 0; group < 3; group++) { Assert.Equal(100, view.Bounds(group, 0, 0).Width, 2); Assert.Equal(60, view.Bounds(group, 0, 1).Width, 2); }
        view.AssertOriginal();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void RowSpansStopAtTheirOriginalGroupAfterVisualPromotion(bool document, bool rtl)
    {
        using var view = new Fixture(document, rtl, "FBH", doubleRow: 0); view.SpanFirstCell(0, 10); view.Layout(); view.AssertOrder(2, 1, 0);
        Assert.Equal(80, view.Bounds(0).Height, 2); Assert.Equal(40, view.Bounds(1).Height, 2); Assert.Equal(80, view.Bounds(0).Top, 2);
        Assert.Equal(view.Bounds(0).Left, view.Bounds(2).Left, 2); view.AssertOriginal();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void CollapsedBordersFollowTheReorderedVisualRowsAndRestoreAfterRoleChanges(bool document, bool rtl)
    {
        using var view = new Fixture(document, rtl, "FBH"); view.SetTable("border-collapse:collapse;");
        view.SetRole(0, 'F', "border-bottom:6px solid blue;"); view.SetRole(2, 'H', "border-top:4px solid red;"); view.Layout(); view.AssertOrder(2, 1, 0);
        Assert.Equal(2, view.Bounds(2).Top, 2); Assert.Equal(Colors.Red, view.Pixel(80, 1)); Assert.Equal(Colors.Blue, view.Pixel(80, (int)view.Bounds(0).Bottom));
        view.SetRole(0, 'H', "border-top:6px solid blue;"); view.SetRole(2, 'F', "border-bottom:4px solid red;"); view.Layout(); view.AssertOrder(0, 1, 2); Assert.Equal(Colors.Blue, view.Pixel(80, 1)); view.AssertOriginal();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void NestedTablesKeepOriginalParagraphsAtTheirPromotedVisualPositions(bool document, bool rtl)
    {
        using var view = new Fixture(document, rtl, "FBH"); view.Wrap(); view.Layout(); view.AssertOrder(2, 1, 0); view.AssertOriginal();
    }

    [Theory]
    [InlineData(false, 56)] [InlineData(true, 56)] [InlineData(false, 80)] [InlineData(true, 80)]
    public void PaginatedSearchSelectionAndPointHitsUseVisualPagesWithOriginalOffsets(bool rtl, int height)
    {
        using var view = new Fixture(true, rtl, "FBH"); view.Document!.PageHeight = height; view.Layout(); var paginator = view.Document.ViewerPaginator;
        Assert.Equal(height == 80 ? 2 : 3, paginator.PageCount); var original = view.Document.GetText();
        for (var pageIndex = 0; pageIndex < paginator.PageCount; pageIndex++)
        {
            using var page = Assert.IsType<FlowDocumentPresentation>(paginator.GetPage(pageIndex).Visual); var provider = Provider(page);
            for (var group = 0; group < 3; group++)
            {
                var expected = group == 2 ? 0 : group == 1 ? (height == 80 ? 0 : 1) : paginator.PageCount - 1;
                var found = provider.DocumentRange.FindText(view.Text(group), false, false)!; var boxes = Rectangles(page, found);
                Assert.Equal(pageIndex == expected, boxes.Length > 0);
                Assert.Equal(expected + 1, FlowDocumentViewerSupport.GetPageNumberForOffset(view.Document, view.Offset(group)));
                if (pageIndex != expected) continue;
                found.Select(); Assert.Equal(view.Text(group), page.Selection!.Text); Assert.Equal(view.Text(group), provider.GetSelection().Single().GetText(-1));
                var box = boxes.Single(); var hit = provider.RangeFromPoint(page.PointToScreen(new(box.Left + .5, box.Top + .5)))!;
                Assert.Equal(0, hit.CompareEndpoints(TextPatternRangeEndpoint.Start, found, TextPatternRangeEndpoint.Start));
            }
            Assert.Equal(original, provider.DocumentRange.GetText(-1));
        }
        view.AssertOriginal();
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CachedPageRefreshMovesTheOriginalFooterAndReusesItsInlineCollection(bool rtl)
    {
        using var view = new Fixture(true, rtl, "FBH"); view.Document!.PageHeight = 56; view.Layout(); using var page = Assert.IsType<FlowDocumentPresentation>(view.Document.ViewerPaginator.GetPage(0).Visual);
        var provider = Provider(page); Assert.Empty(provider.DocumentRange.FindText(view.Text(0), false, false)!.GetBoundingRectangles());
        view.SetRole(0, 'H'); view.SetRole(2, 'F'); var metrics = (IFlowDocumentPagePresentation)view.Document.ViewerPaginator;
        Assert.True(metrics.TryRefreshPage(0, page, out var size)); page.Measure(size); page.Arrange(new Rect(size));
        Assert.NotEmpty(provider.DocumentRange.FindText(view.Text(0), false, false)!.GetBoundingRectangles()); Assert.Empty(provider.DocumentRange.FindText(view.Text(2), false, false)!.GetBoundingRectangles()); view.AssertOriginal();
    }

    private static ITextProvider Provider(UIElement page) => (ITextProvider)page.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
    private static Rect[] Rectangles(UIElement page, ITextRangeProvider range) => range.GetBoundingRectangles().Chunk(4).Select(box => new Rect(page.PointFromScreen(new(box[0], box[1])), page.PointFromScreen(new(box[0] + box[2], box[1] + box[3])))).ToArray();

    private sealed class Fixture : IDisposable
    {
        internal readonly FlowDocument? Document;
        private readonly FlowDocumentPresentation? _body;
        private FrameworkElement _root;
        private readonly DependencyObject _table;
        private readonly DependencyObject[] _groups;
        private readonly List<DependencyObject>[][] _cells;
        private readonly List<Paragraph> _paragraphs = [];
        private readonly List<DependencyObject>[] _rows;
        private string _expected;
        private readonly Dictionary<Paragraph, Inline> _runs = [];
        private readonly Dictionary<Paragraph, DependencyObject?> _parents = [];
        private readonly bool _document;
        private readonly string _sheet = "FlowDocument{white-space:pre-wrap;text-align:start;}Paragraph,Table{margin:0;}.grid{display:table;width:160px;table-layout:auto;border-spacing:0;}.row{display:table-row;height:40px;}.cell{display:table-cell;padding:0;border:0;vertical-align:top;}.body{font-family:'PingFang SC';font-size:16px;line-height:24px;white-space:pre-wrap;text-align:start;}";
        internal Fixture(bool document, bool rtl, string roles, int empty = -1, int doubleRow = -1)
        {
            _document = document; _groups = new DependencyObject[roles.Length]; _cells = new List<DependencyObject>[roles.Length][]; _rows = new List<DependencyObject>[roles.Length];
            var table = document ? (DependencyObject)new Table { CellSpacing = 0 } : new StackPanel(); _table = table; Css.SetClass(table, "grid");
            for (var g = 0; g < roles.Length; g++)
            {
                _groups[g] = document ? new TableRowGroup() : new StackPanel(); _rows[g] = []; var count = g == empty ? 0 : g == doubleRow ? 2 : 1; _cells[g] = new List<DependencyObject>[count];
                if (document) ((Table)table).RowGroups.Add((TableRowGroup)_groups[g]); else ((StackPanel)table).Children.Add((UIElement)_groups[g]);
                SetRole(g, roles[g]);
                for (var rowIndex = 0; rowIndex < count; rowIndex++)
                {
                    var row = document ? (DependencyObject)new TableRow() : new StackPanel(); Css.SetClass(row, "row"); _rows[g].Add(row); _cells[g][rowIndex] = [];
                    if (document) ((TableRowGroup)_groups[g]).Rows.Add((TableRow)row); else ((StackPanel)_groups[g]).Children.Add((UIElement)row);
                    for (var column = 0; column < 2; column++)
                    {
                        var text = $"{g}{rowIndex}{(column == 0 ? "甲" : "乙")}"; DependencyObject cell;
                        if (document) { var p = new Paragraph(new Run(text)); _paragraphs.Add(p); _runs.Add(p, p.Inlines[0]); cell = new TableCell(p) { Padding = new(0), BorderThickness = new(0) }; ((TableRow)row).Cells.Add((TableCell)cell); _parents.Add(p, p.Parent); }
                        else { var label = new TextBlock { Text = text }; Css.SetClass(label, "body"); cell = new Border { Child = label }; ((StackPanel)row).Children.Add((UIElement)cell); }
                        Css.SetClass(cell, "cell"); _cells[g][rowIndex].Add(cell);
                    }
                }
            }
            if (document)
            {
                Document = new FlowDocument((Table)table) { PagePadding = new(0), PageWidth = 160, PageHeight = 80, FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24, Foreground = Brushes.Black, FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight };
                Css.SetStyleSheet(Document, _sheet); _body = new(); _body.SetDocument(Document); _root = _body; _expected = Document.GetText();
            }
            else { _root = (FrameworkElement)table; _root.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight; Css.SetStyleSheet(_root, _sheet); _expected = ""; }
            SetTable("");
        }
        internal void SetTable(string style) => Css.SetStyle(_table, "width:160px;" + style);
        internal void SetRole(int group, char role, string style = "") => Css.SetStyle(_groups[group], "display:" + (role == 'H' ? "table-header-group" : role == 'F' ? "table-footer-group" : "table-row-group") + ";" + style);
        internal void SetWidths(int group, int first, int second)
        { Css.SetStyle(_cells[group][0][0], $"width:{first}px;"); Css.SetStyle(_cells[group][0][1], $"width:{second}px;"); }
        internal string Text(int group) => $"{group}0甲";
        internal int Offset(int group) => Document!.GetDocumentOffset(((TableCell)_cells[group][0][0]).Blocks[0], 0);
        internal Rect Bounds(int group, int row = 0, int column = 0)
        {
            var cell = _cells[group][row][column];
            if (_body is not null) return _body.TableCells.Single(item => ReferenceEquals(item.Source, cell)).Bounds;
            var visual = (FrameworkElement)cell; var at = visual.TransformToAncestor(_root); return new Rect(at, visual.RenderSize);
        }
        internal Color Pixel(int x, int y)
        {
            var width = (int)Math.Ceiling(_root.ActualWidth); var height = (int)Math.Ceiling(_root.ActualHeight); var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Bgra32);
            var drawing = new SoftwareDrawingContext(bitmap); drawing.DrawRectangle(Brushes.White, null, new(0, 0, width, height)); _root.Render(drawing); drawing.Close();
            var pixels = new byte[width * height * 4]; bitmap.CopyPixels(pixels, width * 4, 0); var at = (y * width + x) * 4; return Color.FromArgb(pixels[at+3], pixels[at+2], pixels[at+1], pixels[at]);
        }
        internal void AssertOrder(params int[] groups)
        { for (var i = 1; i < groups.Length; i++) Assert.True(Bounds(groups[i]).Top >= Bounds(groups[i - 1]).Bottom - .01, $"Source groups {groups[i - 1]}/{groups[i]} overlap or remain in source order: {Bounds(groups[i - 1])} / {Bounds(groups[i])}"); }
        internal void AssertHeight(int group, double height) => Assert.Equal(height, Bounds(group).Height, 2);
        internal void AssertGroup(int group, double top)
        {
            Rect bounds;
            if (_document)
                bounds = ((Dictionary<DependencyObject, Rect>)typeof(FlowDocumentPresentation).GetField("_tableGeometry", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(_body)!) [(TableRowGroup)_groups[group]];
            else { var element = (FrameworkElement)_groups[group]; bounds = new Rect(element.TransformToAncestor(_root), element.RenderSize); }
            Assert.Equal(top, bounds.Top, 2); Assert.Equal(0, bounds.Height, 2); Assert.Equal(160, bounds.Width, 2);
        }
        internal void RemoveGroup(int group) { if (_document) ((Table)_table).RowGroups.Remove((TableRowGroup)_groups[group]); else ((StackPanel)_table).Children.Remove((UIElement)_groups[group]); }
        internal void RestoreGroup(int group) { if (_document) ((Table)_table).RowGroups.Insert(group, (TableRowGroup)_groups[group]); else ((StackPanel)_table).Children.Insert(group, (UIElement)_groups[group]); }
        internal void SpanFirstCell(int group, int count)
        {
            var cell = _cells[group][0][0]; if (_document) ((TableCell)cell).RowSpan = count; else Grid.SetRowSpan((UIElement)cell, count);
            var removed = _cells[group][1][0]; if (_document) { ((TableRow)_rows[group][1]).Cells.Remove((TableCell)removed); _paragraphs.Remove((Paragraph)((TableCell)removed).Blocks[0]); } else ((StackPanel)_rows[group][1]).Children.Remove((UIElement)removed);
            _cells[group][1].RemoveAt(0); if (_document) _expected = Document!.GetText();
        }
        internal void Wrap()
        {
            if (_document)
            {
                Document!.Blocks.Clear(); var outer = new Table { CellSpacing = 0 }; var group = new TableRowGroup(); var row = new TableRow(); outer.RowGroups.Add(group); group.Rows.Add(row); row.Cells.Add(new TableCell((Table)_table) { Padding = new(0), BorderThickness = new(0) }); row.Cells.Add(new TableCell(new Section()) { Padding = new(0), BorderThickness = new(0) }); Document.Blocks.Add(outer); _expected = Document.GetText();
            }
            else
            {
                var outer = new StackPanel(); Css.SetStyle(outer, "display:table;width:160px;border-spacing:0;"); var row = new StackPanel(); Css.SetClass(row, "row"); var cell = new Border { Child = (UIElement)_table }; Css.SetClass(cell, "cell"); row.Children.Add(cell); outer.Children.Add(row); Css.SetStyleSheet(outer, _sheet); _root = outer;
            }
        }
        internal void Layout()
        { for (var i = 0; i < 3; i++) { typeof(Dispatcher).GetMethod("ProcessQueue", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!.Invoke(Dispatcher.CurrentDispatcher, null); _root.Measure(new(160, double.PositiveInfinity)); _root.Arrange(new(0, 0, 160, _root.DesiredSize.Height)); _root.UpdateLayout(); } }
        internal void AssertOriginal()
        {
            var actual = _document ? ((Table)_table).RowGroups.Cast<DependencyObject>().ToArray() : ((StackPanel)_table).Children.Cast<DependencyObject>().ToArray(); Assert.Equal(_groups, actual);
            if (!_document) return;
            Assert.Equal(_expected, Document!.GetText()); Assert.Equal(_paragraphs.Count, _body!.Paragraphs.Count);
            for (var index = 0; index < _paragraphs.Count; index++)
            {
                var p = _paragraphs[index]; Assert.Same(p.Inlines, _body.Paragraphs[index].Inlines); Assert.Same(_runs[p], p.Inlines[0]); Assert.Same(_parents[p], p.Parent);
            }
        }
        public void Dispose() => _body?.Dispose();
    }
}
