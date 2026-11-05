using System.Reflection;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssTableAutoHeightTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();
    public static IEnumerable<object[]> NativeHosts()
    { foreach (var host in new[] { "grid", "scroll", "border", "horizontal" }) foreach (var rtl in new[] { false, true }) foreach (var collapse in new[] { false, true }) yield return [host, rtl, collapse]; }
    public static IEnumerable<object[]> Modes()
    { foreach (var rtl in new[] { false, true }) foreach (var collapse in new[] { false, true }) yield return [rtl, collapse]; }
    public static IEnumerable<object[]> CssHosts()
    {
        foreach (var (host, height, top) in new[] { ("css-block", 40d, 0d), ("css-grid-normal", 200d, 0d), ("css-grid-stretch", 200d, 0d), ("css-grid-start", 40d, 0d), ("css-grid-center", 40d, 80d), ("css-grid-end", 40d, 160d), ("css-flex-row", 200d, 0d), ("css-flex-row-start", 40d, 0d), ("css-flex-column", 40d, 0d), ("css-flex-grow", 200d, 0d), ("css-absolute", 40d, 0d), ("css-fixed", 40d, 0d) })
        foreach (var rtl in new[] { false, true }) foreach (var collapse in new[] { false, true }) yield return [host, height, top, rtl, collapse];
    }
    public static IEnumerable<object[]> HeightLimits()
    {
        foreach (var host in new[] { "grid", "scroll" }) foreach (var rtl in new[] { false, true })
        foreach (var (style, height) in new[] { ("height:80px;", 80d), ("min-height:120px;", 120d), ("max-height:20px;", 40d), ("height:50%;", 100d), ("min-height:50%;", 100d), ("height:200%;", 400d), ("height:0;", 40d), ("height:calc(50% + 10px);", 110d), ("height:80px;max-height:20px;", 40d) }) yield return [host, rtl, style, height];
    }

    public static IEnumerable<object[]> CssHeightLimits()
    {
        foreach (var (host, style, height) in new[] {
            ("css-grid-normal", "max-height:100px;", 100d),
            ("css-grid-normal", "height:50%;", 100d), ("css-grid-normal", "height:200%;", 400d),
            ("css-flex-row", "height:50%;", 100d), ("css-flex-row", "height:200%;", 400d),
            ("css-flex-column", "height:50%;", 100d), ("css-flex-column", "height:200%;flex-shrink:0;", 400d),
            ("css-absolute", "position:absolute;top:0;bottom:0;height:50%;", 100d),
            ("css-absolute", "position:absolute;top:0;bottom:0;height:200%;", 400d),
            ("css-fixed", "position:fixed;top:0;bottom:0;height:50%;", 100d),
            ("css-fixed", "position:fixed;top:0;bottom:0;height:200%;", 400d) })
        foreach (var rtl in new[] { false, true }) foreach (var collapse in new[] { false, true }) yield return [host, style, height, rtl, collapse];
    }

    [Theory] [MemberData(nameof(CssHeightLimits))]
    public void CssContainersResolvePercentagesAgainstTheirContainingBlock(string host, string style, double height, bool rtl, bool collapse)
    {
        using var view = new Fixture(host, rtl, collapse); view.Style(style); view.Layout();
        Assert.Equal(height, view.Table.ActualHeight, 2); Assert.Equal(0, view.Table.TransformToAncestor(view.Root).Y, 2);
        Assert.Equal(height, view.Rows[0].ActualHeight, 2); view.Original();
    }

    [Theory] [MemberData(nameof(NativeHosts))]
    public void DefaultNativeHostsKeepTheNaturalGridAndHitArea(string host, bool rtl, bool collapse)
    {
        using var view = new Fixture(host, rtl, collapse, 2);
        foreach (var height in new[] { 200, 360, 160 })
        {
            view.Layout(height); Assert.Equal(80, view.Table.ActualHeight, 2); Assert.Equal(0, view.Table.TransformToAncestor(view.Root).Y, 2);
            Assert.All(view.Rows, row => Assert.Equal(40, row.ActualHeight, 2)); Assert.Null(view.Table.InputHitTest(new(10, 100))); view.Original();
        }
    }

    public static IEnumerable<object[]> ResizingCssHosts()
    { foreach (var host in new[] { "css-flex-row", "css-flex-column", "css-absolute", "css-fixed" }) foreach (var rtl in new[] { false, true }) foreach (var collapse in new[] { false, true }) yield return [host, rtl, collapse]; }

    [Theory] [MemberData(nameof(ResizingCssHosts))]
    public void FlexAndPositionedPercentagesFollowContainerResizing(string host, bool rtl, bool collapse)
    {
        using var view = new Fixture(host, rtl, collapse);
        foreach (var height in new[] { 200, 320, 160, 200 })
        {
            var container = host switch { "css-flex-row" => "display:flex;", "css-flex-column" => "display:flex;flex-direction:column;", _ => "display:block;position:relative;" };
            var position = host switch { "css-absolute" => "position:absolute;top:0;", "css-fixed" => "position:fixed;top:0;", _ => "" };
            Css.SetStyle(view.Root, $"width:300px;height:{height}px;" + container);
            view.Style(position + "height:calc(50% + 10px);flex-shrink:0;"); view.Layout(height);
            Assert.Equal(height / 2 + 10, view.Table.ActualHeight, 2); Assert.Equal(0, view.Table.TransformToAncestor(view.Root).Y, 2);
            Assert.True(view.Table.IsMeasureValid); Assert.True(view.Table.IsArrangeValid); view.Original();
        }
    }

    [Theory] [MemberData(nameof(CssHosts))]
    public void CssContainersStillApplyTheirRequestedAlignmentAndFlexGrowth(string host, double height, double top, bool rtl, bool collapse)
    {
        using var view = new Fixture(host, rtl, collapse); view.Layout(); Assert.Equal(height, view.Table.ActualHeight, 2); Assert.Equal(top, view.Table.TransformToAncestor(view.Root).Y, 2); Assert.Equal(height, view.Rows[0].ActualHeight, 2); view.Original();
    }

    [Theory] [MemberData(nameof(HeightLimits))]
    public void NativeContainingHeightResolvesAuthorBoundsOnce(string host, bool rtl, string style, double height)
    {
        using var view = new Fixture(host, rtl, false); view.Style(style); view.Layout(); Assert.Equal(height, view.Table.ActualHeight, 2); Assert.Equal(height, view.Table.DesiredSize.Height, 2); Assert.Equal(0, view.Table.TransformToAncestor(view.Root).Y, 2); view.Original();
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void BoxSizingCountsPaddingAndBordersOnlyOnce(bool rtl)
    {
        using var view = new Fixture("scroll", rtl, false);
        foreach (var (style, expected) in new[] { ("height:80px;", 104d), ("height:80px;box-sizing:border-box;", 80d), ("height:50%;", 124d), ("height:50%;box-sizing:border-box;", 100d), ("min-height:120px;", 144d), ("min-height:120px;box-sizing:border-box;", 120d), ("max-height:20px;", 64d), ("height:auto;", 64d) })
        { view.Style("padding:10px;border:2px solid green;box-sizing:content-box;" + style); view.Layout(); Assert.Equal(expected, view.Table.ActualHeight, 2); Assert.Equal(expected, view.Table.DesiredSize.Height, 2); }
    }

    [Theory] [MemberData(nameof(Modes))]
    public void LocalSizesAndSameValuedStretchTransitionsInvalidateLayout(bool rtl, bool collapse)
    {
        using var view = new Fixture("grid", rtl, collapse); view.Style("height:50%;"); view.Table.Height = 90; view.Layout(); Assert.Equal(90, view.Table.ActualHeight, 2);
        view.Table.ClearValue(FrameworkElement.HeightProperty); view.Layout(); Assert.Equal(100, view.Table.ActualHeight, 2);
        view.Style("height:auto;"); view.Layout(); Assert.Equal(40, view.Table.ActualHeight, 2);
        view.Table.VerticalAlignment = VerticalAlignment.Stretch; view.Layout(); Assert.Equal(200, view.Table.ActualHeight, 2);
        view.Table.ClearValue(FrameworkElement.VerticalAlignmentProperty); view.Layout(); Assert.Equal(40, view.Table.ActualHeight, 2); view.Original();
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void ExplicitNativeAlignmentIsPreserved(bool rtl)
    {
        using var view = new Fixture("grid", rtl, false);
        foreach (var (alignment, height, top) in new[] { (VerticalAlignment.Top, 40d, 0d), (VerticalAlignment.Center, 40d, 80d), (VerticalAlignment.Bottom, 40d, 160d), (VerticalAlignment.Stretch, 200d, 0d) })
        { view.Table.VerticalAlignment = alignment; view.Layout(); Assert.Equal(height, view.Table.ActualHeight, 2); Assert.Equal(top, view.Table.TransformToAncestor(view.Root).Y, 2); }
        view.Table.ClearValue(FrameworkElement.VerticalAlignmentProperty); view.Layout(); Assert.Equal(40, view.Table.ActualHeight, 2);
    }

    [Theory] [MemberData(nameof(Modes))]
    public void ScrollportPercentagesRemainStableBeyondOneViewport(bool rtl, bool collapse)
    {
        using var view = new Fixture("scroll", rtl, collapse); var scroll = (ScrollViewer)view.Root; view.Style("height:200%;");
        foreach (var height in new[] { 200, 300, 160, 200 })
        {
            view.Layout(height); Assert.Equal(height * 2, view.Table.ActualHeight, 2); Assert.Equal(height * 2, scroll.ExtentHeight, 2); Assert.Equal(height, scroll.ViewportHeight, 2);
            Assert.True(view.Table.IsMeasureValid); Assert.True(view.Table.IsArrangeValid); scroll.ScrollToVerticalOffset(scroll.ScrollableHeight); view.Layout(height); Assert.Equal(height, scroll.VerticalOffset, 2); view.Original();
        }
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void NativeScrollportBasisKeepsTheElementsMargins(bool rtl)
    {
        using var view = new Fixture("scroll", rtl, false); var scroll = (ScrollViewer)view.Root; view.Style("height:200%;margin:12px;"); view.Layout(); Assert.Equal(400, view.Table.ActualHeight, 2); Assert.Equal(424, scroll.ExtentHeight, 2); Assert.Equal(12, view.Table.TransformToAncestor(view.Root).Y, 2);
        view.Style("height:auto;margin:12px;"); view.Layout(); Assert.Equal(40, view.Table.ActualHeight, 2); Assert.Equal(64, scroll.ExtentHeight, 2); Assert.Equal(12, view.Table.TransformToAncestor(view.Root).Y, 2);
    }

    [Theory] [MemberData(nameof(Modes))]
    public void ExplicitFillViewportStillAllocatesTheViewport(bool rtl, bool collapse)
    {
        using var view = new Fixture("scroll", rtl, collapse); view.Layout(); Assert.Equal(40, view.Table.ActualHeight, 2);
        ScrollViewer.SetFillViewport(view.Table, FillViewportMode.Vertical); view.Layout(); Assert.Equal(200, view.Table.ActualHeight, 2);
        view.Style("height:80px;"); view.Layout(); Assert.Equal(80, view.Table.ActualHeight, 2); Assert.Equal(0, view.Table.TransformToAncestor(view.Root).Y, 2);
        view.Style("height:auto;"); ScrollViewer.SetFillViewport(view.Table, FillViewportMode.None); view.Layout(); Assert.Equal(40, view.Table.ActualHeight, 2);
    }

    [Theory] [MemberData(nameof(Modes))]
    public void CollapsedRowsAndCaptionsDoNotConsumeExtraNativeSlotHeight(bool rtl, bool collapse)
    {
        using var view = new Fixture("scroll", rtl, collapse, 2); Css.SetStyle(view.Rows[0], "visibility:collapse;"); view.Layout(); Assert.Equal(40, view.Table.ActualHeight, 2);
        Css.SetStyle(view.Rows[0], "visibility:visible;"); view.Layout(); Assert.Equal(80, view.Table.ActualHeight, 2); view.Original();
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void CaptionHeightsAreAddedToTheGridInsteadOfTheNativeSlot(bool rtl)
    {
        using var view = new Fixture("scroll", rtl, false); var top = new Border { Height = 20 }; var bottom = new Border { Height = 10 }; Css.SetStyle(top, "display:table-caption;"); Css.SetStyle(bottom, "display:table-caption;caption-side:bottom;"); view.Table.Children.Add(top); view.Table.Children.Add(bottom);
        foreach (var (style, expected) in new[] { ("height:auto;", 70d), ("height:80px;", 110d), ("min-height:120px;", 150d), ("height:50%;", 130d) })
        { view.Style(style); view.Layout(); Assert.Equal(expected, view.Table.ActualHeight, 2); Assert.Equal(20, top.ActualHeight, 2); Assert.Equal(expected - 10, bottom.TransformToAncestor(view.Table).Y, 2); }
    }

    [Theory] [MemberData(nameof(Modes))]
    public void NativeAndDocumentTablesShareAutoAndAuthorHeightLimits(bool rtl, bool collapse)
    {
        using var view = new Fixture("scroll", rtl, collapse); var table = new Table { CellSpacing = 0 }; var group = new TableRowGroup(); var row = new TableRow(); var text = new Paragraph(new Run("原文")); row.Cells.Add(new TableCell(text) { Padding = new(0), BorderThickness = new(0) }); group.Rows.Add(row); table.RowGroups.Add(group);
        var footer = new Paragraph(new Run("末段原文")); var document = new FlowDocument(table) { PagePadding = new(0), PageWidth = 300, PageHeight = 200, FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24 }; document.Blocks.Add(footer);
        Css.SetStyleSheet(document, "FlowDocument{height:200px;}Paragraph,Table{margin:0;}TableRow{height:40px;}TableCell{padding:0;border:0;vertical-align:top;}"); using var body = new FlowDocumentPresentation(); body.SetDocument(document); var original = document.GetText(); var run = text.Inlines[0];
        var chrome = collapse ? 2d : 24d;
        foreach (var (style, expected) in new[] {
            ("height:auto;", 40d), ("height:80px;", 80d), ("min-height:120px;", 120d), ("max-height:20px;", 40d), ("height:50%;", 100d),
            ("padding:10px;border:2px solid green;height:80px;box-sizing:content-box;", 80 + chrome),
            ("padding:10px;border:2px solid green;height:80px;box-sizing:border-box;", 80d),
            ("padding:10px;border:2px solid green;height:50%;box-sizing:content-box;", 100 + chrome),
            ("padding:10px;border:2px solid green;height:50%;box-sizing:border-box;", 100d),
            ("padding:10px;border:2px solid green;min-height:120px;box-sizing:content-box;", 120 + chrome),
            ("padding:10px;border:2px solid green;min-height:120px;box-sizing:border-box;", 120d),
            ("padding:10px;border:2px solid green;max-height:20px;box-sizing:content-box;", 40 + chrome),
            ("padding:10px;border:2px solid green;max-height:20px;box-sizing:border-box;", 40 + chrome) })
        {
            view.Style(style); Css.SetStyle(table, $"width:160px;border-spacing:0;border:0;padding:0;border-collapse:{(collapse ? "collapse" : "separate")};" + style); view.Layout(); Layout(body, 300, 400);
            var geometry = (Dictionary<DependencyObject, Rect>)typeof(FlowDocumentPresentation).GetField("_tableGeometry", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(body)!;
            Assert.Equal(expected, view.Table.ActualHeight, 2); Assert.Equal(expected, geometry[table].Height, 2); Assert.Equal(original, document.GetText()); Assert.Same(run, text.Inlines[0]); Assert.IsType<TableCell>(text.Parent);
        }
    }

    [Theory] [MemberData(nameof(Modes))]
    public void CachedDocumentPagesKeepOriginalFooterAfterHostResizing(bool rtl, bool collapse)
    {
        using var view = new Fixture("scroll", rtl, collapse, 2); var table = new Table { CellSpacing = 0 }; var group = new TableRowGroup(); table.RowGroups.Add(group); for (var i = 0; i < 2; i++) { var row = new TableRow(); row.Cells.Add(new TableCell(new Paragraph(new Run("原文" + i))) { Padding = new(0), BorderThickness = new(0) }); group.Rows.Add(row); }
        var footer = new Paragraph(new Run("末段原文")); var doc = new FlowDocument(table) { PagePadding = new(0), PageWidth = 300, PageHeight = 48, FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24 }; doc.Blocks.Add(footer); Css.SetStyleSheet(doc, "Paragraph,Table{margin:0;}TableRow{height:40px;}TableCell{padding:0;border:0;}"); Css.SetStyle(table, "width:160px;"); var original = doc.GetText(); var run = footer.Inlines[0];
        foreach (var height in new[] { 200, 360, 160 })
        {
            view.Layout(height); Assert.Equal(80, view.Table.ActualHeight, 2); var paginator = doc.ViewerPaginator; Assert.Equal(3, paginator.PageCount);
            for (var i = 0; i < 3; i++) { using var page = (IDisposable)paginator.GetPage(i).Visual!; var provider = (ITextProvider)((UIElement)page).GetAutomationPeer()!.GetPattern(PatternInterface.Text)!; Assert.Equal(i == 2, provider.DocumentRange.FindText("末段", false, false)!.GetBoundingRectangles().Length > 0); }
            Assert.Equal(original, doc.GetText()); Assert.Same(run, footer.Inlines[0]); view.Original();
        }
    }

    private static void Layout(FrameworkElement root, int width, int height)
    { for (var pass = 0; pass < 4; pass++) { Dispatcher.CurrentDispatcher.ProcessQueue(); root.Measure(new(width, height)); root.Arrange(new(0, 0, width, height)); root.UpdateLayout(); } }

    private sealed class Fixture : IDisposable
    {
        internal readonly StackPanel Table = new();
        internal readonly FrameworkElement Root;
        internal readonly List<StackPanel> Rows = [];
        private readonly StackPanel _group = new();
        private readonly List<TextBlock> _texts = [];
        private readonly bool _rtl, _collapse;
        internal Fixture(string host, bool rtl, bool collapse, int rows = 1)
        {
            _rtl = rtl; _collapse = collapse; Css.SetClass(Table, "grid"); Css.SetClass(_group, "group"); Table.Children.Add(_group);
            for (var i = 0; i < rows; i++) { var row = new StackPanel(); Css.SetClass(row, "row"); _group.Children.Add(row); Rows.Add(row); var text = new TextBlock { Text = "原文" + i, FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24 }; Css.SetStyle(text, "display:block;white-space:pre-wrap;"); _texts.Add(text); var cell = new Border { Child = text }; Css.SetClass(cell, "cell"); row.Children.Add(cell); }
            Css.SetStyleSheet(Table, ".grid{display:table;} .group{display:table-row-group;} .row{display:table-row;height:40px;} .cell{display:table-cell;padding:0;border:0;vertical-align:top;}");
            if (host == "scroll") Root = new ScrollViewer { Content = Table, Padding = new(0), BorderThickness = new(0), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
            else if (host == "border") Root = new Border { Child = Table };
            else if (host == "horizontal") { var panel = new StackPanel { Orientation = Orientation.Horizontal }; panel.Children.Add(Table); Root = panel; }
            else if (host == "grid") { var grid = new Grid(); grid.Children.Add(Table); Root = grid; }
            else
            {
                var panel = new StackPanel(); panel.Children.Add(Table); Root = panel;
                var style = host switch { "css-block" => "display:block;", "css-grid-normal" => "display:grid;grid-template-rows:200px;", "css-grid-stretch" => "display:grid;grid-template-rows:200px;align-items:stretch;", "css-grid-start" => "display:grid;grid-template-rows:200px;align-items:start;", "css-grid-center" => "display:grid;grid-template-rows:200px;align-items:center;", "css-grid-end" => "display:grid;grid-template-rows:200px;align-items:end;", "css-flex-row" => "display:flex;", "css-flex-row-start" => "display:flex;align-items:flex-start;", "css-flex-column" or "css-flex-grow" => "display:flex;flex-direction:column;", "css-absolute" or "css-fixed" => "display:block;position:relative;", _ => throw new ArgumentException(host) };
                Css.SetStyle(panel, "width:300px;height:200px;" + style);
            }
            Style(host == "css-flex-grow" ? "flex:1;" : host == "css-absolute" ? "position:absolute;top:0;bottom:0;" : host == "css-fixed" ? "position:fixed;top:0;bottom:0;" : "");
        }
        internal void Style(string style) => Css.SetStyle(Table, $"width:160px;height:auto;border-spacing:0;border:0;padding:0;direction:{(_rtl ? "rtl" : "ltr")};border-collapse:{(_collapse ? "collapse" : "separate")};" + style);
        internal void Layout(int height = 200) => CssTableAutoHeightTests.Layout(Root, 300, height);
        internal void Original() { Assert.Equal(Rows, _group.Children.Cast<StackPanel>()); for (var i = 0; i < _texts.Count; i++) Assert.Same(_texts[i], ((Border)Rows[i].Children[0]).Child); }
        public void Dispose() { if (Root is ScrollViewer scroll) scroll.Content = null; }
    }
}
