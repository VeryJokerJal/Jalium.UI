using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssFloatLayoutTests
{
    private static StackPanel Flow(string css = "", Panel? parent = null)
    {
        var root = new StackPanel(); parent?.Children.Add(root); Css.SetStyle(root, "display:flow-root; line-height:0; " + css); return root;
    }
    private static Border Item(Panel parent, string css)
    {
        var item = new Border(); parent.Children.Add(item); Css.SetStyle(item, css); return item;
    }
    private static Border Inline(Panel parent, double width = 80, double height = 20)
        => Item(parent, $"display:inline-block; width:{width}px; height:{height}px; vertical-align:top");
    private static void Layout(Panel root, double width = 300, double height = 200)
    {
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher); root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height));
    }

    [Fact]
    public void LeftFloat_ShapesSuccessiveLinesUntilItsBottom()
    {
        var root = Flow(); var floating = Item(root, "float:left; width:100px; height:60px");
        var children = Enumerable.Range(0, 7).Select(_ => Inline(root)).ToArray(); Layout(root);
        Assert.Equal(0, floating.VisualBounds.X, 6);
        Assert.Equal(100, children[0].VisualBounds.X, 6); Assert.Equal(180, children[1].VisualBounds.X, 6);
        Assert.Equal(20, children[2].VisualBounds.Y, 6); Assert.Equal(40, children[4].VisualBounds.Y, 6);
        Assert.Equal(0, children[6].VisualBounds.X, 6); Assert.Equal(60, children[6].VisualBounds.Y, 6);
        Assert.Equal(80, root.DesiredSize.Height, 6);
    }

    [Fact]
    public void RightFloat_BlockifiesAnInlineBox()
    {
        var root = Flow(); var floating = Item(root, "display:inline-block; float:right; width:100px; height:40px");
        var a = Inline(root); var b = Inline(root); var c = Inline(root); Layout(root);
        Assert.Equal(200, floating.VisualBounds.X, 6); Assert.Equal(0, a.VisualBounds.X, 6);
        Assert.Equal(80, b.VisualBounds.X, 6); Assert.Equal(20, c.VisualBounds.Y, 6);
    }

    [Fact]
    public void Floats_PreferTheHighestRowWhereTheyFit()
    {
        var root = Flow(); var a = Item(root, "float:left; width:80px; height:50px");
        var b = Item(root, "float:left; width:80px; height:20px"); var c = Item(root, "float:right; width:60px; height:30px"); Layout(root, 200);
        Assert.Equal(0, a.VisualBounds.X, 6); Assert.Equal(80, b.VisualBounds.X, 6);
        Assert.Equal(140, c.VisualBounds.X, 6); Assert.Equal(20, c.VisualBounds.Y, 6);
    }

    [Theory]
    [InlineData("left", 40)]
    [InlineData("right", 70)]
    [InlineData("both", 70)]
    public void Clear_UsesOnlyTheRequestedFloatSides(string clear, double y)
    {
        var root = Flow(); Item(root, "float:left; width:80px; height:40px"); Item(root, "float:right; width:80px; height:70px");
        var child = Item(root, $"clear:{clear}; width:50px; height:20px"); Layout(root);
        Assert.Equal(y, child.VisualBounds.Y, 6);
    }

    [Fact]
    public void ClearOnAFloat_AppliesToItsMarginEdge()
    {
        var root = Flow(); Item(root, "float:left; width:80px; height:40px; margin-bottom:10px");
        var child = Item(root, "float:left; clear:left; width:80px; height:20px; margin-top:5px"); Layout(root);
        Assert.Equal(55, child.VisualBounds.Y, 6); Assert.Equal(75, root.DesiredSize.Height, 6);
    }

    [Fact]
    public void Clearance_CanCompensateForMarginsThatPreviouslyCollapsed()
    {
        var root = Flow(); Item(root, "height:20px; margin-bottom:40px");
        var floating = Item(root, "float:left; width:20px; height:20px");
        var child = Item(root, "clear:left; height:20px; margin-top:30px"); Layout(root);
        Assert.Equal(60, floating.VisualBounds.Y, 6); Assert.Equal(80, child.VisualBounds.Y, 6);
    }

    [Fact]
    public void DescendantFloats_EscapeAnOrdinaryBlockButAreContainedByTheFlowRoot()
    {
        var root = Flow(); var block = Flow("display:block", root); var floating = Item(block, "float:left; width:100px; height:80px");
        var inline = Inline(root, 100); Layout(root);
        Assert.Equal(0, block.ActualHeight, 6); Assert.Equal(100, inline.VisualBounds.X, 6);
        Assert.Equal(80, root.DesiredSize.Height, 6); Assert.Same(block, floating.VisualParent);
    }

    [Fact]
    public void EarlierFloats_AffectLinesInsideAnOrdinaryBlock()
    {
        var root = Flow(); Item(root, "float:left; width:100px; height:60px");
        var block = Flow("display:block", root); var a = Inline(block, 160); var b = Inline(block, 160); Layout(root);
        Assert.Equal(300, block.ActualWidth, 6); Assert.Equal(100, a.VisualBounds.X, 6);
        Assert.Equal(100, b.VisualBounds.X, 6); Assert.Equal(20, b.VisualBounds.Y, 6);
    }

    [Fact]
    public void IndependentFormattingContexts_AvoidExternalFloats()
    {
        var root = Flow(); Item(root, "float:left; width:100px; height:60px");
        var independent = Flow("display:flow-root", root); var content = Inline(independent, 160); Layout(root);
        Assert.Equal(100, independent.VisualBounds.X, 6); Assert.Equal(200, independent.ActualWidth, 6);
        Assert.Equal(0, content.VisualBounds.X, 6);
    }

    [Fact]
    public void ATooWideIndependentBox_MovesBelowTheFloat()
    {
        var root = Flow(); Item(root, "float:left; width:100px; height:60px");
        var independent = Flow("width:250px", root); Inline(independent, 160); Layout(root);
        Assert.Equal(60, independent.VisualBounds.Y, 6); Assert.Equal(0, independent.VisualBounds.X, 6);
    }

    [Fact]
    public void FloatsInsideFlowRoot_DoNotEscapeToFollowingContent()
    {
        var root = Flow(); var independent = Flow("width:150px", root); Item(independent, "float:left; width:100px; height:40px");
        var after = Inline(root); Layout(root);
        Assert.Equal(40, independent.ActualHeight, 6); Assert.Equal(0, after.VisualBounds.X, 6); Assert.Equal(40, after.VisualBounds.Y, 6);
    }

    [Theory]
    [InlineData(100, 100, 0)]
    [InlineData(200, 0, 20)]
    public void AFloatAfterInlineContent_ReflowsTheLineOrMovesDown(double floatWidth, double inlineX, double floatY)
    {
        var root = Flow(); var text = Inline(root, 200);
        var floating = Item(root, $"float:left; width:{floatWidth}px; height:40px"); Layout(root);
        Assert.Equal(inlineX, text.VisualBounds.X, 6); Assert.Equal(floatY, floating.VisualBounds.Y, 6);
    }

    [Theory]
    [InlineData(20, 30, 0)]
    [InlineData(40, 0, 40)]
    public void ClearanceAndParentMarginCollapse_ReachAStablePosition(double floatHeight, double parentY, double childY)
    {
        var root = Flow(); Item(root, $"float:left; width:80px; height:{floatHeight}px");
        var block = Flow("display:block", root); var child = Item(block, "clear:left; margin-top:30px; height:20px"); Layout(root);
        Assert.Equal(parentY, block.VisualBounds.Y, 6); Assert.Equal(childY, child.VisualBounds.Y, 6);
    }

    [Fact]
    public void Exclusions_AreTranslatedThroughMarginsAndPadding()
    {
        var root = Flow(); Item(root, "float:left; width:100px; height:60px");
        var block = Flow("display:block; margin:10px 0 0 20px; padding:10px", root);
        var a = Inline(block, 160); Inline(block, 160); var c = Inline(block, 160); Layout(root);
        Assert.Equal(80, a.VisualBounds.X, 6); Assert.Equal(10, a.VisualBounds.Y, 6);
        Assert.Equal(10, c.VisualBounds.X, 6); Assert.Equal(50, c.VisualBounds.Y, 6);
    }

    [Fact]
    public void NegativeOuterFloatHeight_DoesNotObstructLineBoxes()
    {
        var root = Flow(); Item(root, "float:left; width:100px; height:20px; margin-top:-10px; margin-bottom:-20px");
        var inline = Inline(root, 200); Layout(root);
        Assert.Equal(0, inline.VisualBounds.X, 6); Assert.Equal(20, root.DesiredSize.Height, 6);
    }

    [Fact]
    public void FloatWidthChanges_InvalidateSiblingExclusionGeometry()
    {
        var root = Flow(); var floating = Item(root, "float:left; width:100px; height:40px"); var inline = Inline(root, 150); Layout(root);
        Css.SetStyle(floating, "float:left; width:150px; height:40px"); floating.UpdateLayout();
        Assert.Equal(150, inline.VisualBounds.X, 6);
    }

    [Fact]
    public void NativeDimensionsMarginsAndBindings_KeepTheirPriority()
    {
        var root = Flow(); var floating = Item(root, "float:left; width:100px; height:40px; margin:0");
        var source = new Border { Width = 90 }; floating.SetBinding(FrameworkElement.WidthProperty, new Binding(nameof(FrameworkElement.Width)) { Source = source });
        floating.Margin = new Thickness(10, 0, 10, 0); var binding = floating.GetBindingExpression(FrameworkElement.WidthProperty);
        var inline = Inline(root, 100); Layout(root);
        Assert.Equal(90, floating.ActualWidth, 6); Assert.Equal(110, inline.VisualBounds.X, 6);
        Assert.Same(binding, floating.GetBindingExpression(FrameworkElement.WidthProperty));
    }

    [Fact]
    public void FloatsAreIgnoredByNativePanelsAndCssGrid()
    {
        var root = new StackPanel(); var a = Item(root, "float:left; width:100px; height:20px"); var b = Item(root, "height:20px"); Layout(root);
        Assert.Equal(20, b.VisualBounds.Y, 6);
        Css.SetStyle(root, "display:grid; grid-template:20px / 150px 150px"); Layout(root);
        Assert.Equal(150, b.VisualBounds.X, 6); Assert.Equal(0, b.VisualBounds.Y, 6);
        Assert.Same(a, root.Children[0]);
    }

    [Fact]
    public void LogicalFloatSides_UseTheNativeFlowDirection()
    {
        var root = Flow(); root.FlowDirection = FlowDirection.RightToLeft;
        var floating = Item(root, "float:inline-start; width:100px; height:40px"); Layout(root);
        Assert.Equal(200, floating.VisualBounds.X, 6);
    }

    [Fact]
    public void LogicalFloatAndClearSides_UseTheContainingBlockDirection()
    {
        var root = Flow(); root.FlowDirection = FlowDirection.RightToLeft;
        var floating = Item(root, "float:inline-start; width:100px; height:40px"); floating.FlowDirection = FlowDirection.LeftToRight;
        var child = Item(root, "clear:inline-start; height:20px"); child.FlowDirection = FlowDirection.LeftToRight;
        Layout(root);
        Assert.Equal(200, floating.VisualBounds.X, 6); Assert.Equal(40, child.VisualBounds.Y, 6);
    }

    [Fact]
    public void FloatPaintAndHitOrder_IsAboveFollowingBlockBackgrounds()
    {
        var root = Flow(); var floating = Item(root, "float:left; width:100px; height:60px; background-color:red");
        var block = Flow("display:block; height:80px; background-color:blue", root); Layout(root, 300, 100);
        Assert.Equal(Colors.Red, Pixel(root, 50, 20)); Assert.Equal(Colors.Blue, Pixel(root, 150, 20));
        Assert.Same(floating, VisualTreeHelper.HitTest(root, new Point(50, 20))?.VisualHit);
        Assert.Same(floating, root.Children[0]); Assert.Same(block, root.Children[1]);
    }

    [Fact]
    public void NestedFloatPaint_IsAboveLaterBlockBackgrounds()
    {
        var root = Flow(); var block = Flow("display:block; height:20px; background-color:blue", root);
        var floating = Item(block, "float:left; width:100px; height:60px; background-color:red");
        Flow("display:block; height:80px; background-color:green", root); Layout(root, 300, 100);
        Assert.Equal(Colors.Red, Pixel(root, 50, 30)); Assert.Equal(Colors.Green, Pixel(root, 150, 30));
        Assert.Same(floating, VisualTreeHelper.HitTest(root, new Point(50, 30))?.VisualHit);
    }

    [Fact]
    public void AnUnbreakableInlineBox_MovesBelowAnInsufficientFloatBand()
    {
        var root = Flow(); Item(root, "float:left; width:100px; height:60px"); var inline = Inline(root, 250); Layout(root);
        Assert.Equal(0, inline.VisualBounds.X, 6); Assert.Equal(60, inline.VisualBounds.Y, 6);
    }

    [Fact]
    public void NestedExclusions_UpdateWhenAnEarlierSiblingFloatChanges()
    {
        var root = Flow(); var floating = Item(root, "float:left; width:100px; height:40px");
        var block = Flow("display:block", root); var inline = Inline(block, 150); Layout(root);
        Css.SetStyle(floating, "float:left; width:150px; height:40px"); floating.UpdateLayout();
        Assert.Equal(150, inline.VisualBounds.X, 6);
    }

    [Fact]
    public void NativeZIndexAndRemovingFloat_KeepTheirExpectedPaintBehavior()
    {
        var root = Flow(); var floating = Item(root, "float:left; width:100px; height:60px; background-color:red");
        var block = Flow("display:block; height:80px; background-color:blue", root);
        Panel.SetZIndex(block, 10); Layout(root, 300, 100);
        Assert.Equal(Colors.Blue, Pixel(root, 50, 20));
        Css.SetStyle(floating, "width:100px; height:60px; background-color:red"); Layout(root, 300, 100);
        Assert.Equal(60, block.VisualBounds.Y, 6); Assert.Same(floating, root.Children[0]);
    }

    [Fact]
    public void FloatedPanels_UseShrinkToFitAndContainTheirOwnFloats()
    {
        var root = Flow(); var floated = Flow("display:block; float:left", root);
        Item(floated, "float:left; width:60px; height:30px"); Item(floated, "float:right; width:60px; height:40px");
        var inline = Inline(root, 100); Layout(root);
        Assert.Equal(120, floated.ActualWidth, 6); Assert.Equal(40, floated.ActualHeight, 6);
        Assert.Equal(120, inline.VisualBounds.X, 6);
    }

    [Fact]
    public void SharedFloatContexts_KeepDeepMeasurementsBounded()
    {
        var root = Flow(); Item(root, "float:left; width:100px; height:60px"); var parent = root;
        for (var i = 0; i < 6; i++) parent = Flow("display:block", parent);
        var leaf = new MeasuredLeaf(); parent.Children.Add(leaf);
        Css.SetStyle(leaf, "display:inline-block; width:80px; height:20px"); Layout(root);
        Assert.Equal(100, leaf.VisualBounds.X, 6);
        Assert.True(leaf.Measures < 200, $"Six shared flow levels measured the leaf {leaf.Measures} times");
    }

    private sealed class MeasuredLeaf : FrameworkElement
    {
        public int Measures;
        protected override Size MeasureOverride(Size availableSize) { Measures++; return new(20, 20); }
    }

    private static Color Pixel(Visual root, int x, int y)
    {
        var bitmap = new RenderTargetBitmap(300, 100, 96, 96, PixelFormat.Bgra32); bitmap.Clear(Colors.White); bitmap.Render(root);
        var pixels = new byte[300 * 100 * 4]; bitmap.CopyPixels(new Int32Rect(0, 0, 300, 100), pixels, 300 * 4, 0);
        var index = (y * 300 + x) * 4; return Color.FromArgb(pixels[index + 3], pixels[index + 2], pixels[index + 1], pixels[index]);
    }
}
