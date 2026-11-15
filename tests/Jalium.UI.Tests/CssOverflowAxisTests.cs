using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssOverflowAxisTests
{
    [Fact]
    public void ShorthandAndLonghands_CombineByAxisAfterCascade()
    {
        Assert.True(Css.Supports("overflow", "clip visible"));
        Assert.True(Css.Supports("overflow", "overlay"));
        Assert.False(Css.Supports("overflow", "clip visible hidden"));
        Assert.False(Css.Supports("overflow-x", "visible hidden"));
        var panel = Box();

        Css.SetStyle(panel, "overflow:clip visible");
        AssertClip(panel, ClipEdges.Left | ClipEdges.Right);
        Css.SetStyle(panel, "overflow:clip visible; overflow-y:hidden");
        AssertClip(panel, ClipEdges.All); // clip computes to hidden beside a scrollable axis.
        Css.SetStyle(panel, "overflow:hidden; overflow-x:visible");
        AssertClip(panel, ClipEdges.All); // visible computes to auto beside hidden.
        Assert.Equal(CssOverflowMode.Auto, CssOverflowProperties.Get(panel)?.X);
        Css.SetStyle(panel, "overflow-x:hidden; overflow:visible clip");
        AssertClip(panel, ClipEdges.Top | ClipEdges.Bottom);
        Css.SetStyle(panel, "overflow:clip visible; overflow-x:initial");
        Assert.False(panel.ClipToBounds);
        Css.SetStyle(panel, "overflow:clip visible; overflow:unset");
        Assert.False(panel.ClipToBounds);
        Css.SetStyle(panel, "overflow-x:clip!important; overflow:visible");
        AssertClip(panel, ClipEdges.Left | ClipEdges.Right);
        Css.SetStyle(panel, "overflow:clip visible; overflow:revert");
        Assert.False(panel.ClipToBounds);
        Css.SetStyle(panel, "overflow:visible");
        Assert.False(panel.ClipToBounds);
        Assert.Equal(ClipEdges.All, panel.ClipToBoundsEdges);
        Assert.Null(panel.GetLayoutClip());
    }

    [Fact]
    public void LogicalOverflow_MapsToTheCurrentHorizontalWritingMode()
    {
        Assert.True(Css.Supports("overflow-inline", "clip"));
        Assert.True(Css.Supports("overflow-block", "hidden"));
        Assert.False(Css.Supports("overflow-inline", "clip hidden"));
        Assert.False(Css.Supports("overflow-block", "unknown"));
        var panel = Box();

        Css.SetStyle(panel, "overflow-inline:clip");
        AssertClip(panel, ClipEdges.Left | ClipEdges.Right);
        Assert.Null(panel.HitTest(new(50, 15)));
        Assert.NotNull(panel.HitTest(new(15, 40)));

        Css.SetStyle(panel, "direction:rtl; overflow-block:clip");
        AssertClip(panel, ClipEdges.Top | ClipEdges.Bottom);

        Css.SetStyle(panel, "overflow-inline:visible; overflow-block:hidden");
        AssertClip(panel, ClipEdges.All);
        Assert.Equal(CssOverflowMode.Auto, CssOverflowProperties.Get(panel)?.X);

        Css.SetStyle(panel, "--axis:clip; overflow-inline:var(--axis)");
        AssertClip(panel, ClipEdges.Left | ClipEdges.Right);
    }

    [Fact]
    public void LogicalAndPhysicalOverflow_CompeteByDeclarationOrderAndImportance()
    {
        var panel = Box();

        Css.SetStyle(panel, "overflow-x:hidden; overflow-inline:visible");
        Assert.False(panel.ClipToBounds);
        Css.SetStyle(panel, "overflow-inline:visible; overflow-x:hidden");
        AssertClip(panel, ClipEdges.All);
        Assert.Equal(CssOverflowMode.Auto, CssOverflowProperties.Get(panel)?.Y);
        Css.SetStyle(panel, "overflow-x:clip; overflow-inline:visible; overflow-x:hidden");
        AssertClip(panel, ClipEdges.All);
        Assert.Equal(CssOverflowMode.Hidden, CssOverflowProperties.Get(panel)?.X);

        Css.SetStyle(panel, "overflow:clip visible; overflow-inline:visible; overflow-block:clip");
        AssertClip(panel, ClipEdges.Top | ClipEdges.Bottom);
        Css.SetStyle(panel, "overflow-inline:clip; overflow:visible clip");
        AssertClip(panel, ClipEdges.Top | ClipEdges.Bottom);

        Css.SetStyle(panel, "overflow-x:clip!important; overflow-inline:visible");
        AssertClip(panel, ClipEdges.Left | ClipEdges.Right);
        Css.SetStyle(panel, "overflow-x:clip; overflow-inline:visible!important");
        Assert.False(panel.ClipToBounds);
        Css.SetStyle(panel, "overflow-x:clip; overflow-inline:initial");
        Assert.False(panel.ClipToBounds);
        Css.SetStyle(panel, "overflow-x:clip; all:initial");
        Assert.False(panel.ClipToBounds);
    }

    [Fact]
    public void LogicalOverflow_RevertLayerAndInheritanceKeepSpecifiedPropertiesSeparate()
    {
        var panel = Box();
        Css.SetClass(panel, "box");
        Css.SetStyleSheet(panel, "@layer base {.box {overflow-x:clip}} @layer app {.box {overflow-inline:hidden; overflow-inline:revert-layer}}");
        CssEvaluationScheduler.FlushIfPending(panel.Dispatcher);
        AssertClip(panel, ClipEdges.Left | ClipEdges.Right);
        Assert.Equal(CssOverflowMode.Clip, CssOverflowProperties.Get(panel)?.X);

        Css.SetStyleSheet(panel, "@layer base {.box {overflow-x:clip}} @layer app {.box {overflow-x:hidden; overflow-inline:revert-layer}}");
        CssEvaluationScheduler.FlushIfPending(panel.Dispatcher);
        Assert.Equal(CssOverflowMode.Clip, CssOverflowProperties.Get(panel)?.X);
        AssertClip(panel, ClipEdges.Left | ClipEdges.Right);

        Css.SetStyleSheet(panel, "@layer base {.box {overflow-inline:clip}} @layer app {.box {overflow-x:hidden; overflow-inline:revert-layer}}");
        CssEvaluationScheduler.FlushIfPending(panel.Dispatcher);
        Assert.Equal(CssOverflowMode.Clip, CssOverflowProperties.Get(panel)?.X);
        AssertClip(panel, ClipEdges.Left | ClipEdges.Right);

        var parent = new Grid();
        parent.Children.Add(panel);
        Css.SetStyleSheet(panel, "");
        Css.SetStyle(parent, "overflow:hidden");
        Css.SetStyle(panel, "overflow-inline:inherit");
        CssBorderRadiusTests.Layout(parent, 40, 30);
        AssertClip(panel, ClipEdges.All);
        Assert.Equal(CssOverflowMode.Hidden, CssOverflowProperties.Get(panel)?.X);

        Css.SetStyle(parent, "overflow:clip visible");
        CssEvaluationScheduler.FlushIfPending(parent.Dispatcher);
        Assert.Equal(CssOverflowMode.Clip, CssOverflowProperties.Get(panel)?.X);
        Css.SetStyle(panel, "overflow-inline:revert");
        Assert.False(panel.ClipToBounds);
        Css.SetStyle(panel, "overflow-x:clip; overflow-inline:revert");
        Assert.False(panel.ClipToBounds);
    }

    [Fact]
    public void LogicalOverflow_CascadesAcrossRulesSpecificityAndLayers()
    {
        var panel = Box();
        panel.Name = "hero";
        Css.SetClass(panel, "box");

        Css.SetStyleSheet(panel, ".box {overflow-inline:clip} .box {overflow-x:visible}");
        CssEvaluationScheduler.FlushIfPending(panel.Dispatcher);
        Assert.False(panel.ClipToBounds);

        Css.SetStyleSheet(panel, "#hero {overflow-inline:clip} .box {overflow-x:visible}");
        CssEvaluationScheduler.FlushIfPending(panel.Dispatcher);
        AssertClip(panel, ClipEdges.Left | ClipEdges.Right);

        Css.SetStyleSheet(panel, "@layer base, app; @layer base {#hero {overflow-inline:clip}} @layer app {.box {overflow-x:visible}}");
        CssEvaluationScheduler.FlushIfPending(panel.Dispatcher);
        Assert.False(panel.ClipToBounds);

        Css.SetStyleSheet(panel, "@layer base, app; @layer base {#hero {overflow-inline:clip!important}} @layer app {.box {overflow-x:visible!important}}");
        CssEvaluationScheduler.FlushIfPending(panel.Dispatcher);
        AssertClip(panel, ClipEdges.Left | ClipEdges.Right);
    }

    [Fact]
    public void AxisClip_PreservesVisibleOverflowAndPointerInputOnTheOtherAxis()
    {
        var panel = Box();
        Css.SetStyle(panel, "overflow-x:clip");
        AssertClip(panel, ClipEdges.Left | ClipEdges.Right);
        Assert.Null(panel.HitTest(new(50, 15)));
        Assert.NotNull(panel.HitTest(new(15, 40)));

        Css.SetStyle(panel, "overflow-y:clip");
        AssertClip(panel, ClipEdges.Top | ClipEdges.Bottom);
        Assert.NotNull(panel.HitTest(new(50, 15)));
        Assert.Null(panel.HitTest(new(15, 40)));

        Css.SetStyle(panel, "overflow-x:clip; overflow-y:hidden");
        AssertClip(panel, ClipEdges.All);
        Assert.Null(panel.HitTest(new(50, 15)));
        Assert.Null(panel.HitTest(new(15, 40)));
    }

    [Fact]
    public void InheritAndRemoval_KeepComputedAxesAndRestoreNativeClipValues()
    {
        var parent = new Grid();
        var child = Box();
        parent.Children.Add(child);
        Css.SetStyle(parent, "overflow:clip visible");
        Css.SetStyle(child, "overflow-x:inherit; overflow-y:visible");
        CssBorderRadiusTests.Layout(parent, 40, 30);
        AssertClip(child, ClipEdges.Left | ClipEdges.Right);

        Css.SetStyle(parent, "overflow:hidden");
        CssEvaluationScheduler.FlushIfPending(parent.Dispatcher);
        AssertClip(child, ClipEdges.All);

        Css.SetStyle(child, "overflow:inherit");
        AssertClip(child, ClipEdges.All);
        Css.SetStyle(parent, "overflow:auto");
        CssEvaluationScheduler.FlushIfPending(parent.Dispatcher);
        Assert.Equal(CssOverflowMode.Auto, CssOverflowProperties.Get(child)?.X);

        child.ClipToBoundsEdges = ClipEdges.Top | ClipEdges.Bottom;
        Css.SetStyle(child, "overflow-x:clip");
        AssertClip(child, ClipEdges.Top | ClipEdges.Bottom);
        child.ClearValue(UIElement.ClipToBoundsEdgesProperty);
        AssertClip(child, ClipEdges.Left | ClipEdges.Right);

        child.ClipToBounds = true;
        Css.SetStyle(child, "overflow:visible");
        Assert.True(child.ClipToBounds);
        Assert.Equal(ClipEdges.All, child.ClipToBoundsEdges);
        child.ClearValue(UIElement.ClipToBoundsProperty);
        Assert.False(child.ClipToBounds);
        child.ClipToBounds = false;
        Css.SetStyle(child, "overflow:hidden");
        Assert.False(child.ClipToBounds);
        child.ClearValue(UIElement.ClipToBoundsProperty);
        AssertClip(child, ClipEdges.All);
        Css.SetStyle(child, "");
        Assert.Null(CssOverflowProperties.Get(child));
    }

    [Fact]
    public void SingleAxisClip_KeepsRoundedCornersSquareUntilBothAxesClip()
    {
        var panel = Box();
        Css.SetStyle(panel, "border-radius:50%; overflow:clip visible");
        var clip = Assert.IsType<CssRoundedRectangleGeometry>(panel.GetLayoutClip());
        Assert.Equal(default, clip.Radii.TopLeft);
        Assert.True(clip.FillContains(new Point(1, 1)));

        Css.SetStyle(panel, "border-radius:50%; overflow:hidden");
        clip = Assert.IsType<CssRoundedRectangleGeometry>(panel.GetLayoutClip());
        Assert.NotEqual(default, clip.Radii.TopLeft);
        Assert.False(clip.FillContains(new Point(1, 1)));
    }

    [Fact]
    public void Border_UsesItsPaddingEdgeForCssAxisClipping()
    {
        var border = new Border { BorderThickness = new Thickness(4) };
        Css.SetStyle(border, "border-radius:50%; overflow:clip visible");
        CssBorderRadiusTests.Layout(border, 40, 30);
        var clip = Assert.IsType<CssRoundedRectangleGeometry>(border.GetLayoutClip());
        Assert.Equal(new Rect(4, 4, 32, 22), clip.ClipReferenceRect);
        Assert.Equal(ClipEdges.Left | ClipEdges.Right, clip.ClipEdges);
        Assert.Equal(default, clip.Radii.TopLeft);

        Css.SetStyle(border, "border-radius:50%; overflow:hidden");
        clip = Assert.IsType<CssRoundedRectangleGeometry>(border.GetLayoutClip());
        Assert.Equal(ClipEdges.All, clip.ClipEdges);
        Assert.Equal(new Size(16, 11), clip.Radii.TopLeft);
    }

    private static Canvas Box()
    {
        var child = new Border { Width = 80, Height = 80, Background = Brushes.Red };
        var panel = new Canvas { Width = 40, Height = 30 };
        panel.Children.Add(child);
        CssBorderRadiusTests.Layout(panel, 40, 30);
        return panel;
    }

    private static void AssertClip(FrameworkElement element, ClipEdges edges)
    {
        Assert.True(element.ClipToBounds);
        Assert.Equal(edges, element.ClipToBoundsEdges);
        Assert.NotNull(element.GetLayoutClip());
    }
}
