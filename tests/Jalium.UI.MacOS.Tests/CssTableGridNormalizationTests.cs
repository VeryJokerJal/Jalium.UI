using System.Reflection;
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
public sealed class CssTableGridNormalizationTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();
    public static IEnumerable<object[]> Modes()
    { foreach (var document in new[] { false, true }) foreach (var rtl in new[] { false, true }) foreach (var collapse in new[] { false, true }) yield return [document, rtl, collapse]; }

    [Theory] [MemberData(nameof(Modes))]
    public void AutomaticColumnsMergeOnlyIndistinguishableImplicitTracks(bool document, bool rtl, bool collapse)
    {
        using var view = new Fixture(document, rtl, collapse); var a = view.Row(3, 1); view.Row(3, 1);
        foreach (var (columns, first, last) in new[] { (0, 150d, 150d), (1, 150d, 150d), (2, 200d, 100d), (4, 225d, 75d) })
        {
            view.Columns(columns); view.Layout(); Assert.Equal(first, view.Bounds(a[0]).Width, 2); Assert.Equal(last, view.Bounds(a[1]).Width, 2);
            Assert.Equal(300, view.Bounds(view.Table).Width, 2); view.OriginalSpans(a[0], 3);
        }
    }

    [Theory] [MemberData(nameof(Modes))]
    public void IntrinsicSpacingUsesTheMergedGridEvenWhenFixedHasAutoWidth(bool document, bool rtl, bool collapse)
    {
        using var view = new Fixture(document, rtl, collapse); var a = view.Row(10, 1); view.Row(10, 1);
        foreach (var cell in view.Cells) view.CellStyle(cell, "width:50px;");
        foreach (var mode in new[] { "auto", "fixed" })
        {
            view.TableStyle($"width:auto;table-layout:{mode};border-spacing:20px;"); view.Layout();
            Assert.Equal(collapse ? 100 : 160, view.Bounds(view.Table).Width, 2);
            Assert.Equal(50, view.Bounds(a[0]).Width, 2); Assert.Equal(50, view.Bounds(a[1]).Width, 2);
        }
    }

    [Theory] [MemberData(nameof(Modes))]
    public void EveryRowsCellBoundariesProtectTheirOwnTrack(bool document, bool rtl, bool collapse)
    {
        using var view = new Fixture(document, rtl, collapse); var top = view.Row(4); var bottom = view.Row(1, 3);
        view.Layout(); Assert.Equal(300, view.Bounds(top[0]).Width, 2); Assert.Equal(150, view.Bounds(bottom[0]).Width, 2); Assert.Equal(150, view.Bounds(bottom[1]).Width, 2);
        view.Span(bottom[1], 2); var third = view.AddCell(view.Rows[1], 1); view.Layout();
        Assert.Equal(100, view.Bounds(bottom[0]).Width, 2); Assert.Equal(100, view.Bounds(bottom[1]).Width, 2); Assert.Equal(100, view.Bounds(third).Width, 2);
        view.OriginalSpans(top[0], 4);
    }

    [Theory] [MemberData(nameof(Modes))]
    public void ModeAndColumnDefinitionChangesRestoreSourceSpans(bool document, bool rtl, bool collapse)
    {
        using var view = new Fixture(document, rtl, collapse); var a = view.Row(3, 1); view.Row(3, 1); var parents = view.Cells.Select(view.Parent).ToArray();
        view.Layout(); Assert.Equal(150, view.Bounds(a[0]).Width, 2);
        view.TableStyle("table-layout:fixed;"); view.Layout(); Assert.Equal(225, view.Bounds(a[0]).Width, 2);
        view.TableStyle("table-layout:auto;"); view.Columns(4); view.Layout(); Assert.Equal(225, view.Bounds(a[0]).Width, 2);
        view.Columns(0); view.Layout(); Assert.Equal(150, view.Bounds(a[0]).Width, 2);
        Assert.Equal(parents, view.Cells.Select(view.Parent)); view.OriginalSpans(a[0], 3); view.OriginalCollections();
    }

    [Theory] [MemberData(nameof(Modes))]
    public void RealCjkTextUsesTheSameIntrinsicAdvancesInBothTableAdapters(bool document, bool rtl, bool collapse)
    {
        using var view = new Fixture(document, rtl, collapse); var cells = view.Row(10, 1); view.Text(cells[0], "汇总"); view.Text(cells[1], "原文");
        view.TableStyle("width:auto;border-spacing:8px;"); view.Layout(); Assert.Equal(collapse ? 64 : 88, view.Bounds(view.Table).Width, 2);
        Assert.Equal(32, view.Bounds(cells[0]).Width, 2); Assert.Equal(32, view.Bounds(cells[1]).Width, 2); view.OriginalSpans(cells[0], 10); view.OriginalCollections();
    }

    [Fact]
    public void CssIntrinsicAdvancesExcludeTheNativeInkAllowance()
    {
        var text = new TextBlock { Text = "汇总", FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24, TextWrapping = TextWrapping.NoWrap };
        text.Measure(new(double.PositiveInfinity, double.PositiveInfinity)); var nativeWidth = text.DesiredSize.Width;
        var intrinsic = new CssLayoutBox(text, 0); intrinsic.MeasureIntrinsicWidth(); Assert.Equal(32, intrinsic.MaxContent.Width, 2); Assert.Equal(intrinsic.MaxContent.Width + 2, nativeWidth, 2);
    }

    [Theory] [MemberData(nameof(Modes))]
    public void MissingCellsPaintInheritedRowGroupAndColumnLayers(bool document, bool rtl, bool collapse)
    {
        using var view = new Fixture(document, rtl, collapse); view.Row(1, 1, 1); view.Row(1); view.Columns(3);
        view.TableStyle("background:#f0d0b0;"); view.RowStyle(1, "background:#2050a0;"); view.Layout(); view.AssertHole(1, 2, Color.FromRgb(32, 80, 160));
        view.RowStyle(1, "background:transparent;"); Css.SetStyle(view.Group, "background:#ccddee;"); view.Layout(); view.AssertHole(1, 2, Color.FromRgb(204, 221, 238));
        Css.SetStyle(view.Group, "background:transparent;"); view.ColumnStyle(1, "background:#224466;"); view.Layout(); view.AssertHole(1, 1, Color.FromRgb(34, 68, 102)); view.AssertHole(1, 2, Color.FromRgb(240, 208, 176));
        view.OriginalCollections();
    }

    [Theory] [MemberData(nameof(Modes))]
    public void ShowingMissingCellsKeepsARowWhoseOnlySourceCellIsHiddenEmpty(bool document, bool rtl, bool collapse)
    {
        using var view = new Fixture(document, rtl, collapse); view.Row(1, 1, 1); var second = view.Row(1);
        view.TableStyle("background:#f0d0b0;"); view.RowStyle(1, "background:#2050a0;"); view.CellStyle(second[0], "empty-cells:hide;"); view.Layout();
        Assert.Equal(40, view.Bounds(view.Rows[1]).Height, 2); Assert.Equal(80, view.Bounds(view.Table).Height, 2); view.AssertHole(1, 2, Color.FromRgb(32, 80, 160));
        view.RowStyle(1, "background:#2050a0;empty-cells:hide;"); view.Layout(); Assert.Equal(collapse ? 40 : 0, view.Bounds(view.Rows[1]).Height, 2);
        view.RowStyle(1, "background:#2050a0;empty-cells:show;"); view.Layout(); Assert.Equal(80, view.Bounds(view.Table).Height, 2); view.AssertHole(1, 2, Color.FromRgb(32, 80, 160));
    }

    [Theory] [MemberData(nameof(Modes))]
    public void RowsWithOnlyMissingCellsPaintWithoutCreatingSourceCells(bool document, bool rtl, bool collapse)
    {
        using var view = new Fixture(document, rtl, collapse); view.Row(); view.Row(); view.Columns(3); view.ColumnStyle(1, "background:#224466;");
        view.Layout(); Assert.Equal(80, view.Bounds(view.Table).Height, 2); view.AssertHole(0, 1, Color.FromRgb(34, 68, 102)); view.AssertHole(1, 1, Color.FromRgb(34, 68, 102)); view.OriginalCollections(); view.NoSyntheticText();
    }

    [Theory] [MemberData(nameof(Modes))]
    public void RowspansCoverSlotsAndCollapsedColumnsProjectTheRemainingHoles(bool document, bool rtl, bool collapse)
    {
        using var view = new Fixture(document, rtl, collapse); var top = view.Row(1, 1, 1); view.Row(1); view.Columns(3); view.Rowspan(top[0], 2);
        view.CellStyle(top[0], "background:#336699;"); view.RowStyle(1, "background:#2050a0;"); view.Layout(); view.AssertHole(1, 0, Color.FromRgb(51, 102, 153)); view.AssertHole(1, 2, Color.FromRgb(32, 80, 160));
        view.ColumnStyle(1, "visibility:collapse;"); view.Layout(); Assert.Equal(200, view.Bounds(view.Table).Width, 2); view.AssertAt(view.Bounds(view.Table).Left + (rtl ? 50 : 150), 60, Color.FromRgb(32, 80, 160));
        view.ColumnStyle(1, "visibility:visible;"); view.Layout(); Assert.Equal(300, view.Bounds(view.Table).Width, 2); view.AssertHole(1, 2, Color.FromRgb(32, 80, 160)); view.OriginalCollections();
    }

    [Theory] [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void CachedPagesPreserveOriginalTextAcrossMissingCellChanges(bool rtl, bool collapse)
    {
        using var view = new Fixture(true, rtl, collapse); view.Row(1, 1, 1); var lower = view.Row(1); view.CellStyle(lower[0], "empty-cells:hide;");
        var footer = new Paragraph(new Run("末段原文")); view.Document!.Blocks.Add(footer); var run = footer.Inlines[0]; var original = view.Document.GetText(); view.Document.PageHeight = 48;
        for (var pass = 0; pass < 2; pass++)
        {
            if (pass != 0) view.Columns(3); view.Layout(); var paginator = view.Document.ViewerPaginator; Assert.Equal(3, paginator.PageCount);
            for (var index = 0; index < 3; index++)
            {
                using var page = (IDisposable)paginator.GetPage(index).Visual!; var provider = Provider((UIElement)page);
                Assert.Equal(index == 2, provider.GetVisibleRanges().Any(range => range.GetText(-1).Contains("末段")));
                Assert.Equal(index == 2, provider.DocumentRange.FindText("末段", false, false)!.GetBoundingRectangles().Length > 0);
            }
            Assert.Equal(original, view.Document.GetText()); Assert.Same(run, footer.Inlines[0]); view.OriginalCollections();
        }
    }

    private static ITextProvider Provider(UIElement element) => (ITextProvider)element.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
    private sealed class Fixture : IDisposable
    {
        private readonly bool _document, _rtl, _collapse;
        private readonly FrameworkElement _root;
        private readonly FlowDocumentPresentation? _body;
        private readonly List<DependencyObject> _columns = [];
        internal readonly List<DependencyObject> Rows = [], Cells = [];
        internal readonly DependencyObject Table, Group;
        internal readonly FlowDocument? Document;
        private const string Sheet = ".grid{display:table;margin:0;padding:0;border:0;} .group{display:table-row-group;} .row{display:table-row;height:40px;} .cell{display:table-cell;padding:0;border:0;vertical-align:top;} .col{display:table-column;width:auto;} Paragraph{margin:0;}";
        internal Fixture(bool document, bool rtl, bool collapse)
        {
            _document = document; _rtl = rtl; _collapse = collapse;
            if (document)
            {
                var table = new Table { CellSpacing = 0 }; Table = table; var group = new TableRowGroup(); Group = group; table.RowGroups.Add(group);
                Document = new FlowDocument(table) { PagePadding = new(0), PageWidth = 400, FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24 };
                Css.SetStyleSheet(Document, Sheet); _body = new(); _body.SetDocument(Document); _root = _body;
            }
            else
            {
                var table = new StackPanel(); Table = table; var group = new StackPanel(); Group = group; Css.SetStyle(group, "display:table-row-group;"); table.Children.Add(group); _root = table; Css.SetStyleSheet(table, Sheet);
            }
            Css.SetClass(Table, "grid"); Css.SetClass(Group, "group"); TableStyle("");
        }
        internal DependencyObject[] Row(params int[] spans)
        {
            DependencyObject row;
            if (_document) { var native = new TableRow(); ((TableRowGroup)Group).Rows.Add(native); row = native; }
            else { var ordinary = new StackPanel(); ((StackPanel)Group).Children.Add(ordinary); row = ordinary; }
            Css.SetClass(row, "row"); Rows.Add(row); return spans.Select(span => AddCell(row, span)).ToArray();
        }
        internal DependencyObject AddCell(DependencyObject row, int span)
        {
            DependencyObject cell;
            if (_document) { var native = new TableCell { Padding = new(0), BorderThickness = new(0) }; ((TableRow)row).Cells.Add(native); cell = native; }
            else { var ordinary = new Border(); ((StackPanel)row).Children.Add(ordinary); cell = ordinary; }
            Css.SetClass(cell, "cell"); Span(cell, span); Cells.Add(cell); return cell;
        }
        internal void Span(DependencyObject cell, int span) { if (_document) ((TableCell)cell).ColumnSpan = span; else Grid.SetColumnSpan((UIElement)cell, span); }
        internal void Rowspan(DependencyObject cell, int span) { if (_document) ((TableCell)cell).RowSpan = span; else Grid.SetRowSpan((UIElement)cell, span); }
        internal void OriginalSpans(DependencyObject cell, int expected) => Assert.Equal(expected, _document ? ((TableCell)cell).ColumnSpan : Grid.GetColumnSpan((UIElement)cell));
        internal object? Parent(DependencyObject cell) => _document ? ((TableCell)cell).Parent : ((FrameworkElement)cell).VisualParent;
        internal void TableStyle(string style) => Css.SetStyle(Table, $"width:300px;table-layout:auto;border-spacing:0;direction:{(_rtl ? "rtl" : "ltr")};border-collapse:{(_collapse ? "collapse" : "separate")};" + style);
        internal void CellStyle(DependencyObject cell, string style) => Css.SetStyle(cell, style);
        internal void Text(DependencyObject cell, string words)
        {
            if (_document) ((TableCell)cell).Blocks.Add(new Paragraph(new Run(words)));
            else { var text = new TextBlock { Text = words, FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24 }; Css.SetStyle(text, "display:block;"); ((Border)cell).Child = text; }
        }
        internal void RowStyle(int index, string style) => Css.SetStyle(Rows[index], style);
        internal void ColumnStyle(int index, string style) => Css.SetStyle(_columns[index], style);
        internal void Columns(int count)
        {
            if (_document) ((Table)Table).Columns.Clear(); else foreach (var column in _columns) ((StackPanel)Table).Children.Remove((UIElement)column);
            _columns.Clear();
            for (var i = 0; i < count; i++)
            {
                DependencyObject column;
                if (_document) { var native = new TableColumn(); ((Table)Table).Columns.Add(native); column = native; }
                else { var ordinary = new Border(); ((StackPanel)Table).Children.Insert(i, ordinary); column = ordinary; }
                Css.SetClass(column, "col"); _columns.Add(column);
            }
        }
        internal void Layout()
        { for (var i = 0; i < 4; i++) { Dispatcher.CurrentDispatcher.ProcessQueue(); _root.Measure(new(400, double.PositiveInfinity)); _root.Arrange(new(0, 0, _document ? 400 : _root.DesiredSize.Width, Math.Max(1, _root.DesiredSize.Height))); _root.UpdateLayout(); } }
        internal Rect Bounds(DependencyObject source) => _document ? ((Dictionary<DependencyObject, Rect>)typeof(FlowDocumentPresentation).GetField("_tableGeometry", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_body)!) [source] : new(((FrameworkElement)source).TransformToAncestor(_root), ((FrameworkElement)source).RenderSize);
        internal void AssertHole(int row, int column, Color expected)
        { var table = Bounds(Table); var x = _rtl ? table.Right - (column + .5) * 100 : table.Left + (column + .5) * 100; AssertAt(x, Bounds(Rows[row]).Top + 20, expected); }
        internal void AssertAt(double x, double y, Color expected)
        {
            var width = Math.Max(1, (int)Math.Ceiling(_root.ActualWidth)); var height = Math.Max(1, (int)Math.Ceiling(_root.ActualHeight));
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Bgra32); var drawing = new SoftwareDrawingContext(bitmap); _root.Render(drawing); drawing.Close();
            var pixels = new byte[width * height * 4]; bitmap.CopyPixels(pixels, width * 4, 0); var index = ((int)y * width + (int)x) * 4;
            Assert.InRange(index, 0, pixels.Length - 4); Assert.Equal(new byte[] { expected.B, expected.G, expected.R, expected.A }, pixels[index..(index + 4)]);
        }
        internal void OriginalCollections()
        {
            if (_document) { Assert.Equal(Rows, ((TableRowGroup)Group).Rows.Cast<DependencyObject>()); Assert.Equal(Cells, ((TableRowGroup)Group).Rows.SelectMany(row => row.Cells).Cast<DependencyObject>()); Assert.Equal(Cells.Count, _body!.TableCells.Count); }
            else { Assert.Equal(Rows, ((StackPanel)Group).Children.Cast<DependencyObject>()); Assert.Equal(Cells, ((StackPanel)Group).Children.Cast<StackPanel>().SelectMany(row => row.Children.Cast<DependencyObject>())); }
        }
        internal void NoSyntheticText() { if (!_document) return; Assert.Empty(_body!.Paragraphs); Assert.Empty(Provider(_body).DocumentRange.GetBoundingRectangles()); }
        public void Dispose() => _body?.Dispose();
    }
}
