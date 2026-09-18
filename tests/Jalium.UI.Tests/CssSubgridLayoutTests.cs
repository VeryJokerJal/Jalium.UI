using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

/// <summary>Subgrid semantics from CSS Grid 2 (2025-03-26), including native ownership and value precedence.</summary>
public class CssSubgridLayoutTests
{
    private static StackPanel Grid(string css, Panel? parent = null)
    {
        var panel = new StackPanel(); parent?.Children.Add(panel);
        Css.SetStyle(panel, "display:grid; " + css);
        return panel;
    }

    private static Border Item(Panel parent, string css = "")
    {
        var border = new Border(); parent.Children.Add(border); Css.SetStyle(border, css); return border;
    }

    private static void Layout(Panel root, double width = 360, double height = 110)
    {
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height));
    }

    [Fact]
    public void BothAxes_ShareParentTracksAndDefaultGaps()
    {
        var root = Grid("grid-template:40px 60px / 80px 160px 100px; gap:10px");
        var subgrid = Grid("grid:subgrid / subgrid; grid-area:1 / 2 / 3 / 4", root);
        var a = Item(subgrid, "grid-area:1 / 1"); var b = Item(subgrid, "grid-area:2 / 2");
        Layout(root);
        Assert.Equal(90, subgrid.VisualBounds.X, 6);
        Assert.Equal(160, a.ActualWidth, 6); Assert.Equal(40, a.ActualHeight, 6);
        Assert.Equal(170, b.VisualBounds.X, 6); Assert.Equal(50, b.VisualBounds.Y, 6);
        Assert.Equal(100, b.ActualWidth, 6); Assert.Equal(60, b.ActualHeight, 6);
        Assert.Same(subgrid, a.VisualParent); Assert.Same(root, subgrid.VisualParent);
    }

    [Fact]
    public void DescendantWidths_ContributeToTheirCorrespondingParentTracks()
    {
        var root = Grid("grid-template-columns:auto 1fr; grid-auto-rows:30px");
        var subgrid = Grid("grid-template-columns:subgrid; grid-column:span 2", root);
        Item(subgrid, "width:120px"); Item(subgrid, "width:60px");
        var probe = Item(root, "grid-area:2 / 1");
        Layout(root, 300);
        Assert.Equal(120, probe.ActualWidth, 6);
        Assert.Equal(300, subgrid.ActualWidth, 6);
    }

    [Fact]
    public void DescendantHeights_ContributeToSharedParentRows()
    {
        var root = Grid("grid-template-rows:auto auto; grid-template-columns:100px; align-content:start");
        var subgrid = Grid("grid-template-rows:subgrid; grid-row:span 2", root);
        Item(subgrid, "height:30px"); Item(subgrid, "height:70px");
        Layout(root, 100, 200);
        Assert.Equal(100, subgrid.ActualHeight, 6);
        Assert.Equal(30, ((Border)subgrid.Children[1]).VisualBounds.Y, 6);
    }

    [Theory]
    [InlineData("normal", 100, 150, 150)]
    [InlineData("0px", 125, 175, 125)]
    [InlineData("100px", 75, 125, 175)]
    public void DifferentSubgridGutters_KeepTheirCentersAligned(string gap, double firstWidth, double secondWidth, double secondX)
    {
        var root = Grid("grid-template-columns:100px 1fr; column-gap:50px");
        var subgrid = Grid($"grid-template-columns:subgrid; grid-column:span 2; column-gap:{gap}", root);
        var a = Item(subgrid); var b = Item(subgrid);
        Layout(root, 300);
        Assert.Equal(firstWidth, a.ActualWidth, 6); Assert.Equal(secondWidth, b.ActualWidth, 6); Assert.Equal(secondX, b.VisualBounds.X, 6);
    }

    [Fact]
    public void GutterDifference_AlsoAdjustsIntrinsicContributions()
    {
        var root = Grid("grid-template-columns:auto 1fr; column-gap:50px; grid-auto-rows:30px");
        var subgrid = Grid("grid-template-columns:subgrid; grid-column:span 2; column-gap:0", root);
        Item(subgrid, "width:120px"); Item(subgrid);
        var probe = Item(root, "grid-area:2 / 1");
        Layout(root, 300);
        Assert.Equal(95, probe.ActualWidth, 6);
    }

    [Fact]
    public void Names_AreInheritedAndLocallyExtended()
    {
        var root = Grid("grid-template-columns:[a] 80px [b] 120px [c] 160px [d]; gap:10px");
        var middle = Grid("grid-template-columns:subgrid [local] [] [end]; grid-column:b / d", root);
        var inner = Grid("grid-template-columns:subgrid; grid-column:local / end", middle);
        var child = Item(inner, "grid-column:c / d");
        Layout(root, 380);
        Assert.Equal(160, child.ActualWidth, 6); Assert.Equal(130, child.VisualBounds.X, 6);
        Assert.Equal(90, middle.VisualBounds.X, 6);
    }

    [Fact]
    public void ParentNamedAreas_AreClippedToTheSubgridIntersection()
    {
        var root = Grid("grid-template-columns:[outer] 20px [main-start] 100px [center] 100px 80px [main-end]; grid-template-areas:'gutter info info photos'");
        var middle = Grid("grid-template-columns:subgrid; grid-column:main-start / main-end", root);
        var inner = Grid("grid-template-columns:subgrid; grid-column:center / -1", middle);
        var info = Item(inner, "grid-column:info");
        Layout(root, 300);
        Assert.Equal(100, info.ActualWidth, 6); Assert.Equal(0, info.VisualBounds.X, 6);
    }

    [Fact]
    public void AutomaticSpan_CanBeDerivedFromLocalLineNames()
    {
        var root = Grid("grid-template-columns:50px 100px 150px");
        var subgrid = Grid("grid-template-columns:subgrid [one] [two] [three]", root);
        var a = Item(subgrid, "grid-column:one / two"); var b = Item(subgrid, "grid-column:two / three");
        var last = Item(root);
        Layout(root, 300);
        Assert.Equal(150, subgrid.ActualWidth, 6); Assert.Equal(50, a.ActualWidth, 6);
        Assert.Equal(100, b.ActualWidth, 6); Assert.Equal(150, last.VisualBounds.X, 6);
    }

    [Fact]
    public void LineNameAutoRepeat_FillsTheBorrowedSpan()
    {
        var root = Grid("grid-template-columns:50px 100px 150px");
        var subgrid = Grid("grid-template-columns:subgrid repeat(auto-fill,[col]); grid-column:span 3", root);
        var child = Item(subgrid, "grid-column:col 2 / col 4");
        Layout(root, 300);
        Assert.Equal(50, child.VisualBounds.X, 6); Assert.Equal(250, child.ActualWidth, 6);
    }

    [Fact]
    public void SharedDimensions_DoNotCreateImplicitTracks()
    {
        var root = Grid("grid-template-columns:100px 200px");
        var subgrid = Grid("grid-template-columns:subgrid; grid-column:span 2; grid-auto-columns:1000px", root);
        var child = Item(subgrid, "grid-column:99 / span 3");
        Layout(root, 300);
        Assert.Equal(100, child.VisualBounds.X, 6); Assert.Equal(200, child.ActualWidth, 6);
    }

    [Fact]
    public void ParentTrackChanges_ReflowChildrenEvenWhenTheOuterSizeIsUnchanged()
    {
        var root = Grid("grid-template-columns:100px 200px");
        var subgrid = Grid("grid-template-columns:subgrid; grid-column:span 2", root);
        var a = Item(subgrid); var b = Item(subgrid);
        Layout(root, 300);
        Css.SetStyle(root, "display:grid; grid-template-columns:200px 100px"); Layout(root, 300);
        Assert.Equal(200, a.ActualWidth, 6); Assert.Equal(200, b.VisualBounds.X, 6); Assert.Equal(100, b.ActualWidth, 6);
    }

    [Fact]
    public void CssSizeAndAlignmentConstraints_AreIgnoredInSharedDimensions()
    {
        var root = Grid("grid-template:40px / 100px 200px");
        var subgrid = Grid("grid:subgrid / subgrid; grid-column:span 2; width:10px; max-width:20px; height:10px; max-height:15px; place-self:end; place-content:end", root);
        var child = Item(subgrid);
        Layout(root, 300, 40);
        Assert.Equal(300, subgrid.ActualWidth, 6); Assert.Equal(40, subgrid.ActualHeight, 6);
        Assert.Equal(0, subgrid.VisualBounds.X, 6); Assert.Equal(100, child.ActualWidth, 6);
    }

    [Fact]
    public void NativeWidthBinding_IsPreservedAboveCssSubgridRules()
    {
        var root = Grid("grid-template-columns:100px 200px");
        var subgrid = Grid("grid-template-columns:subgrid; grid-column:span 2; width:20px", root);
        var source = new Border { Width = 90 };
        subgrid.SetBinding(FrameworkElement.WidthProperty, new Binding(nameof(FrameworkElement.Width)) { Source = source });
        var binding = subgrid.GetBindingExpression(FrameworkElement.WidthProperty);
        Item(subgrid);
        Layout(root, 300);
        Assert.Equal(90, subgrid.ActualWidth, 6); Assert.Same(binding, subgrid.GetBindingExpression(FrameworkElement.WidthProperty));
    }

    [Fact]
    public void RemovingParentCssGrid_RestoresIndependentLayoutAndCssWidth()
    {
        var root = Grid("grid-template-columns:100px 200px");
        var subgrid = Grid("grid-template-columns:subgrid; grid-column:span 2; width:70px", root);
        var child = Item(subgrid, "height:20px");
        Layout(root, 300); Assert.Equal(300, subgrid.ActualWidth, 6);
        Css.SetStyle(root, ""); Layout(root, 300);
        Assert.Equal(70, subgrid.ActualWidth, 6); Assert.Same(subgrid, child.VisualParent);
    }

    [Fact]
    public void SubgridMargins_ContributeAtTheCorrectOuterEdges()
    {
        var root = Grid("grid-template-columns:auto auto; justify-content:start; column-gap:10px");
        var subgrid = Grid("grid-template-columns:subgrid; grid-column:span 2; margin:10px", root);
        var first = Item(subgrid, "width:30px; height:20px"); var second = Item(subgrid, "width:40px; height:20px");
        Layout(root, 200);
        Assert.Equal(10, subgrid.VisualBounds.X, 6); Assert.Equal(80, subgrid.ActualWidth, 6);
        Assert.Equal(0, first.VisualBounds.X, 6); Assert.Equal(40, second.VisualBounds.X, 6);
    }

    [Fact]
    public void OppositeNativeFlowDirections_ReverseTheBorrowedTrackOrder()
    {
        var root = Grid("grid-template-columns:100px 200px"); root.FlowDirection = FlowDirection.RightToLeft;
        var subgrid = Grid("grid-template-columns:subgrid; grid-column:span 2", root); subgrid.FlowDirection = FlowDirection.LeftToRight;
        var first = Item(subgrid); var second = Item(subgrid);
        Layout(root, 300);
        Assert.Equal(200, first.ActualWidth, 6); Assert.Equal(200, second.VisualBounds.X, 6); Assert.Equal(100, second.ActualWidth, 6);
    }

    [Fact]
    public void NestedGeometry_DrivesSoftwarePixelsAndHitTesting()
    {
        var root = Grid("grid-template:40px / 30px 50px; gap:10px");
        var subgrid = Grid("grid:subgrid / subgrid; grid-column:span 2", root);
        var red = Item(subgrid, "background-color:red"); var blue = Item(subgrid, "background-color:blue");
        Layout(root, 90, 40);
        var bitmap = new RenderTargetBitmap(90, 40, 96, 96, PixelFormat.Bgra32); bitmap.Clear(Color.FromRgb(0, 0, 0)); bitmap.Render(root);
        var pixels = new byte[90 * 40 * 4]; bitmap.CopyPixels(new Int32Rect(0, 0, 90, 40), pixels, 90 * 4, 0);
        Assert.Equal(255, pixels[(20 * 90 + 15) * 4 + 2]); Assert.Equal(255, pixels[(20 * 90 + 65) * 4]);
        Assert.Same(red, VisualTreeHelper.HitTest(root, new Point(15, 20))?.VisualHit);
        Assert.Same(blue, VisualTreeHelper.HitTest(root, new Point(65, 20))?.VisualHit);
    }

    [Theory]
    [InlineData("subgrid 10px")]
    [InlineData("subgrid repeat(auto-fit,[a])")]
    [InlineData("subgrid repeat(auto-fill,[a]) repeat(auto-fill,[b])")]
    [InlineData("subgrid repeat(2,20px)")]
    [InlineData("repeat(2,subgrid)")]
    public void InvalidSubgridTrackSyntax_IsRejected(string value) => Assert.False(Css.Supports("grid-template-columns", value));

    [Fact]
    public void PaddingAndMargins_AffectParentTracksAndContentOffsets()
    {
        var root = Grid("grid-template-columns:auto auto; justify-content:start; column-gap:10px");
        var subgrid = Grid("grid-template-columns:subgrid; grid-column:span 2; padding:5px; margin:10px", root);
        var a = Item(subgrid, "width:30px; height:20px"); var b = Item(subgrid, "width:40px; height:20px");
        Layout(root, 200);
        Assert.Equal(90, subgrid.ActualWidth, 6);
        Assert.Equal(5, a.VisualBounds.X, 6); Assert.Equal(5, a.VisualBounds.Y, 6);
        Assert.Equal(45, b.VisualBounds.X, 6);
    }

    [Fact]
    public void PercentageInsets_UseTheGridAreaWidth()
    {
        var root = Grid("grid-template:100px / 100px 100px");
        var subgrid = Grid("grid:subgrid / subgrid; grid-column:span 2; margin:5%; padding:5%", root);
        var a = Item(subgrid); var b = Item(subgrid);
        Layout(root, 200, 100);
        Assert.Equal(180, subgrid.ActualWidth, 6); Assert.Equal(80, subgrid.ActualHeight, 6);
        Assert.Equal(80, a.ActualWidth, 6); Assert.Equal(60, a.ActualHeight, 6);
        Assert.Equal(10, a.VisualBounds.X, 6); Assert.Equal(90, b.VisualBounds.X, 6);
    }

    [Fact]
    public void StandaloneCssGrid_UsesPaddingWithoutChangingNativePanelProperties()
    {
        var root = Grid("box-sizing:content-box; width:200px; padding:10px; grid-template:40px / 1fr");
        var child = Item(root);
        Layout(root, 400, 60);
        Assert.Equal(220, root.ActualWidth, 6); Assert.Equal(200, child.ActualWidth, 6);
        Assert.Equal(10, child.VisualBounds.X, 6); Assert.Equal(10, child.VisualBounds.Y, 6);
    }

    [Fact]
    public void SubgridBoxOccupiesAutoFitTracks_EvenWhenItsMiddleTrackHasNoChild()
    {
        var root = Grid("grid-template-columns:repeat(auto-fit,100px); column-gap:10px");
        var subgrid = Grid("grid-template-columns:subgrid; grid-column:span 3", root);
        Item(subgrid);
        Layout(root, 320);
        Assert.Equal(320, subgrid.ActualWidth, 6);
    }

    [Theory]
    [InlineData("grid:subgrid / subgrid")]
    [InlineData("grid-template-columns:subgrid")]
    [InlineData("grid-template-rows:subgrid")]
    public void NestedSubgrids_KeepIntrinsicMeasurementBounded(string css)
    {
        var root = Grid("grid-template:40px / 100px");
        var parent = root;
        for (var i = 0; i < 6; i++) parent = Grid(css, parent);
        var leaf = new MeasuredLeaf(); parent.Children.Add(leaf);
        Layout(root, 100, 40);
        Assert.Equal(100, leaf.ActualWidth, 6);
        Assert.True(leaf.Measures < 200, $"A six-level subgrid measured its leaf {leaf.Measures} times");
    }

    [Fact]
    public void ChangingDescendantContent_InvalidatesCachedIntrinsicContributions()
    {
        var root = Grid("grid-template-columns:auto auto; justify-content:start; align-content:start");
        var subgrid = Grid("grid-template-columns:subgrid; grid-column:span 2", root);
        var nested = Grid("grid-template-columns:subgrid; grid-column:span 2", subgrid);
        var child = Item(nested, "width:30px; height:20px"); Item(nested, "width:40px; height:20px");
        Layout(root, 200, 100);
        Css.SetStyle(child, "width:80px; height:60px"); Layout(root, 200, 150);
        Assert.Equal(120, subgrid.ActualWidth, 6); Assert.Equal(60, subgrid.ActualHeight, 6);
    }

    private sealed class MeasuredLeaf : FrameworkElement
    {
        public int Measures;
        protected override Size MeasureOverride(Size availableSize) { Measures++; return new(20, 20); }
    }
}
