using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using System.Reflection;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssEmptyTableGridTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();
    public static IEnumerable<object[]> Modes()
    { foreach (var document in new[] { false, true }) foreach (var rtl in new[] { false, true }) foreach (var collapse in new[] { false, true }) yield return [document, rtl, collapse]; }
    public static IEnumerable<object[]> Insets()
    { foreach (var mode in Modes()) foreach (var borderBox in new[] { false, true }) yield return [..mode, borderBox]; }

    [Theory]
    [MemberData(nameof(Modes))]
    public void UnusedColumnWidthsCannotEnlargeAnEmptyTableOrItsZeroHeightGroups(bool document, bool rtl, bool collapse)
    {
        using var view = new Fixture(document, rtl, collapse);
        foreach (var layout in new[] { "auto", "fixed" })
        {
            view.Style($"width:160px;height:200px;table-layout:{layout};"); view.Layout(); view.Size(160, 200); view.AllGroups(0, 0, 160, 0); view.EmptyText();
            view.Column(0, "width:1000px;visibility:collapse;"); view.Column(1, "width:2000px;visibility:collapse;"); view.Layout(); view.Size(160, 200); view.AllGroups(0, 0, 160, 0);
            view.Column(0, "width:300px;"); view.Column(1, "width:400px;");
        }
        view.OriginalGroups();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void AutoEmptyTablesOnlyContributeTheirOwnChromeAndAuthoredMinimum(bool document, bool rtl, bool collapse)
    {
        using var view = new Fixture(document, rtl, collapse); view.Style("width:auto;height:auto;padding:10px;border:2px solid blue;"); view.Layout();
        view.Size(collapse ? 4 : 24, collapse ? 4 : 24); view.AllGroups(collapse ? 2 : 12, collapse ? 2 : 12, 0, 0); view.EmptyText();
        view.Style("width:auto;height:80px;min-width:90px;max-width:120px;"); view.Layout(); view.Size(90, 80);
        view.Style("width:160px;height:80px;max-width:120px;"); view.Layout(); view.Size(120, 80);
    }

    [Theory]
    [MemberData(nameof(Insets))]
    public void EmptyGridInsetsRespectBoxSizingAndIgnoreUnusedColumnAndSpacingMinima(bool document, bool rtl, bool collapse, bool borderBox)
    {
        using var view = new Fixture(document, rtl, collapse); view.Style($"width:160px;height:200px;padding:10px;border:2px solid blue;border-spacing:5px 10px;box-sizing:{(borderBox ? "border-box" : "content-box")};"); view.Layout();
        var inset = collapse ? 2 : 12; var spacingX = collapse ? 0 : 5; var spacingY = collapse ? 0 : 10;
        var contentWidth = borderBox ? 160 - 2 * inset : 160; view.Size(borderBox ? 160 : 160 + 2 * inset, borderBox ? 200 : 200 + 2 * inset);
        view.AllGroups(inset + spacingX, inset + spacingY, contentWidth - 2 * spacingX, 0); Assert.True(view.Count(Colors.Blue) > 100); view.EmptyText();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void EmptyRowsStillUseTheContentWidthAndDistributeTheirSpecifiedTableHeight(bool document, bool rtl, bool collapse)
    {
        using var view = new Fixture(document, rtl, collapse); view.RemoveColumns(); view.Rows(2, cells: false); view.Layout(); view.Size(160, 200);
        Assert.Equal(new Rect(0, 0, 160, 100), view.Row(0)); Assert.Equal(new Rect(0, 100, 160, 100), view.Row(1)); Assert.Equal(new Rect(0, 0, 160, 200), view.Group(2));
        view.EmptyText(); view.OriginalGroups();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void RowGroupsSpanInnerTracksWithoutIncludingOuterBorderSpacing(bool document, bool rtl, bool collapse)
    {
        using var view = new Fixture(document, rtl, collapse); view.RemoveColumns(); view.Rows(2, cells: true); view.Style("width:160px;height:200px;border-spacing:10px 6px;"); view.Layout();
        Assert.Equal(collapse ? new Rect(0, 0, 160, 200) : new Rect(10, 6, 140, 188), view.Group(2)); view.OriginalGroups();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void AddingAndClearingCellsRestoresEmptySizingWithoutRecreatingColumnsOrRuns(bool document, bool rtl, bool collapse)
    {
        using var view = new Fixture(document, rtl, collapse); view.Style("width:160px;height:200px;table-layout:fixed;"); view.Layout(); view.Size(160, 200);
        view.Rows(1, cells: true); view.Layout(); view.Size(700, 200); view.SourceIdentity();
        view.Column(0, "width:500px;"); view.Layout(); view.Size(900, 200); view.SourceIdentity();
        view.ClearRows(); view.Layout(); view.Size(160, 200); view.AllGroups(0, 0, 160, 0); view.EmptyText();
        view.RestoreRows(); view.Layout(); view.Size(900, 200); view.SourceIdentity(); view.OriginalGroups();
        view.ClearRows(); view.Column(0, "width:300px;"); view.Layout(); view.Size(160, 200);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void LocalWidthOverridesAndClearKeepTheirSourcePriorityWithoutUnusedColumnPressure(bool document, bool rtl, bool collapse)
    {
        using var view = new Fixture(document, rtl, collapse); view.Style("width:auto;height:80px;"); view.Layout(); view.Size(0, 80);
        view.Table.SetValue(FrameworkElement.WidthProperty, 90d); view.Layout(); view.Size(90, 80);
        view.Table.ClearValue(FrameworkElement.WidthProperty); view.Layout(); view.Size(0, 80); view.EmptyText();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void RemovingAndRestoringAllPartsLeavesNoInventedRowsOrText(bool document, bool rtl, bool collapse)
    {
        using var view = new Fixture(document, rtl, collapse); view.RemoveAllParts(); view.Layout(); view.Size(160, 200); view.EmptyText();
        view.RestoreAllParts(); view.Layout(); view.Size(160, 200); view.AllGroups(0, 0, 160, 0); view.OriginalGroups(); view.EmptyText();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void EmptyNestedTablesDoNotPassUnusedColumnMinimaToTheirOuterCell(bool document, bool rtl, bool collapse)
    {
        using var view = new Fixture(document, rtl, collapse); view.Style("width:auto;height:40px;"); view.Wrap(); view.Layout(); view.Size(0, 40); Assert.Equal(40, view.OuterWidth, 2);
        view.Rows(1, cells: true); view.Layout(); view.Size(360, 40); Assert.Equal(400, view.OuterWidth, 2); view.SourceIdentity();
        view.ClearRows(); view.Layout(); view.Size(0, 40); Assert.Equal(40, view.OuterWidth, 2); view.EmptyText();
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public void CaptionsCanSetTheWidthAndWrapAroundAnEmptyGridWithoutColumnPressure(bool rtl, bool collapse)
    {
        using var view = new Fixture(false, rtl, collapse); view.Style("width:160px;height:80px;"); view.Captions(); view.Layout(); view.Size(240, 110); view.AllGroups(0, 20, 240, 0); view.OriginalGroups();
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public void EmptyPagesHaveNoTextHitsAndTheFollowingOriginalParagraphStaysOnItsOwnPage(bool rtl, bool collapse)
    {
        using var view = new Fixture(true, rtl, collapse); var footer = new Paragraph(new Run("末段原文")); view.Document!.Blocks.Add(footer); var run = footer.Inlines[0]; var original = view.Document.GetText(); view.Layout(); var paginator = view.Document.ViewerPaginator; Assert.Equal(3, paginator.PageCount);
        void CheckPages()
        {
            for (var index = 0; index < paginator.PageCount; index++)
            {
                using var page = Assert.IsType<FlowDocumentPresentation>(paginator.GetPage(index).Visual); var provider = Provider(page); var range = provider.DocumentRange.FindText("末段", false, false)!;
                Assert.Equal(index == paginator.PageCount - 1, range.GetBoundingRectangles().Length > 0);
                if (index < paginator.PageCount - 1) { Assert.Empty(provider.GetVisibleRanges()); Assert.Null(provider.RangeFromPoint(page.PointToScreen(new(20, 20)))); }
            }
        }
        CheckPages(); view.Rows(1, cells: true); view.Layout(); view.SourceIdentity(); view.ClearRows(); view.Layout(); CheckPages(); Assert.Same(run, footer.Inlines[0]); Assert.Equal(original, view.Document.GetText());
    }

    public static IEnumerable<object[]> LiveMutations()
    { foreach (var kind in new[] { "groups", "rows", "cells", "columns" }) foreach (var operation in new[] { "clear", "range", "replace" }) foreach (var rtl in new[] { false, true }) yield return [kind, operation, rtl]; }

    [Theory]
    [MemberData(nameof(LiveMutations))]
    public void LiveViewerCollectionEditsPublishOnlyParentedItems(string kind, string operation, bool rtl)
    {
        TableCell Cell(string text) => new(new Paragraph(new Run(text))) { Padding = new(0), BorderThickness = new(0) };
        TableRow Row() { var row = new TableRow(); for (var i = 0; i < 3; i++) row.Cells.Add(Cell("原文" + i)); return row; }
        TableRowGroup Group() { var group = new TableRowGroup(); for (var i = 0; i < 3; i++) group.Rows.Add(Row()); return group; }
        var table = new Table { CellSpacing = 0 }; for (var i = 0; i < 3; i++) { table.Columns.Add(new TableColumn()); table.RowGroups.Add(Group()); }
        var footer = new Paragraph(new Run("末段原文")); var originalRun = footer.Inlines[0];
        var doc = new FlowDocument(table) { FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24, PagePadding = new(0), PageWidth = 400, PageHeight = 80, FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight }; doc.Blocks.Add(footer); Css.SetStyleSheet(doc, "Table,Paragraph{margin:0;}Table{width:320px;table-layout:fixed;}TableRow{height:40px;}");
        var viewer = new FlowDocumentScrollViewer { Document = doc, Height = 400, IsToolBarVisible = false, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        void Layout() { for (var i = 0; i < 3; i++) { Dispatcher.CurrentDispatcher.ProcessQueue(); viewer.Measure(new(400, 400)); viewer.Arrange(new(0, 0, 400, 400)); viewer.UpdateLayout(); } }
        var notifications = 0;
        void Snapshot(object? sender, EventArgs e)
        {
            notifications++;
            foreach (var column in table.Columns) Assert.Same(table, column.Parent);
            foreach (var group in table.RowGroups) { Assert.Same(table, group.Parent); foreach (var row in group.Rows) { Assert.Same(group, row.Parent); foreach (var cell in row.Cells) Assert.Same(row, cell.Parent); } }
        }
        try
        {
            Layout(); Assert.True(viewer.Find("末段")); Layout(); doc.ViewerPaginationChanged += Snapshot;
            if (kind == "groups") { if (operation == "clear") table.RowGroups.Clear(); else if (operation == "range") table.RowGroups.RemoveRange(0, 2); else table.RowGroups[0] = Group(); }
            else if (kind == "rows") { var rows = table.RowGroups[0].Rows; if (operation == "clear") rows.Clear(); else if (operation == "range") rows.RemoveRange(0, 2); else rows[0] = Row(); }
            else if (kind == "cells") { var cells = table.RowGroups[0].Rows[0].Cells; if (operation == "clear") cells.Clear(); else if (operation == "range") cells.RemoveRange(0, 2); else cells[0] = Cell("替换原文"); }
            else { var columns = table.Columns; if (operation == "clear") columns.Clear(); else if (operation == "range") columns.RemoveRange(0, 2); else columns[0] = new TableColumn { Width = new GridLength(90) }; }
            Layout(); Assert.True(notifications > 0); Assert.True(viewer.Find("末段")); Assert.Equal("末段", viewer.Selection!.Text); Assert.Same(originalRun, footer.Inlines[0]); Assert.True(doc.ViewerPaginator.PageCount > 0);
        }
        finally { doc.ViewerPaginationChanged -= Snapshot; viewer.Document = null; }
    }

    private static ITextProvider Provider(UIElement visual) => (ITextProvider)visual.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
    private sealed class Fixture : IDisposable
    {
        internal readonly DependencyObject Table;
        internal readonly FlowDocument? Document;
        private readonly bool _document, _collapse;
        private readonly FlowDocumentPresentation? _body;
        private FrameworkElement _root;
        private Table? _outerDocument;
        private StackPanel? _outerOrdinary;
        private readonly DependencyObject[] _groups = new DependencyObject[3], _columns = new DependencyObject[2];
        private readonly List<DependencyObject> _rows = [];
        private readonly List<Paragraph> _sources = [];
        private readonly List<Inline> _runs = [];
        private readonly string _sheet = "Table,Paragraph{margin:0;}.grid{display:table;}.col{display:table-column;}.row{display:table-row;height:40px;}.cell{display:table-cell;padding:0;border:0;vertical-align:top;}.body{font-family:'PingFang SC';font-size:16px;line-height:24px;white-space:pre-wrap;}";
        private Dictionary<DependencyObject, Rect> Geometry => (Dictionary<DependencyObject, Rect>)typeof(FlowDocumentPresentation).GetField("_tableGeometry", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_body)!;
        internal Fixture(bool document, bool rtl, bool collapse)
        {
            _document = document; _collapse = collapse; Table = document ? new Table { CellSpacing = 0 } : new StackPanel(); Css.SetClass(Table, "grid");
            for (var i = 0; i < 2; i++) { _columns[i] = document ? new TableColumn() : new Border(); Css.SetClass(_columns[i], "col"); Column(i, $"width:{(i == 0 ? 300 : 400)}px;"); }
            for (var i = 0; i < 3; i++) { _groups[i] = document ? new TableRowGroup() : new StackPanel(); Css.SetStyle(_groups[i], "display:" + (i == 0 ? "table-footer-group" : i == 1 ? "table-header-group" : "table-row-group") + ";"); }
            RestoreAllParts();
            if (document) { Document = new FlowDocument((Table)Table) { PagePadding = new(0), PageWidth = 400, PageHeight = 80, FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24, Foreground = Brushes.Black, FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight }; Css.SetStyleSheet(Document, _sheet); _body = new(); _body.SetDocument(Document); _root = _body; }
            else { _root = (FrameworkElement)Table; _root.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight; Css.SetStyleSheet(_root, _sheet); }
            Style("width:160px;height:200px;");
        }
        internal void Style(string style) => Css.SetStyle(Table, "border-spacing:0;border:0;padding:0;border-collapse:" + (_collapse ? "collapse" : "separate") + ";" + style);
        internal void Column(int index, string style) => Css.SetStyle(_columns[index], style);
        internal Rect TableBounds => _document ? Geometry[Table] : new Rect(((FrameworkElement)Table).RenderSize);
        private Rect Relative(Rect box) { var origin = TableBounds; return new(box.X - origin.X, box.Y - origin.Y, box.Width, box.Height); }
        private Rect Bounds(DependencyObject source) => _document ? Relative(Geometry[source]) : new Rect(((FrameworkElement)source).TransformToAncestor((Visual)Table), ((FrameworkElement)source).RenderSize);
        internal Rect Row(int index) => Bounds(_rows[index]);
        internal Rect Group(int index) => Bounds(_groups[index]);
        internal void Size(double width, double height) { Assert.Equal(width, TableBounds.Width, 2); Assert.Equal(height, TableBounds.Height, 2); }
        internal void AllGroups(double x, double y, double width, double height) { foreach (var group in Enumerable.Range(0, 3)) Assert.Equal(new Rect(x, y, width, height), Group(group)); }
        internal double OuterWidth => _document ? Geometry[_outerDocument!].Width : _outerOrdinary!.ActualWidth;
        internal void Rows(int count, bool cells)
        {
            ClearRows(); _rows.Clear(); _sources.Clear(); _runs.Clear();
            for (var index = 0; index < count; index++)
            {
                var row = _document ? (DependencyObject)new TableRow() : new StackPanel(); Css.SetClass(row, "row"); _rows.Add(row);
                if (cells) for (var column = 0; column < 2; column++)
                {
                    var words = column == 0 ? "原文甲" : "原文乙"; DependencyObject cell;
                    if (_document) { var p = new Paragraph(new Run(words)); _sources.Add(p); _runs.Add(p.Inlines[0]); cell = new TableCell(p) { Padding = new(0), BorderThickness = new(0) }; ((TableRow)row).Cells.Add((TableCell)cell); }
                    else { var text = new TextBlock { Text = words }; Css.SetClass(text, "body"); cell = new Border { Child = text }; ((StackPanel)row).Children.Add((UIElement)cell); }
                    Css.SetClass(cell, "cell");
                }
            }
            RestoreRows();
        }
        internal void RestoreRows() { foreach (var row in _rows) if (_document) ((TableRowGroup)_groups[2]).Rows.Add((TableRow)row); else ((StackPanel)_groups[2]).Children.Add((UIElement)row); }
        internal void ClearRows() { if (_document) ((TableRowGroup)_groups[2]).Rows.Clear(); else ((StackPanel)_groups[2]).Children.Clear(); }
        internal void RemoveColumns() { if (_document) ((Table)Table).Columns.Clear(); else foreach (var column in _columns) ((StackPanel)Table).Children.Remove((UIElement)column); }
        internal void RemoveAllParts() { if (_document) { ((Table)Table).RowGroups.Clear(); ((Table)Table).Columns.Clear(); } else ((StackPanel)Table).Children.Clear(); }
        internal void RestoreAllParts()
        {
            foreach (var column in _columns) if (_document) ((Table)Table).Columns.Add((TableColumn)column); else ((StackPanel)Table).Children.Add((UIElement)column);
            foreach (var group in _groups) if (_document) ((Table)Table).RowGroups.Add((TableRowGroup)group); else ((StackPanel)Table).Children.Add((UIElement)group);
        }
        internal void OriginalGroups()
        { var actual = _document ? ((Table)Table).RowGroups.Cast<DependencyObject>() : ((StackPanel)Table).Children.Cast<DependencyObject>().Where(x => _groups.Contains(x)); Assert.Equal(_groups, actual); }
        internal void EmptyText()
        {
            if (!_document) return; Assert.Empty(_body!.Paragraphs); Assert.Empty(Provider(_body).DocumentRange.GetBoundingRectangles()); Assert.Null(Provider(_body).RangeFromPoint(_body.PointToScreen(new(20, 20))));
        }
        internal void SourceIdentity()
        { if (!_document) return; for (var i = 0; i < _sources.Count; i++) { Assert.Same(_runs[i], _sources[i].Inlines[0]); Assert.IsType<TableCell>(_sources[i].Parent); Assert.Single(_body!.Paragraphs, text => ReferenceEquals(text.Inlines, _sources[i].Inlines)); } }
        internal void Wrap()
        {
            if (_document) { Document!.Blocks.Clear(); _outerDocument = new Table { CellSpacing = 0 }; var group = new TableRowGroup(); var row = new TableRow(); _outerDocument.RowGroups.Add(group); group.Rows.Add(row); row.Cells.Add(new TableCell((Table)Table) { Padding = new(0), BorderThickness = new(0) }); var side = new Section(); Css.SetStyle(side, "width:40px;height:40px;"); row.Cells.Add(new TableCell(side) { Padding = new(0), BorderThickness = new(0) }); Css.SetStyle(_outerDocument, "width:auto;"); Document.Blocks.Add(_outerDocument); }
            else { _outerOrdinary = new StackPanel(); Css.SetStyle(_outerOrdinary, "display:table;width:auto;border-spacing:0;"); var row = new StackPanel(); Css.SetClass(row, "row"); var cell = new Border { Child = (UIElement)Table }; Css.SetClass(cell, "cell"); row.Children.Add(cell); var side = new Border { Child = new Border { Width = 40, Height = 40 } }; Css.SetClass(side, "cell"); row.Children.Add(side); _outerOrdinary.Children.Add(row); Css.SetStyleSheet(_outerOrdinary, _sheet); _root = _outerOrdinary; }
        }
        internal void Captions()
        { var top = new Border { Width = 240, Height = 20 }; Css.SetStyle(top, "display:table-caption;"); var bottom = new Border { Height = 10 }; Css.SetStyle(bottom, "display:table-caption;caption-side:bottom;"); ((StackPanel)Table).Children.Add(top); ((StackPanel)Table).Children.Add(bottom); }
        internal void Layout()
        { for (var i = 0; i < 3; i++) { Dispatcher.CurrentDispatcher.ProcessQueue(); _root.Measure(new(400, double.PositiveInfinity)); _root.Arrange(new(0, 0, _document ? 400 : _root.DesiredSize.Width, _root.DesiredSize.Height)); _root.UpdateLayout(); } }
        internal int Count(Color color)
        { var width = Math.Max(1, (int)Math.Ceiling(_root.ActualWidth)); var height = Math.Max(1, (int)Math.Ceiling(_root.ActualHeight)); var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Bgra32); var drawing = new SoftwareDrawingContext(bitmap); _root.Render(drawing); drawing.Close(); var pixels = new byte[width * height * 4]; bitmap.CopyPixels(pixels, width * 4, 0); return Enumerable.Range(0, width * height).Count(i => pixels[i*4] == color.B && pixels[i*4+1] == color.G && pixels[i*4+2] == color.R && pixels[i*4+3] == color.A); }
        public void Dispose() => _body?.Dispose();
    }
}
