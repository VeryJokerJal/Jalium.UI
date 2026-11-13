using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssTableCaptionTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData("top")] [InlineData("bottom")] [InlineData("inherit")] [InlineData("initial")] [InlineData("unset")] [InlineData("revert-layer")]
    public void CaptionSideUsesTheSharedPropertyGrammar(string value) => Assert.True(CssAuthoring.IsValueSupported("caption-side", value));
    [Theory]
    [InlineData("left")] [InlineData("right")] [InlineData("auto")] [InlineData("top bottom")] [InlineData("1px")] [InlineData("inline-start")]
    public void InvalidCaptionSideValuesAreRejected(string value) => Assert.False(CssAuthoring.IsValueSupported("caption-side", value));

    [Theory]
    [InlineData("top", 0d, 20d)] [InlineData("bottom", 30d, 0d)]
    public void CaptionsOccupySpaceOutsideTheSharedGrid(string side, double captionY, double rowY)
    {
        var caption = Caption(20); Css.SetStyle(caption, "display:table-caption;caption-side:" + side); var row = Row(Cell(30)); var table = Table(row, caption); Layout(table);
        Assert.Equal(50, table.DesiredSize.Height, 3); Assert.Equal(50, table.ActualHeight, 3); Assert.Equal(captionY, caption.VisualBounds.Y, 3); Assert.Equal(rowY, row.VisualBounds.Y, 3);
        Assert.Equal(200, caption.ActualWidth, 3); Assert.Same(table, caption.VisualParent); Assert.Same(row, table.Children[0]); Assert.Equal(2, CssMatcher.Children(CssNode.Get(table)).Count());
    }

    [Fact]
    public void CaptionMarginsStayInsideTheWrapperAndOutsideTheTableBorder()
    {
        var caption = Caption(20); Css.SetStyle(caption, "display:table-caption;margin:5px 7px 11px;"); var row = Row(Cell(30)); var table = Table(row, caption); Layout(table);
        Assert.Equal(5, caption.VisualBounds.Top, 3); Assert.Equal(7, caption.VisualBounds.Left, 3); Assert.Equal(186, caption.ActualWidth, 3);
        Assert.Equal(36, row.VisualBounds.Top, 3); Assert.Equal(66, table.DesiredSize.Height, 3);
    }

    [Fact]
    public void MultipleCaptionsPreserveOrderOnEachSideAndCollapseAdjacentMargins()
    {
        var a = Caption(10); var b = Caption(20); var c = Caption(15); var d = Caption(10); var row = Row(Cell(30));
        Css.SetStyle(a, "display:table-caption;margin-bottom:12px;"); Css.SetStyle(b, "display:table-caption;margin-top:8px;margin-bottom:4px;");
        Css.SetStyle(c, "display:table-caption;caption-side:bottom;margin-top:6px;margin-bottom:9px;"); Css.SetStyle(d, "display:table-caption;caption-side:bottom;margin-top:4px;");
        var table = Table(c, a, row, d, b); Layout(table);
        Assert.Equal(0, a.VisualBounds.Top, 3); Assert.Equal(22, b.VisualBounds.Top, 3); Assert.Equal(46, row.VisualBounds.Top, 3);
        Assert.Equal(82, c.VisualBounds.Top, 3); Assert.Equal(106, d.VisualBounds.Top, 3); Assert.Equal(116, table.DesiredSize.Height, 3);
    }

    [Theory]
    [InlineData("border-box", 120d, 76d)] [InlineData("content-box", 144d, 100d)]
    public void TableHeightAppliesToTheGridAndCaptionHeightIsAddedOnce(string sizing, double total, double rowHeight)
    {
        var caption = Caption(20); var cell = Cell(10); var table = Table(caption, Row(cell)); Css.SetStyle(table, $"display:table;width:200px;height:100px;box-sizing:{sizing};padding:10px;border:2px solid green;table-layout:fixed;"); Layout(table);
        Assert.Equal(total, table.DesiredSize.Height, 3); Assert.Equal(total, table.ActualHeight, 3); Assert.Equal(rowHeight, cell.ActualHeight, 3);
        Assert.Equal(32, cell.TransformToAncestor(table).Y, 3); Assert.Equal(0, caption.VisualBounds.Left, 3);
        for (var i = 0; i < 3; i++) { table.InvalidateMeasure(); Layout(table); Assert.Equal(total, table.DesiredSize.Height, 3); }
    }

    [Fact]
    public void CaptionMinimumContentCanWidenTheGrid()
    {
        var caption = Caption(20); caption.Child = new Border { Width = 270, Height = 20 }; var cell = Cell(30); var table = Table(Row(cell), caption); Layout(table);
        Assert.Equal(270, table.DesiredSize.Width, 3); Assert.Equal(270, table.ActualWidth, 3); Assert.Equal(270, cell.ActualWidth, 3);
    }

    [Fact]
    public void AutoTableUsesCaptionMinimumEvenWhenItsCellsAreNarrow()
    {
        var caption = Caption(20); caption.Child = new Border { Width = 80, Height = 20 }; var table = Table(Row(Cell(10)), caption); Css.SetStyle(table, "display:table;");
        var root = new StackPanel(); root.Children.Add(table); Css.SetStyle(root, "display:block;"); Layout(root); Assert.Equal(80, table.ActualWidth, 3);
    }

    [Fact]
    public void CaptionPercentageWidthsAndAutoMarginsUseTheWrapperWidth()
    {
        var caption = Caption(20); Css.SetStyle(caption, "display:table-caption;width:50%;box-sizing:border-box;margin-inline:auto;"); var table = Table(Row(Cell(30)), caption); Layout(table);
        Assert.Equal(100, caption.ActualWidth, 3); Assert.Equal(50, caption.VisualBounds.Left, 3); Assert.Equal(200, table.ActualWidth, 3);
    }

    [Fact]
    public void CaptionPaddingPercentagesUseTheTableWrapperContainingBlock()
    {
        var caption = Caption(10); Css.SetStyle(caption, "display:table-caption;padding:10%;"); var row = Row(Cell(30)); var table = Table(caption, row); Layout(table);
        Assert.Equal(new Thickness(20), caption.Padding); Assert.Equal(20, caption.Child!.VisualBounds.X, 3); Assert.Equal(50, caption.ActualHeight, 3); Assert.Equal(50, row.VisualBounds.Top, 3);
    }

    [Fact]
    public void InheritanceExplicitOverrideAndRemovalMoveExistingCaptions()
    {
        var caption = Caption(20); var row = Row(Cell(30)); var table = Table(caption, row); var root = new StackPanel(); root.Children.Add(table); Css.SetStyle(root, "display:block;caption-side:bottom;"); Layout(root);
        Assert.Equal(30, caption.VisualBounds.Top, 3);
        Css.SetStyle(caption, "display:table-caption;caption-side:top;"); Layout(root); Assert.Equal(0, caption.VisualBounds.Top, 3);
        Css.SetStyle(caption, "display:table-caption;caption-side:inherit;"); Layout(root); Assert.Equal(30, caption.VisualBounds.Top, 3);
        Css.SetStyle(root, "display:block;caption-side:top;"); Layout(root); Assert.Equal(0, caption.VisualBounds.Top, 3);
    }

    [Fact]
    public void EmptyTableWithCaptionRetainsItsSpecifiedGridWidthAndHeight()
    {
        var caption = Caption(20); var table = Table(caption); Css.SetStyle(table, "display:table;width:200px;height:40px;"); Layout(table);
        Assert.Equal(200, table.ActualWidth, 3); Assert.Equal(60, table.ActualHeight, 3); Assert.Equal(200, caption.ActualWidth, 3);
    }

    [Fact]
    public void CaptionSideChangesAndContentChangesRepublishWrapperGeometry()
    {
        var caption = new TextBlock { Text = "短说明", FontFamily = "PingFang SC", FontSize = 16 }; Css.SetStyle(caption, "display:table-caption;white-space:normal;"); var row = Row(Cell(30)); var table = Table(row, caption); Layout(table); var before = caption.ActualHeight;
        caption.Text = "说明会随着窗口宽度换行，并持续保留原来的文字来源和布局关系。"; Layout(table); Assert.True(caption.ActualHeight > before); Assert.Equal(caption.ActualHeight, row.VisualBounds.Top, 3);
        Css.SetStyle(caption, "display:table-caption;white-space:normal;caption-side:bottom;"); Layout(table); Assert.Equal(0, row.VisualBounds.Top, 3); Assert.Equal(30, caption.VisualBounds.Top, 3);
    }

    [Fact]
    public void MisparentedCaptionAndRowsGenerateOneAnonymousWrapper()
    {
        var prefix = new Border { Height = 7 }; var caption = Caption(20); var row = Row(Cell(30)); var suffix = new Border { Height = 9 }; var root = new StackPanel();
        foreach (var child in new UIElement[] { prefix, row, caption, suffix }) root.Children.Add(child); Css.SetStyle(root, "display:block;"); Layout(root);
        Assert.Equal(7, caption.VisualBounds.Top, 3); Assert.Equal(27, row.VisualBounds.Top, 3); Assert.Equal(57, suffix.VisualBounds.Top, 3); Assert.Same(root, caption.VisualParent);
    }

    [Theory]
    [InlineData("top", 20d)] [InlineData("bottom", 0d)]
    public void InlineTableBaselineIncludesOnlyCaptionsAboveTheGrid(string side, double shift)
    {
        var text = new TextBlock { Text = "行基线", FontFamily = "PingFang SC", FontSize = 16 }; var cell = new Border { Child = text }; Css.SetStyle(cell, "display:table-cell;");
        var caption = Caption(20); Css.SetStyle(caption, "display:table-caption;caption-side:" + side); var table = Table(Row(cell), caption); Css.SetStyle(table, "display:inline-table;width:200px;table-layout:fixed;"); Layout(table);
        Assert.True(CssDisplayLayout.TryGetBaseline(table, table.ActualHeight, false, out var baseline)); Assert.Equal(text.GetFirstBaselineOffset(text.ActualHeight) + shift, baseline, 3);
    }

    [Fact]
    public void TableBackgroundAndBorderCoverTheGridAndCaptionKeepsItsOwnFrame()
    {
        var caption = Caption(20); Css.SetStyle(caption, "display:table-caption;background:yellow;"); var table = Table(Row(Cell(30)), caption); Css.SetStyle(table, "display:table;width:200px;table-layout:fixed;background:blue;border:2px solid red;"); Layout(table);
        Assert.Equal(Colors.Yellow, Pixel(table, 100, 10)); Assert.Equal(Colors.Blue, Pixel(table, 100, 25)); Assert.Equal(Colors.Red, Pixel(table, 100, 20)); Assert.Equal(Colors.Red, Pixel(table, 0, 30));
    }

    [Fact]
    public void BorderImageFillAndOutlineUseTheGridRectangle()
    {
        var caption = Caption(20); var table = Table(caption, Row(Cell(30))); Css.SetStyle(table, "display:table;width:200px;table-layout:fixed;border-image:image(red) 0 fill / 0;"); Layout(table);
        Assert.Equal(Colors.White, Pixel(table, 100, 10)); Assert.Equal(Colors.Red, Pixel(table, 100, 25));
        Css.SetStyle(table, "display:table;width:200px;table-layout:fixed;outline:2px solid red;"); Layout(table); Assert.Equal(Colors.White, Pixel(table, 100, 10)); Assert.Equal(Colors.Red, Pixel(table, 100, 18));
    }

    [Fact]
    public void HiddenOverflowAndRoundedGridDoNotClipTheCaption()
    {
        var caption = Caption(20); Css.SetStyle(caption, "display:table-caption;background:yellow;"); var row = Row(Cell(30)); var table = Table(caption, row); Css.SetStyle(table, "display:table;width:200px;table-layout:fixed;border:2px solid blue;border-radius:15px;overflow:hidden;"); Layout(table);
        Assert.Equal(Colors.Yellow, Pixel(table, 1, 1)); Assert.Same(caption.Child, table.InputHitTest(new(1, 1)));
    }

    [Theory]
    [InlineData("flex")] [InlineData("grid")]
    public void CaptionsAreBlockifiedOutsideATableFormattingContext(string display)
    {
        var caption = Caption(20); var root = new StackPanel(); root.Children.Add(caption); Css.SetStyle(root, "display:" + display); Layout(root);
        Assert.Equal(CssDisplayMode.Block, CssTableLayout.Role(caption)); Assert.Null(CssTableLayout.DecorationBox(root)); Assert.Equal(CssDisplayMode.Block, CssDisplayProperties.Computed(CssNode.Get(caption)).Mode);
    }

    [Fact]
    public void RemovingCaptionRoleRestoresOrdinaryCellAndTablePainting()
    {
        var caption = Caption(20); var table = Table(Row(Cell(30)), caption); Css.SetStyle(table, "display:table;width:200px;table-layout:fixed;background:blue;"); Layout(table); Assert.Equal(Colors.White, Pixel(table, 100, 10));
        Css.SetStyle(caption, "display:block;"); Layout(table); Assert.Null(CssTableLayout.DecorationBox(table)); Assert.Equal(Colors.Blue, Pixel(table, 100, 10)); Assert.Same(table, caption.VisualParent);
    }

    [Theory]
    [InlineData(20d, -5d, 55d, 15d)] [InlineData(10d, -30d, 20d, -20d)]
    public void NegativeCaptionMarginsDoNotInflateTheSpecifiedGridHeight(double height, double margin, double total, double gridY)
    {
        var caption = Caption(height); Css.SetStyle(caption, $"display:table-caption;margin-bottom:{margin}px;"); var row = Row(Cell(30)); var table = Table(caption, row);
        Css.SetStyle(table, "display:table;width:200px;height:40px;table-layout:fixed;"); Layout(table);
        Assert.Equal(total, table.ActualHeight, 3); Assert.Equal(gridY, row.VisualBounds.Top, 3); Assert.Equal(40, row.ActualHeight, 3);
        Assert.True(table.GetExtraDirtyPadding() >= Math.Max(0, -gridY));
    }

    [Fact]
    public void OverlappingTopAndBottomCaptionsUseNormalBoxPaintOrder()
    {
        var top = Caption(20); var bottom = Caption(20); var table = Table(Row(Cell(30)), bottom, top);
        Css.SetStyle(top, "display:table-caption;background:yellow;margin-bottom:-5px;"); Css.SetStyle(bottom, "display:table-caption;caption-side:bottom;background:yellow;margin-top:-5px;");
        Css.SetStyle(table, "display:table;width:200px;table-layout:fixed;background:blue;border:2px solid red;"); Layout(table);
        Assert.Equal(Colors.Yellow, Pixel(table, 100, 10)); Assert.Equal(Colors.Red, Pixel(table, 100, 15)); Assert.Equal(Colors.Blue, Pixel(table, 100, 18));
        Assert.Equal(Colors.Yellow, Pixel(table, 100, 45));
    }

    [Fact]
    public void VariablesAndInitialCaptionSideParticipateInTheCascade()
    {
        var caption = Caption(20); var table = Table(caption, Row(Cell(30))); Css.SetClass(table, "matrix");
        Css.SetStyleSheet(table, ".matrix{caption-side:var(--side);}"); Css.SetStyle(table, "display:table;width:200px;--side:bottom;"); Layout(table); Assert.Equal(30, caption.VisualBounds.Top, 3);
        Css.SetStyle(caption, "display:table-caption;caption-side:initial;"); Layout(table); Assert.Equal(0, caption.VisualBounds.Top, 3);
    }

    [Fact]
    public void GeneratedCaptionsKeepTheirOwnerAndDoNotBecomeCells()
    {
        var row = Row(Cell(30)); var table = Table(row); Css.SetClass(table, "matrix"); Css.SetStyleSheet(table, ".matrix::before{content:'表格说明';display:table-caption;height:20px;line-height:20px;}"); Layout(table);
        var caption = CssGeneratedContent.Get(table)!.Before!; Assert.Equal(0, caption.VisualBounds.Top, 3); Assert.Equal(20, row.VisualBounds.Top, 3); Assert.Same(table, caption.VisualParent);
        Assert.Equal(1, CssMatcher.Children(CssNode.Get(table)).Count());
    }

    [Fact]
    public void CaptionRelocationPreservesKeyboardFocusAndOriginalAutomationNames()
    {
        Keyboard.Initialize(); Keyboard.ClearFocus(); var a = new Button { Content = "正文" }; var b = new Button { Content = "说明" }; AutomationProperties.SetName(b, "表格说明操作");
        var cell = new Border { Child = a }; Css.SetStyle(cell, "display:table-cell;"); var caption = new Border { Child = b }; Css.SetStyle(caption, "display:table-caption;"); var table = Table(Row(cell), caption);
        var window = new DisplayedTestWindow { Width = 240, Height = 180, Content = table, TitleBarStyle = WindowTitleBarStyle.Native };
        try
        {
            Layout(window); Assert.True(a.Focus()); Assert.True(KeyboardNavigation.MoveFocus(a)); Assert.Same(b, Keyboard.FocusedElement);
            Css.SetStyle(caption, "display:table-caption;caption-side:bottom;"); Layout(window); Assert.Same(b, Keyboard.FocusedElement);
            Assert.Equal("表格说明操作", FrameworkElementAutomationPeer.CreatePeerForElement(b)!.GetName()); Assert.True(KeyboardNavigation.MoveFocus(b, reverse: true)); Assert.Same(a, Keyboard.FocusedElement);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    private static Border Caption(double height) { var result = new Border { Child = new Border { Height = height, Width = 10 } }; Css.SetStyle(result, "display:table-caption;"); return result; }
    private static Border Cell(double height) { var result = new Border { Child = new Border { Width = 10, Height = height } }; Css.SetStyle(result, "display:table-cell;"); return result; }
    private static StackPanel Row(params UIElement[] cells) { var result = new StackPanel(); foreach (var cell in cells) result.Children.Add(cell); Css.SetStyle(result, "display:table-row;"); return result; }
    private static StackPanel Table(params UIElement[] children) { var result = new StackPanel(); foreach (var child in children) result.Children.Add(child); Css.SetStyle(result, "display:table;width:200px;table-layout:fixed;"); return result; }
    private static void Layout(FrameworkElement root, double width = 200)
    { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); root.Measure(new(width, double.PositiveInfinity)); root.Arrange(new(0, 0, width, Math.Max(1, root.DesiredSize.Height))); }
    private static Color Pixel(FrameworkElement root, int x, int y)
    {
        var w = Math.Max(1, (int)Math.Ceiling(root.ActualWidth)); var h = Math.Max(1, (int)Math.Ceiling(root.ActualHeight)); var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Bgra32);
        var drawing = new SoftwareDrawingContext(bitmap); drawing.DrawRectangle(Brushes.White, null, new(0, 0, w, h)); root.Render(drawing); drawing.Close();
        var bytes = new byte[w * h * 4]; bitmap.CopyPixels(bytes, w * 4, 0); var at = (y * w + x) * 4; return Color.FromArgb(bytes[at + 3], bytes[at + 2], bytes[at + 1], bytes[at]);
    }
}
