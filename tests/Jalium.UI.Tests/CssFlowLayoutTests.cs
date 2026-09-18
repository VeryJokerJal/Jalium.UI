using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssFlowLayoutTests
{
    private static StackPanel Flow(string css = "", Panel? parent = null)
    {
        var panel = new StackPanel(); parent?.Children.Add(panel);
        Css.SetStyle(panel, "display:flow-root; line-height:0; " + css); return panel;
    }
    private static Border Item(Panel parent, string css)
    {
        var border = new Border(); parent.Children.Add(border); Css.SetStyle(border, css); return border;
    }
    private static void Layout(Panel root, double width = 300, double height = 200)
    {
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height));
    }

    [Fact]
    public void BlockFlow_FillsAutoWidthAndCollapsesAdjacentMargins()
    {
        var root = Flow(); var a = Item(root, "height:20px; margin:0 10px 20px");
        var b = Item(root, "height:30px; margin:30px 10px 0"); Layout(root);
        Assert.Equal(280, a.ActualWidth, 6); Assert.Equal(10, a.VisualBounds.X, 6);
        Assert.Equal(50, b.VisualBounds.Y, 6); Assert.Equal(80, root.DesiredSize.Height, 6);
    }

    [Theory]
    [InlineData(20, -10, 30)]
    [InlineData(-20, -10, 0)]
    [InlineData(-10, 30, 40)]
    public void NegativeMargins_CollapseByPositiveAndNegativeExtrema(double bottom, double top, double y)
    {
        var root = Flow(); Item(root, $"height:20px; margin-bottom:{bottom}px");
        var b = Item(root, $"height:30px; margin-top:{top}px"); Layout(root);
        Assert.Equal(y, b.VisualBounds.Y, 6);
    }

    [Fact]
    public void EmptyBlocks_CollapseThroughWithoutAddingTheirMarginsTwice()
    {
        var root = Flow(); Item(root, "height:20px; margin-bottom:10px");
        var empty = Flow("display:block; height:0; margin:20px 0 30px", root);
        var last = Item(root, "height:20px; margin-top:40px"); Layout(root);
        Assert.Equal(40, empty.VisualBounds.Y, 6); Assert.Equal(60, last.VisualBounds.Y, 6);
        Assert.Equal(80, root.DesiredSize.Height, 6);
    }

    [Fact]
    public void ChildMargins_CanCollapseWithTheirBlockParent()
    {
        var root = Flow(); var nested = Flow("display:block; margin:10px 0 5px", root);
        var child = Item(nested, "height:20px; margin:30px 0 40px"); Layout(root);
        Assert.Equal(30, nested.VisualBounds.Y, 6); Assert.Equal(0, child.VisualBounds.Y, 6);
        Assert.Equal(20, nested.ActualHeight, 6); Assert.Equal(90, root.DesiredSize.Height, 6);
        Assert.Same(nested, child.VisualParent); Assert.Same(root, nested.VisualParent);
    }

    [Theory]
    [InlineData("display:flow-root", 30, 90)]
    [InlineData("display:block; padding:1px", 31, 92)]
    public void FormattingRootsAndPadding_StopParentChildMarginCollapse(string css, double childY, double parentHeight)
    {
        var root = Flow(); var nested = Flow(css, root);
        var child = Item(nested, "height:20px; margin:30px 0 40px"); Layout(root);
        Assert.Equal(childY, child.VisualBounds.Y, 6); Assert.Equal(parentHeight, nested.ActualHeight, 6);
    }

    [Fact]
    public void Percentages_UseTheContainingBlockRatherThanTheAllocatedBorderBox()
    {
        var root = Flow("width:200px; height:100px; padding:10px");
        var child = Item(root, "width:50%; height:50%; margin:10% 0 0 10%"); Layout(root, 200, 100);
        Assert.Equal(90, child.ActualWidth, 6); Assert.Equal(40, child.ActualHeight, 6);
        Assert.Equal(28, child.VisualBounds.X, 6); Assert.Equal(28, child.VisualBounds.Y, 6);
    }

    [Fact]
    public void PercentageHeight_RemainsAutoInsideAnAutoHeightBlock()
    {
        var root = Flow(); var child = Item(root, "height:50%"); child.Child = new Border { Height = 30 };
        Layout(root);
        Assert.Equal(30, child.ActualHeight, 6);
    }

    [Fact]
    public void AutoMargins_CenterAnExplicitWidthWithoutWritingLocalValues()
    {
        var root = Flow(); var child = Item(root, "width:100px; height:20px; margin:0 auto"); Layout(root);
        Assert.Equal(100, child.VisualBounds.X, 6); Assert.Equal(100, child.ActualWidth, 6);
        Assert.Same(DependencyProperty.UnsetValue, child.ReadLocalValue(FrameworkElement.MarginProperty));
    }

    [Fact]
    public void FlowUsesSourceOrder_RegardlessOfFlexOrderDeclarations()
    {
        var root = Flow(); var a = Item(root, "height:20px; order:10"); var b = Item(root, "height:20px; order:-1");
        Layout(root); Assert.Equal(0, a.VisualBounds.Y, 6); Assert.Equal(20, b.VisualBounds.Y, 6);
    }

    [Fact]
    public void AtomicInlineBoxes_WrapIntoLinesAndRespectMargins()
    {
        var root = Flow(); var a = Item(root, "display:inline-block; width:120px; height:20px; margin-right:10px; vertical-align:top");
        var b = Item(root, Css.GetStyle(a)); var c = Item(root, Css.GetStyle(a)); Layout(root);
        Assert.Equal(0, a.VisualBounds.X, 6); Assert.Equal(130, b.VisualBounds.X, 6);
        Assert.Equal(0, c.VisualBounds.X, 6); Assert.Equal(20, c.VisualBounds.Y, 6);
        Assert.Equal(40, root.DesiredSize.Height, 6);
    }

    [Fact]
    public void BlocksSplitAnonymousInlineRuns()
    {
        var root = Flow(); var a = Item(root, "display:inline-block; width:100px; height:20px; vertical-align:top");
        var block = Item(root, "display:block; height:30px");
        var b = Item(root, Css.GetStyle(a)); Layout(root);
        Assert.Equal(20, block.VisualBounds.Y, 6); Assert.Equal(50, b.VisualBounds.Y, 6); Assert.Equal(70, root.DesiredSize.Height, 6);
    }

    [Fact]
    public void NativeBaselineOffsetsAndCssOffsets_ControlLineAlignment()
    {
        var root = Flow(); var a = Item(root, "display:inline-block; width:50px; height:20px");
        var b = Item(root, "display:inline-block; width:50px; height:30px");
        TextBlock.SetBaselineOffset(a, 10); TextBlock.SetBaselineOffset(b, 25); Layout(root);
        Assert.Equal(15, a.VisualBounds.Y, 6); Assert.Equal(0, b.VisualBounds.Y, 6); Assert.Equal(35, root.DesiredSize.Height, 6);
        Css.SetStyle(a, "display:inline-block; width:50px; height:20px; vertical-align:5px"); Layout(root);
        Assert.Equal(10, a.VisualBounds.Y, 6); Assert.Equal(30, root.DesiredSize.Height, 6);
    }

    [Theory]
    [InlineData("center", 50, 150)]
    [InlineData("right", 100, 200)]
    public void TextAlignment_PositionsAtomicInlineRuns(string alignment, double firstX, double secondX)
    {
        var root = Flow($"text-align:{alignment}");
        var a = Item(root, "display:inline-block; width:100px; height:20px"); var b = Item(root, Css.GetStyle(a)); Layout(root);
        Assert.Equal(firstX, a.VisualBounds.X, 6); Assert.Equal(secondX, b.VisualBounds.X, 6);
    }

    [Fact]
    public void RightToLeftStartAlignment_UsesTheNativeFlowDirection()
    {
        var root = Flow("text-align:start"); root.FlowDirection = FlowDirection.RightToLeft;
        var a = Item(root, "display:inline-block; width:100px; height:20px"); var b = Item(root, Css.GetStyle(a)); Layout(root);
        Assert.Equal(200, a.VisualBounds.X, 6); Assert.Equal(100, b.VisualBounds.X, 6);
    }

    [Fact]
    public void InlineBlock_ShrinksToItsNativeContents()
    {
        var root = Flow(); var nested = Flow("display:inline-block", root);
        Item(nested, "width:50px; height:20px"); Item(nested, "width:80px; height:30px");
        var next = Item(root, "display:inline-block; width:40px; height:20px"); Layout(root, 100);
        Assert.Equal(80, nested.ActualWidth, 6); Assert.Equal(50, nested.ActualHeight, 6);
        Assert.Equal(0, next.VisualBounds.X, 6); Assert.Equal(50, next.VisualBounds.Y, 6);
    }

    [Theory]
    [InlineData("inline-grid")]
    [InlineData("inline grid")]
    [InlineData("grid inline")]
    public void InlineGrid_HasInlineOuterPlacementAndGridInnerLayout(string display)
    {
        var root = Flow(); var grid = Flow($"display:{display}; grid-template:20px / 100px 50px; gap:10px", root);
        var first = Item(grid, ""); var second = Item(grid, "");
        var after = Item(root, "display:inline-block; width:100px; height:20px"); Layout(root);
        Assert.Equal(160, grid.ActualWidth, 6); Assert.Equal(100, first.ActualWidth, 6); Assert.Equal(110, second.VisualBounds.X, 6);
        Assert.Equal(160, after.VisualBounds.X, 6); Assert.Equal(0, after.VisualBounds.Y, 6);
    }

    [Fact]
    public void InlineFlex_PreservesItsInternalFlexLayout()
    {
        var root = Flow(); var flex = Flow("display:inline-flex; gap:10px", root);
        Item(flex, "flex:0 0 80px; height:20px"); Item(flex, "flex:0 0 40px; height:20px");
        var after = Item(root, "display:inline-block; width:100px; height:20px"); Layout(root);
        Assert.Equal(130, flex.ActualWidth, 6); Assert.Equal(130, after.VisualBounds.X, 6);
    }

    [Fact]
    public void Nowrap_PreventsAtomicInlineWrapping()
    {
        var root = Flow("white-space:nowrap"); var a = Item(root, "display:inline-block; width:200px; height:20px");
        var b = Item(root, Css.GetStyle(a)); Layout(root);
        Assert.Equal(200, b.VisualBounds.X, 6); Assert.Equal(0, b.VisualBounds.Y, 6);
    }

    [Theory]
    [InlineData("display:none; visibility:visible")]
    [InlineData("visibility:visible; display:none")]
    public void DisplayNone_IsNotOverriddenByVisibilityVisible(string css)
    {
        var child = new Border(); Css.SetStyle(child, css); Assert.Equal(Visibility.Collapsed, child.Visibility);
        Css.SetStyle(child, "display:block; visibility:hidden"); Assert.Equal(Visibility.Hidden, child.Visibility);
    }

    [Fact]
    public void ClearingCssFlow_RestoresNativeLayoutAndKeepsBindings()
    {
        var root = Flow(); var a = Item(root, "display:inline-block; height:20px");
        var b = Item(root, "display:inline-block; width:50px; height:20px");
        var source = new Border { Width = 50 }; a.SetBinding(FrameworkElement.WidthProperty, new Binding(nameof(FrameworkElement.Width)) { Source = source });
        var binding = a.GetBindingExpression(FrameworkElement.WidthProperty); Layout(root);
        Assert.Equal(50, b.VisualBounds.X, 6);
        Css.SetStyle(root, ""); Layout(root);
        Assert.Equal(20, b.VisualBounds.Y, 6); Assert.Same(binding, a.GetBindingExpression(FrameworkElement.WidthProperty));
        Assert.Same(root, a.VisualParent); Assert.Same(a, root.Children[0]);
    }

    [Fact]
    public void FlowRectangles_ReachNativeSoftwarePaintingAndHitTesting()
    {
        var root = Flow(); var a = Item(root, "display:inline-block; width:30px; height:40px; margin-right:10px; background-color:red");
        var b = Item(root, "display:inline-block; width:50px; height:40px; background-color:blue"); Layout(root, 90, 40);
        var bitmap = new RenderTargetBitmap(90, 40, 96, 96, PixelFormat.Bgra32); bitmap.Clear(Color.FromRgb(0, 0, 0)); bitmap.Render(root);
        var pixels = new byte[90 * 40 * 4]; bitmap.CopyPixels(new Int32Rect(0, 0, 90, 40), pixels, 90 * 4, 0);
        Assert.Equal(255, pixels[(20 * 90 + 15) * 4 + 2]); Assert.Equal(255, pixels[(20 * 90 + 65) * 4]);
        Assert.Same(a, VisualTreeHelper.HitTest(root, new Point(15, 20))?.VisualHit);
        Assert.Same(b, VisualTreeHelper.HitTest(root, new Point(65, 20))?.VisualHit);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("block inline")]
    [InlineData("flex grid")]
    [InlineData("inline grid extra")]
    public void InvalidDisplayValues_AreRejected(string value) => Assert.False(Css.Supports("display", value));

    [Fact]
    public void AbsoluteFlowContainers_EstablishAnIndependentFormattingContext()
    {
        var root = Flow(); var absolute = Flow("display:block; position:absolute; left:10px; top:10px; width:100px", root);
        var child = Item(absolute, "height:20px; margin:20px 0"); Layout(root);
        Assert.Equal(60, absolute.ActualHeight, 6); Assert.Equal(20, child.VisualBounds.Y, 6);
        Assert.Equal(0, root.DesiredSize.Height, 6);
    }

    [Fact]
    public void OverflowClipping_StopsMarginCollapseAndUpdatesWhenRemoved()
    {
        var root = Flow(); var nested = Flow("display:block; overflow:hidden", root);
        var child = Item(nested, "height:20px; margin:20px 0"); Layout(root);
        Assert.Equal(60, nested.ActualHeight, 6); Assert.Equal(20, child.VisualBounds.Y, 6);
        Css.SetStyle(nested, "display:block; overflow:visible"); Layout(root);
        Assert.Equal(20, nested.ActualHeight, 6); Assert.Equal(0, child.VisualBounds.Y, 6);
    }

    [Fact]
    public void DeepBlockFlow_DoesNotRepeatedlyMeasureTheWholeSubtree()
    {
        var root = Flow(); var parent = root;
        for (var i = 0; i < 8; i++) parent = Flow("display:block", parent);
        var leaf = new MeasuringLeaf(); parent.Children.Add(leaf); Layout(root);
        Assert.True(leaf.Count < 100, $"Leaf measured {leaf.Count} times");
    }

    private sealed class MeasuringLeaf : FrameworkElement
    {
        public int Count;
        protected override Size MeasureOverride(Size availableSize) { Count++; return new(20, 20); }
    }

    [Theory]
    [InlineData("2", 20)]
    [InlineData("200%", 40)]
    [InlineData("2em", 40)]
    [InlineData("calc(1em + 4px)", 24)]
    public void LineHeightInheritance_PreservesNumbersAndComputesLengths(string value, double expected)
    {
        var root = Flow($"font-size:20px; line-height:{value}");
        var child = new TextBlock { Text = "text" }; root.Children.Add(child);
        Css.SetStyle(child, "font-size:10px; line-height:inherit"); Layout(root);
        Assert.Equal(expected, child.LineHeight, 6);
    }

    [Fact]
    public void ChildUpdateLayout_RestoresTheParentFormattingContext()
    {
        var root = Flow("width:200px; height:100px; padding:10px");
        var child = Item(root, "width:50%; height:20px; margin:10px 0"); Layout(root, 200, 100);
        Css.SetStyle(child, "width:50%; height:20px; margin:20px 0");
        child.UpdateLayout();
        Assert.Equal(90, child.ActualWidth, 6); Assert.Equal(30, child.VisualBounds.Y, 6);
    }

    [Fact]
    public void ManagedChildUpdateLayout_UsesTheParentFormattingContext()
    {
        var root = new LayoutHost(); Css.SetStyle(root, "display:flow-root; width:200px; height:100px; padding:10px");
        var child = Item(root, "width:50%; height:20px; margin:10px 0"); Layout(root, 200, 100);
        Css.SetStyle(child, "width:75%; height:20px; margin:20px 0"); child.UpdateLayout();
        Assert.Equal(135, child.ActualWidth, 6); Assert.Equal(30, child.VisualBounds.Y, 6);
    }

    private sealed class LayoutHost : StackPanel, ILayoutManagerHost
    {
        public LayoutManager LayoutManager { get; } = new();
    }

    [Fact]
    public void HeadlessUpdateLayout_AlsoUpdatesOrdinaryNativePanels()
    {
        var root = new StackPanel(); var first = new Border { Height = 20 }; var second = new Border { Height = 20 };
        root.Children.Add(first); root.Children.Add(second); Layout(root);
        first.Height = 30; first.UpdateLayout();
        Assert.Equal(30, first.ActualHeight, 6); Assert.Equal(30, second.VisualBounds.Y, 6);
    }
}
