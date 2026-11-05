using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssTableFormattingTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData("table")] [InlineData("inline-table")] [InlineData("block table")] [InlineData("inline table")]
    [InlineData("table-row")] [InlineData("table-cell")] [InlineData("table-row-group")]
    [InlineData("table-header-group")] [InlineData("table-footer-group")] [InlineData("table-column")] [InlineData("table-column-group")] [InlineData("table-caption")]
    public void TableDisplayGrammarIsAccepted(string value) => Assert.True(CssAuthoring.IsValueSupported("display", value));

    [Theory]
    [InlineData("inline table-row")] [InlineData("table-cell list-item")] [InlineData("table list-item")]
    [InlineData("table flex")] [InlineData("inline-table block")]
    public void IncompatibleOrUnimplementedValuesAreRejected(string value) => Assert.False(CssAuthoring.IsValueSupported("display", value));

    [Fact]
    public void OriginalRowsAndCellsUseSharedTracksAndKeepTheirParents()
    {
        var a = Cell(30, 20); var b = Cell(40, 40); var c = Cell(70, 30); var d = Cell(10, 10);
        var first = Row(a, b); var second = Row(c, d); var table = Table(first, second); Layout(table);
        Assert.Equal(160, a.ActualWidth, 3); Assert.Equal(a.ActualWidth, c.ActualWidth, 3);
        Assert.Equal(40, a.ActualHeight, 3); Assert.Equal(40, b.ActualHeight, 3);
        Assert.Equal(40, second.VisualBounds.Top, 3); Assert.Equal(160, b.VisualBounds.Left, 3);
        Assert.Same(first, a.VisualParent); Assert.Same(table, first.VisualParent); Assert.Equal(2, first.Children.Count);
        Assert.Equal(70, table.DesiredSize.Height, 3);
    }

    [Fact]
    public void FixedLayoutUsesFirstRowWhileAutoIncludesLaterIntrinsicWidths()
    {
        var a = Cell(20, 20); var b = Cell(20, 20); var longCell = Cell(250, 20);
        var table = Table(Row(a, b), Row(longCell, Cell(20, 20))); Layout(table); Assert.Equal(160, a.ActualWidth, 3);
        Css.SetStyle(table, "display:table;width:320px;table-layout:auto;"); Layout(table);
        Assert.True(a.ActualWidth >= 250); Assert.True(b.ActualWidth >= 20); Assert.Equal(320, a.ActualWidth + b.ActualWidth, 3);
    }

    [Fact]
    public void ColumnDefinitionsPercentWidthsAndSpacingUseTheTableWidth()
    {
        var column = new Border(); Css.SetStyle(column, "display:table-column;width:25%;");
        var a = Cell(10, 20); var b = Cell(10, 20); var table = Table(column, Row(a, b));
        Css.SetStyle(table, "display:table;width:320px;table-layout:fixed;border-spacing:4px 6px;"); Layout(table);
        Assert.Equal(77, a.ActualWidth, 3); Assert.Equal(4, a.TransformToAncestor(table).X, 3);
        Assert.Equal(6, a.TransformToAncestor(table).Y, 3); Assert.Equal(316, b.TransformToAncestor(table).X + b.ActualWidth, 3);
        Assert.False(column.IsVisible); Assert.NotSame(column, table.InputHitTest(new(5, 7)));
        Css.SetStyle(column, "display:table-column;width:100px;"); Layout(table); Assert.Equal(100, a.ActualWidth, 3);
    }

    [Fact]
    public void AutoTablesShrinkToTheirContentInsideBlockFlow()
    {
        var table = Table(Row(Cell(30, 20), Cell(40, 20))); Css.SetStyle(table, "display:table;");
        var root = new StackPanel(); root.Children.Add(table); Css.SetStyle(root, "display:block;"); Layout(root);
        Assert.Equal(70, table.ActualWidth, 3); Assert.Equal(70, table.DesiredSize.Width, 3);
    }

    [Fact]
    public void PercentageColumnsLeaveSpaceForAllOuterAndInnerGaps()
    {
        var a = new Border(); var b = new Border(); var c = new Border();
        foreach (var column in new[] { a, b, c }) Css.SetStyle(column, "display:table-column;width:33.3333333333%;");
        var cells = new[] { Cell(1, 20), Cell(1, 20), Cell(1, 20) }; var table = Table(a, b, c, Row(cells));
        Css.SetStyle(table, "display:table;width:320px;table-layout:fixed;border-spacing:5px;"); Layout(table);
        Assert.All(cells, cell => Assert.Equal(100, cell.ActualWidth, 3)); Assert.Equal(315, cells[^1].TransformToAncestor(table).X + cells[^1].ActualWidth, 3);
        Assert.Equal(320, table.ActualWidth, 3);
    }

    [Fact]
    public void SpecifiedTableDimensionsAreMinimaWhenContentNeedsMoreSpace()
    {
        var a = Cell(180, 80); var b = Cell(180, 20); var table = Table(Row(a, b)); Css.SetStyle(table, "display:table;width:100px;height:10px;table-layout:auto;"); Layout(table);
        Assert.Equal(360, table.DesiredSize.Width, 3); Assert.Equal(360, table.ActualWidth, 3);
        Assert.Equal(80, table.DesiredSize.Height, 3); Assert.Equal(80, table.ActualHeight, 3); Assert.Equal(180, b.VisualBounds.Left, 3);
    }

    [Theory]
    [InlineData("border")] [InlineData("content")] [InlineData("presenter")]
    public void SingleContentHostsUseTheSameTableFormatter(string kind)
    {
        var a = Cell(10, 20); var b = Cell(10, 20); var row = Row(a, b);
        FrameworkElement table = kind switch { "border" => new Border { Child = row }, "content" => new ContentControl { Content = row }, _ => new ContentPresenter { Content = row } };
        Css.SetStyle(table, "display:table;width:320px;table-layout:fixed;"); Layout(table);
        Assert.Equal(160, a.ActualWidth, 3); Assert.Equal(160, b.VisualBounds.Left, 3); Assert.Same(table, row.VisualParent);
    }

    [Fact]
    public void InlineTablesUseTheFirstRowBaselineAndWrapAsOneAtomicBox()
    {
        var label = new TextBlock { Text = "前", FontSize = 16, FontFamily = "PingFang SC" }; Css.SetStyle(label, "display:inline;");
        var text = new TextBlock { Text = "格", FontSize = 16, FontFamily = "PingFang SC" }; var cell = new Border { Child = text }; Css.SetStyle(cell, "display:table-cell;padding:4px;");
        var table = Table(Row(cell)); Css.SetStyle(table, "display:inline-table;"); var root = new StackPanel(); root.Children.Add(label); root.Children.Add(table); Css.SetStyle(root, "display:block;"); Layout(root);
        Assert.Equal(label.TransformToAncestor(root).Y + label.GetFirstBaselineOffset(label.ActualHeight), text.TransformToAncestor(root).Y + text.GetFirstBaselineOffset(text.ActualHeight), 3);
        Assert.True(table.VisualBounds.Left > 0); Layout(root, 25); Assert.Equal(0, table.VisualBounds.Left, 3); Assert.True(table.VisualBounds.Top > 0);
    }

    [Fact]
    public void GeneratedCellsAndNestedTablesPreserveTheirSourceOrder()
    {
        var innerA = Cell(10, 20); var innerB = Cell(10, 20); var inner = Table(Row(innerA, innerB)); Css.SetStyle(inner, "display:table;width:100%;table-layout:fixed;");
        var cell = new Border { Child = inner }; Css.SetClass(cell, "nested"); var row = Row(cell); var table = Table(row);
        Css.SetStyleSheet(table, ".nested{display:table-cell;padding:4px;}.nested::before{content:'前';display:block;}.nested::after{content:'后';display:block;}"); Layout(table);
        Assert.Equal(312, inner.ActualWidth, 3); Assert.Equal(156, innerA.ActualWidth, 3); Assert.Same(cell, inner.VisualParent);
        var slots = CssGeneratedContent.Get(cell)!; Assert.True(slots.Before!.VisualBounds.Bottom <= inner.VisualBounds.Top); Assert.True(slots.After!.VisualBounds.Top >= inner.VisualBounds.Bottom);
    }

    [Fact]
    public void AnonymousCellsObserveTheOriginalSiblingsWhenTheirContentChanges()
    {
        var original = new Border { Height = 10 }; var table = Table(Row(original, Cell(10, 10))); Layout(table); Assert.Equal(10, table.DesiredSize.Height, 3);
        original.Height = 50; table.InvalidateMeasure(); Layout(table); Assert.Equal(50, table.DesiredSize.Height, 3); Assert.Equal(50, original.ActualHeight, 3);
    }

    [Fact]
    public void TextBlockRowsRetainTheirOriginalBodyAndPaintItThroughTheAnonymousCell()
    {
        var row = new TextBlock { Text = "表格正文", FontFamily = "PingFang SC", FontSize = 16, Foreground = Brushes.Black }; Css.SetStyle(row, "display:table-row;");
        var table = Table(row); Layout(table);
        Assert.True(row.ActualHeight > 15, $"row={row.VisualBounds}, table={table.DesiredSize}");
        var drawing = new TextCapture(); table.Render(drawing); Assert.Equal(new[] { "表格正文" }, drawing.Text);
        Assert.Equal("表格正文", row.Text);
    }

    [Theory]
    [InlineData("border-box", 200d, 88d)] [InlineData("content-box", 224d, 100d)]
    public void TableChromeAndMinimumHeightAreAccountedForOnce(string sizing, double total, double rowHeight)
    {
        var a = Cell(10, 10); var b = Cell(10, 10); var table = Table(Row(a), Row(b)); Css.SetStyle(table, $"display:table;width:200px;height:200px;box-sizing:{sizing};padding:10px;border:2px solid green;table-layout:fixed;");
        Layout(table); Assert.Equal(total, table.ActualWidth, 3); Assert.Equal(total, table.ActualHeight, 3); Assert.Equal(rowHeight, a.ActualHeight, 3); Assert.Equal(12, a.TransformToAncestor(table).X, 3);
    }

    [Fact]
    public void ColumnSubtreesBecomeVisibleWhenTheirParentChangesToFlex()
    {
        var button = new Button { Content = "列内容" }; var column = new Border { Child = button }; Css.SetStyle(column, "display:table-column;"); var table = Table(column); Layout(table); Assert.False(button.IsVisible);
        Css.SetStyle(table, "display:flex;"); Layout(table); Assert.True(button.IsVisible); Assert.Equal(CssDisplayMode.Block, CssTableLayout.Role(column));
    }

    [Fact]
    public void HeaderAndFooterGroupsAreOrderedWithoutChangingTheNativeCollection()
    {
        var footer = Group("table-footer-group", Row(Cell(20, 15))); var body = Group("table-row-group", Row(Cell(20, 30)));
        var header = Group("table-header-group", Row(Cell(20, 10))); var secondHeader = Group("table-header-group", Row(Cell(20, 20)));
        var table = Table(footer, body, header, secondHeader); Layout(table);
        Assert.Equal(0, header.VisualBounds.Top, 3); Assert.Equal(10, body.VisualBounds.Top, 3);
        Assert.Equal(40, secondHeader.VisualBounds.Top, 3); Assert.Equal(60, footer.VisualBounds.Top, 3);
        Assert.Same(footer, table.Children[0]); Assert.Same(header, table.Children[2]);
    }

    [Fact]
    public void RowSpansAreClippedBeforePlacingTheNextGroup()
    {
        var span = Cell(10, 60); Grid.SetRowSpan(span, 10); Css.SetStyle(span, "display:table-cell;vertical-align:top;"); var a = Cell(10, 20); var b = Cell(10, 20); var next = Cell(10, 20);
        var table = Table(Group("table-row-group", Row(span, a), Row(b)), Group("table-row-group", Row(next))); Layout(table);
        Assert.Equal(0, span.VisualBounds.Left, 3); Assert.Equal(a.VisualBounds.Left, b.VisualBounds.Left, 3);
        Assert.Equal(0, next.VisualBounds.Left, 3); Assert.Equal(160, span.ActualWidth, 3); Assert.Equal(60, span.ActualHeight, 3);
        Assert.True(next.TransformToAncestor(table).Y >= span.TransformToAncestor(table).Y + span.ActualHeight);
    }

    [Fact]
    public void ColumnSpansIncludeTheInternalSpacing()
    {
        var span = Cell(10, 20); Grid.SetColumnSpan(span, 2); var a = Cell(10, 20); var b = Cell(10, 20); var c = Cell(10, 20);
        var table = Table(Row(span, Cell(10, 20)), Row(a, b, c)); Css.SetStyle(table, "display:table;width:320px;table-layout:fixed;border-spacing:5px;"); Layout(table);
        Assert.Equal(a.ActualWidth + b.ActualWidth + 5, span.ActualWidth, 3);
        Assert.Equal(c.TransformToAncestor(table).X, span.TransformToAncestor(table).X + span.ActualWidth + 5, 3);
    }

    [Theory]
    [InlineData("top", 0d)] [InlineData("middle", .5)] [InlineData("bottom", 1d)]
    public void VerticalAlignmentOffsetsTheOriginalChild(string align, double fraction)
    {
        var small = Cell(10, 20); var table = Table(Row(Cell(10, 80), small)); Css.SetStyle(small, "display:table-cell;vertical-align:" + align);
        Layout(table); Assert.Equal(80, small.ActualHeight, 3); Assert.Equal(60 * fraction, small.Child!.VisualBounds.Y, 3);
    }

    [Fact]
    public void FirstTextBaselinesAlignAcrossDifferentFontSizes()
    {
        var large = new TextBlock { Text = "大字", FontSize = 30, FontFamily = "PingFang SC" };
        var small = new TextBlock { Text = "小字", FontSize = 14, FontFamily = "PingFang SC" };
        var a = new Border { Child = large }; var b = new Border { Child = small }; Css.SetStyle(a, "display:table-cell;"); Css.SetStyle(b, "display:table-cell;");
        var table = Table(Row(a, b)); Layout(table);
        Assert.Equal(large.TransformToAncestor(table).Y + large.GetFirstBaselineOffset(large.ActualHeight), small.TransformToAncestor(table).Y + small.GetFirstBaselineOffset(small.ActualHeight), 3);
    }

    [Fact]
    public void RtlReversesColumnsAndHitTestingStillFindsTheOriginalContent()
    {
        var a = Cell(10, 20); var b = Cell(10, 20); a.Child!.IsHitTestVisible = false; b.Child!.IsHitTestVisible = false;
        var table = Table(Row(a, b)); Css.SetStyle(a, "display:table-cell;background:red;"); Css.SetStyle(b, "display:table-cell;background:blue;");
        table.FlowDirection = FlowDirection.RightToLeft; Layout(table); Assert.True(a.VisualBounds.Left > b.VisualBounds.Left);
        Assert.Same(a, table.InputHitTest(new(170, 10))); Assert.Same(b, table.InputHitTest(new(10, 10)));
        table.FlowDirection = FlowDirection.LeftToRight; Layout(table); Assert.True(a.VisualBounds.Left < b.VisualBounds.Left);
    }

    [Fact]
    public void RtlRestorationAlsoRestoresTheInheritedTextDirection()
    {
        var text = new TextBlock { Text = "状态" }; var cell = new Border { Child = text }; var table = Table(Row(cell));
        Css.SetStyleSheet(table, "Border{display:table-cell;}TextBlock{display:block;}"); Layout(table);
        Assert.Equal(FlowDirection.LeftToRight, text.FlowDirection);
        table.FlowDirection = FlowDirection.RightToLeft; Layout(table); Assert.Equal(FlowDirection.RightToLeft, text.FlowDirection);
        var rtlDrawing = new TextCapture(); table.Render(rtlDrawing); Assert.All(rtlDrawing.Origins, origin => Assert.True(origin.X > 100));
        table.FlowDirection = FlowDirection.LeftToRight; Layout(table); Assert.Equal(FlowDirection.LeftToRight, text.FlowDirection);
        var drawing = new TextCapture(); table.Render(drawing); Assert.Equal(new[] { "状态" }, drawing.Text); Assert.All(drawing.Origins, origin => Assert.Equal(0, origin.X, 3));
    }

    [Fact]
    public void CellMarginsAndRowPaddingDoNotShiftTheSharedGrid()
    {
        var a = Cell(10, 20); var b = Cell(10, 20); var row = Row(a, b); var table = Table(row);
        Css.SetStyle(a, "display:table-cell;margin:40px;"); Css.SetStyle(row, "display:table-row;padding:30px;border:12px solid red;"); Layout(table);
        Assert.Equal(0, a.VisualBounds.Left); Assert.Equal(160, b.VisualBounds.Left, 3); Assert.Equal(20, table.DesiredSize.Height, 3);
        Assert.Equal(0, Count(table, Colors.Red));
    }

    [Fact]
    public void NonCellSiblingsShareOneAnonymousCellAndKeepTheirNativeParent()
    {
        var a = new Border { Width = 30, Height = 20 }; var b = new Border { Width = 40, Height = 20 }; var explicitCell = Cell(10, 40);
        Css.SetStyle(a, "display:inline-block;"); Css.SetStyle(b, "display:inline-block;"); var row = Row(a, b, explicitCell); var table = Table(row); Layout(table);
        Assert.Same(row, a.VisualParent); Assert.Same(row, b.VisualParent); Assert.Equal(30, b.VisualBounds.Left - a.VisualBounds.Left, 3);
        Assert.Equal(160, explicitCell.VisualBounds.Left, 3); Assert.Equal(3, row.Children.Count);
        Assert.Equal(3, CssMatcher.Children(CssNode.Get(row)).Count());
    }

    [Fact]
    public void DirectCellsGenerateAnAnonymousRow()
    {
        var a = Cell(10, 20); var b = Cell(10, 40); var table = Table(a, b); Layout(table);
        Assert.Equal(160, a.ActualWidth, 3); Assert.Equal(160, b.VisualBounds.Left, 3); Assert.Equal(40, a.ActualHeight, 3); Assert.Same(table, a.VisualParent);
    }

    [Fact]
    public void MisparentedRowsUseOneAnonymousTableWithTheCorrectFlowOrigin()
    {
        var prefix = new Border { Height = 17 }; var a = Cell(30, 20); var b = Cell(40, 20); var row = Row(a, b); var suffix = new Border { Height = 13 };
        var root = new StackPanel(); root.Children.Add(prefix); root.Children.Add(row); root.Children.Add(suffix); Css.SetStyle(root, "display:block;padding:5px;"); Layout(root);
        Assert.Equal(22, row.VisualBounds.Top, 3); Assert.Equal(5, row.VisualBounds.Left, 3); Assert.Equal(70, row.ActualWidth, 3);
        Assert.Equal(42, suffix.VisualBounds.Top, 3); Assert.Same(root, row.VisualParent); Assert.Equal(3, root.Children.Count);
    }

    [Fact]
    public void AnonymousHelpersAreReleasedWhenDisplayChangesRepeatedly()
    {
        var a = new Border { Width = 20, Height = 10 }; var row = Row(a, Cell(10, 10)); var table = Table(row); Layout(table);
        var count = VisualTreeHelper.GetChildrenCount(row);
        for (var i = 0; i < 4; i++)
        {
            Css.SetStyle(table, "display:block;"); Css.SetStyle(row, "display:block;"); Layout(table);
            Assert.False(CssTableLayout.IsManaged(a)); Assert.Equal(2, VisualTreeHelper.GetChildrenCount(row));
            Css.SetStyle(table, "display:table;width:320px;table-layout:fixed;"); Css.SetStyle(row, "display:table-row;"); Layout(table);
            Assert.Equal(count, VisualTreeHelper.GetChildrenCount(row)); Assert.Same(row, a.VisualParent);
        }
    }

    [Fact]
    public void EmptyCellsHideAllTheirLayersAndShowRestoresThem()
    {
        var empty = new Border(); Css.SetStyle(empty, "display:table-cell;background:red;border:2px solid red;padding:8px;");
        var row = Row(empty); Css.SetStyle(row, "display:table-row;background:red;"); var column = new Border(); Css.SetStyle(column, "display:table-column;background:red;");
        var table = Table(column, row); Layout(table); Assert.True(Count(table, Colors.Red) > 100);
        Css.SetStyle(table, "display:table;width:320px;table-layout:fixed;empty-cells:hide;"); Layout(table); Assert.Equal(0, Count(table, Colors.Red));
        Css.SetStyle(table, "display:table;width:320px;table-layout:fixed;empty-cells:show;"); Layout(table); Assert.True(Count(table, Colors.Red) > 100);
    }

    [Theory]
    [InlineData("normal", false)] [InlineData("pre", true)] [InlineData("pre-line", false)]
    public void CollapsedWhitespaceDoesNotPreventAnOtherwiseEmptyCellFromHiding(string whitespace, bool visible)
    {
        var cell = new TextBlock { Text = " \t ", Height = 40 }; Css.SetStyle(cell, "display:table-cell;background:red;white-space:" + whitespace);
        var table = Table(Row(cell)); Css.SetStyle(table, "display:table;width:320px;table-layout:fixed;empty-cells:hide;"); Layout(table);
        Assert.Equal(visible, Count(table, Colors.Red) > 100);
    }

    [Fact]
    public void BackgroundLayersRespectColumnRowGroupRowAndCellOrder()
    {
        var column = new Border(); Css.SetStyle(column, "display:table-column;background:red;");
        var cell = Cell(1, 20); var row = Row(cell); var group = Group("table-row-group", row); var table = Table(column, group); Layout(table);
        Assert.Equal(Colors.Red, Pixel(table, 100, 10));
        Css.SetStyle(group, "display:table-row-group;background:green;"); Layout(table); Assert.Equal(Colors.Green, Pixel(table, 100, 10));
        Css.SetStyle(row, "display:table-row;background:blue;"); Layout(table); Assert.Equal(Colors.Blue, Pixel(table, 100, 10));
        Css.SetStyle(cell, "display:table-cell;background:yellow;"); Layout(table); Assert.Equal(Colors.Yellow, Pixel(table, 100, 10));
        Css.SetStyle(cell, "display:table-cell;"); Css.SetStyle(row, "display:table-row;"); Css.SetStyle(group, "display:table-row-group;"); Css.SetStyle(column, "display:table-column;background:purple;");
        Layout(table); Assert.Equal(Colors.Purple, Pixel(table, 100, 10));
    }

    [Fact]
    public void SpacingShowsTheTableBackgroundAndColumnPaintCoversOriginatingSpans()
    {
        var column = new Border(); Css.SetStyle(column, "display:table-column;background:red;");
        var span = Cell(10, 20); Grid.SetColumnSpan(span, 2); var table = Table(column, Row(span), Row(Cell(10, 20), Cell(10, 20)));
        Css.SetStyle(table, "display:table;width:320px;table-layout:fixed;border-spacing:5px;background:white;"); Layout(table);
        Assert.Equal(Colors.White, Pixel(table, 2, 10)); Assert.Equal(Colors.Red, Pixel(table, 250, 10));
        Assert.Equal(Colors.White, Pixel(table, 250, 35));
    }

    [Fact]
    public void ColumnSubtreesStayHiddenAndRestoreWhenChangedToBlock()
    {
        var button = new Button { Content = "隐藏列内容" }; var column = new Border { Child = button };
        Css.SetStyle(column, "display:table-column;width:80px;"); var table = Table(column, Row(Cell(10, 20))); Layout(table);
        Assert.False(column.IsVisible); Assert.False(button.IsVisible);
        Css.SetStyle(column, "display:block;"); Layout(table); Assert.True(column.IsVisible); Assert.True(button.IsVisible);
    }

    [Theory]
    [InlineData("flex")] [InlineData("grid")]
    public void InternalTablePartsAreBlockifiedAsFlexOrGridItems(string display)
    {
        var a = Cell(20, 20); var root = new StackPanel(); root.Children.Add(a); Css.SetStyle(root, "display:" + display); Layout(root);
        Assert.Equal(CssDisplayMode.Block, CssTableLayout.Role(a)); Assert.False(CssTableLayout.IsManaged(a));
        Assert.Equal(CssDisplayMode.Block, CssDisplayProperties.Computed(CssNode.Get(a)).Mode);
    }

    [Fact]
    public void KeyboardFocusAndAutomationKeepTheOriginalButtonsAcrossRelayout()
    {
        Keyboard.Initialize(); Keyboard.ClearFocus(); var a = new Button { Content = "甲" }; var b = new Button { Content = "乙" };
        AutomationProperties.SetName(a, "第一格"); AutomationProperties.SetName(b, "第二格"); var cellA = new Border { Child = a }; var cellB = new Border { Child = b };
        Css.SetStyle(cellA, "display:table-cell;"); Css.SetStyle(cellB, "display:table-cell;"); var table = Table(Row(cellA, cellB));
        var window = new DisplayedTestWindow { Width = 320, Height = 120, Content = table, TitleBarStyle = WindowTitleBarStyle.Native };
        try
        {
            Layout(window); Assert.True(a.Focus()); Assert.True(KeyboardNavigation.MoveFocus(a)); Assert.Same(b, Keyboard.FocusedElement);
            Css.SetStyle(table, "display:table;width:300px;table-layout:fixed;border-spacing:6px;"); Layout(window);
            Assert.Same(b, Keyboard.FocusedElement); Assert.True(KeyboardNavigation.MoveFocus(b, reverse: true)); Assert.Same(a, Keyboard.FocusedElement);
            Assert.Equal("第一格", FrameworkElementAutomationPeer.CreatePeerForElement(a)!.GetName()); Assert.Same(cellA, a.VisualParent);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    private static Border Cell(double width, double height)
    { var result = new Border { Child = new Border { Width = width, Height = height, HorizontalAlignment = HorizontalAlignment.Left } }; Css.SetStyle(result, "display:table-cell;"); return result; }
    private static StackPanel Row(params UIElement[] cells) { var result = new StackPanel(); foreach (var cell in cells) result.Children.Add(cell); Css.SetStyle(result, "display:table-row;"); return result; }
    private static StackPanel Group(string display, params UIElement[] rows) { var result = new StackPanel(); foreach (var row in rows) result.Children.Add(row); Css.SetStyle(result, "display:" + display); return result; }
    private static StackPanel Table(params UIElement[] rows) { var result = new StackPanel(); foreach (var row in rows) result.Children.Add(row); Css.SetStyle(result, "display:table;width:320px;table-layout:fixed;"); return result; }
    private static void Layout(FrameworkElement root, double width = 320)
    { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); root.Measure(new(width, double.PositiveInfinity)); root.Arrange(new(0, 0, width, Math.Max(1, root.DesiredSize.Height))); }
    private static byte[] Pixels(FrameworkElement element, out int width)
    {
        width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth)); var height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight)); var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Bgra32);
        var drawing = new SoftwareDrawingContext(image); element.Render(drawing); drawing.Close(); var bytes = new byte[width * height * 4]; image.CopyPixels(bytes, width * 4, 0); return bytes;
    }
    private static Color Pixel(FrameworkElement element, int x, int y)
    { var pixels = Pixels(element, out var width); var at = (y * width + x) * 4; return Color.FromArgb(pixels[at + 3], pixels[at + 2], pixels[at + 1], pixels[at]); }
    private static int Count(FrameworkElement element, Color color)
    { var pixels = Pixels(element, out _); var count = 0; for (var at = 0; at < pixels.Length; at += 4) if (pixels[at] == color.B && pixels[at + 1] == color.G && pixels[at + 2] == color.R && pixels[at + 3] == color.A) count++; return count; }
    private sealed class TextCapture : DrawingContextAdapter
    {
        internal readonly List<string> Text = [];
        internal readonly List<Point> Origins = [];
        public override void DrawText(FormattedText text, Point origin) { Text.Add(text.Text); Origins.Add(origin); }
        public override void DrawLine(Pen pen, Point a, Point b) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rect) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rect, double x, double y) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double x, double y) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource source, Rect rect) { }
        public override void PushClip(Geometry clip) { }
        public override void PushOpacity(double opacity) { }
        public override void PushTransform(Transform transform) { }
        public override void Pop() { }
        public override void Close() { }
        public override void DrawBackdropEffect(Rect rect, IBackdropEffect effect, CornerRadius radius) { }
    }
}
